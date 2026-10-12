using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GLAS.Core.Platforms;
using GLAS.Core.Processes;
using GLAS.Core.Snapshots;

namespace GLAS.Core.Epic;

/// <summary>
/// Epic Games Launcher account switching:
///   1. Close EpicGamesLauncher.exe.
///   2. Replace GameUserSettings.ini with the saved account copy
///      (the login session lives in its big Data= token).
///   3. Relaunch the launcher.
///
/// The active account is detected by hashing the Data= token of the live
/// ini and comparing it with the hash stored per account, so Epic can
/// rewrite unrelated settings without breaking detection.
/// </summary>
public sealed partial class EpicPlatform : IPlatform
{
    private const string SessionFile = "GameUserSettings.ini";
    private const string DataKey = "dataHash";
    private const string WholeFileKey = "wholeFileHash";
    private static readonly string[] ProcessesToClose = ["EpicGamesLauncher"];

    [GeneratedRegex(@"(?m)^Data=(?<data>\S.*)\r?$")]
    private static partial Regex DataRegex();

    // Epic re-encrypts the RememberMe Data token on every launch, so token
    // hashes never survive a launcher restart. The per-account GUID section
    // names ([<32hex>_General] etc.) are stable and identify the remembered
    // account even after the token rotates.
    [GeneratedRegex(@"(?mi)^\[([0-9a-f]{32})_")]
    private static partial Regex SectionIdRegex();

    private readonly SnapshotStore _store;
    private readonly EpicPaths _paths;
    private readonly IProcessController _processes;
    private readonly string _backupRoot;

    public event Action<string>? StatusChanged;

    public EpicPlatform(SnapshotStore store, EpicPaths paths, IProcessController processes, string backupRoot)
    {
        _store = store;
        _paths = paths;
        _processes = processes;
        _backupRoot = backupRoot;
    }

    public string Id => "epic";

    public string Name => "Epic Games";

    public bool IsInstalled =>
        _paths.LauncherExePath is not null && File.Exists(_paths.LauncherExePath);

    public string? DetectedPath => IsInstalled ? _paths.LauncherExePath : null;

    private string? SessionIniPath => _paths.SessionIniPath;

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AccountMeta> metas = await _store.ListAccountsAsync(Id, ct);
        byte[]? liveIni = await TryReadLiveIniAsync(ct);
        (string? dataHash, string? wholeHash) = HashLiveSession(liveIni);
        string? liveSectionIds = liveIni is null ? null : ExtractSectionIds(Encoding.UTF8.GetString(liveIni));

        var result = new List<Account>();
        foreach (AccountMeta meta in metas)
        {
            bool isActive = meta.Data.TryGetValue(DataKey, out string? storedDataHash) && storedDataHash == dataHash;
            if (!isActive && liveIni is not null)
            {
                isActive = meta.Data.TryGetValue(WholeFileKey, out string? storedWhole)
                    && storedWhole == wholeHash;
            }

            // Token-rotation tolerant match: the account whose GUID sections
            // equal the live ones is the remembered account.
            if (!isActive && liveSectionIds is { Length: > 0 })
            {
                string? storedIds = await TryGetStoredSectionIdsAsync(meta, ct);
                isActive = storedIds == liveSectionIds;
            }

            result.Add(new Account
            {
                PlatformId = Id,
                Id = meta.Id,
                Username = meta.Username,
                DisplayName = meta.DisplayName,
                CreatedAtUtc = meta.CreatedAtUtc,
                Data = meta.Data,
                IsActive = isActive,
            });
        }

