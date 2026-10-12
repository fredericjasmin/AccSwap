using GLAS.Core.RegistryAccess;
using GLAS.Core.Steam;
using GLAS.Core.Snapshots;
using Xunit;

namespace GLAS.Core.Tests;

public class SteamPlatformTests : IDisposable
{
    private readonly TempDir _work = new();
    private readonly TempDir _backup = new();
    private readonly FakeProcessController _processes = new();
    private readonly InMemoryRegistryService _registry = new();
    private readonly SteamPaths _paths;
    private readonly SnapshotStore _store;

    public SteamPlatformTests()
    {
        string steamRoot = _work.Combine("Steam");
        Directory.CreateDirectory(steamRoot);
        _paths = new SteamPaths
        {
            SteamRoot = steamRoot,
            LauncherExePath = Path.Combine(steamRoot, "steam.exe"),
            LoginUsersPath = Path.Combine(steamRoot, "config", "loginusers.vdf"),
        };
        File.WriteAllText(_paths.LauncherExePath!, "fake exe");
        _store = new SnapshotStore(_work.Combine("store"));
        _registry.SetStringValue(
            RegistryHive.CurrentUser,
            SteamPaths.RegistrySteamKey,
            "AutoLoginUser",
            "userA");
    }

    [Fact]
    public async Task Capture_SavesAccountWithSteamIdAsIdentity()
    {
        WriteLiveVdf(Samples.LoginUsersVdf());
        SteamPlatform platform = CreatePlatform();

        Account account = await platform.CaptureCurrentSessionAsync();

        Assert.Equal(Samples.SteamIdA, account.Id);
        Assert.Equal("userA", account.Username);
        Assert.True((await platform.ListAccountsAsync()).Single().IsActive);
    }

    [Fact]
    public async Task Capture_WithoutMostRecentField_UsesAutoLoginFallback()
    {
        // Real-world case: Steam omits MostRecent for single-account installs.
        WriteLiveVdf(Samples.SingleUserLoginUsersVdf());
        SteamPlatform platform = CreatePlatform();

        Account account = await platform.CaptureCurrentSessionAsync();

        Assert.Equal(Samples.SteamIdA, account.Id);
        Assert.Equal("userA", account.Username);
        Assert.True((await platform.ListAccountsAsync()).Single().IsActive);
    }

    [Fact]
    public async Task Capture_SameUserTwice_DoesNotDuplicate()
    {
        WriteLiveVdf(Samples.LoginUsersVdf());
        SteamPlatform platform = CreatePlatform();

        await platform.CaptureCurrentSessionAsync();
        await platform.CaptureCurrentSessionAsync();

        Assert.Single(await platform.ListAccountsAsync());
    }

    [Fact]
    public async Task Switch_WritesLoginUsers_SetsRegistry_Relaunches()
    {
        WriteLiveVdf(Samples.LoginUsersVdf());
        SteamPlatform platform = CreatePlatform();
        Account accountA = await platform.CaptureCurrentSessionAsync("principal");
        await platform.RenameAccountAsync(accountA.Id, "principal");

        // Second user logs in with Steam directly.
        WriteLiveVdf(Samples.LoginUsersVdf(mostRecent: Samples.SteamIdB));
        _processes.Running.Add("steam");
        _processes.Running.Add("steamwebhelper");

        await platform.SwitchAsync(accountA.Id);

        string live = await File.ReadAllTextAsync(_paths.LoginUsersPath!);
        LoginUsersFile parsed = LoginUsersFile.Parse(live);
        Assert.Equal(Samples.SteamIdA, parsed.MostRecentUser()!.SteamId);
        Assert.Contains("kill:steam", _processes.Log);
        Assert.Contains("kill:steamwebhelper", _processes.Log);
        Assert.Contains("start:steam.exe:-silent", _processes.Log);

        Assert.Equal(
            "userA",
            _registry.GetStringValue(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "AutoLoginUser"));
        Assert.Equal(
            "1",
            _registry.GetStringValue(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "RememberPassword"));

        IReadOnlyList<Account> accounts = await platform.ListAccountsAsync();
        Assert.True(accounts.Single(a => a.Id == Samples.SteamIdA).IsActive);
    }

    [Fact]
    public async Task Switch_WhenLaunchFails_RollsBackFilesAndRegistry()
    {
        WriteLiveVdf(Samples.LoginUsersVdf());
        SteamPlatform platform = CreatePlatform();
        Account accountA = await platform.CaptureCurrentSessionAsync();

        WriteLiveVdf(Samples.LoginUsersVdf(mostRecent: Samples.SteamIdB));
        _processes.StartBehavior = _ => throw new IOException("launch failed");

        await Assert.ThrowsAsync<IOException>(() => platform.SwitchAsync(accountA.Id));

        string live = await File.ReadAllTextAsync(_paths.LoginUsersPath!);
        Assert.Equal(Samples.SteamIdB, LoginUsersFile.Parse(live).MostRecentUser()!.SteamId);
        Assert.Equal(
            "userA",
            _registry.GetStringValue(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "AutoLoginUser"));
    }

    [Fact]
    public async Task AddNewLogin_ClearsAutoLoginAndOpensSteam()
    {
        WriteLiveVdf(Samples.LoginUsersVdf());
        SteamPlatform platform = CreatePlatform();
        _processes.Running.Add("steam");

        await platform.AddNewLoginAsync();

        Assert.Null(
            _registry.GetStringValue(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "AutoLoginUser"));
        string live = await File.ReadAllTextAsync(_paths.LoginUsersPath!);
        Assert.All(LoginUsersFile.Parse(live).Users(), u => Assert.False(u.IsMostRecent));
        Assert.Contains("start:steam.exe:-silent", _processes.Log);
    }

    [Fact]
    public async Task Logout_ClearsAutoLogin_WithoutRelaunching()
    {
        WriteLiveVdf(Samples.LoginUsersVdf());
        SteamPlatform platform = CreatePlatform();

        await platform.LogoutAsync();

        Assert.Null(
            _registry.GetStringValue(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "AutoLoginUser"));
        Assert.DoesNotContain(_processes.Log, entry => entry.StartsWith("start:"));
    }

    [Fact]
    public async Task Capture_WithoutActiveSession_Throws()
    {
        WriteLiveVdf(Samples.LoginUsersVdf(mostRecent: "none"));
        SteamPlatform platform = CreatePlatform();

        // A single remembered user without flags is still the active session
        // (Steam omits MostRecent for single-account installs); only an empty
        // users node means nobody is logged in.
        await File.WriteAllTextAsync(
            _paths.LoginUsersPath!,
            "\"users\"\n{\n}\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() => platform.CaptureCurrentSessionAsync());
    }

    private SteamPlatform CreatePlatform() =>
        new(_store, _paths, _processes, _registry, _backup.Path, launchArguments: "-silent");

    private void WriteLiveVdf(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.LoginUsersPath!)!);
        File.WriteAllText(_paths.LoginUsersPath!, content);
    }

    public void Dispose() => _work.Dispose();
}
