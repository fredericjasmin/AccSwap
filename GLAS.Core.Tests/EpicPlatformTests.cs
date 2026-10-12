using GLAS.Core.Epic;
using GLAS.Core.Steam;
using GLAS.Core.Snapshots;
using Xunit;

namespace GLAS.Core.Tests;

public class EpicPlatformTests : IDisposable
{
    private readonly TempDir _work = new();
    private readonly TempDir _backup = new();
    private readonly FakeProcessController _processes = new();
    private readonly EpicPaths _paths;
    private readonly SnapshotStore _store;

    public EpicPlatformTests()
    {
        _paths = new EpicPaths
        {
            LauncherExePath = _work.Combine("EpicGamesLauncher.exe"),
            SessionIniPath = _work.Combine("GameUserSettings.ini"),
        };
        File.WriteAllText(_paths.LauncherExePath!, "fake exe");
        _store = new SnapshotStore(_work.Combine("store"));
    }

    [Fact]
    public async Task Capture_SavesAccount_AndListDetectsItActive()
    {
        WriteLiveIni(Samples.EpicIni("tokenAAA111"));
        EpicPlatform platform = CreatePlatform();

        await platform.CaptureCurrentSessionAsync("Cuenta principal");

        IReadOnlyList<Account> accounts = await platform.ListAccountsAsync();
        Assert.Single(accounts);
        Assert.Equal("Cuenta principal", accounts[0].DisplayLabel);
        Assert.True(accounts[0].IsActive);
    }

