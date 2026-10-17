using System.IO.Compression;
using System.Text.Json;
using AccSwap.Core.Security;
using AccSwap.Core.Snapshots;

namespace AccSwap.Core.ExportImport;

/// <summary>
/// Packs every saved account (metadata + payload files) into a single
/// .glas file (a zip) for migrating to another PC. Entries are optionally
/// encrypted with a password, independent of the at-rest protection.
/// </summary>
public static class AccountPackage
{
    private const string AccountsPrefix = "accounts/";
    private const string MetaName = "account.json";

    /// <summary>How many accounts were added and how many were skipped.</summary>
    public sealed record ImportResult(int Added, int Skipped);

    public static async Task ExportAsync(
        SnapshotStore store,
        IReadOnlyList<string> platformIds,
        string zipPath,
        string? password,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        await using FileStream output = File.Create(zipPath);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);

        foreach (string platformId in platformIds)
        {
            foreach (AccountMeta meta in await store.ListAccountsAsync(platformId, ct))
            {
                string prefix = $"{AccountsPrefix}{platformId}/{meta.Id}/";
                await WriteEntryAsync(archive, prefix + MetaName, JsonSerializer.SerializeToUtf8Bytes(meta), password, ct);

                foreach ((string relativeName, _) in store.GetAccountFileMap(platformId, meta.Id))
                {
                    // ReadAccountFileAsync returns decrypted content even when
                    // snapshots are protected at rest; the export password
                    // decides whether the package itself is encrypted.
                    byte[] content = await store.ReadAccountFileAsync(platformId, meta.Id, relativeName, ct);
                    await WriteEntryAsync(archive, prefix + relativeName, content, password, ct);
                }
            }
        }
    }

    /// <summary>
    /// Adds the package's accounts to the store. Existing accounts are never
    /// modified or deleted: an account with an id already present locally is
    /// skipped, so importing an old backup never rolls accounts back.
    /// </summary>
    public static async Task<ImportResult> ImportAsync(
        SnapshotStore store,
        string zipPath,
        string? password,
        CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("Package file not found.", zipPath);
        }

        // Lazily-loaded id sets per platform: existing accounts stay intact.
        var knownIds = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        int added = 0;
        int skipped = 0;
        using var archive = new ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(AccountsPrefix, StringComparison.OrdinalIgnoreCase)
                || !entry.FullName.EndsWith(MetaName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] metaBytes = await ReadEntryAsync(entry, password, ct);
            AccountMeta? meta = JsonSerializer.Deserialize<AccountMeta>(metaBytes);
            if (meta is null)
            {
                continue;
            }

            if (!knownIds.TryGetValue(meta.PlatformId, out HashSet<string>? ids))
            {
                ids = (await store.ListAccountsAsync(meta.PlatformId, ct))
                    .Select(m => m.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                knownIds[meta.PlatformId] = ids;
            }

            if (!ids.Add(meta.Id))
            {
                // Already present locally (or earlier in this same package).
                skipped++;
                continue;
            }

            string accountPrefix = Path.GetDirectoryName(entry.FullName)!.Replace('\\', '/') + "/";
            var files = new Dictionary<string, byte[]>();
            foreach (ZipArchiveEntry fileEntry in archive.Entries)
            {
                if (!fileEntry.FullName.StartsWith(accountPrefix, StringComparison.OrdinalIgnoreCase)
                    || fileEntry.FullName.EndsWith(MetaName, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(fileEntry.Name))
                {
                    continue;
                }

                // Subdirectories are allowed: generic platforms store dir trees.
                string relative = fileEntry.FullName[accountPrefix.Length..];
                files[relative] = await ReadEntryAsync(fileEntry, password, ct);
            }

            await store.SaveAccountBytesAsync(meta, files, ct);
            added++;
        }

        return new ImportResult(added, skipped);
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string entryName,
        byte[] content,
        string? password,
        CancellationToken ct)
    {
        byte[] stored = password is not null ? SnapshotEncryption.Encrypt(content, password) : content;
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await stream.WriteAsync(stored, ct);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, string? password, CancellationToken ct)
    {
        await using Stream stream = entry.Open();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        byte[] raw = memory.ToArray();
        return password is not null ? SnapshotEncryption.Decrypt(raw, password) : raw;
    }
}
