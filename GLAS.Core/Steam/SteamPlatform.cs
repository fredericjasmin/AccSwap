using System.Text;
using GLAS.Core.Files;
using GLAS.Core.Platforms;
using GLAS.Core.Processes;
using GLAS.Core.RegistryAccess;
using GLAS.Core.Snapshots;

namespace GLAS.Core.Steam;

/// <summary>
/// Steam account switching:
///   1. Close steam.exe (and its steamwebhelper / overlay children).
///   2. Restore the saved loginusers.vdf with the target account marked
///      MostRecent=1 / RememberPassword=1.
///   3. Point HKCU\Software\Valve\Steam at the account (AutoLoginUser).
///   4. Relaunch steam.exe.
///
/// Account identity is the SteamID64, so re-capturing an existing account
/// updates it instead of duplicating it.
/// </summary>
public sealed class SteamPlatform : IPlatform
{
    private const string LoginUsersFileName = "loginusers.vdf";
    private static readonly string[] ProcessesToClose = ["steam", "steamwebhelper", "GameOverlayUI"];

    private readonly SnapshotStore _store;
    private readonly SteamPaths _paths;
    private readonly IProcessController _processes;
    private readonly IRegistryService _registry;
    private readonly string _backupRoot;
    private readonly string _launchArguments;

    public event Action<string>? StatusChanged;

    public SteamPlatform(
        SnapshotStore store,
        SteamPaths paths,
        IProcessController processes,
        IRegistryService registry,
        string backupRoot,
        string launchArguments = "")
    {
        _store = store;
        _paths = paths;
        _processes = processes;
        _registry = registry;
        _backupRoot = backupRoot;
        _launchArguments = launchArguments;
    }

    public string Id => "steam";

    public string Name => "Steam";

    public bool IsInstalled =>
        _paths.LauncherExePath is not null && File.Exists(_paths.LauncherExePath);

    public string? DetectedPath => IsInstalled ? _paths.LauncherExePath : null;

    private string? LoginUsersPath => _paths.LoginUsersPath;

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AccountMeta> metas = await _store.ListAccountsAsync(Id, ct);
        string? activeSteamId = await TryGetActiveSteamIdAsync(ct);

