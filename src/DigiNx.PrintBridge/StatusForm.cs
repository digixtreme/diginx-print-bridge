using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace DigiNx.PrintBridge;

internal sealed class StatusForm : Form
{
    private readonly BridgeConfig config;
    private readonly PrintEngine engine;
    private readonly JobRegistry registry;
    private readonly Label serviceValue;
    private readonly Label listenerValue;
    private readonly Label lastPrintValue;
    private readonly ComboBox printerCombo;
    private readonly Button testButton;
    private readonly Label actionLabel;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private bool serverRunning;
    private string? serverError;

    public StatusForm(BridgeConfig config, PrintEngine engine, JobRegistry registry)
    {
        this.config = config;
        this.engine = engine;
        this.registry = registry;
        Text = "DigiNx Print Bridge";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        Width = 620;
        Height = 500;
        Font = new Font("Segoe UI", 9F);

        var title = new Label { Text = "DigiNx Print Bridge", Font = new Font("Segoe UI", 16F, FontStyle.Bold), Left = 24, Top = 20, AutoSize = true };
        var subtitle = new Label { Text = "Local printing service for DigiNx POS", ForeColor = Color.DimGray, Left = 26, Top = 54, AutoSize = true };

        var serviceLabel = new Label { Text = "Bridge status", Left = 26, Top = 94, Width = 130 };
        serviceValue = new Label { Text = "Starting...", Left = 180, Top = 94, Width = 360, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        var listenerLabel = new Label { Text = "Local service", Left = 26, Top = 124, Width = 130 };
        listenerValue = new Label { Text = $"http://{config.Host}:{config.Port}", Left = 180, Top = 124, Width = 360 };

        var divider1 = new Label { BorderStyle = BorderStyle.Fixed3D, Left = 24, Top = 158, Width = 550, Height = 2 };
        var printerLabel = new Label { Text = "Test printer", Font = new Font("Segoe UI", 9F, FontStyle.Bold), Left = 26, Top = 180, AutoSize = true };
        printerCombo = new ComboBox { Left = 26, Top = 206, Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
        testButton = new Button { Text = "Test Print", Left = 430, Top = 204, Width = 140, Height = 30 };
        testButton.Click += async (_, _) => await TestPrintAsync();

        var note = new Label { Text = "This selection is for diagnostics only. DigiNx POS remains the source of truth for the printer used by real sales.", Left = 26, Top = 244, Width = 540, Height = 40, ForeColor = Color.DimGray };

        var lastLabel = new Label { Text = "Last POS print", Left = 26, Top = 296, Width = 130 };
        lastPrintValue = new Label { Text = "No completed POS print in this session", Left = 180, Top = 296, Width = 390 };
        actionLabel = new Label { Text = "", Left = 26, Top = 330, Width = 540, Height = 38, ForeColor = Color.DimGray };

        var refresh = new Button { Text = "Refresh", Left = 26, Top = 382, Width = 100, Height = 32 };
        refresh.Click += (_, _) => RefreshStatus();
        var configure = new Button { Text = "Configure POS URL", Left = 136, Top = 382, Width = 140, Height = 32 };
        configure.Click += (_, _) => { using var setup = new SetupForm(config); setup.ShowDialog(this); };
        var logs = new Button { Text = "Open Logs", Left = 286, Top = 382, Width = 100, Height = 32 };
        logs.Click += (_, _) => OpenLogs();
        var copy = new Button { Text = "Copy Diagnostics", Left = 396, Top = 382, Width = 140, Height = 32 };
        copy.Click += (_, _) => CopyDiagnostics();

        Controls.AddRange([title, subtitle, serviceLabel, serviceValue, listenerLabel, listenerValue, divider1, printerLabel, printerCombo, testButton, note, lastLabel, lastPrintValue, actionLabel, refresh, configure, logs, copy]);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };

        refreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        refreshTimer.Tick += (_, _) => RefreshStatus();
        refreshTimer.Start();
        RefreshStatus();
    }

    public void SetServerState(bool running, string? error = null)
    {
        serverRunning = running;
        serverError = error;
        RefreshStatus();
    }

    public void ShowStatus()
    {
        RefreshStatus();
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
        Activate();
    }

    private void RefreshStatus()
    {
        serviceValue.Text = serverRunning ? "● Running" : serverError is null ? "● Starting" : "● Error";
        serviceValue.ForeColor = serverRunning ? Color.SeaGreen : serverError is null ? Color.DarkOrange : Color.Firebrick;
        listenerValue.Text = serverRunning ? $"Listening on http://{config.Host}:{config.Port}" : serverError ?? $"http://{config.Host}:{config.Port}";

        var selected = printerCombo.SelectedItem?.ToString();
        var printers = PrinterCatalog.List();
        printerCombo.BeginUpdate();
        printerCombo.Items.Clear();
        foreach (var printer in printers) printerCombo.Items.Add(printer.printerId);
        if (selected is not null && printerCombo.Items.Contains(selected)) printerCombo.SelectedItem = selected;
        else if (printers.Count > 0)
        {
            var preferred = printers.FirstOrDefault(p => p.isDefault)?.printerId ?? printers[0].printerId;
            printerCombo.SelectedItem = preferred;
        }
        printerCombo.EndUpdate();
        testButton.Enabled = serverRunning && printerCombo.Items.Count > 0;

        var recent = registry.Recent(1).FirstOrDefault();
        lastPrintValue.Text = recent is null ? "No completed POS print in this session" : $"{recent.receiptNumber} · {recent.printerId} · {recent.processedAt.ToLocalTime():dd/MM/yyyy hh:mm tt}";
    }

    private async Task TestPrintAsync()
    {
        var printer = printerCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(printer)) { actionLabel.ForeColor = Color.Firebrick; actionLabel.Text = "No Windows printer is available for Test Print."; return; }
        testButton.Enabled = false;
        actionLabel.ForeColor = Color.DimGray;
        actionLabel.Text = $"Sending Test Print to {printer}...";
        try
        {
            var result = await engine.PrintHtmlAsync(printer, TestHtml(), 1, config.PrintTimeoutSeconds);
            if (result == CoreWebView2PrintStatus.Succeeded)
            {
                actionLabel.ForeColor = Color.SeaGreen;
                actionLabel.Text = $"Test Print sent successfully to {printer}.";
            }
            else
            {
                actionLabel.ForeColor = Color.Firebrick;
                actionLabel.Text = $"Test Print failed: {result}.";
            }
        }
        catch (Exception ex)
        {
            actionLabel.ForeColor = Color.Firebrick;
            actionLabel.Text = $"Test Print failed: {ex.Message}";
        }
        finally { testButton.Enabled = serverRunning && printerCombo.Items.Count > 0; }
    }

