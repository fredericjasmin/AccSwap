using System.Windows;
using System.Windows.Controls;
using GLAS.App.Localization;
using GLAS.App.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using GLAS.Core.Platforms;

namespace GLAS.App.Services;

/// <summary>
/// Tray icon with a dynamic context menu: per-platform submenus listing the
/// saved accounts for one-click switching.
/// </summary>
public sealed class TrayService : IDisposable
{
    private TaskbarIcon? _icon;
    private MainViewModel? _viewModel;
    private Window? _mainWindow;

    public void Initialize(MainViewModel viewModel, Window mainWindow)
    {
        _viewModel = viewModel;
        _mainWindow = mainWindow;
        viewModel.AccountsChanged += OnAccountsChanged;

        _icon = new TaskbarIcon
        {
            Icon = AppIcon.GetTrayIcon(),
            ToolTipText = "GLAS",
        };
        _icon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
        _ = RebuildMenuAsync();
    }

    private void OnAccountsChanged() => _ = RebuildMenuAsync();

    private int _menuBuildInFlight;

    /// <summary>
    /// Async on purpose: the account list is loaded with awaits, and blocking
    /// on them from the UI thread deadlocks the dispatcher (the window never
    /// appears). Fire-and-forget is fine here: the menu just appears when
    /// the data is ready.
    /// </summary>
    private async Task RebuildMenuAsync()
    {
        if (_icon is null || _viewModel is null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _menuBuildInFlight, 1) == 1)
        {
            return; // a rebuild is already running; the next refresh rebuilds again
        }

        try
        {
        Loc loc = Loc.Instance;
        var menu = new ContextMenu();

        // The app-wide implicit TextBlock style is light; the native menu
        // background is white. Scope a black-text style to this menu only
        // so its entries stay readable without touching the theme.
        menu.Resources[typeof(System.Windows.Controls.TextBlock)] = new System.Windows.Style(
            typeof(System.Windows.Controls.TextBlock))
        {
            Setters =
            {
                new System.Windows.Setter(
                    System.Windows.Controls.TextBlock.ForegroundProperty,
                    System.Windows.SystemColors.WindowTextBrush),
            },
        };
        menu.Resources[typeof(Separator)] = new System.Windows.Style(typeof(Separator))
        {
            Setters =
            {
                new System.Windows.Setter(
                    Separator.BackgroundProperty,
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD3, 0xD8))),
                new System.Windows.Setter(
                    Separator.ForegroundProperty,
                    new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD0, 0xD3, 0xD8))),
            },
        };

        foreach (PlatformPanelViewModel platform in _viewModel.Platforms)
        {
            IPlatform? core = AppServices.Instance.Platforms.Find(platform.Id);
            if (core is null)
            {
                continue;
            }

            var platformItem = new MenuItem { Header = platform.Name };
            IReadOnlyList<Core.Account> accounts = await core.ListAccountsAsync();
            if (accounts.Count == 0)
            {
                var empty = new MenuItem
                {
                    Header = loc["Accounts.Empty.Title"],
                    IsEnabled = false,
                };
                platformItem.Items.Add(empty);
            }

            foreach (Core.Account account in accounts)
            {
                var accountItem = new MenuItem
                {
                    Header = account.DisplayLabel,
                    IsCheckable = false,
                    IsChecked = false,
                    FontWeight = account.IsActive ? FontWeights.SemiBold : FontWeights.Normal,
                };
                string accountId = account.Id;
                string platformId = platform.Id;
                accountItem.Click += async (_, _) =>
                {
                    await _viewModel.SwitchToAccountAsync(platformId, accountId);
                };
                platformItem.Items.Add(accountItem);
            }

            menu.Items.Add(platformItem);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem(loc["Tray.Show"], ShowMainWindow));
        menu.Items.Add(MakeItem(loc["Tray.Exit"], ExitApp));

        _icon.ContextMenu = menu;
        }
        finally
        {
            Volatile.Write(ref _menuBuildInFlight, 0);
        }
    }

    private static MenuItem MakeItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private static void ExitApp()
    {
        App.RealExitRequested = true;
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_viewModel is not null)
        {
            _viewModel.AccountsChanged -= OnAccountsChanged;
        }

        _icon?.Dispose();
        _icon = null;
    }
}
