using System.Text;
using AccSwap.Core.Platforms.Generic;
using AccSwap.Core.RegistryAccess;
using AccSwap.Core.Snapshots;
using Xunit;

namespace AccSwap.Core.Tests;

public class GenericPlatformTests : IDisposable
{
    private readonly TempDir _work = new();
    private readonly TempDir _backup = new();
    private readonly FakeProcessController _processes = new();
    private readonly InMemoryRegistryService _registry = new();
    private readonly SnapshotStore _store;
    private readonly string _exePath;

    public GenericPlatformTests()
    {
        _store = new SnapshotStore(_work.Combine("store"));
        _exePath = _work.Combine("FakeLauncher.exe");
        File.WriteAllText(_exePath, "fake exe");
    }

    private PlatformDefinition MakeDefinition() => new()
    {
        Id = "fake",
        Name = "Fake Launcher",
        ExeCandidates = [_exePath],
        ProcessesToClose = ["FakeLauncher"],
        Targets =
        [
            new LoginTarget { Type = LoginTargetKind.File, Path = _work.Combine("settings.yml") },
            new LoginTarget { Type = LoginTargetKind.Glob, Path = _work.Combine("cache", "data_*") },
            new LoginTarget { Type = LoginTargetKind.Directory, Path = _work.Combine("sessions") },
            new LoginTarget
            {
                Type = LoginTargetKind.Registry,
                KeyPath = @"Software\AccSwapFake",
                ValueName = "user",
            },
            new LoginTarget
            {
                Type = LoginTargetKind.JsonField,
                Path = _work.Combine("client.config.json"),
                JsonPath = "Client.SavedAccountNames",
            },
        ],
        UniqueId = new UniqueIdDefinition
        {
            Kind = UniqueIdKind.FileRegex,
            Path = _work.Combine("settings.yml"),
            Regex = "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}",
        },
    };

    private void WriteSessionA()
    {
        File.WriteAllText(_work.Combine("settings.yml"), "email=a@mail.com\ntheme=dark");
        Directory.CreateDirectory(_work.Combine("cache"));
        File.WriteAllText(_work.Combine("cache", "data_001"), "A1");
        File.WriteAllText(_work.Combine("cache", "data_002"), "A2");
        Directory.CreateDirectory(_work.Combine("sessions", "userA"));
        File.WriteAllText(_work.Combine("sessions", "userA", "token.txt"), "tokenA");
        _registry.SetStringValue(RegistryHive.CurrentUser, @"Software\AccSwapFake", "user", "userA");
        File.WriteAllText(
            _work.Combine("client.config.json"),
            """{ "Client": { "SavedAccountNames": "a@mail.com", "Other": 42 } }""");
    }

    private void WriteSessionB()
    {
        File.WriteAllText(_work.Combine("settings.yml"), "email=b@mail.com\ntheme=light");
        Directory.CreateDirectory(_work.Combine("cache"));
        File.WriteAllText(_work.Combine("cache", "data_001"), "B1");
        File.WriteAllText(_work.Combine("cache", "data_003"), "B3");
        Directory.CreateDirectory(_work.Combine("sessions", "userB"));
        File.WriteAllText(_work.Combine("sessions", "userB", "token.txt"), "tokenB");
        _registry.SetStringValue(RegistryHive.CurrentUser, @"Software\AccSwapFake", "user", "userB");
        File.WriteAllText(
            _work.Combine("client.config.json"),
            """{ "Client": { "SavedAccountNames": "b@mail.com", "Other": 42 } }""");
    }

    private GenericPlatform CreatePlatform() =>
        new(MakeDefinition(), _store, _processes, _registry, _backup.Path);

    [Fact]
    public async Task Capture_SavesAccount_WithRawIdAsUsername()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();

        await platform.CaptureCurrentSessionAsync("principal");

