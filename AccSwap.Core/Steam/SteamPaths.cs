using AccSwap.Core.Platforms;
using AccSwap.Core.RegistryAccess;

namespace AccSwap.Core.Steam;

/// <summary>
/// Injectable Steam paths. Tests construct instances directly with temp
/// directories; production uses <see cref="Detect"/>.
/// </summary>
public sealed class SteamPaths
{
    public const string RegistrySteamKey = @"Software\Valve\Steam";

    public string? SteamRoot { get; init; }

    public string? LauncherExePath { get; init; }

    public string? LoginUsersPath { get; init; }

    public static SteamPaths Detect(IRegistryService registry)
    {
        string? root =
            registry.GetStringValue(RegistryHive.CurrentUser, RegistrySteamKey, "SteamPath")
            ?? registry.GetStringValue(RegistryHive.LocalMachine, RegistrySteamKey, "InstallPath")
            ?? registry.GetStringValue(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");

        if (root is null || !Directory.Exists(root))
        {
            // Last resort for custom drives: the Steam Start Menu shortcut
            // points straight at steam.exe.
            string? viaShortcut = InstallFinder.FindViaStartMenu(["^Steam$"]);
            if (viaShortcut is not null)
            {
                root = Directory.GetParent(viaShortcut)?.FullName;
            }
        }

        if (root is null || !Directory.Exists(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam");
        }

        if (!Directory.Exists(root))
        {
            return new SteamPaths();
        }

        return new SteamPaths
        {
            SteamRoot = root,
            LauncherExePath = Path.Combine(root, "steam.exe"),
            LoginUsersPath = Path.Combine(root, "config", "loginusers.vdf"),
        };
    }
}
