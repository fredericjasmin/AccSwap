using System.IO;
using MessageBox = System.Windows.MessageBox;
using System.Threading;
using System.Windows;
using GLAS.App.Localization;
using GLAS.App.Services;
using GLAS.App.ViewModels;

namespace GLAS.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private TrayService? _tray;

    public static bool RealExitRequested { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "GLAS.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "GLAS is already running. Check the system tray.",
                "GLAS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppServices services = AppServices.Instance;
        Loc.Instance.Language = services.Settings.Language;

        // Snapshot protection: unlock before building the platform graph.
        string? sessionPassword = null;
        if (services.ProtectionIsEnabled)
        {
            while (true)
            {
                sessionPassword = PasswordWindow.AskPassword(
                    Loc.Instance["Settings.Password.Unlock"]);
                if (sessionPassword is null)
                {
                    sessionPassword = null; // continue without unlocking
                    break;
                }

                if (services.VerifyPassword(sessionPassword))
                {
                    break;
                }

                UiDialogs.Error(
                    Loc.Instance["Common.Error"],
                    Loc.Instance["Settings.Password.Wrong"]);
            }
        }

        services.Rebuild(sessionPassword);

        var main = new MainViewModel();
        var window = new MainWindow(main);
        MainWindow = window;

        _tray = new TrayService();
        _tray.Initialize(main, window);

        if (services.Settings.StartMinimized)
        {
            window.Hide();
        }
        else
        {
            window.Show();
        }

        _ = CheckForUpdatesAsync(notifyUpToDate: false);
    }

    /// <summary>
    /// GitHub Releases update check. Silent failures: with no network or no
    /// published release the user simply hears nothing.
    /// </summary>
    private static async Task CheckForUpdatesAsync(bool notifyUpToDate)
    {
        UpdateChecker.ReleaseInfo? release = await UpdateChecker.GetLatestReleaseAsync();
        if (release is null)
        {
            if (notifyUpToDate)
            {
                UiDialogs.Info(
                    Loc.Instance["Update.Title"],
                    Loc.Instance["Update.Unavailable"]);
            }

            return;
        }

        if (UpdateChecker.IsNewer(release.Tag))
        {
            bool open = UiDialogs.Confirm(
                Loc.Instance["Update.Title"],
                string.Format(Loc.Instance["Update.Available"], release.Tag));
            if (open)
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(release.HtmlUrl) { UseShellExecute = true });
            }
        }
        else if (notifyUpToDate)
        {
            UiDialogs.Info(
                Loc.Instance["Update.Title"],
                string.Format(Loc.Instance["Update.UpToDate"], AppInfo.Version));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        AppServices.Instance.SettingsStore.Save();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
