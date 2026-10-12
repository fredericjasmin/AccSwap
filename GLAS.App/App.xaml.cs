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

        UpdateService.CleanupPreviousUpdate();

        if (services.Settings.StartMinimized)
        {
            window.Hide();
        }
        else
        {
            window.Show();
        }

        // GitHub Releases check: silent unless a newer version exists.
        _ = UpdateService.CheckAsync(manual: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        AppServices.Instance.SettingsStore.Save();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
