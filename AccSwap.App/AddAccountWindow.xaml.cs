using System.Windows;
using System.Windows.Input;
using AccSwap.App.ViewModels;

namespace AccSwap.App;

public partial class AddAccountWindow : Window
{
    public AddAccountViewModel ViewModel { get; }

    public AddAccountWindow(string platformId)
    {
        ViewModel = new AddAccountViewModel(Close, platformId);
        InitializeComponent();
        Icon = Services.AppIcon.GetWindowIcon();
        DataContext = ViewModel;

        // Standard dialog behaviour: Esc cancels, Enter accepts the visible step.
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
