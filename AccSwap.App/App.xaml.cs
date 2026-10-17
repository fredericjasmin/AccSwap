using System.IO;
using System.Threading;
using System.Windows;
using AccSwap.App.Localization;
using AccSwap.App.Services;
using AccSwap.App.ViewModels;

namespace AccSwap.App;

public partial class App : Application
{
    private const string ShowSignalName = "AccSwap.ShowInstance";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _showSignal;
    private TrayService? _tray;

    public static bool RealExitRequested { get; set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, "AccSwap.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // A second launch shows the running instance's window instead of
            // nagging the user about it.
            using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
            signal.Set();
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

        if (services.Settings.AutoStart)
        {
            try
            {
                AutoStartService.RepairEntry(services.Registry);
            }
            catch
            {
                // Best effort: a stale entry only means no autostart.
            }
        }

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

        StartShowSignalListener();
    }

    /// <summary>
    /// Listens for a second launch: the new process sets the named signal
    /// and dies; this instance brings its window to the front.
    /// </summary>
    private void StartShowSignalListener()
    {
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        var listener = new Thread(WaitForShowSignal)
        {
            IsBackground = true,
            Name = "AccSwap.ShowSignalListener",
        };
        listener.Start();
    }

    private void WaitForShowSignal()
    {
        while (_showSignal is { } signal && signal.WaitOne())
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is not { } window)
                {
                    return;
                }

                window.Show();
                window.WindowState = WindowState.Normal;
                window.Activate();
            });
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
