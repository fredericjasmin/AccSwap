using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AccSwap.App.Localization;
using AccSwap.App.Services;
using AccSwap.Core;
using AccSwap.Core.ExportImport;
using AccSwap.Core.Platforms;

namespace AccSwap.App.ViewModels;

public partial class AccountItemViewModel : ObservableObject
{
    public required string PlatformId { get; init; }

    public required string AccountId { get; init; }

    public required string Name { get; init; }

    public required string Username { get; init; }

    public required string CreatedText { get; init; }

    public required string Accent { get; init; }

    public string Initials => ComputeInitials();

    [ObservableProperty]
    private bool _isActive;

    public bool IsSwitchable => !IsActive;

    private string ComputeInitials()
    {
        string source = string.IsNullOrWhiteSpace(Name) ? Username : Name;
        string[] parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "?";
        }

        return parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant()
            : $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
    }
}

public partial class PlatformPanelViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Glyph { get; init; }

    public required string Accent { get; init; }

    public required bool IsInstalled { get; init; }

    public string DisplayLabel => IsInstalled
        ? Name
        : Name + Loc.Instance["AddAccount.NotInstalledSuffix"];
}

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<PlatformPanelViewModel> Platforms { get; } = [];

    [ObservableProperty]
    private PlatformPanelViewModel? _selectedPlatform;

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _showAccounts;

    [ObservableProperty]
    private bool _hasAccounts;

    /// <summary>Raised after every account refresh so the tray menu can rebuild.</summary>
    public event Action? AccountsChanged;

    [ObservableProperty]
    private string _emptyTitle = string.Empty;

    [ObservableProperty]
    private string _emptyBody = string.Empty;

    public Loc Loc => Loc.Instance;

    public MainViewModel()
    {
        LoadPlatforms();
    }

    public void LoadPlatforms()
    {
        // The ListBox clears SelectedPlatform while the list is empty, so
        // remember which platform the user was on and restore it: dialogs
        // (add account / settings) call this on close and must not yank the
        // user back to the first platform in the list.
        string? selectedId = SelectedPlatform?.Id;
        Platforms.Clear();
        foreach (IPlatform platform in AppServices.Instance.Platforms.Platforms)
        {
            (string glyph, string accent) = Services.PlatformVisuals.For(platform.Id);
            Platforms.Add(new PlatformPanelViewModel
            {
                Id = platform.Id,
                Name = platform.Name,
                Glyph = glyph,
                Accent = accent,
                IsInstalled = platform.IsInstalled,
            });
        }

        SelectedPlatform = Platforms.FirstOrDefault(p => p.Id == selectedId)
            ?? Platforms.FirstOrDefault();
    }

    partial void OnSelectedPlatformChanged(PlatformPanelViewModel? value)
    {
        // A status line from the previous platform ("Switched to...") must
        // not follow the user around.
        Status = string.Empty;
        _ = RefreshAccountsAsync();
    }

    public async Task RefreshAccountsAsync()
    {
        if (SelectedPlatform is null)
        {
            return;
        }

        IPlatform? platform = AppServices.Instance.Platforms.Find(SelectedPlatform.Id);
        if (platform is null)
        {
            return;
        }

        IReadOnlyList<Account> accounts = await platform.ListAccountsAsync();
        Accounts.Clear();
        foreach (Account account in accounts)
        {
            Accounts.Add(new AccountItemViewModel
            {
                PlatformId = platform.Id,
                AccountId = account.Id,
                Name = account.DisplayLabel,
                Username = account.Username,
                CreatedText = string.Format(
                    Loc.Instance["Account.Created"],
                    (account.CreatedAtUtc ?? DateTimeOffset.UtcNow).LocalDateTime.ToShortDateString()),
                IsActive = account.IsActive,
                Accent = SelectedPlatform.Accent,
            });
        }

        HasAccounts = Accounts.Count > 0;
        ShowAccounts = SelectedPlatform.IsInstalled;
        EmptyTitle = Loc.Instance["Accounts.Empty.Title"];
        EmptyBody = SelectedPlatform.IsInstalled
            ? Loc.Instance["Accounts.Empty.Body"]
            : string.Format(Loc.Instance["Accounts.NotInstalled"], SelectedPlatform.Name);
        AccountsChanged?.Invoke();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        LoadPlatforms();
        await RefreshAccountsAsync();
        Status = string.Empty;
    }

    [RelayCommand]
    private async Task SwitchAsync(AccountItemViewModel? account)
    {
        if (account is null || IsBusy)
        {
            return;
        }

        IPlatform? platform = AppServices.Instance.Platforms.Find(account.PlatformId);
        if (platform is null)
        {
            return;
        }

        IsBusy = true;
        Status = string.Format(Loc.Instance["Account.Switching"], account.Name);
        try
        {
            await RunWithStatusAsync(platform, () => platform.SwitchAsync(account.AccountId));
            await RefreshAccountsAsync();
            Status = string.Format(Loc.Instance["Account.SwitchDone"], account.Name);
        }
        catch (Exception exception)
        {
            Status = string.Empty;
            UiDialogs.Error(
                Loc.Instance["Common.Error"],
                string.Format(Loc.Instance["Account.SwitchFailed"], exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SwitchToAccountAsync(string platformId, string accountId)
    {
        IPlatform? platform = AppServices.Instance.Platforms.Find(platformId);
        if (platform is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunWithStatusAsync(platform, () => platform.SwitchAsync(accountId));
            await RefreshAccountsAsync();
        }
        catch
        {
            // Tray-initiated switches fail silently: the window may be hidden.
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunWithStatusAsync(IPlatform platform, Func<Task> action)
    {
        void OnStatus(string message) => Status = message;
        platform.StatusChanged += OnStatus;
        try
        {
            await action();
        }
        finally
        {
            platform.StatusChanged -= OnStatus;
        }
    }

    [RelayCommand]
    private async Task RenameAsync(AccountItemViewModel? account)
    {
        if (account is null)
        {
            return;
        }

        IPlatform? platform = AppServices.Instance.Platforms.Find(account.PlatformId);
        if (platform is null)
        {
            return;
        }

        string? newName = UiDialogs.AskText(
            Loc.Instance["Account.Rename"],
            Loc.Instance["AddAccount.NameLabel"],
            account.Name);
        if (newName is null)
        {
            return;
        }

        await platform.RenameAccountAsync(account.AccountId, newName);
        await RefreshAccountsAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(AccountItemViewModel? account)
    {
        if (account is null)
        {
            return;
        }

        IPlatform? platform = AppServices.Instance.Platforms.Find(account.PlatformId);
        if (platform is null)
        {
            return;
        }

        bool confirmed = UiDialogs.Confirm(
            Loc.Instance["Confirm.DeleteTitle"],
            string.Format(Loc.Instance["Confirm.DeleteBody"], account.Name));
        if (!confirmed)
        {
            return;
        }

        await platform.DeleteAccountAsync(account.AccountId);
        await RefreshAccountsAsync();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        new SettingsWindow { Owner = System.Windows.Application.Current.MainWindow }
            .ShowDialog();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private void AddAccount()
    {
        if (SelectedPlatform is null)
        {
            return;
        }

        if (!SelectedPlatform.IsInstalled)
        {
            UiDialogs.Error(
                Loc.Instance["Common.Error"],
                string.Format(Loc.Instance["Accounts.NotInstalled"], SelectedPlatform.Name));
            return;
        }

        var window = new AddAccountWindow(SelectedPlatform.Id)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        window.ShowDialog();
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task SaveCurrentSessionAsync()
    {
        if (SelectedPlatform is null || IsBusy)
        {
            return;
        }

        IPlatform? platform = AppServices.Instance.Platforms.Find(SelectedPlatform.Id);
        if (platform is null)
        {
            return;
        }

        if (!platform.IsInstalled)
        {
            UiDialogs.Error(
                Loc.Instance["Common.Error"],
                string.Format(Loc.Instance["Accounts.NotInstalled"], platform.Name));
            return;
        }

        string? name = UiDialogs.AskText(
            Loc.Instance["Toolbar.SaveCurrent"],
            Loc.Instance["AddAccount.NameLabel"]);
        if (name is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await RunWithStatusAsync(platform, () => platform.CaptureCurrentSessionAsync(name));
            await RefreshAccountsAsync();
            Status = string.Format(Loc.Instance["Capture.Saved"], name);
        }
        catch (Exception exception)
        {
            Status = string.Empty;
            UiDialogs.Error(
                Loc.Instance["Common.Error"],
                string.Format(Loc.Instance["Capture.Failed"], exception.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        string? path = UiDialogs.AskSaveFile(
            Loc.Instance["Export.Title"],
            "Paquete AccSwap (*.accswap)|*.accswap",
            "accswap-backup.accswap");
        if (path is null)
        {
            return;
        }

        try
        {
            List<string> platformIds = AppServices.Instance.Platforms.Platforms.Select(p => p.Id).ToList();
            await AccountPackage.ExportAsync(
                AppServices.Instance.Store,
                platformIds,
                path,
                AppServices.Instance.SessionPassword);
            Status = string.Format(Loc.Instance["Export.Done"], path);
        }
        catch (Exception exception)
        {
            UiDialogs.Error(Loc.Instance["Common.Error"], exception.Message);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        string? path = UiDialogs.AskOpenFile(
            Loc.Instance["Import.Title"],
            "Paquete AccSwap (*.accswap)|*.accswap");
        if (path is null)
        {
            return;
        }

        try
        {
            AccountPackage.ImportResult result = await AccountPackage.ImportAsync(
                AppServices.Instance.Store,
                path,
                AppServices.Instance.SessionPassword);
            await RefreshAccountsAsync();
            UiDialogs.Info(
                Loc.Instance["Import.Title"],
                result.Skipped > 0
                    ? string.Format(Loc.Instance["Import.DoneSkipped"], result.Added, result.Skipped)
                    : string.Format(Loc.Instance["Import.Done"], result.Added));
        }
        catch
        {
            // Package may carry its own password; ask once.
            string? password = PasswordWindow.AskPassword(
                Loc.Instance["Import.PasswordNeeded"]);
            if (password is null)
            {
                return;
            }

            try
            {
                AccountPackage.ImportResult result = await AccountPackage.ImportAsync(
                    AppServices.Instance.Store,
                    path,
                    password);
                await RefreshAccountsAsync();
                UiDialogs.Info(
                    Loc.Instance["Import.Title"],
                    result.Skipped > 0
                        ? string.Format(Loc.Instance["Import.DoneSkipped"], result.Added, result.Skipped)
                        : string.Format(Loc.Instance["Import.Done"], result.Added));
            }
            catch (Exception exception)
            {
                UiDialogs.Error(Loc.Instance["Common.Error"], exception.Message);
            }
        }
    }
}