    [Fact]
    public async Task Capture_WithoutSession_Throws()
    {
        EpicPlatform platform = CreatePlatform();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => platform.CaptureCurrentSessionAsync("x"));
    }

    [Fact]
    public async Task Capture_SameSessionTwice_UpdatesInsteadOfDuplicating()
    {
        WriteLiveIni(Samples.EpicIni("tokenAAA111"));
        EpicPlatform platform = CreatePlatform();

        await platform.CaptureCurrentSessionAsync("uno");
        await platform.CaptureCurrentSessionAsync("uno renombrado");

        Assert.Single(await platform.ListAccountsAsync());
    }

    [Fact]
    public async Task ListAccounts_ActiveSurvivesDataTokenRotation()
    {
        const string guidA = "b7e74919ba4444309a5ef489321029dd";
        WriteLiveIni(Samples.EpicIni("tokenAAA111", guidA));
        EpicPlatform platform = CreatePlatform();
        await platform.CaptureCurrentSessionAsync("principal");

        // Epic re-encrypts the RememberMe token on every launch; the account
        // GUID sections stay the same.
        WriteLiveIni(Samples.EpicIni("tokenROTATED999", guidA).Replace("WindowPos=100,100", "WindowPos=200,200"));

        Account account = (await platform.ListAccountsAsync()).Single();
        Assert.True(account.IsActive);
    }

    [Fact]
    public async Task ListAccounts_TwoAccounts_DetectsActiveBySectionGuids()
    {
        const string guidA = "b7e74919ba4444309a5ef489321029dd";
        const string guidB = "7c2c913866fd455ea259b9f426417ecf";
        EpicPlatform platform = CreatePlatform();

        WriteLiveIni(Samples.EpicIni("tokenAAA111", guidA));
        await platform.CaptureCurrentSessionAsync("principal");

        WriteLiveIni(Samples.EpicIni("tokenBBB222", guidA, guidB));
        await platform.CaptureCurrentSessionAsync("secundaria");

        // Switched back: rotated token, only principal's GUID sections.
        WriteLiveIni(Samples.EpicIni("tokenROTATED999", guidA));

        IReadOnlyList<Account> accounts = await platform.ListAccountsAsync();
        Assert.Equal(2, accounts.Count);
        Assert.True(accounts.Single(a => a.DisplayLabel == "principal").IsActive);
        Assert.False(accounts.Single(a => a.DisplayLabel == "secundaria").IsActive);
    }

    [Fact]
    public async Task Capture_AfterTokenRotation_UpdatesInsteadOfDuplicating()
    {
        const string guidA = "b7e74919ba4444309a5ef489321029dd";
        WriteLiveIni(Samples.EpicIni("tokenAAA111", guidA));
        EpicPlatform platform = CreatePlatform();
        await platform.CaptureCurrentSessionAsync("principal");

        WriteLiveIni(Samples.EpicIni("tokenROTATED999", guidA));

        await platform.CaptureCurrentSessionAsync("principal actualizada");

        Assert.Single(await platform.ListAccountsAsync());
    }

    [Fact]
    public async Task Switch_RestoresSnapshot_KillsAndRelaunches()
    {
        WriteLiveIni(Samples.EpicIni("tokenAAA111"));
        EpicPlatform platform = CreatePlatform();
        Account first = await platform.CaptureCurrentSessionAsync("primera");

        WriteLiveIni(Samples.EpicIni("tokenBBB222"));
        _processes.Running.Add("EpicGamesLauncher");

        await platform.SwitchAsync(first.Id);

        string live = await File.ReadAllTextAsync(_paths.SessionIniPath!);
        Assert.Contains("tokenAAA111", live);
        Assert.DoesNotContain("tokenBBB222", live);
        Assert.Contains("kill:EpicGamesLauncher", _processes.Log);
        Assert.Contains("start:EpicGamesLauncher.exe:", _processes.Log[1]);

        IReadOnlyList<Account> accounts = await platform.ListAccountsAsync();
        Assert.True(accounts.Single(a => a.Id == first.Id).IsActive);
    }

    [Fact]
    public async Task Switch_WhenLauncherFailsToStart_RollsBackLiveIni()
    {
        WriteLiveIni(Samples.EpicIni("tokenAAA111"));
        EpicPlatform platform = CreatePlatform();
        Account first = await platform.CaptureCurrentSessionAsync("primera");

        WriteLiveIni(Samples.EpicIni("tokenBBB222"));
        _processes.StartBehavior = _ => throw new IOException("launch failed");

        await Assert.ThrowsAsync<IOException>(() => platform.SwitchAsync(first.Id));

        string live = await File.ReadAllTextAsync(_paths.SessionIniPath!);
        Assert.Contains("tokenBBB222", live);
        Assert.DoesNotContain("tokenAAA111", live);
    }

    [Fact]
    public async Task AddNewLogin_DeletesSessionAndOpensLauncher()
    {
        WriteLiveIni(Samples.EpicIni("tokenAAA111"));
        EpicPlatform platform = CreatePlatform();
        _processes.Running.Add("EpicGamesLauncher");

        await platform.AddNewLoginAsync();

        Assert.False(File.Exists(_paths.SessionIniPath!));
        Assert.Equal(2, _processes.Log.Count);
        Assert.StartsWith("kill:", _processes.Log[0]);
        Assert.StartsWith("start:EpicGamesLauncher.exe", _processes.Log[1]);
    }

    [Fact]
    public async Task ListAccounts_SettingsChangedInIni_StillDetectsActiveAccount()
    {
        WriteLiveIni(Samples.EpicIni("tokenAAA111"));
        EpicPlatform platform = CreatePlatform();
        Account first = await platform.CaptureCurrentSessionAsync("primera");

        // Epic rewrites unrelated settings; the session token stays.
        await File.WriteAllTextAsync(_paths.SessionIniPath!, Samples.EpicIni("tokenAAA111").Replace("WindowPos=100,100", "WindowPos=200,200"));

        IReadOnlyList<Account> accounts = await platform.ListAccountsAsync();
        Assert.True(accounts.Single(a => a.Id == first.Id).IsActive);
    }

    private EpicPlatform CreatePlatform() =>
        new(_store, _paths, _processes, _backup.Path);

    private void WriteLiveIni(string content) =>
        File.WriteAllText(_paths.SessionIniPath!, content);

    public void Dispose() => _work.Dispose();
}