        IReadOnlyList<Core.Account> accounts = await platform.ListAccountsAsync();
        Assert.Single(accounts);
        Assert.Equal("principal", accounts[0].DisplayLabel);
        Assert.Equal("a@mail.com", accounts[0].Username);
        Assert.True(accounts[0].IsActive);
    }

    [Fact]
    public async Task Capture_WithoutSession_Throws()
    {
        GenericPlatform platform = CreatePlatform();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => platform.CaptureCurrentSessionAsync("x"));
    }

    [Fact]
    public async Task Capture_LockedFile_ClosesLauncherAndCapturesIt()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();

        // The launcher holds an exclusive handle on a session file.
        await using FileStream lockHandle = new(
            _work.Combine("sessions", "userA", "token.txt"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);

        _processes.Running.Add("FakeLauncher");
        _processes.OnEnsureClosed = () => lockHandle.Dispose();

        Core.Account account = await platform.CaptureCurrentSessionAsync("principal");

        Assert.Contains("kill:FakeLauncher", _processes.Log);
        IReadOnlyDictionary<string, string> files = _store.GetAccountFileMap("fake", account.Id);
        Assert.Contains(files.Keys, k => k.EndsWith("token.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Capture_StillLockedFile_IsSkippedButCaptureSucceeds()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();

        // Nothing releases this handle: the file stays locked after the
        // close-and-retry pass and must be skipped, not crash the capture.
        await using FileStream lockHandle = new(
            _work.Combine("sessions", "userA", "token.txt"),
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            bufferSize: 1,
            FileOptions.None);

        Core.Account account = await platform.CaptureCurrentSessionAsync("principal");

        IReadOnlyDictionary<string, string> files = _store.GetAccountFileMap("fake", account.Id);
        Assert.Contains(files.Keys, k => k.EndsWith("settings.yml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(files.Keys, k => k.EndsWith("token.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Capture_SameUidTwice_DoesNotDuplicate()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();

        await platform.CaptureCurrentSessionAsync("uno");
        await platform.CaptureCurrentSessionAsync("uno");

        Assert.Single(await platform.ListAccountsAsync());
    }

    [Fact]
    public async Task Switch_RestoresAllTargetTypes_AndClearsStaleGlobFiles()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();
        Core.Account accountA = await platform.CaptureCurrentSessionAsync("principal");

        WriteSessionB();
        _processes.Running.Add("FakeLauncher");

        await platform.SwitchAsync(accountA.Id);

        Assert.Equal("email=a@mail.com\ntheme=dark", await File.ReadAllTextAsync(_work.Combine("settings.yml")));

        Assert.Equal("A1", await File.ReadAllTextAsync(_work.Combine("cache", "data_001")));
        Assert.Equal("A2", await File.ReadAllTextAsync(_work.Combine("cache", "data_002")));
        Assert.False(File.Exists(_work.Combine("cache", "data_003")));

        Assert.Equal("tokenA", await File.ReadAllTextAsync(_work.Combine("sessions", "userA", "token.txt")));
        Assert.False(File.Exists(_work.Combine("sessions", "userB", "token.txt")));

        Assert.Equal(
            "userA",
            _registry.GetStringValue(RegistryHive.CurrentUser, @"Software\AccSwapFake", "user"));

        string config = await File.ReadAllTextAsync(_work.Combine("client.config.json"));
        Assert.Contains("a@mail.com", config);
        Assert.DoesNotContain("b@mail.com", config);
        Assert.Contains("\"Other\": 42", config); // unknown JSON fields survive

        Assert.Contains("kill:FakeLauncher", _processes.Log);
        Assert.Contains($"start:{Path.GetFileName(_exePath)}:", _processes.Log);

        IReadOnlyList<Core.Account> accounts = await platform.ListAccountsAsync();
        Assert.True(accounts.Single().IsActive);
    }

    [Fact]
    public async Task Switch_WhenLaunchFails_RollsBackEverything()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();
        Core.Account accountA = await platform.CaptureCurrentSessionAsync("principal");

        WriteSessionB();
        _processes.StartBehavior = _ => throw new IOException("launch failed");

        await Assert.ThrowsAsync<IOException>(() => platform.SwitchAsync(accountA.Id));

        Assert.Equal("email=b@mail.com\ntheme=light", await File.ReadAllTextAsync(_work.Combine("settings.yml")));
        Assert.Equal(
            "userB",
            _registry.GetStringValue(RegistryHive.CurrentUser, @"Software\AccSwapFake", "user"));
        string config = await File.ReadAllTextAsync(_work.Combine("client.config.json"));
        Assert.Contains("b@mail.com", config);
    }

    [Fact]
    public async Task AddNewLogin_ClearsSession_AndOpensLauncher()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();
        _processes.Running.Add("FakeLauncher");

        await platform.AddNewLoginAsync();

        Assert.False(File.Exists(_work.Combine("settings.yml")));
        Assert.False(File.Exists(_work.Combine("cache", "data_001")));
        Assert.False(File.Exists(_work.Combine("sessions", "userA", "token.txt")));
        Assert.Null(_registry.GetStringValue(RegistryHive.CurrentUser, @"Software\AccSwapFake", "user"));
        string config = await File.ReadAllTextAsync(_work.Combine("client.config.json"));
        Assert.Contains("\"SavedAccountNames\": \"\"", config);
        Assert.Contains($"start:{Path.GetFileName(_exePath)}:", _processes.Log[1]);
    }

    [Fact]
    public async Task Logout_ClearsSession_WithoutLaunching()
    {
        WriteSessionA();
        GenericPlatform platform = CreatePlatform();

        await platform.LogoutAsync();

        Assert.False(File.Exists(_work.Combine("settings.yml")));
        Assert.DoesNotContain(_processes.Log, entry => entry.StartsWith("start:"));
    }

    [Fact]
    public async Task Switch_DirOnlyCandidate_InstallsButSkipsLaunch()
    {
        Directory.CreateDirectory(_work.Combine("versions"));
        var definition = new PlatformDefinition
        {
            Id = "fake",
            Name = "Fake",
            ExeCandidates = [_work.Combine("versions")],
            ProcessesToClose = ["FakeLauncher"],
            Targets = [new LoginTarget { Type = LoginTargetKind.File, Path = _work.Combine("settings.yml") }],
            UniqueId = new UniqueIdDefinition
            {
                Kind = UniqueIdKind.FileHash,
                Path = _work.Combine("settings.yml"),
            },
        };
        File.WriteAllText(_work.Combine("settings.yml"), "A");
        var platform = new GenericPlatform(definition, _store, _processes, _registry, _backup.Path);

        Assert.True(platform.IsInstalled);
        Core.Account account = await platform.CaptureCurrentSessionAsync();

        File.WriteAllText(_work.Combine("settings.yml"), "B");
        await platform.SwitchAsync(account.Id);

        Assert.Equal("A", await File.ReadAllTextAsync(_work.Combine("settings.yml")));
        Assert.DoesNotContain(_processes.Log, entry => entry.StartsWith("start:"));
    }

    public void Dispose() => _work.Dispose();
}

