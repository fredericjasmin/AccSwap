using System.Text.Json;
using System.Text.Json.Serialization;
using AccSwap.Core.Files;
using AccSwap.Core.Security;

namespace AccSwap.Core.Snapshots;

public sealed class AccountMeta
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("platformId")]
    public required string PlatformId { get; set; }

    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("createdAtUtc")]
    public required DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Platform-specific identifiers (steamId, session hash...).</summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string> Data { get; set; } = [];

    public Account ToAccount() => new()
    {
        Id = Id,
        PlatformId = PlatformId,
        Username = Username,
        DisplayName = DisplayName,
        CreatedAtUtc = CreatedAtUtc,
        Data = Data,
    };
}

/// <summary>
/// Stores account snapshots under a root directory:
///
///   {root}/{platformId}/{accountId}/account.json
///   {root}/{platformId}/{accountId}/{file...}
///
/// Payload files are optionally encrypted at rest via an IFileProtector.
/// </summary>
public sealed class SnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _root;
    private readonly IFileProtector? _protector;

    public SnapshotStore(string root, IFileProtector? protector = null)
    {
        _root = root;
        _protector = protector;
        Directory.CreateDirectory(root);
    }

    public string Root => _root;

    public string CreateAccountId() => Guid.NewGuid().ToString("N");

    public async Task SaveAccountAsync(
        AccountMeta meta,
        IReadOnlyDictionary<string, string> files,
        CancellationToken ct = default)
    {
        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string relativeName, string sourcePath) in files)
        {
            bytes[relativeName] = await File.ReadAllBytesAsync(sourcePath, ct);
        }

        await SaveAccountBytesAsync(meta, bytes, ct);
    }

    public async Task SaveAccountBytesAsync(
        AccountMeta meta,
        IReadOnlyDictionary<string, byte[]> files,
        CancellationToken ct = default)
    {
        string accountDir = AccountDir(meta.PlatformId, meta.Id);
        Directory.CreateDirectory(accountDir);

        foreach ((string relativeName, byte[] content) in files)
        {
            await WriteAccountFileAsync(accountDir, relativeName, content, ct);
        }

        string metaPath = Path.Combine(accountDir, "account.json");
        string json = JsonSerializer.Serialize(meta, JsonOptions);
        await File.WriteAllTextAsync(metaPath, json, ct);
    }

    public Task WriteAccountFileAsync(
        string platformId,
        string accountId,
        string relativeName,
        byte[] content,
        CancellationToken ct = default) =>
        WriteAccountFileAsync(AccountDir(platformId, accountId), relativeName, content, ct);

    public async Task<IReadOnlyList<AccountMeta>> ListAccountsAsync(string platformId, CancellationToken ct = default)
    {
        var result = new List<AccountMeta>();
        string platformDir = Path.Combine(_root, platformId);
        if (!Directory.Exists(platformDir))
        {
            return result;
        }

        foreach (string dir in Directory.EnumerateDirectories(platformDir))
        {
            string metaPath = Path.Combine(dir, "account.json");
            if (!File.Exists(metaPath))
            {
                continue;
            }

            try
            {
                await using FileStream stream = File.OpenRead(metaPath);
                AccountMeta? meta = await JsonSerializer.DeserializeAsync<AccountMeta>(stream, JsonOptions, ct);
                if (meta is not null)
                {
                    result.Add(meta);
                }
            }
            catch (JsonException)
            {
                // Unreadable meta: skip rather than crash the account list.
            }
        }

        result.Sort((a, b) => string.Compare(a.Username, b.Username, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public async Task<AccountMeta> GetAccountAsync(string platformId, string accountId, CancellationToken ct = default)
    {
        string metaPath = Path.Combine(AccountDir(platformId, accountId), "account.json");
        if (!File.Exists(metaPath))
        {
            throw new KeyNotFoundException($"No saved account '{accountId}' for platform '{platformId}'.");
        }

        await using FileStream stream = File.OpenRead(metaPath);
        AccountMeta? meta = await JsonSerializer.DeserializeAsync<AccountMeta>(stream, JsonOptions, ct);
        return meta ?? throw new InvalidDataException($"Account metadata is empty for '{accountId}'.");
    }

    /// <summary>Relative name to absolute stored path mapping for an account.
    /// Names may include subdirectories (generic platforms store dir trees).</summary>
    public IReadOnlyDictionary<string, string> GetAccountFileMap(string platformId, string accountId)
    {
        string accountDir = AccountDir(platformId, accountId);
        if (!Directory.Exists(accountDir))
        {
            throw new KeyNotFoundException($"No saved account '{accountId}' for platform '{platformId}'.");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(accountDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(accountDir, file);
            if (!relative.Equals("account.json", StringComparison.OrdinalIgnoreCase))
            {
                map[relative] = file;
            }
        }

        return map;
    }

    public async Task<byte[]> ReadAccountFileAsync(
        string platformId,
        string accountId,
        string relativeName,
        CancellationToken ct = default)
    {
        string path = Path.Combine(AccountDir(platformId, accountId), relativeName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Snapshot file '{relativeName}' missing for account '{accountId}'.");
        }

        byte[] raw = await File.ReadAllBytesAsync(path, ct);
        return _protector is not null ? _protector.Unprotect(raw) : raw;
    }

    public Task RenameAsync(string platformId, string accountId, string newName, CancellationToken ct = default) =>
        MutateMetaAsync(platformId, accountId, meta => meta.DisplayName = newName, ct);

    public void DeleteAccount(string platformId, string accountId)
    {
        string dir = AccountDir(platformId, accountId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public async Task MutateMetaAsync(
        string platformId,
        string accountId,
        Action<AccountMeta> change,
        CancellationToken ct = default)
    {
        AccountMeta meta = await GetAccountAsync(platformId, accountId, ct);
        change(meta);
        string json = JsonSerializer.Serialize(meta, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(AccountDir(platformId, accountId), "account.json"), json, ct);
    }

    private async Task WriteAccountFileAsync(
        string accountDir,
        string relativeName,
        byte[] content,
        CancellationToken ct)
    {
        byte[] stored = _protector is not null ? _protector.Protect(content) : content;
        AtomicFile.WriteAllBytes(Path.Combine(accountDir, relativeName), stored);
        await Task.CompletedTask;
    }

    private string AccountDir(string platformId, string accountId) =>
        Path.Combine(_root, platformId, accountId);
}
