using System.Text.Json;
using System.Text.Json.Serialization;

namespace GLAS.Core.Settings;

public sealed class AppSettings
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "es";

    [JsonPropertyName("startMinimized")]
    public bool StartMinimized { get; set; }

    [JsonPropertyName("closeToTray")]
    public bool CloseToTray { get; set; } = true;

    [JsonPropertyName("autoStart")]
    public bool AutoStart { get; set; }

    [JsonPropertyName("steamLaunchArguments")]
    public string SteamLaunchArguments { get; set; } = "-silent";

    [JsonPropertyName("protectSnapshots")]
    public bool ProtectSnapshots { get; set; }

    /// <summary>User-configured launcher paths per platform id (empty = auto-detect).</summary>
    [JsonPropertyName("launcherPaths")]
    public Dictionary<string, string> LauncherPaths { get; set; } = [];
}

public sealed class AppSettingsStore(string settingsPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Settings { get; private set; } = new();

    public static AppSettingsStore CreateDefault(string rootDir) =>
        new(Path.Combine(rootDir, "settings.json"));

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath), JsonOptions);
                if (loaded is not null)
                {
                    Settings = loaded;
                }
            }
        }
        catch (JsonException)
        {
            // Corrupt settings fall back to defaults; they get rewritten on save.
        }

        return Settings;
    }

    public void Save(AppSettings? settings = null)
    {
        if (settings is not null)
        {
            Settings = settings;
        }

        string? dir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(settingsPath, JsonSerializer.Serialize(Settings, JsonOptions));
    }
}
