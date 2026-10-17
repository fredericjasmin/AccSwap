using AccSwap.Core.Platforms;
using AccSwap.Core.RegistryAccess;
using Xunit;

namespace AccSwap.Core.Tests;

public class InstallFinderTests : IDisposable
{
    private readonly TempDir _work = new();

    [Fact]
    public void FindViaUninstallRegistry_UsesInstallLocationPlusRelativeExe()
    {
        string installDir = _work.Combine("Epic", "Launcher");
        string exe = Path.Combine(installDir, "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "fake exe");

        var registry = new FakeUninstallRegistry();
        registry.SetValue(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            "{EPIC-GUID}",
            "DisplayName",
            "Epic Games Launcher");
        registry.SetValue(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            "{EPIC-GUID}",
            "InstallLocation",
            installDir);

        string? found = InstallFinder.FindViaUninstallRegistry(
            registry,
            "^Epic Games Launcher$",
            "Portal\\Binaries\\Win64\\EpicGamesLauncher.exe");

        Assert.Equal(exe, found);
    }

    [Fact]
    public void FindViaUninstallRegistry_DisplayIconStripsIconIndex()
    {
        string exe = _work.Combine("Galaxy", "GalaxyClient.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "fake exe");

        var registry = new FakeUninstallRegistry();
        registry.SetValue(
            RegistryHive.LocalMachine,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            "GOG GALAXY",
            "DisplayName",
            "GOG GALAXY");
        registry.SetValue(
            RegistryHive.LocalMachine,
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            "GOG GALAXY",
            "DisplayIcon",
            $"\"{exe}\",0");

        string? found = InstallFinder.FindViaUninstallRegistry(registry, "GOG GALAXY", relativeExe: null);

        Assert.Equal(exe, found);
    }

    [Fact]
    public void FindViaUninstallRegistry_NoMatch_ReturnsNull()
    {
        Assert.Null(InstallFinder.FindViaUninstallRegistry(
            new FakeUninstallRegistry(),
            "^Epic Games Launcher$",
            "nope.exe"));
    }

    private sealed class FakeUninstallRegistry : IRegistryService
    {
        private readonly Dictionary<(RegistryHive Hive, string KeyPath, string Name), string> _values = [];
        private readonly List<string> _subKeys = [];

        public void SetValue(RegistryHive hive, string keyPath, string subKey, string name, string value)
        {
            _subKeys.Add($"{keyPath}\\{subKey}");
            _values[(hive, $"{keyPath}\\{subKey}", name)] = value;
        }

        public string? GetStringValue(RegistryHive hive, string keyPath, string valueName) =>
            _values.TryGetValue((hive, keyPath, valueName), out string? value) ? value : null;

        public string[] GetSubKeyNames(RegistryHive hive, string keyPath) =>
            _subKeys
                .Where(k => k.StartsWith($"{keyPath}\\", StringComparison.Ordinal))
                .Select(k => k[(keyPath.Length + 1)..])
                .ToArray();

        public bool SetStringValue(RegistryHive hive, string keyPath, string valueName, string value)
        {
            bool existed = _values.ContainsKey((hive, keyPath, valueName));
            _values[(hive, keyPath, valueName)] = value;
            return existed;
        }

        public bool DeleteValue(RegistryHive hive, string keyPath, string valueName) =>
            _values.Remove((hive, keyPath, valueName));
    }

    public void Dispose() => _work.Dispose();
}