public class PlatformCatalogTests : IDisposable
{
    private readonly TempDir _work = new();

    [Fact]
    public void EmbeddedCatalog_LoadsAllPlatforms()
    {
        List<PlatformDefinition> platforms = PlatformCatalog.Load(_work.Path);

        Assert.Contains(platforms, p => p.Id == "riot");
        Assert.Contains(platforms, p => p.Id == "ubisoft");
        Assert.Contains(platforms, p => p.Id == "tarkov");
        Assert.Contains(platforms, p => p.Id == "battlenet");
        Assert.Contains(platforms, p => p.Id == "ea");
        Assert.Contains(platforms, p => p.Id == "gog");
        Assert.Contains(platforms, p => p.Id == "discord");
        Assert.Contains(platforms, p => p.Id == "roblox");
        Assert.All(platforms, p => Assert.False(string.IsNullOrEmpty(p.Name)));
    }

    [Fact]
    public void UserFile_OverridesById_AndAddsNew()
    {
        File.WriteAllText(
            Path.Combine(_work.Path, "platforms.json"),
            """
            [
              { "id": "riot", "name": "Riot Custom", "exeCandidates": ["C:\\a.exe"], "processesToClose": ["x"] },
              { "id": "custom", "name": "Custom", "exeCandidates": ["C:\\b.exe"], "processesToClose": ["y"] }
            ]
            """);

        List<PlatformDefinition> platforms = PlatformCatalog.Load(_work.Path);

        Assert.Equal("Riot Custom", platforms.Single(p => p.Id == "riot").Name);
        Assert.Contains(platforms, p => p.Id == "custom");
        Assert.Contains(platforms, p => p.Id == "ubisoft");
    }

    [Fact]
    public void BrokenUserFile_FallsBackToEmbedded()
    {
        File.WriteAllText(Path.Combine(_work.Path, "platforms.json"), "not json at all");

        List<PlatformDefinition> platforms = PlatformCatalog.Load(_work.Path);

        Assert.Contains(platforms, p => p.Id == "riot");
    }

    public void Dispose() => _work.Dispose();
}
