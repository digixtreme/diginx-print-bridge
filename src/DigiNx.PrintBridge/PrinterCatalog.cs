using System.Drawing.Printing;

namespace DigiNx.PrintBridge;

internal sealed record BridgePrinter(string printerId, string label, string state, bool isDefault);

internal static class PrinterCatalog
{
    public static IReadOnlyList<BridgePrinter> List()
    {
        var defaultName = "";
        try { defaultName = new PrinterSettings().PrinterName ?? ""; } catch { }

        var result = new List<BridgePrinter>();
        foreach (string name in PrinterSettings.InstalledPrinters)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            // Installed does not prove ready/paper status. Report unknown honestly.
            result.Add(new BridgePrinter(name, name, "unknown",
                string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)));
        }
        return result.OrderByDescending(p => p.isDefault).ThenBy(p => p.label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool Exists(string printerName) =>
        List().Any(p => string.Equals(p.printerId, printerName, StringComparison.OrdinalIgnoreCase));
}
