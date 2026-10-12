using System.ComponentModel;
using System.Windows;
using GLAS.App.Services;
using GLAS.App.ViewModels;
using GLAS.Core.Settings;

namespace GLAS.App;

public partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public HotkeyService Hotkey { get; } = new();

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Icon = AppIcon.GetWindowIcon();
        DataContext = viewModel;

        Hotkey.HotkeyPressed += ToggleFromTray;
        Loaded += (_, _) => Hotkey.Register(this);
    }

    public void ToggleFromTray()
    {
        if (IsVisible)
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
                Activate();
            }
            else
            {
                Hide();
            }
        }
        else
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (App.RealExitRequested || !AppServices.Instance.Settings.CloseToTray)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        Hotkey.Dispose();
        base.OnClosed(e);
    }
}
