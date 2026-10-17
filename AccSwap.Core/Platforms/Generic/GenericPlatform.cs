using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AccSwap.Core.Platforms;
using AccSwap.Core.Processes;
using AccSwap.Core.RegistryAccess;
using AccSwap.Core.Snapshots;

namespace AccSwap.Core.Platforms.Generic;

/// <summary>
/// Data-driven platform implementation. Every operation (capture, switch,
/// add-new-login, logout) is derived from a <see cref="PlatformDefinition"/>:
/// a list of session targets (files, globs, directory trees, registry
/// values, JSON fields) plus a unique-id rule to tell accounts apart.
///
/// Target snapshots taken at capture time are stored in account metadata as
/// a JSON list, so a switch restores exactly what was captured even if glob
/// expansions change. Every write goes through SwitchTransaction: a failure
/// mid-switch restores files and registry to their previous state.
/// </summary>
public sealed partial class GenericPlatform : IPlatform
{
    private static readonly JsonSerializerOptions TargetJsonOptions = new()
    {
        // Property names must stay stable: the serialized list lives in account meta.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly PlatformDefinition _definition;
    private readonly SnapshotStore _store;
    private readonly IProcessController _processes;
    private readonly IRegistryService _registry;
    private readonly string _backupRoot;
    private readonly string? _exeOverride;

    public event Action<string>? StatusChanged;

    public GenericPlatform(
        PlatformDefinition definition,
        SnapshotStore store,
        IProcessController processes,
        IRegistryService registry,
        string backupRoot,
        string? exeOverride = null)
    {
        definition.Validate();
        _definition = definition;
        _store = store;
        _processes = processes;
        _registry = registry;
        _backupRoot = backupRoot;
        _exeOverride = string.IsNullOrWhiteSpace(exeOverride) ? null : exeOverride.Trim();
    }

    public string Id => _definition.Id;

    public string Name => _definition.Name;

    public bool IsInstalled => ResolveExistingCandidate() is not null;

    public string? DetectedPath => ResolveExistingCandidate();

    /// <summary>
    /// First candidate that exists as a file (launchable) or directory
    /// (installation marker only, e.g. Roblox's versioned folder).
    /// Order: user path override, definition candidates, Start Menu
    /// shortcuts, Windows uninstall registry. This catches launchers
    /// installed on non-standard drives.
    /// </summary>
    private string? ResolveExistingCandidate()
    {
        if (_exeOverride is not null
            && (File.Exists(_exeOverride) || Directory.Exists(_exeOverride)))
        {
            return _exeOverride;
        }

        foreach (string candidate in _definition.ExeCandidates)
        {
            string expanded = ExpandPath(candidate);
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                return expanded;
            }
        }

        string? viaShortcuts = _definition.ShortcutNames.Count > 0
            ? InstallFinder.FindViaStartMenu(_definition.ShortcutNames)
            : null;
        if (viaShortcuts is not null)
        {
            return viaShortcuts;
        }

        if (_definition.Uninstall is { } uninstall)
        {
            string? viaRegistry = InstallFinder.FindViaUninstallRegistry(
                _registry,
                uninstall.DisplayNamePattern,
                uninstall.RelativeExe);
            if (viaRegistry is not null)
            {
                return viaRegistry;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Account listing
    // ------------------------------------------------------------------

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AccountMeta> metas = await _store.ListAccountsAsync(Id, ct);
        (string? liveHash, string? liveRaw) = ComputeUniqueId();

        return metas.Select(m =>
        {
            m.Data.TryGetValue("uidHash", out string? storedHash);
            return new Account
            {
                PlatformId = Id,
                Id = m.Id,
                Username = m.Data.GetValueOrDefault("uidRaw") is { Length: > 0 } raw ? raw : m.Username,
                DisplayName = m.DisplayName ?? m.Username,
                CreatedAtUtc = m.CreatedAtUtc,
                Data = m.Data,
                IsActive = storedHash is not null && storedHash == liveHash,
            };
        }).ToList();
    }

    // ------------------------------------------------------------------
    // Capture
    // ------------------------------------------------------------------

    public async Task<Account> CaptureCurrentSessionAsync(string? displayName = null, CancellationToken ct = default)
    {
        (string? hash, string? raw) = ComputeUniqueId();
        if (hash is null)
        {
            throw new InvalidOperationException(
                $"No {_definition.Name} session found. Log into the launcher first, then capture.");
        }

        // Re-capturing the same session (same unique id) updates that account.
        AccountMeta? existing = (await _store.ListAccountsAsync(Id, ct))
            .FirstOrDefault(m => m.Data.GetValueOrDefault("uidHash") == hash);
        string accountId = existing?.Id ?? _store.CreateAccountId();

        var targets = new List<TargetSnapshot>();
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        foreach (LoginTarget target in _definition.Targets)
        {
            index = await CaptureTargetAsync(target, accountId, index, targets, locked, retryPass: false, ct);
        }

        // A running launcher holds locks on some session files (Riot's
        // lockfile, Chromium caches). Close it and capture just the files
        // that stayed locked; ones that are still unreadable or vanished
        // (a lockfile deleted on close) are skipped.
        if (locked.Count > 0)
        {
            Report($"Closing {_definition.Name} so locked session files can be read...");
            await _processes.EnsureClosedAsync(_definition.ProcessesToClose, ct);
            foreach (LoginTarget target in _definition.Targets)
            {
                index = await CaptureTargetAsync(target, accountId, index, targets, locked, retryPass: true, ct);
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException(
                $"No {_definition.Name} session files were found. Log into the launcher first, then capture.");
        }

        var meta = new AccountMeta
        {
            Id = accountId,
            PlatformId = Id,
            Username = displayName ?? raw ?? $"account-{accountId[..8]}",
            DisplayName = displayName,
            CreatedAtUtc = existing?.CreatedAtUtc ?? DateTimeOffset.UtcNow,
            Data = new Dictionary<string, string>(existing?.Data ?? new Dictionary<string, string>())
            {
                ["uidHash"] = hash,
                ["uidRaw"] = raw ?? "",
                ["targets"] = SerializeTargets(targets),
            },
        };

        Report("Saving account...");
        await _store.SaveAccountBytesAsync(meta, new Dictionary<string, byte[]>(), ct);
        return meta.ToAccount();
    }

    private async Task<int> CaptureTargetAsync(
        LoginTarget target,
        string accountId,
        int index,
        List<TargetSnapshot> targets,
        ISet<string> locked,
        bool retryPass,
        CancellationToken ct)
    {
        switch (target.Type)
        {
            case LoginTargetKind.File:
            {
                string live = ExpandPath(target.Path);
                if (retryPass && !locked.Contains(live))
                {
                    return index;
                }

                if (!File.Exists(live))
                {
                    // Includes a lockfile the launcher deleted when closing.
                    return index;
                }

                byte[]? content = await TryReadFileAsync(live, locked, ct);
                if (content is null)
                {
                    return index;
                }

                string storedName = $"{index}_{Sanitize(Path.GetFileName(live))}";
                await StoreFileAsync(accountId, storedName, content, ct);
                targets.Add(new FileSnapshot { LivePath = live, StoredName = storedName });
                return index + 1;
            }

            case LoginTargetKind.Glob:
            {
                (string? dir, string? pattern) = SplitGlob(ExpandPath(target.Path));
                if (dir is null || pattern is null || !Directory.Exists(dir))
                {
                    return index;
                }

                foreach (string file in Directory.EnumerateFiles(dir, pattern).Order())
                {
                    if (retryPass && !locked.Contains(file))
                    {
                        continue;
                    }

                    byte[]? content = await TryReadFileAsync(file, locked, ct);
                    if (content is null)
                    {
                        continue;
                    }

                    string storedName = $"{index}_{Sanitize(Path.GetFileName(file))}";
                    await StoreFileAsync(accountId, storedName, content, ct);
                    targets.Add(new FileSnapshot
                    {
                        LivePath = file,
                        StoredName = storedName,
                        ClearPattern = pattern,
                    });
                }

                return index + 1;
            }

            case LoginTargetKind.Directory:
            {
                string live = ExpandPath(target.Path);
                if (!Directory.Exists(live))
                {
                    return index;
                }

                // The retry pass must keep the stored root from pass 1, or
                // restore would look for files under a different name.
                string storedRoot = retryPass
                    ? targets.OfType<DirSnapshot>().FirstOrDefault(t => t.LivePath == live)?.StoredRoot
                        ?? $"{index}_{Sanitize(Path.GetFileName(live.TrimEnd('\\', '/')))}"
                    : $"{index}_{Sanitize(Path.GetFileName(live.TrimEnd('\\', '/')))}";

                foreach (string file in Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories).Order())
                {
                    if (retryPass && !locked.Contains(file))
                    {
                        continue;
                    }

                    byte[]? content = await TryReadFileAsync(file, locked, ct);
                    if (content is null)
                    {
                        continue;
                    }

                    string relative = Path.GetRelativePath(live, file);
                    await StoreFileAsync(accountId, Path.Combine(storedRoot, relative), content, ct);
                }

                if (!retryPass)
                {
                    targets.Add(new DirSnapshot { LivePath = live, StoredRoot = storedRoot });
                }

                return index + 1;
            }

            case LoginTargetKind.Registry:
            {
                if (target.KeyPath is null || target.ValueName is null)
                {
                    return index;
                }

                Dictionary<string, string> values = ReadRegistryValues(target.KeyPath, target.ValueName);
                if (values.Count == 0)
                {
                    return index;
                }

                targets.Add(new RegistrySnapshot { KeyPath = target.KeyPath, Values = values });
                return index;
            }

            case LoginTargetKind.JsonField:
            {
                string live = ExpandPath(target.Path);
                if (target.JsonPath is null || !File.Exists(live))
                {
                    return index;
                }

                string? value = ReadJsonField(live, target.JsonPath);
                if (value is null)
                {
                    return index;
                }

                targets.Add(new JsonFieldSnapshot
                {
                    LivePath = live,
                    JsonPath = target.JsonPath,
                    Value = value,
                });
                return index;
            }

            default:
                return index;
        }
    }

    private Task StoreFileAsync(string accountId, string storedName, byte[] content, CancellationToken ct) =>
        _store.WriteAccountFileAsync(Id, accountId, storedName, content, ct);

    /// <summary>
    /// Reads a session file even while the launcher keeps it open for
    /// writing (caches, leveldb, lockfiles) by asking for read-write
    /// sharing. Returns null and records the path when the file is still
    /// locked: capture then closes the launcher and retries once; files
    /// that remain locked are skipped.
    /// </summary>
    private static async Task<byte[]?> TryReadFileAsync(string path, ISet<string> locked, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 0,
                useAsync: true);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, ct);
            return memory.ToArray();
        }
        catch (IOException)
        {
            locked.Add(path);
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // ACL restrictions are not transient: skip instead of retrying.
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Switch
    // ------------------------------------------------------------------

    public async Task SwitchAsync(string accountId, CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException($"{_definition.Name} installation not found.");
        }

        AccountMeta meta = await _store.GetAccountAsync(Id, accountId, ct);
        var targets = DeserializeTargets(meta.Data.GetValueOrDefault("targets"))
            ?? throw new InvalidDataException($"Account '{accountId}' has no stored session targets.");

        Report($"Closing {_definition.Name}...");
        await _processes.EnsureClosedAsync(_definition.ProcessesToClose, ct);

        await using (var tx = new SwitchTransaction(
            Path.Combine(_backupRoot, $"{Id}_{Guid.NewGuid():N}"),
            _registry))
        {
            Report("Restoring account...");
            foreach (TargetSnapshot target in targets)
            {
                await StageRestoreAsync(tx, accountId, target, ct);
            }

            await tx.CommitAsync(ct);

            string? exe = ResolveExistingCandidate();
            if (exe is not null && File.Exists(exe))
            {
                Report($"Starting {_definition.Name}...");
                await _processes.StartAsync(exe, _definition.LaunchArguments, ct);
            }
            else
            {
                Report("Session restored.");
            }

            tx.Complete();
        }
    }

