using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GLAS.Core.RegistryAccess;

namespace GLAS.Core.Platforms;

/// <summary>
/// Fallback launcher discovery for custom install locations:
///   1. Start Menu shortcuts (.lnk target path, any drive).
///   2. Windows uninstall registry entries (InstallLocation / DisplayIcon).
/// Used when the standard candidate paths miss a launcher.
/// </summary>
public static class InstallFinder
{
    private static readonly string[] StartMenuRoots =
    [
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs"),
    ];

    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    /// <summary>
    /// Resolves the launcher exe from a Start Menu shortcut whose file name
    /// matches one of the patterns (e.g. "Epic Games Launcher").
    /// </summary>
    public static string? FindViaStartMenu(IEnumerable<string> shortcutNamePatterns)
    {
        foreach (string pattern in shortcutNamePatterns)
        {
            foreach (string root in StartMenuRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string link in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                {
                    if (!Regex.IsMatch(Path.GetFileNameWithoutExtension(link), pattern, RegexOptions.IgnoreCase))
                    {
                        continue;
                    }

                    string? target = ReadShortcutTarget(link);
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                    {
                        return target;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Looks for an uninstall entry whose DisplayName matches, and derives
    /// the launcher exe from InstallLocation + relativeExe or from DisplayIcon.
    /// </summary>
    public static string? FindViaUninstallRegistry(
        IRegistryService registry,
        string displayNamePattern,
        string? relativeExe)
    {
        foreach (string root in UninstallRoots)
        {
            RegistryHive hive = root.StartsWith(@"Software\", StringComparison.Ordinal)
                ? RegistryHive.CurrentUser
                : RegistryHive.LocalMachine;

            foreach (string subKey in registry.GetSubKeyNames(hive, root))
            {
                string keyPath = $"{root}\\{subKey}";
                string? displayName = registry.GetStringValue(hive, keyPath, "DisplayName");
                if (displayName is null || !Regex.IsMatch(displayName, displayNamePattern, RegexOptions.IgnoreCase))
                {
                    continue;
                }

                if (relativeExe is not null
                    && registry.GetStringValue(hive, keyPath, "InstallLocation") is { Length: > 0 } location)
                {
                    string candidate = Path.Combine(location.TrimEnd('\\', '/'), relativeExe);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }

                if (registry.GetStringValue(hive, keyPath, "DisplayIcon") is { Length: > 0 } icon)
                {
                    string cleaned = icon.Trim().Trim('"');
                    int comma = cleaned.LastIndexOf(',');
                    if (comma > 0)
                    {
                        cleaned = cleaned[..comma];
                    }

                    cleaned = cleaned.Trim().Trim('"');
                    if (cleaned.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(cleaned))
                    {
                        return cleaned;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Reads a .lnk target path through the Windows Script Host COM object.</summary>
    public static string? ReadShortcutTarget(string linkPath)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            object shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Cannot create WScript.Shell.");
            object shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                [linkPath])
                ?? throw new InvalidOperationException("Cannot open shortcut.");
            return shellType.InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                null,
                shortcut,
                null) as string;
        }
        catch (COMException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
