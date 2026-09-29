using System.Text.Json;

namespace DigiNx.PrintBridge;

internal sealed class BridgeConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 17777;
    public List<string> AllowedOrigins { get; set; } = new();
    public int MaxPayloadBytes { get; set; } = 2_000_000;
    public int PrintTimeoutSeconds { get; set; } = 30;

    public static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigiNx",
        "PrintBridge");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    public static BridgeConfig Load()
    {
        Directory.CreateDirectory(ConfigDirectory);
        if (!File.Exists(ConfigPath))
        {
            var created = new BridgeConfig();
            Save(created);
            return created;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<BridgeConfig>(File.ReadAllText(ConfigPath), JsonOptions)
                ?? new BridgeConfig();
            Normalize(loaded);
            return loaded;
        }
        catch
        {
            var fallback = new BridgeConfig();
            Save(fallback);
            return fallback;
        }
    }

    public static void Save(BridgeConfig config)
    {
        Normalize(config);
        Directory.CreateDirectory(ConfigDirectory);
        var tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(config, JsonOptions));
        File.Move(tempPath, ConfigPath, true);
    }

    public static string NormalizeOrigin(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return value.Trim().TrimEnd('/');
        var port = uri.IsDefaultPort ? "" : $":{uri.Port}";
        return $"{uri.Scheme}://{uri.Host}{port}";
    }

    private static void Normalize(BridgeConfig config)
    {
        if (config.Port is < 1 or > 65535) config.Port = 17777;
        config.Host = "127.0.0.1";
        config.AllowedOrigins = config.AllowedOrigins
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(NormalizeOrigin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        config.MaxPayloadBytes = Math.Clamp(config.MaxPayloadBytes, 64_000, 8_000_000);
        config.PrintTimeoutSeconds = Math.Clamp(config.PrintTimeoutSeconds, 5, 120);
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
