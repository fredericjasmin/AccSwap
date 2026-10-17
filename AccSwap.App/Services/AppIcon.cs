using System.Drawing;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AccSwap.App.Services;

/// <summary>
/// The brand icon (logo.ico, embedded as a WPF Resource) exposed both as a
/// WinForms icon for the tray and as an ImageSource for windows. The exe
/// file icon itself comes from ApplicationIcon in the csproj.
/// </summary>
public static class AppIcon
{
    private const string IconPackUri = "pack://application:,,,/logo.ico";

    private static Icon? _cachedIcon;
    private static ImageSource? _cachedSource;

    public static Icon GetTrayIcon() => _cachedIcon ??= CreateIcon();

    public static ImageSource GetWindowIcon() => _cachedSource ??= CreateWindowIcon();

    private static Icon CreateIcon()
    {
        using var stream = OpenIconStream();
        // 32px matches the tray; the ICO carries all sizes.
        using Icon full = new(stream);
        return new Icon(full, 32, 32);
    }

    private static ImageSource CreateWindowIcon()
    {
        using var stream = OpenIconStream();
        BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    private static System.IO.Stream OpenIconStream() =>
        Application.GetResourceStream(new Uri(IconPackUri))?.Stream
        ?? throw new InvalidOperationException($"Embedded icon not found: {IconPackUri}");
}
