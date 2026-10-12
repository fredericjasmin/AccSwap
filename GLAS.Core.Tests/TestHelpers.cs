using GLAS.Core.Processes;
using GLAS.Core.RegistryAccess;

namespace GLAS.Core.Tests;

public sealed class FakeProcessController : IProcessController
{
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Log { get; } = [];

    /// <summary>When set, invoked on StartAsync; throw here to simulate a launch failure.</summary>
    public Func<string, Task>? StartBehavior { get; set; }

    /// <summary>When set, invoked at the end of EnsureClosedAsync (e.g. release a test lock).</summary>
    public Action? OnEnsureClosed { get; set; }

    public Task EnsureClosedAsync(IReadOnlyList<string> processNames, CancellationToken ct = default)
    {
        foreach (string name in processNames)
        {
            if (Running.Remove(Path.GetFileNameWithoutExtension(name.Trim())))
            {
                Log.Add($"kill:{name}");
            }
        }

        OnEnsureClosed?.Invoke();
        return Task.CompletedTask;
    }

    public bool IsRunning(string processName) =>
        Running.Contains(Path.GetFileNameWithoutExtension(processName.Trim()));

    public async Task StartAsync(string exePath, string arguments = "", CancellationToken ct = default)
    {
        Log.Add($"start:{Path.GetFileName(exePath)}:{arguments}");
        Running.Add(Path.GetFileNameWithoutExtension(exePath));
        if (StartBehavior is not null)
        {
            await StartBehavior(exePath);
        }
    }
}

public sealed class InMemoryRegistryService : IRegistryService
{
    private readonly Dictionary<(RegistryHive Hive, string KeyPath, string Name), string> _values = [];

    public string? GetStringValue(RegistryHive hive, string keyPath, string valueName) =>
        _values.TryGetValue((hive, keyPath, valueName), out string? value) ? value : null;

    public string[] GetSubKeyNames(RegistryHive hive, string keyPath) => [];

    public bool SetStringValue(RegistryHive hive, string keyPath, string valueName, string value)
    {
        bool existed = _values.ContainsKey((hive, keyPath, valueName));
        _values[(hive, keyPath, valueName)] = value;
        return existed;
    }

    public bool DeleteValue(RegistryHive hive, string keyPath, string valueName) =>
        _values.Remove((hive, keyPath, valueName));
}

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"glas_test_{Guid.NewGuid():N}");

    public TempDir()
    {
        Directory.CreateDirectory(Path);
    }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort.
        }
    }
}

public static class Samples
{
    public const string SteamIdA = "76561198000000001";
    public const string SteamIdB = "76561198000000002";

    public static string LoginUsersVdf(string mostRecent = SteamIdA)
    {
        string flag(string steamId) => mostRecent == steamId ? "1" : "0";
        return string.Join('\n',
            "\"users\"",
            "{",
            $"\t\"{SteamIdA}\"",
            "\t{",
            "\t\t\"AccountName\"\t\"userA\"",
            "\t\t\"PersonaName\"\t\"User A\"",
            $"\t\t\"RememberPassword\"\t\"{flag(SteamIdA)}\"",
            $"\t\t\"MostRecent\"\t\"{flag(SteamIdA)}\"",
            "\t\t\"Timestamp\"\t\"1700000000\"",
            "\t}",
            $"\t\"{SteamIdB}\"",
            "\t{",
            "\t\t\"AccountName\"\t\"userB\"",
            "\t\t\"PersonaName\"\t\"User B\"",
            $"\t\t\"RememberPassword\"\t\"{flag(SteamIdB)}\"",
            $"\t\t\"MostRecent\"\t\"{flag(SteamIdB)}\"",
            "\t\t\"Timestamp\"\t\"1700000001\"",
            "\t}",
            "}");
    }

    /// <summary>
    /// Mirrors a real single-account loginusers.vdf: Steam omits MostRecent
    /// when only one account is remembered.
    /// </summary>
    public static string SingleUserLoginUsersVdf() => string.Join('\n',
        "\"users\"",
        "{",
        $"\t\"{SteamIdA}\"",
        "\t{",
        "\t\t\"AccountName\"\t\"userA\"",
        "\t\t\"PersonaName\"\t\"User A\"",
        "\t\t\"RememberPassword\"\t\"1\"",
        "\t\t\"AutoLogin\"\t\"1\"",
        "\t\t\"Timestamp\"\t\"1700000000\"",
        "\t}",
        "}");

    public static string EpicIni(string dataToken, params string[] sectionGuids)
    {
        var lines = new List<string>
        {
            "[Portal]",
            "LastLocale=en",
            $"Data={dataToken}",
            "WindowPos=100,100",
            "[Memory]",
            "Settings=Something",
        };

        // Epic writes one section per logged-in account, named by its GUID.
        foreach (string guid in sectionGuids)
        {
            lines.Add($"[{guid}_General]");
            lines.Add("Language=es");
        }

        return string.Join('\n', lines);
    }
}
