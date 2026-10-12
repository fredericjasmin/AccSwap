using System.Windows;
using System.Windows.Input;
using GLAS.App.ViewModels;

namespace GLAS.App;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        var viewModel = new SettingsViewModel(Close);
        InitializeComponent();
        Icon = Services.AppIcon.GetWindowIcon();
        DataContext = viewModel;

        // Standard dialog behaviour: Esc closes.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }
}