    private static string TestHtml() => $"<!doctype html><html><head><meta charset='utf-8'><style>@page{{size:80mm auto;margin:0}}body{{width:80mm;margin:0;padding:3mm;font:12px Arial,sans-serif;color:#111;text-align:center}}h2{{margin:0 0 3mm}}.line{{border-top:1px dashed #111;margin:3mm 0}}</style></head><body><h2>DigiNx Print Bridge</h2><div>Test Print</div><div class='line'></div><div>English: Print OK</div><div dir='rtl'>Arabic: اختبار الطباعة</div><div>{DateTimeOffset.Now:dd/MM/yyyy hh:mm tt}</div><div class='line'></div><div>DigiNx POS</div></body></html>";

    private void OpenLogs()
    {
        Directory.CreateDirectory(BridgeConfig.ConfigDirectory);
        Process.Start(new ProcessStartInfo { FileName = BridgeConfig.ConfigDirectory, UseShellExecute = true });
    }

    private void CopyDiagnostics()
    {
        var printers = PrinterCatalog.List();
        var recent = registry.Recent(1).FirstOrDefault();
        var text = $"DigiNx Print Bridge 1.1.0\r\nStatus: {(serverRunning ? "Running" : "Not running")}\r\nListener: http://{config.Host}:{config.Port}\r\nConfigured POS origins: {string.Join(", ", config.AllowedOrigins)}\r\nInstalled printers: {string.Join(", ", printers.Select(p => p.printerId))}\r\nLast POS print: {(recent is null ? "None in this session" : $"{recent.receiptNumber} / {recent.printerId} / {recent.processedAt.ToLocalTime():u}")}\r\nError: {serverError ?? "None"}";
        Clipboard.SetText(text);
        actionLabel.ForeColor = Color.SeaGreen;
        actionLabel.Text = "Diagnostics copied to clipboard.";
    }
}
