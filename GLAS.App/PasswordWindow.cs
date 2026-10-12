using System.Windows;
using System.Windows.Controls;
using GLAS.App.Localization;

namespace GLAS.App;

/// <summary>
/// Password prompts built in code: unlock an existing protection password,
/// or create a new one with confirmation.
/// </summary>
public static class PasswordWindow
{
    public static string? AskPassword(string title)
    {
        Loc loc = Loc.Instance;
        var window = new Window
        {
            Title = loc["Settings.Password.Title"],
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = Application.Current?.MainWindow,
        };

        var box = new PasswordBox
        {
            Margin = new Thickness(0, 10, 0, 20),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 14,
        };

        var error = new TextBlock
        {
            FontSize = 12,
            Foreground = System.Windows.Media.Brushes.IndianRed,
            Text = loc["Settings.Password.Wrong"],
            Visibility = Visibility.Collapsed,
        };

        string? result = null;
        var accept = new Button { Content = loc["Settings.Password.Accept"], Width = 110, IsDefault = true };
        var skip = new Button
        {
            Content = loc["Settings.Password.Skip"],
            Width = 110,
            Margin = new Thickness(10, 0, 0, 0),
        };
        accept.Click += (_, _) =>
        {
            result = box.Password;
            window.Close();
        };
        skip.Click += (_, _) =>
        {
            result = null;
            window.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(skip);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(error);
        panel.Children.Add(buttons);
        window.Content = panel;

        box.Loaded += (_, _) => box.Focus();
        window.ShowDialog();
        return result;
    }

    public static string? AskNewPassword()
    {
        Loc loc = Loc.Instance;
        var window = new Window
        {
            Title = loc["Settings.Password.Title"],
            Width = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = Application.Current?.MainWindow,
        };

        var first = new PasswordBox { Margin = new Thickness(0, 10, 0, 6), Padding = new Thickness(8, 6, 8, 6), FontSize = 14 };
        var second = new PasswordBox { Margin = new Thickness(0, 6, 0, 12), Padding = new Thickness(8, 6, 8, 6), FontSize = 14 };

        var error = new TextBlock
        {
            FontSize = 12,
            Foreground = System.Windows.Media.Brushes.IndianRed,
            Text = loc["Settings.Password.Mismatch"],
            Visibility = Visibility.Collapsed,
        };

        string? result = null;
        var accept = new Button { Content = loc["Settings.Password.Accept"], Width = 110, IsDefault = true };
        var cancel = new Button { Content = loc["AddAccount.Cancel"], Width = 110, Margin = new Thickness(10, 0, 0, 0), IsCancel = true };
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(first.Password) || first.Password != second.Password)
            {
                error.Visibility = Visibility.Visible;
                return;
            }

            result = first.Password;
            window.Close();
        };
        cancel.Click += (_, _) => { result = null; window.Close(); };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = loc["Settings.Password.New"], FontSize = 13 });
        panel.Children.Add(first);
        panel.Children.Add(new TextBlock { Text = loc["Settings.Password.Confirm"], FontSize = 13 });
        panel.Children.Add(second);
        panel.Children.Add(error);
        panel.Children.Add(buttons);
        window.Content = panel;

        first.Loaded += (_, _) => first.Focus();
        window.ShowDialog();
        return result;
    }
}
