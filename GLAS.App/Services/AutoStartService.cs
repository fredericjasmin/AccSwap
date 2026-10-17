using System.IO;
using GLAS.Core.RegistryAccess;

namespace GLAS.App.Services;

/// <summary>
/// Windows autostart via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Task Manager's enabled/disabled flags for Run entries.</summary>
    private const string StartupApprovedKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private const string ValueName = "GLAS";

    public static bool IsEnabled(IRegistryService registry) =>
        registry.GetStringValue(RegistryHive.CurrentUser, RunKey, ValueName) is not null;

    public static void SetEnabled(IRegistryService registry, bool enabled)
    {
        if (enabled)
        {
            registry.SetStringValue(RegistryHive.CurrentUser, RunKey, ValueName, $"\"{ResolveExePath()}\"");

            // A disable left here by Task Manager overrides the Run entry, so
            // re-enabling from Settings must clear it, not just rewrite Run.
            registry.DeleteValue(RegistryHive.CurrentUser, StartupApprovedKey, ValueName);
        }
        else
        {
            registry.DeleteValue(RegistryHive.CurrentUser, RunKey, ValueName);
        }
    }

    /// <summary>
    /// Repoints the Run entry at the running exe. Windows silently skips Run
    /// entries whose file no longer exists, so the entry is refreshed at every
    /// startup while autostart is enabled: the exe may have been renamed by an
    /// update or moved since the entry was written. The Task Manager flag is
    /// deliberately left alone here; an explicit disable there stands.
    /// </summary>
    public static void RepairEntry(IRegistryService registry)
    {
        string wanted = $"\"{ResolveExePath()}\"";
        string? current = registry.GetStringValue(RegistryHive.CurrentUser, RunKey, ValueName);
        if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
        {
            registry.SetStringValue(RegistryHive.CurrentUser, RunKey, ValueName, wanted);
        }
    }

    /// <summary>
    /// The running exe's path. Environment.ProcessPath reports the name the
    /// process was started with, which goes stale after UpdateService renames
    /// the exe to its stable name at startup; in that case fall back to the
    /// stable name next to the binary.
    /// </summary>
    private static string ResolveExePath()
    {
        if (Environment.ProcessPath is { } path && File.Exists(path))
        {
            return path;
        }

        string stable = Path.Combine(AppContext.BaseDirectory, AppInfo.StableExeName);
        if (File.Exists(stable))
        {
            return stable;
        }

        throw new InvalidOperationException("Cannot locate the running executable.");
    }
}
