using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DigiNx.PrintBridge;

internal sealed class PrintEngine : IDisposable
{
    private readonly Form _host;
    private readonly WebView2 _webView;
    private readonly SemaphoreSlim _printGate = new(1, 1);
    private bool _ready;

    public PrintEngine()
    {
        _host = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(2, 2),
            Opacity = 0,
        };
        _webView = new WebView2 { Dock = DockStyle.Fill };
        _host.Controls.Add(_webView);
    }

    public async Task InitializeAsync()
    {
        _host.Show();
        await _webView.EnsureCoreWebView2Async();
        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _ready = true;
    }

    public Task<CoreWebView2PrintStatus> PrintHtmlAsync(string printerName, string html, int copies, int timeoutSeconds)
    {
        var tcs = new TaskCompletionSource<CoreWebView2PrintStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run()
        {
            _ = PrintHtmlOnUiAsync(printerName, html, copies, timeoutSeconds).ContinueWith(task =>
            {
                if (task.IsCanceled) tcs.TrySetCanceled();
                else if (task.IsFaulted) tcs.TrySetException(task.Exception!.InnerExceptions);
                else tcs.TrySetResult(task.Result);
            }, TaskScheduler.Default);
        }
        if (_host.InvokeRequired) _host.BeginInvoke((Action)Run); else Run();
        return tcs.Task;
    }

    private async Task<CoreWebView2PrintStatus> PrintHtmlOnUiAsync(string printerName, string html, int copies, int timeoutSeconds)
    {
        if (!_ready) throw new InvalidOperationException("WEBVIEW_NOT_READY");
        await _printGate.WaitAsync();
        var tempDir = Path.Combine(BridgeConfig.ConfigDirectory, "jobs");
        Directory.CreateDirectory(tempDir);
        var file = Path.Combine(tempDir, $"job-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(file, html);
            var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void NavigationCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                _webView.NavigationCompleted -= NavigationCompleted;
                if (e.IsSuccess) navTcs.TrySetResult(true);
                else navTcs.TrySetException(new InvalidOperationException($"HTML_NAVIGATION_FAILED_{e.WebErrorStatus}"));
            }
            _webView.NavigationCompleted += NavigationCompleted;
            _webView.Source = new Uri(file);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(5, timeoutSeconds)));
            await navTcs.Task.WaitAsync(timeout.Token);

            // Wait briefly for fonts/images used by the custom receipt template.
            for (var i = 0; i < 30; i++)
            {
                var state = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify({ready:document.readyState==='complete',images:[...document.images].every(i=>i.complete)})");
                if (state.Contains("\\\"ready\\\":true") && state.Contains("\\\"images\\\":true")) break;
                await Task.Delay(100, timeout.Token);
            }
            await Task.Delay(150, timeout.Token);

            var settings = _webView.CoreWebView2.Environment.CreatePrintSettings();
            settings.PrinterName = printerName;
            settings.Copies = Math.Max(1, copies);
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.ScaleFactor = 1.0;
            settings.Orientation = CoreWebView2PrintOrientation.Portrait;

            return await _webView.CoreWebView2.PrintAsync(settings);
        }
        finally
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            _printGate.Release();
        }
    }

    public void Dispose()
    {
        _webView.Dispose();
        _host.Dispose();
        _printGate.Dispose();
    }
}
