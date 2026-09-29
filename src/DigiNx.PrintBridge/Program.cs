using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Web.WebView2.Core;

namespace DigiNx.PrintBridge;

internal static class Program
{
    private const string Protocol = "1";
    private const string Version = "1.0.0";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var config = BridgeConfig.Load();

        if (args.Any(arg => arg.Equals("--configure", StringComparison.OrdinalIgnoreCase)))
        {
            using var setup = new SetupForm(config);
            setup.ShowDialog();
            return;
        }

        if (config.AllowedOrigins.Count == 0)
        {
            using var setup = new SetupForm(config);
            if (setup.ShowDialog() != DialogResult.OK || !setup.Saved) return;
            config = BridgeConfig.Load();
        }

        using var mutex = new Mutex(true, "DigiNx.PrintBridge.Singleton", out var isFirstInstance);
        if (!isFirstInstance) return;
        using var engine = new PrintEngine();
        var registry = new JobRegistry();
        WebApplication? server = null;

        var bootstrap = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1),
            Opacity = 0,
        };

        bootstrap.Shown += async (_, _) =>
        {
            try
            {
                await engine.InitializeAsync();
                server = BuildServer(config, engine, registry);
                await server.StartAsync();
                File.WriteAllText(Path.Combine(BridgeConfig.ConfigDirectory, "bridge.log"),
                    $"{DateTimeOffset.Now:u} DigiNx Print Bridge {Version} listening on http://{config.Host}:{config.Port}\r\n");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(BridgeConfig.ConfigDirectory, "startup-error.log"), ex.ToString());
                MessageBox.Show($"DigiNx Print Bridge could not start.\n\n{ex.Message}", "DigiNx Print Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        };

        Application.ApplicationExit += (_, _) =>
        {
            if (server is null) return;
            try { server.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
            try { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        };

        Application.Run(bootstrap);
    }

    private static WebApplication BuildServer(BridgeConfig config, PrintEngine engine, JobRegistry registry)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://{config.Host}:{config.Port}");
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            var origin = ctx.Request.Headers.Origin.ToString();
            if (!OriginAllowed(origin, config))
            {
                await Json(ctx, 403, new { errorCode = "ORIGIN_NOT_ALLOWED" });
                return;
            }

            if (!string.IsNullOrEmpty(origin))
            {
                ctx.Response.Headers["Access-Control-Allow-Origin"] = origin;
                ctx.Response.Headers["Vary"] = "Origin";
            }
            ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            ctx.Response.Headers["Access-Control-Allow-Headers"] = "content-type,x-diginx-print-protocol";

            if (HttpMethods.IsOptions(ctx.Request.Method))
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next();
        });

        app.MapGet("/v1/health", async ctx =>
        {
            if (!ProtocolAllowed(ctx, allowNavigationWithoutOrigin: true))
            {
                await Json(ctx, 400, new { status = "failed", errorCode = "PRINT_PROTOCOL_MISMATCH" });
                return;
            }
            await Json(ctx, 200, new
            {
                status = "ok",
                version = Version,
                printers = PrinterCatalog.List(),
                recentJobs = registry.Recent(),
            });
        });

        app.MapPost("/v1/test-print", async ctx =>
        {
            if (!ProtocolAllowed(ctx, false)) { await Json(ctx, 400, new { status = "failed", errorCode = "PRINT_PROTOCOL_MISMATCH" }); return; }
            var body = await ReadJson<TestPrintRequest>(ctx, 64_000, config.MaxPayloadBytes);
            if (body is null || string.IsNullOrWhiteSpace(body.printerId)) { await Json(ctx, 400, new { status = "failed", errorCode = "INVALID_TEST_PRINT_COMMAND" }); return; }
            if (!PrinterCatalog.Exists(body.printerId)) { await Json(ctx, 404, new { status = "failed", errorCode = "PRINTER_NOT_FOUND" }); return; }
            var text = string.IsNullOrWhiteSpace(body.text) ? "DigiNx POS\nPrinter test\n" : body.text;
            var html = TestHtml(text);
            var result = await engine.PrintHtmlAsync(body.printerId, html, 1, config.PrintTimeoutSeconds);
            if (result != CoreWebView2PrintStatus.Succeeded)
            {
                await Json(ctx, 503, new { status = "failed", errorCode = $"PRINT_{result.ToString().ToUpperInvariant()}" });
                return;
            }
            await Json(ctx, 200, new { status = "printed", printerId = body.printerId, processedAt = DateTimeOffset.UtcNow });
        });

        app.MapPost("/v1/print", async ctx =>
        {
            if (!ProtocolAllowed(ctx, false)) { await Json(ctx, 400, new { status = "failed", errorCode = "PRINT_PROTOCOL_MISMATCH" }); return; }
            var body = await ReadJson<ReceiptPrintRequest>(ctx, config.MaxPayloadBytes, config.MaxPayloadBytes);
            if (body is null || string.IsNullOrWhiteSpace(body.commandId) || string.IsNullOrWhiteSpace(body.receiptId) ||
                string.IsNullOrWhiteSpace(body.receiptNumber) || string.IsNullOrWhiteSpace(body.printerId) || string.IsNullOrWhiteSpace(body.printableHtml))
            {
                await Json(ctx, 400, new { status = "failed", errorCode = "INVALID_PRINT_COMMAND" });
                return;
            }
            if (registry.TryGet(body.commandId, out var existing) && existing is not null)
            {
                await Json(ctx, 200, existing);
                return;
            }
            if (!PrinterCatalog.Exists(body.printerId))
            {
                await Json(ctx, 404, new { status = "failed", errorCode = "PRINTER_NOT_FOUND", body.commandId, body.receiptId });
                return;
            }

            var copies = Math.Clamp(body.copies <= 0 ? 1 : body.copies, 1, 10);
            var result = await engine.PrintHtmlAsync(body.printerId, body.printableHtml, copies, config.PrintTimeoutSeconds);
            if (result != CoreWebView2PrintStatus.Succeeded)
            {
                await Json(ctx, 503, new { status = "failed", errorCode = $"PRINT_{result.ToString().ToUpperInvariant()}", body.commandId, body.receiptId });
                return;
            }
            var job = new CompletedJob("printed", body.commandId, body.receiptId, body.receiptNumber, body.printerId, copies, DateTimeOffset.UtcNow);
            registry.Add(job);
            await Json(ctx, 200, job);
        });

        app.MapFallback(async ctx => await Json(ctx, 404, new { errorCode = "NOT_FOUND" }));
        return app;
    }

    private static bool ProtocolAllowed(HttpContext ctx, bool allowNavigationWithoutOrigin)
    {
        if (ctx.Request.Headers["x-diginx-print-protocol"].ToString() == Protocol) return true;
        return allowNavigationWithoutOrigin && string.IsNullOrEmpty(ctx.Request.Headers.Origin.ToString());
    }

    private static bool OriginAllowed(string origin, BridgeConfig config)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if ((uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1" || uri.Host == "::1") &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)) return true;
        return config.AllowedOrigins.Any(item => BridgeConfig.NormalizeOrigin(item).Equals(BridgeConfig.NormalizeOrigin(origin), StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<T?> ReadJson<T>(HttpContext ctx, int maxBytes, int configuredMax)
    {
        var max = Math.Min(maxBytes, configuredMax);
        if (ctx.Request.ContentLength is > 0 && ctx.Request.ContentLength > max) return default;
        using var reader = new StreamReader(ctx.Request.Body);
        var json = await reader.ReadToEndAsync();
        if (System.Text.Encoding.UTF8.GetByteCount(json) > max) return default;
        try { return JsonSerializer.Deserialize<T>(json, BridgeConfig.JsonOptions); } catch { return default; }
    }

    private static Task Json(HttpContext ctx, int status, object value)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(value, BridgeConfig.JsonOptions));
    }

    private static string TestHtml(string text)
    {
        var safe = System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>");
        return $"<!doctype html><html><head><meta charset='utf-8'><style>@page{{size:80mm auto;margin:0}}body{{width:80mm;margin:0;padding:3mm;font:12px Arial,sans-serif;color:#111}}h3{{margin:0 0 4mm}}</style></head><body><h3>DigiNx POS</h3><div>{safe}</div><div style='margin-top:4mm'>{DateTimeOffset.Now:dd/MM/yyyy hh:mm tt}</div></body></html>";
    }

    private sealed class TestPrintRequest { public string printerId { get; set; } = ""; public string text { get; set; } = ""; }
    private sealed class ReceiptPrintRequest
    {
        public string commandId { get; set; } = "";
        public string receiptId { get; set; } = "";
        public string receiptNumber { get; set; } = "";
        public string printerId { get; set; } = "";
        public string printableHtml { get; set; } = "";
        public int copies { get; set; } = 1;
    }
}
