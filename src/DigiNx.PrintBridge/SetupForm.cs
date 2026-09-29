namespace DigiNx.PrintBridge;

internal sealed class SetupForm : Form
{
    private readonly TextBox originTextBox;
    private readonly Label validationLabel;
    private readonly BridgeConfig config;

    public bool Saved { get; private set; }

    public SetupForm(BridgeConfig config)
    {
        this.config = config;
        Text = "DigiNx Print Bridge Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Width = 560;
        Height = 285;
        Font = new Font("Segoe UI", 9F);

        var title = new Label
        {
            Text = "Connect DigiNx POS Browser",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            AutoSize = true,
            Left = 24,
            Top = 22,
        };

        var description = new Label
        {
            Text = "Enter the DigiNx POS website address used on this computer.\nExample: https://app.diginxpos.com",
            AutoSize = true,
            Left = 24,
            Top = 62,
        };

        var originLabel = new Label
        {
            Text = "DigiNx POS URL",
            AutoSize = true,
            Left = 24,
            Top = 112,
        };

        originTextBox = new TextBox
        {
            Left = 24,
            Top = 134,
            Width = 495,
            Text = config.AllowedOrigins.FirstOrDefault() ?? "https://app.diginxpos.com",
        };

        validationLabel = new Label
        {
            Left = 24,
            Top = 164,
            Width = 495,
            ForeColor = Color.Firebrick,
            AutoSize = false,
            Height = 22,
        };

        var saveButton = new Button
        {
            Text = "Save & Start",
            Left = 389,
            Top = 194,
            Width = 130,
            Height = 34,
        };
        saveButton.Click += (_, _) => SaveConfiguration();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Left = 289,
            Top = 194,
            Width = 90,
            Height = 34,
        };
        cancelButton.Click += (_, _) => Close();

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.AddRange([title, description, originLabel, originTextBox, validationLabel, cancelButton, saveButton]);
    }

    private void SaveConfiguration()
    {
        var raw = originTextBox.Text.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            validationLabel.Text = "Enter a valid http:// or https:// DigiNx POS URL.";
            return;
        }

        var normalized = BridgeConfig.NormalizeOrigin(raw);
        config.AllowedOrigins = [normalized];
        BridgeConfig.Save(config);
        Saved = true;
        DialogResult = DialogResult.OK;
        Close();
    }
}
