using System.Windows;
using System.Windows.Controls;
using GLAS.App.Localization;

namespace GLAS.App.Services;

/// <summary>
/// Window-based dialogs used from the view models.
/// </summary>
public static class UiDialogs
{
    public static string? AskText(string title, string label, string initial = "")
    {
        var window = new Window
        {
            Title = title,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = ActiveOwner(),
        };

        var textbox = new TextBox
        {
            Margin = new Thickness(0, 10, 0, 20),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 14,
            Text = initial,
        };
        textbox.SelectAll();

        var accept = new Button
        {
            Content = Loc.Instance["Account.Rename"] == "Rename" ? "OK" : "Aceptar",
            Width = 100,
            IsDefault = true,
        };
        var cancel = new Button
        {
            Content = Loc.Instance["Common.Close"] == "Close" ? "Cancel" : "Cancelar",
            Width = 100,
            IsCancel = true,
            Margin = new Thickness(10, 0, 0, 0),
        };
        accept.Click += (_, _) => window.Close();
        cancel.Click += (_, _) => textbox.Clear();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 13, Opacity = 0.8 });
        panel.Children.Add(textbox);
        panel.Children.Add(buttons);
        window.Content = panel;

        bool cancelled = false;
        cancel.Click += (_, _) => cancelled = true;
        textbox.Loaded += (_, _) => textbox.Focus();
        window.ShowDialog();

        return cancelled || string.IsNullOrWhiteSpace(textbox.Text) ? null : textbox.Text.Trim();
    }

    public static bool Confirm(string title, string body) =>
        MessageBox.Show(ActiveOwner(), body, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public static void Error(string title, string message) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(string title, string message) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public static string? AskSaveFile(string title, string filter, string fileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, Filter = filter, FileName = fileName };
        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    }

    public static string? AskOpenFile(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    }

    private static Window? ActiveOwner() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}