        return result;
    }

    public async Task<Account> CaptureCurrentSessionAsync(string? displayName = null, CancellationToken ct = default)
    {
        byte[] liveIni = await TryReadLiveIniAsync(ct)
            ?? throw new InvalidOperationException(
                "No Epic session found. Log into the Epic Games Launcher first, then capture.");

        (string? dataHash, string? wholeHash) = HashLiveSession(liveIni);
        string liveSectionIds = ExtractSectionIds(Encoding.UTF8.GetString(liveIni));

        // Re-capturing an already saved account (same session token, or the
        // same account GUIDs after Epic rotated the token) updates it.
        AccountMeta? existing = null;
        foreach (AccountMeta candidate in await _store.ListAccountsAsync(Id, ct))
        {
            if (candidate.Data.GetValueOrDefault(DataKey) == dataHash
                || (liveSectionIds.Length > 0 && await TryGetStoredSectionIdsAsync(candidate, ct) == liveSectionIds))
            {
                existing = candidate;
                break;
            }
        }

        string accountId = existing?.Id ?? _store.CreateAccountId();
        var meta = new AccountMeta
        {
            Id = accountId,
            PlatformId = Id,
            Username = displayName ?? existing?.Username ?? $"Epic-{accountId[..8]}",
            DisplayName = displayName ?? existing?.DisplayName,
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Data = new Dictionary<string, string>(existing?.Data ?? new Dictionary<string, string>())
            {
                [DataKey] = dataHash ?? "",
                [WholeFileKey] = wholeHash ?? "",
            },
        };

        Report("Saving account...");
        string tempCopy = Path.Combine(Path.GetTempPath(), $"glas_epic_{accountId}.ini");
        await File.WriteAllBytesAsync(tempCopy, liveIni, ct);
        try
        {
            await _store.SaveAccountAsync(meta, new Dictionary<string, string> { [SessionFile] = tempCopy }, ct);
        }
        finally
        {
            GLAS.Core.Files.AtomicFile.TryDelete(tempCopy);
        }

        return meta.ToAccount();
    }

    public async Task SwitchAsync(string accountId, CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException("Epic Games Launcher installation not found.");
        }

        byte[] target = await _store.ReadAccountFileAsync(Id, accountId, SessionFile, ct);

        Report("Closing Epic Games Launcher...");
        await _processes.EnsureClosedAsync(ProcessesToClose, ct);

        await using (var tx = new SwitchTransaction(Path.Combine(_backupRoot, $"epic_{Guid.NewGuid():N}"), NullRegistry.Instance))
        {
            tx.StageFileWrite(SessionIniPath!, target);
            Report("Restoring account...");
            await tx.CommitAsync(ct);

            Report("Starting Epic Games Launcher...");
            await _processes.StartAsync(_paths.LauncherExePath!, ct: ct);
            tx.Complete();
        }
    }

    public async Task AddNewLoginAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException("Epic Games Launcher installation not found.");
        }

        Report("Closing Epic Games Launcher...");
        await _processes.EnsureClosedAsync(ProcessesToClose, ct);

        await using (var tx = new SwitchTransaction(Path.Combine(_backupRoot, $"epic_{Guid.NewGuid():N}"), NullRegistry.Instance))
        {
            if (SessionIniPath is not null && File.Exists(SessionIniPath))
            {
                tx.StageFileDelete(SessionIniPath);
            }

            Report("Opening Epic login...");
            await tx.CommitAsync(ct);
            await _processes.StartAsync(_paths.LauncherExePath!, ct: ct);
            tx.Complete();
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        Report("Closing Epic Games Launcher...");
        await _processes.EnsureClosedAsync(ProcessesToClose, ct);

        await using var tx = new SwitchTransaction(Path.Combine(_backupRoot, $"epic_{Guid.NewGuid():N}"), NullRegistry.Instance);
        if (SessionIniPath is not null && File.Exists(SessionIniPath))
        {
            tx.StageFileDelete(SessionIniPath);
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

    private async Task<byte[]?> TryReadLiveIniAsync(CancellationToken ct) =>
        SessionIniPath is not null && File.Exists(SessionIniPath)
            ? await File.ReadAllBytesAsync(SessionIniPath, ct)
            : null;

    private static (string? DataHash, string? WholeHash) HashLiveSession(byte[]? liveIni)
    {
        if (liveIni is null)
        {
            return (null, null);
        }

        string text = Encoding.UTF8.GetString(liveIni);
        Match match = DataRegex().Match(text);
        string? dataHash = match.Success
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups["data"].Value)))
            : null;
        string wholeHash = Convert.ToHexString(SHA256.HashData(liveIni));
        return (dataHash, wholeHash);
    }

    /// <summary>Sorted, comma-joined account GUIDs from the ini's section names.</summary>
    private static string ExtractSectionIds(string iniText) =>
        string.Join(",", SectionIdRegex().Matches(iniText)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .Order());

    /// <summary>Account GUIDs of a stored snapshot (read from its saved ini).</summary>
    private async Task<string?> TryGetStoredSectionIdsAsync(AccountMeta meta, CancellationToken ct)
    {
        try
        {
            byte[] stored = await _store.ReadAccountFileAsync(Id, meta.Id, SessionFile, ct);
            return ExtractSectionIds(Encoding.UTF8.GetString(stored));
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private void Report(string message) => StatusChanged?.Invoke(message);

    /// <summary>Epic needs no registry access; this keeps SwitchTransaction's shape uniform.</summary>
    private sealed class NullRegistry : RegistryAccess.IRegistryService
    {
        public static readonly NullRegistry Instance = new();

        public string? GetStringValue(RegistryAccess.RegistryHive hive, string keyPath, string valueName) => null;

        public string[] GetSubKeyNames(RegistryAccess.RegistryHive hive, string keyPath) => [];

        public bool SetStringValue(RegistryAccess.RegistryHive hive, string keyPath, string valueName, string value) => false;

        public bool DeleteValue(RegistryAccess.RegistryHive hive, string keyPath, string valueName) => false;
    }
}
