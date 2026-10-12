using GLAS.Core.Platforms;
using GLAS.Core.RegistryAccess;

namespace GLAS.Core.Epic;

/// <summary>
/// Injectable Epic Games Launcher paths. Tests construct instances directly
/// with temp directories; production uses <see cref="Detect"/>.
/// </summary>
public sealed class EpicPaths
{
    public string? LauncherExePath { get; init; }

    /// <summary>%LOCALAPPDATA%\EpicGamesLauncher\Saved\Config\Windows\GameUserSettings.ini</summary>
    public string? SessionIniPath { get; init; }

    /// <summary>
    /// Probes the standard install candidates (Program Files x64 and x86)
    /// and, when a registry service is provided, falls back to Start Menu
    /// shortcuts and the Windows uninstall entries for custom drives.
    /// </summary>
    public static EpicPaths Detect(IRegistryService? registry = null)
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        const string launcherTail = "Epic Games\\Launcher\\Portal\\Binaries";
        string? exe =
            FirstExisting(
                Path.Combine(programFiles, launcherTail, "Win64", "EpicGamesLauncher.exe"),
                Path.Combine(programFiles, launcherTail, "Win32", "EpicGamesLauncher.exe"),
                Path.Combine(programFilesX86, launcherTail, "Win64", "EpicGamesLauncher.exe"),
                Path.Combine(programFilesX86, launcherTail, "Win32", "EpicGamesLauncher.exe"));

        if (exe is null && registry is not null)
        {
            exe = InstallFinder.FindViaStartMenu(["Epic Games Launcher"])
                ?? InstallFinder.FindViaUninstallRegistry(
                    registry,
                    "^Epic Games Launcher$",
                    "Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe");
        }

        // Epic builds the launcher as a UE editor app on some installs, so
        // the session ini lives under WindowsEditor instead of Windows.
        string configRoot = Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Config");
        string windowsIni = Path.Combine(configRoot, "Windows", "GameUserSettings.ini");
        string editorIni = Path.Combine(configRoot, "WindowsEditor", "GameUserSettings.ini");
        string? ini = FirstExisting(windowsIni, editorIni) ?? windowsIni;

        return new EpicPaths
        {
            LauncherExePath = exe,
            SessionIniPath = ini,
        };
    }

    private static string? FirstExisting(params string[] paths)
    {
        foreach (string path in paths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
