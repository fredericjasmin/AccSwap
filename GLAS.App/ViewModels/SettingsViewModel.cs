using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GLAS.App.Localization;
using GLAS.App.Services;
using GLAS.Core.Settings;

namespace GLAS.App.ViewModels;

public partial class LauncherPathRow : ObservableObject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string DetectedText { get; init; }

    /// <summary>Raised when the user edits the override path.</summary>
    public event Action? Changed;

    [ObservableProperty]
    private string _overridePath = "";

    partial void OnOverridePathChanged(string value) => Changed?.Invoke();
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly Action _close;

    public Loc Loc => Loc.Instance;

    public record LanguageOption(string Code, string Label);

    public List<LanguageOption> Languages { get; } =
    [
        new("es", "Español"),
        new("en", "English"),
    ];

    public SettingsViewModel(Action close)
    {
        _close = close;
        AppSettings settings = AppServices.Instance.Settings;
        _language = settings.Language;
        _startMinimized = settings.StartMinimized;
        _closeToTray = settings.CloseToTray;
        _autoStart = settings.AutoStart;
        _steamArguments = settings.SteamLaunchArguments;
        _protectSnapshots = settings.ProtectSnapshots;

        foreach (Core.Platforms.IPlatform platform in AppServices.Instance.Platforms.Platforms)
        {
            var row = new LauncherPathRow
            {
                Id = platform.Id,
                Name = platform.Name,
                DetectedText = platform.DetectedPath is { } path
                    ? string.Format(Loc.Instance["Settings.Detected"], path)
                    : Loc.Instance["Settings.NotDetected"],
                OverridePath = settings.LauncherPaths.GetValueOrDefault(platform.Id, ""),
            };
            row.Changed += () =>
            {
                string trimmed = row.OverridePath.Trim();
                if (trimmed.Length == 0)
                {
                    settings.LauncherPaths.Remove(platform.Id);
                }
                else
                {
                    settings.LauncherPaths[platform.Id] = trimmed;
                }

                SaveNoRebuild();
                AppServices.Instance.Rebuild(AppServices.Instance.SessionPassword);
            };
            LauncherPaths.Add(row);
        }
    }

    public ObservableCollection<LauncherPathRow> LauncherPaths { get; } = [];

    private void SaveNoRebuild()
    {
        AppServices services = AppServices.Instance;
        services.SettingsStore.Save(services.Settings);
    }

    [ObservableProperty]
    private string _language;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _closeToTray;

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private string _steamArguments;

    [ObservableProperty]
    private bool _protectSnapshots;

    partial void OnLanguageChanged(string value) => Apply(s => s.Language = value);

    partial void OnStartMinimizedChanged(bool value) => Apply(s => s.StartMinimized = value);

    partial void OnCloseToTrayChanged(bool value) => Apply(s => s.CloseToTray = value);

    partial void OnSteamArgumentsChanged(string value) => Apply(s => s.SteamLaunchArguments = value);

    partial void OnAutoStartChanged(bool value)
    {
        try
        {
            AutoStartService.SetEnabled(AppServices.Instance.Registry, value);
            Apply(s => s.AutoStart = value);
        }
        catch
        {
            AutoStart = !value;
        }
    }

    partial void OnProtectSnapshotsChanged(bool value)
    {
        AppServices services = AppServices.Instance;

        if (value && !services.ProtectionIsEnabled)
        {
            string? password = PasswordWindow.AskNewPassword();
            if (password is null)
            {
                ProtectSnapshots = false;
                return;
            }

            services.SetProtectionPassword(password);
        }
        else if (!value && services.ProtectionIsEnabled)
        {
            services.DisableProtection();
            Apply(s => s.ProtectSnapshots = false);
        }
    }

    private void Apply(Action<AppSettings> change)
    {
        AppServices services = AppServices.Instance;
        change(services.Settings);
        services.SettingsStore.Save(services.Settings);

        if (services.Settings.Language != Loc.Instance.Language)
        {
            Loc.Instance.Language = services.Settings.Language;
            Loc.Instance.OnLanguageChanged();
        }

        services.Rebuild(services.SessionPassword);
    }

    [RelayCommand]
    private Task CheckUpdatesAsync() => Services.UpdateService.CheckAsync(manual: true);

    [RelayCommand]
    private void Close() => _close();
}
