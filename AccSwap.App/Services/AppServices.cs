using System.IO;
using AccSwap.Core;
using AccSwap.Core.Epic;
using AccSwap.Core.Platforms;
using AccSwap.Core.Platforms.Generic;
using AccSwap.Core.Processes;
using AccSwap.Core.RegistryAccess;
using AccSwap.Core.Security;
using AccSwap.Core.Settings;
using AccSwap.Core.Snapshots;
using AccSwap.Core.Steam;

namespace AccSwap.App.Services;

/// <summary>
/// Composition root: owns the core services and rebuilds the platform graph
/// when snapshot protection is enabled or the password changes.
/// </summary>
public sealed class AppServices
{
    public const string ProtectionFileName = "protection.accswap";
    private const string ProtectionToken = "accswap-protection-check";

    public static AppServices Instance { get; } = new();

    private AppServices()
    {
        RootDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AccSwap");
        Directory.CreateDirectory(RootDir);

        SettingsStore = AppSettingsStore.CreateDefault(RootDir);
        Settings = SettingsStore.Load();
        Registry = new RegistryService();
        Processes = new ProcessController();
    }

    public string RootDir { get; }

    public string BackupRoot => Path.Combine(RootDir, "backups");

    public AppSettingsStore SettingsStore { get; }

    public AppSettings Settings { get; private set; }

    public IRegistryService Registry { get; }

    public IProcessController Processes { get; }

    public string? SessionPassword { get; private set; }

    public SnapshotStore Store { get; private set; } = null!;

    public PlatformRegistry Platforms { get; } = new();

    public string ProtectionFilePath => Path.Combine(RootDir, ProtectionFileName);

    public bool ProtectionIsEnabled => File.Exists(ProtectionFilePath);

    public void Rebuild(string? sessionPassword)
    {
        SessionPassword = sessionPassword;
        IFileProtector? protector = sessionPassword is not null
            ? new PasswordFileProtector(sessionPassword)
            : null;
        Store = new SnapshotStore(RootDir, protector);

        Platforms.Clear();
        Platforms.Register(new SteamPlatform(
            Store,
            ResolveSteamPaths(),
            Processes,
            Registry,
            BackupRoot,
            Settings.SteamLaunchArguments));
        Platforms.Register(new EpicPlatform(
            Store,
            ResolveEpicPaths(),
            Processes,
            BackupRoot));

        foreach (PlatformDefinition definition in PlatformCatalog.Load(RootDir))
        {
            Platforms.Register(new GenericPlatform(
                definition,
                Store,
                Processes,
                Registry,
                BackupRoot,
                Settings.LauncherPaths.GetValueOrDefault(definition.Id)));
        }
    }

    private SteamPaths ResolveSteamPaths()
    {
        if (Settings.LauncherPaths.GetValueOrDefault("steam") is { Length: > 0 } overridePath
            && File.Exists(overridePath))
        {
            string root = Directory.GetParent(Path.GetFullPath(overridePath))!.FullName;
            return new SteamPaths
            {
                SteamRoot = root,
                LauncherExePath = overridePath,
                LoginUsersPath = Path.Combine(root, "config", "loginusers.vdf"),
            };
        }

        return SteamPaths.Detect(Registry);
    }

    private EpicPaths ResolveEpicPaths()
    {
        if (Settings.LauncherPaths.GetValueOrDefault("epic") is { Length: > 0 } overridePath
            && File.Exists(overridePath))
        {
            return new EpicPaths
            {
                LauncherExePath = overridePath,
                SessionIniPath = EpicPaths.Detect().SessionIniPath,
            };
        }

        return EpicPaths.Detect(Registry);
    }

    public void SetProtectionPassword(string password)
    {
        File.WriteAllBytes(
            ProtectionFilePath,
            SnapshotEncryption.Encrypt(
                System.Text.Encoding.UTF8.GetBytes(ProtectionToken),
                password));
        Settings.ProtectSnapshots = true;
        SettingsStore.Save(Settings);
        Rebuild(password);
    }

    public void DisableProtection() =>
        // Runs the async decrypt/rewrite on the thread pool: blocking the UI
        // thread on these awaits would deadlock the WPF dispatcher.
        Task.Run(DisableProtectionCoreAsync).GetAwaiter().GetResult();

    private async Task DisableProtectionCoreAsync()
    {
        // Decrypt every stored account and rewrite it in plain form.
        SnapshotStore plain = new(RootDir, protector: null);
        foreach (IPlatform platform in Platforms.Platforms)
        {
            foreach (AccountMeta meta in Store.ListAccountsAsync(platform.Id).GetAwaiter().GetResult())
            {
                var files = new Dictionary<string, byte[]>();
                foreach ((string relativeName, _) in Store.GetAccountFileMap(platform.Id, meta.Id))
                {
                    files[relativeName] = Store.ReadAccountFileAsync(platform.Id, meta.Id, relativeName).GetAwaiter().GetResult();
                }

                plain.SaveAccountBytesAsync(meta, files).GetAwaiter().GetResult();
            }
        }

        if (File.Exists(ProtectionFilePath))
        {
            File.Delete(ProtectionFilePath);
        }

        Settings.ProtectSnapshots = false;
        SettingsStore.Save(Settings);
        Rebuild(null);
    }

    public bool VerifyPassword(string password)
    {
        try
        {
            byte[] raw = File.ReadAllBytes(ProtectionFilePath);
            byte[] token = SnapshotEncryption.Decrypt(raw, password);
            return System.Text.Encoding.UTF8.GetString(token) == ProtectionToken;
        }
        catch
        {
            return false;
        }
    }
}