    private async Task StageRestoreAsync(SwitchTransaction tx, string accountId, TargetSnapshot target, CancellationToken ct)
    {
        switch (target)
        {
            case FileSnapshot file:
                if (file.ClearPattern is not null)
                {
                    string? dir = Path.GetDirectoryName(file.LivePath);
                    if (dir is not null && Directory.Exists(dir))
                    {
                        foreach (string stale in Directory.EnumerateFiles(dir, file.ClearPattern))
                        {
                            tx.StageFileDelete(stale);
                        }
                    }
                }

                tx.StageFileWrite(
                    file.LivePath,
                    await _store.ReadAccountFileAsync(Id, accountId, file.StoredName, ct));
                break;

            case DirSnapshot dir:
                foreach (string live in Directory.EnumerateFiles(dir.LivePath, "*", SearchOption.AllDirectories))
                {
                    tx.StageFileDelete(live);
                }

                foreach ((string storedName, _) in _store.GetAccountFileMap(Id, accountId)
                             .Where(kv => StartsWithSegment(kv.Key, dir.StoredRoot)))
                {
                    string relative = storedName[dir.StoredRoot.Length..].TrimStart('\\', '/');
                    tx.StageFileWrite(
                        Path.Combine(dir.LivePath, relative),
                        await _store.ReadAccountFileAsync(Id, accountId, storedName, ct));
                }

                break;

            case RegistrySnapshot registry:
                foreach ((string name, string value) in registry.Values)
                {
                    tx.StageRegistryValue(RegistryHive.CurrentUser, registry.KeyPath, name, value);
                }

                break;

            case JsonFieldSnapshot json:
            {
                byte[] modified = WriteJsonField(json.LivePath, json.JsonPath, json.Value);
                tx.StageFileWrite(json.LivePath, modified);
                break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Add new login / logout
    // ------------------------------------------------------------------

    public async Task AddNewLoginAsync(CancellationToken ct = default)
    {
        if (!IsInstalled)
        {
            throw new InvalidOperationException($"{_definition.Name} installation not found.");
        }

        Report($"Closing {_definition.Name}...");
        await _processes.EnsureClosedAsync(_definition.ProcessesToClose, ct);

        await using (var tx = new SwitchTransaction(
            Path.Combine(_backupRoot, $"{Id}_{Guid.NewGuid():N}"),
            _registry))
        {
            StageClear(tx);
            await tx.CommitAsync(ct);

            string? exe = ResolveExistingCandidate();
            if (exe is not null && File.Exists(exe))
            {
                Report($"Opening {_definition.Name} login...");
                await _processes.StartAsync(exe, _definition.LaunchArguments, ct);
            }
            else
            {
                Report("Login cleared.");
            }

            tx.Complete();
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        Report($"Closing {_definition.Name}...");
        await _processes.EnsureClosedAsync(_definition.ProcessesToClose, ct);

        await using var tx = new SwitchTransaction(
            Path.Combine(_backupRoot, $"{Id}_{Guid.NewGuid():N}"),
            _registry);
        StageClear(tx);
        Report("Ending session...");
        await tx.CommitAsync(ct);
        tx.Complete();
    }

    /// <summary>Stages deletion of every live target derived from the definition.</summary>
    private void StageClear(SwitchTransaction tx)
    {
        foreach (LoginTarget target in _definition.Targets)
        {
            switch (target.Type)
            {
                case LoginTargetKind.File:
                {
                    string live = ExpandPath(target.Path);
                    if (File.Exists(live))
                    {
                        tx.StageFileDelete(live);
                    }

                    break;
                }

                case LoginTargetKind.Glob:
                {
                    (string? dir, string? pattern) = SplitGlob(ExpandPath(target.Path));
                    if (dir is not null && pattern is not null && Directory.Exists(dir))
                    {
                        foreach (string file in Directory.EnumerateFiles(dir, pattern))
                        {
                            tx.StageFileDelete(file);
                        }
                    }

                    break;
                }

                case LoginTargetKind.Directory:
                {
                    string live = ExpandPath(target.Path);
                    if (Directory.Exists(live))
                    {
                        foreach (string file in Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories))
                        {
                            tx.StageFileDelete(file);
                        }
                    }

                    break;
                }

                case LoginTargetKind.Registry:
                {
                    if (target.KeyPath is not null && target.ValueName is not null)
                    {
                        foreach (string name in ReadRegistryValues(target.KeyPath, target.ValueName).Keys)
                        {
                            tx.StageRegistryDelete(RegistryHive.CurrentUser, target.KeyPath, name);
                        }
                    }

                    break;
                }

                case LoginTargetKind.JsonField:
                {
                    string live = ExpandPath(target.Path);
                    if (target.JsonPath is not null && File.Exists(live))
                    {
                        tx.StageFileWrite(live, WriteJsonField(live, target.JsonPath, ""));
                    }

                    break;
                }
            }
        }
    }

    public Task DeleteAccountAsync(string accountId, CancellationToken ct = default)
    {
        _store.DeleteAccount(Id, accountId);
        return Task.CompletedTask;
    }

    public async Task RenameAccountAsync(string accountId, string newName, CancellationToken ct = default) =>
        await _store.MutateMetaAsync(Id, accountId, meta => meta.DisplayName = newName, ct);

    // ------------------------------------------------------------------
    // Unique id
    // ------------------------------------------------------------------

    private (string? Hash, string? Raw) ComputeUniqueId()
    {
        UniqueIdDefinition? uid = _definition.UniqueId;
        if (uid is null)
        {
            return (null, null);
        }

        switch (uid.Kind)
        {
            case UniqueIdKind.FileHash:
            {
                string path = ExpandPath(uid.Path);
                return File.Exists(path) ? (HashBytes(File.ReadAllBytes(path)), null) : (null, null);
            }

            case UniqueIdKind.FileRegex:
            {
                string path = ExpandPath(uid.Path);
                if (!File.Exists(path) || uid.Regex is null)
                {
                    return (null, null);
                }

                string content = File.ReadAllText(path);
                Match match = Regex.Match(content, uid.Regex);
                return match.Success
                    ? (HashText(match.Groups.Count > 1 ? match.Groups[1].Value : match.Value),
                        match.Groups.Count > 1 ? match.Groups[1].Value : match.Value)
                    : (null, null);
            }

            case UniqueIdKind.DirHash:
            {
                string path = ExpandPath(uid.Path);
                if (!Directory.Exists(path))
                {
                    return (null, null);
                }

                var listing = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Order()
                    .Select(f => $"{Path.GetRelativePath(path, f)}|{new FileInfo(f).Length}");
                return (HashText(string.Join(";", listing)), null);
            }

            case UniqueIdKind.RegistryValue:
            {
                string? value = uid.KeyPath is null || uid.ValueName is null
                    ? null
                    : _registry.GetStringValue(RegistryHive.CurrentUser, uid.KeyPath, uid.ValueName);
                return value is null ? (null, null) : (HashText(value), value);
            }

            case UniqueIdKind.JsonField:
            {
                string path = ExpandPath(uid.Path);
                string? value = uid.JsonPath is null || !File.Exists(path)
                    ? null
                    : ReadJsonField(path, uid.JsonPath);
                return value is null ? (null, null) : (HashText(value), value);
            }

            default:
                return (null, null);
        }
    }

    // ------------------------------------------------------------------
    // Target snapshot serialization (stored in account meta)
    // ------------------------------------------------------------------

    private abstract record TargetSnapshot;

    private sealed record FileSnapshot : TargetSnapshot
    {
        public required string LivePath { get; init; }

        public required string StoredName { get; init; }

        public string? ClearPattern { get; init; }
    }

    private sealed record DirSnapshot : TargetSnapshot
    {
        public required string LivePath { get; init; }

        public required string StoredRoot { get; init; }
    }

    private sealed record RegistrySnapshot : TargetSnapshot
    {
        public required string KeyPath { get; init; }

        public required Dictionary<string, string> Values { get; init; }
    }

    private sealed record JsonFieldSnapshot : TargetSnapshot
    {
        public required string LivePath { get; init; }

        public required string JsonPath { get; init; }

        public required string Value { get; init; }
    }

    private static string SerializeTargets(List<TargetSnapshot> targets)
    {
        var array = new JsonArray();
        foreach (TargetSnapshot target in targets)
        {
            var obj = new JsonObject();
            switch (target)
            {
                case FileSnapshot file:
                    obj["type"] = "file";
                    obj["livePath"] = file.LivePath;
                    obj["storedName"] = file.StoredName;
                    if (file.ClearPattern is not null)
                    {
                        obj["clearPattern"] = file.ClearPattern;
                    }

                    break;
                case DirSnapshot dir:
                    obj["type"] = "dir";
                    obj["livePath"] = dir.LivePath;
                    obj["storedRoot"] = dir.StoredRoot;
                    break;
                case RegistrySnapshot registry:
                    obj["type"] = "registry";
                    obj["keyPath"] = registry.KeyPath;
                    var values = new JsonObject();
                    foreach ((string name, string value) in registry.Values)
                    {
                        values[name] = value;
                    }

                    obj["values"] = values;
                    break;
                case JsonFieldSnapshot json:
                    obj["type"] = "jsonField";
                    obj["livePath"] = json.LivePath;
                    obj["jsonPath"] = json.JsonPath;
                    obj["value"] = json.Value;
                    break;
            }

            array.Add(obj);
        }

        return array.ToJsonString(TargetJsonOptions);
    }

    private static List<TargetSnapshot>? DeserializeTargets(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        var result = new List<TargetSnapshot>();
        foreach (JsonNode? node in JsonNode.Parse(json)!.AsArray())
        {
            var obj = node!.AsObject();
            switch (obj["type"]!.GetValue<string>())
            {
                case "file":
                    result.Add(new FileSnapshot
                    {
                        LivePath = obj["livePath"]!.GetValue<string>(),
                        StoredName = obj["storedName"]!.GetValue<string>(),
                        ClearPattern = obj["clearPattern"]?.GetValue<string>(),
                    });
                    break;
                case "dir":
                    result.Add(new DirSnapshot
                    {
                        LivePath = obj["livePath"]!.GetValue<string>(),
                        StoredRoot = obj["storedRoot"]!.GetValue<string>(),
                    });
                    break;
                case "registry":
                    result.Add(new RegistrySnapshot
                    {
                        KeyPath = obj["keyPath"]!.GetValue<string>(),
                        Values = obj["values"]!.AsObject().ToDictionary(
                            kv => kv.Key, kv => kv.Value!.GetValue<string>()),
                    });
                    break;
                case "jsonField":
                    result.Add(new JsonFieldSnapshot
                    {
                        LivePath = obj["livePath"]!.GetValue<string>(),
                        JsonPath = obj["jsonPath"]!.GetValue<string>(),
                        Value = obj["value"]?.GetValue<string>() ?? "",
                    });
                    break;
            }
        }

        return result;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private Dictionary<string, string> ReadRegistryValues(string keyPath, string valueNamePattern)
    {
        var values = new Dictionary<string, string>();
        if (valueNamePattern.Contains('*'))
        {
            // Wildcard value names require enumeration, which IRegistryService does
            // not expose: read the exact-name fallback and skip wildcards here.
            // (No bundled platform currently needs wildcard registry values.)
            return values;
        }

        string? value = _registry.GetStringValue(RegistryHive.CurrentUser, keyPath, valueNamePattern);
        if (value is not null)
        {
            values[valueNamePattern] = value;
        }

        return values;
    }

    private static string? ReadJsonField(string path, string jsonPath)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));
            foreach (string segment in jsonPath.Split('.'))
            {
                node = node?[segment];
                if (node is null)
                {
                    return null;
                }
            }

            return node is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[] WriteJsonField(string path, string jsonPath, string value)
    {
        JsonNode? root = JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Cannot edit JSON file: {path}");

        JsonNode node = root;
        string[] segments = jsonPath.Split('.');
        for (int i = 0; i < segments.Length - 1; i++)
        {
            node = node[segments[i]] ?? (node[segments[i]] = new JsonObject());
        }

        node[segments[^1]] = value;

        var options = new JsonSerializerOptions { WriteIndented = true };
        return Encoding.UTF8.GetBytes(root.ToJsonString(options));
    }

    private static (string? Dir, string? Pattern) SplitGlob(string path)
    {
        string name = Path.GetFileName(path);
        if (!name.Contains('*'))
        {
            return (null, null);
        }

        return (Path.GetDirectoryName(path), name);
    }

    private static bool StartsWithSegment(string storedName, string root) =>
        storedName.StartsWith(root, StringComparison.Ordinal)
        && (storedName.Length == root.Length
            || storedName[root.Length] == '\\'
            || storedName[root.Length] == '/');

    private static string ExpandPath(string path) =>
        path
            .Replace("%LocalAppData%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("%AppData%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramData%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramFiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase)
            .Replace("%ProgramFiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string name) =>
        string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    private static string HashBytes(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private void Report(string message) => StatusChanged?.Invoke(message);
}
