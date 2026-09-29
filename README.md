# DigiNx Print Bridge - Windows MSI baseline

This package builds a proper per-user Windows MSI for DigiNx Browser Direct Print.

## Client experience

1. Double-click `DigiNx-Print-Bridge-Setup-x64.msi`.
2. Open **Start > DigiNx > DigiNx Print Bridge** once after installation (or sign out/in and it starts automatically).
3. On first run, enter the DigiNx POS browser URL, for example `https://app.diginxpos.com`.
4. The bridge listens only on `127.0.0.1:17777`.
5. DigiNx POS Browser Direct Print sends the existing `printableHtml` to the bridge, which renders it in WebView2 and silently prints through the selected Windows printer.

No Node.js or PowerShell installation command is required on the client.

## Build the MSI

The MSI is Windows-specific. Use the included GitHub Actions workflow:

`Actions > Build DigiNx Print Bridge MSI > Run workflow`

Download artifact `DigiNx-Print-Bridge-Setup-x64` containing:

- `DigiNx-Print-Bridge-Setup-x64.msi`
- `SHA256SUMS.txt`

## Verify after installation

Open in the client browser:

`http://127.0.0.1:17777/v1/health`

The response should show `status: ok` and the real Windows printers.

Then use DigiNx POS Printer Settings / Test Print before enabling Billing Direct Print.

## Security

- Loopback only (`127.0.0.1`).
- Browser origins are allow-listed.
- Protocol header remains `x-diginx-print-protocol: 1`.
- No raw network exposure.

## Upgrade/uninstall

The MSI supports normal Windows Apps & Features uninstall and major upgrades. Configuration is stored under `%LOCALAPPDATA%\DigiNx\PrintBridge\config.json` so upgrades do not need to recreate printer/browser setup.
