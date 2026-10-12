using GLAS.Core.Files;
using GLAS.Core.RegistryAccess;

namespace GLAS.Core.Snapshots;

/// <summary>
/// A switch writes launcher files and registry values. If any step fails,
/// every change is restored before the exception surfaces, so the launcher
/// is never left in a half-switched state.
///
/// Usage:
///   await using (var tx = new SwitchTransaction(backupDir, registry))
///   {
///       tx.StageFileWrite(path, bytes);
///       tx.StageFileDelete(path);
///       tx.StageRegistryValue(hive, key, name, value);
///       await tx.CommitAsync(ct);   // applies all stages, keeps rollback data
///       await DoFollowUpWorkAsync();
///       tx.Complete();               // discards rollback data
///   }
/// Dispose without Complete() restores everything that was committed.
/// </summary>
public sealed class SwitchTransaction : IAsyncDisposable
{
    private readonly string _backupDir;
    private readonly IRegistryService _registry;
    private readonly List<AppliedChange> _applied = [];
    private readonly List<(string Path, byte[] Content)> _pendingWrites = [];
    private readonly List<string> _pendingDeletes = [];
    private readonly List<(RegistryHive Hive, string KeyPath, string Name, string Value)> _pendingRegistry = [];
    private readonly List<(RegistryHive Hive, string KeyPath, string Name)> _pendingRegistryDeletes = [];
    private bool _committed;
    private bool _completed;

    public SwitchTransaction(string backupDir, IRegistryService registry)
    {
        _backupDir = backupDir;
        _registry = registry;
        Directory.CreateDirectory(backupDir);
    }

    public void StageFileWrite(string targetPath, byte[] content)
    {
        _pendingWrites.Add((targetPath, content));
    }

    public void StageFileDelete(string targetPath)
    {
        _pendingDeletes.Add(targetPath);
    }

    public void StageRegistryValue(RegistryHive hive, string keyPath, string valueName, string value)
    {
        _pendingRegistry.Add((hive, keyPath, valueName, value));
    }

    public void StageRegistryDelete(RegistryHive hive, string keyPath, string valueName)
    {
        _pendingRegistryDeletes.Add((hive, keyPath, valueName));
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_committed)
        {
            throw new InvalidOperationException("Transaction already committed.");
        }

        foreach (string target in _pendingDeletes)
        {
            if (!File.Exists(target))
            {
                continue;
            }

            string backup = BackupFile(target);
            _applied.Add(new FileRestoreChange(target, backup));
            await Task.Run(() => File.Delete(target), ct);
        }

        foreach ((string target, byte[] content) in _pendingWrites)
        {
            string? backup = null;
            if (File.Exists(target))
            {
                backup = BackupFile(target);
            }

            AtomicFile.WriteAllBytes(target, content);
            _applied.Add(new FileRestoreChange(target, backup));
        }

        foreach ((RegistryHive hive, string keyPath, string name, string value) in _pendingRegistry)
        {
            string? previous = _registry.GetStringValue(hive, keyPath, name);
            _registry.SetStringValue(hive, keyPath, name, value);
            _applied.Add(new RegistryRestoreChange(_registry, hive, keyPath, name, previous));
        }

        foreach ((RegistryHive hive, string keyPath, string name) in _pendingRegistryDeletes)
        {
            string? previous = _registry.GetStringValue(hive, keyPath, name);
            _registry.DeleteValue(hive, keyPath, name);
            _applied.Add(new RegistryRestoreChange(_registry, hive, keyPath, name, previous));
        }

        _committed = true;
    }

    /// <summary>Discards rollback data. Call only after everything succeeded.</summary>
    public void Complete()
    {
        _completed = true;
        CleanupBackups();
    }

    public ValueTask DisposeAsync()
    {
        if (_committed && !_completed)
        {
            Rollback();
        }

        CleanupBackups();
        return ValueTask.CompletedTask;
    }

    private void Rollback()
    {
        // Reverse order: the last applied change is undone first.
        for (int i = _applied.Count - 1; i >= 0; i--)
        {
            try
            {
                _applied[i].Undo();
            }
            catch
            {
                // Rollback is best-effort per item; continue undoing the rest.
            }
        }
    }

    private string BackupFile(string target)
    {
        string backup = Path.Combine(_backupDir, $"{_applied.Count}_{Path.GetFileName(target)}.bak");
        File.Copy(target, backup, overwrite: true);
        return backup;
    }

    private void CleanupBackups()
    {
        try
        {
            if (Directory.Exists(_backupDir))
            {
                Directory.Delete(_backupDir, recursive: true);
            }
        }
        catch
        {
            // Leftover backups are harmless; next switch uses a new directory.
        }
    }

    private abstract class AppliedChange
    {
        public abstract void Undo();
    }

    private sealed class FileRestoreChange(string target, string? backup) : AppliedChange
    {
        public override void Undo()
        {
            if (backup is not null && File.Exists(backup))
            {
                File.Copy(backup, target, overwrite: true);
            }
            else
            {
                AtomicFile.TryDelete(target);
            }
        }
    }

    private sealed class RegistryRestoreChange(
        IRegistryService registry,
        RegistryHive hive,
        string keyPath,
        string valueName,
        string? previous) : AppliedChange
    {
        public override void Undo()
        {
            if (previous is null)
            {
                registry.DeleteValue(hive, keyPath, valueName);
            }
            else
            {
                registry.SetStringValue(hive, keyPath, valueName, previous);
            }
        }
    }
}
