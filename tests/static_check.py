from pathlib import Path
import re, sys
root = Path(__file__).resolve().parents[1]
program = (root/'src/DigiNx.PrintBridge/Program.cs').read_text()
config = (root/'src/DigiNx.PrintBridge/BridgeConfig.cs').read_text()
setup = (root/'src/DigiNx.PrintBridge/SetupForm.cs').read_text()
engine = (root/'src/DigiNx.PrintBridge/PrintEngine.cs').read_text()
wix = (root/'installer/DigiNx.PrintBridge.wxs').read_text()
checks = {
    'localhost only': 'builder.WebHost.UseUrls($"http://{config.Host}:{config.Port}")' in program and 'config.Host = "127.0.0.1"' in config,
    'protocol endpoints': all(x in program for x in ['/v1/health','/v1/test-print','/v1/print']),
    'real printer catalog': 'PrinterCatalog.Exists' in program,
    'same html WebView2 print': 'body.printableHtml' in program and 'PrintHtmlAsync' in program and 'CoreWebView2' in engine,
    'first-run configuration': 'config.AllowedOrigins.Count == 0' in program and 'SetupForm' in program,
    'config command': '--configure' in program and 'Configure DigiNx Print Bridge' in wix,
    'auto start': r'Software\Microsoft\Windows\CurrentVersion\Run' in wix,
    'msi source': 'DigiNx.PrintBridge.exe' in wix,
}
failed=[k for k,v in checks.items() if not v]
for k,v in checks.items(): print(('PASS' if v else 'FAIL'), k)
if failed: sys.exit(1)