        return metas.Select(m => new Account
        {
            PlatformId = Id,
            Id = m.Id,
            Username = m.Username,
            DisplayName = m.DisplayName,
            CreatedAtUtc = m.CreatedAtUtc,
            Data = m.Data,
            IsActive = string.Equals(m.Id, activeSteamId, StringComparison.Ordinal),
        }).ToList();
    }

    public async Task<Account> CaptureCurrentSessionAsync(string? displayName = null, CancellationToken ct = default)
    {
        if (LoginUsersPath is null || !File.Exists(LoginUsersPath))
        {
            throw new InvalidOperationException("Steam is not installed or there is no active session to capture.");
        }

        Report("Reading loginusers.vdf...");
        LoginUsersFile loginUsers = LoginUsersFile.Parse(await File.ReadAllTextAsync(LoginUsersPath, ct));
        SteamUserEntry? current = loginUsers.ActiveUser()
            ?? throw new InvalidOperationException(
                "No account with an active session was found. Log into Steam first, then capture.");

        var meta = new AccountMeta
        {
            Id = current.SteamId,
            PlatformId = Id,
            Username = current.AccountName ?? current.SteamId,
            DisplayName = displayName ?? current.PersonaName ?? current.AccountName,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Data = new Dictionary<string, string>
            {
                ["steamId"] = current.SteamId,
                ["accountName"] = current.AccountName ?? "",
                ["personaName"] = current.PersonaName ?? "",
            },
        };

        Report("Saving account...");
        await _store.SaveAccountAsync(meta, new Dictionary<string, string> { [LoginUsersFileName] = LoginUsersPath }, ct);
        return meta.ToAccount();
    }

    public async Task SwitchAsync(string accountId, CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException("Steam installation not found.");
        }

        AccountMeta meta = await _store.GetAccountAsync(Id, accountId, ct);
        byte[] stored = await _store.ReadAccountFileAsync(Id, accountId, LoginUsersFileName, ct);
        LoginUsersFile loginUsers = LoginUsersFile.Parse(Encoding.UTF8.GetString(stored));
        loginUsers.SetActive(accountId);
        byte[] targetContent = Encoding.UTF8.GetBytes(loginUsers.ToVdfText());

        Report("Closing Steam...");
        await _processes.EnsureClosedAsync(ProcessesToClose, ct);

        Report("Restoring account...");
        await using (var tx = new SwitchTransaction(Path.Combine(_backupRoot, $"steam_{Guid.NewGuid():N}"), _registry))
        {
            tx.StageFileWrite(LoginUsersPath!, targetContent);
            tx.StageRegistryValue(
                RegistryHive.CurrentUser,
                SteamPaths.RegistrySteamKey,
                "AutoLoginUser",
                meta.Data.GetValueOrDefault("accountName", meta.Username));
            tx.StageRegistryValue(
                RegistryHive.CurrentUser,
                SteamPaths.RegistrySteamKey,
                "RememberPassword",
                "1");
            await tx.CommitAsync(ct);

            Report("Starting Steam...");
            await _processes.StartAsync(_paths.LauncherExePath!, _launchArguments, ct);
            tx.Complete();
        }
    }

    public async Task AddNewLoginAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException("Steam installation not found.");
        }

        Report("Closing Steam...");
        await _processes.EnsureClosedAsync(ProcessesToClose, ct);

        await using (var tx = new SwitchTransaction(Path.Combine(_backupRoot, $"steam_{Guid.NewGuid():N}"), _registry))
        {
            tx.StageRegistryDelete(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "AutoLoginUser");
            if (LoginUsersPath is not null && File.Exists(LoginUsersPath))
            {
                LoginUsersFile loginUsers = LoginUsersFile.Parse(await File.ReadAllTextAsync(LoginUsersPath, ct));
                loginUsers.ClearActive();
                tx.StageFileWrite(LoginUsersPath, Encoding.UTF8.GetBytes(loginUsers.ToVdfText()));
            }

            await tx.CommitAsync(ct);

            Report("Opening Steam login...");
            await _processes.StartAsync(_paths.LauncherExePath!, _launchArguments, ct);
            tx.Complete();
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        Report("Closing Steam...");
        await _processes.EnsureClosedAsync(ProcessesToClose, ct);

        await using var tx = new SwitchTransaction(Path.Combine(_backupRoot, $"steam_{Guid.NewGuid():N}"), _registry);
        tx.StageRegistryDelete(RegistryHive.CurrentUser, SteamPaths.RegistrySteamKey, "AutoLoginUser");
        if (LoginUsersPath is not null && File.Exists(LoginUsersPath))
        {
            LoginUsersFile loginUsers = LoginUsersFile.Parse(await File.ReadAllTextAsync(LoginUsersPath, ct));
            loginUsers.ClearActive();
            tx.StageFileWrite(LoginUsersPath, Encoding.UTF8.GetBytes(loginUsers.ToVdfText()));
        }

        Report("Ending session...");
        await tx.CommitAsync(ct);
        tx.Complete();
    }

    public Task DeleteAccountAsync(string accountId, CancellationToken ct = default)
    {
        _store.DeleteAccount(Id, accountId);
        return Task.CompletedTask;
    }

    public async Task RenameAccountAsync(string accountId, string newName, CancellationToken ct = default)
    {
        await _store.MutateMetaAsync(Id, accountId, meta => meta.DisplayName = newName, ct);
    }

    private async Task<string?> TryGetActiveSteamIdAsync(CancellationToken ct)
    {
        if (LoginUsersPath is null || !File.Exists(LoginUsersPath))
        {
            return null;
        }

        try
        {
            LoginUsersFile loginUsers = LoginUsersFile.Parse(await File.ReadAllTextAsync(LoginUsersPath, ct));
            return loginUsers.ActiveUser()?.SteamId;
        }
        catch (Vdf.VdfFormatException)
        {
            // A loginusers.vdf we cannot parse must not break the account list.
            return null;
        }
    }

    private void Report(string message) => StatusChanged?.Invoke(message);
}
