using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LinearGradientBrush = System.Drawing.Drawing2D.LinearGradientBrush;
using Pen = System.Drawing.Pen;

namespace GLAS.App.Services;

/// <summary>
/// Draws the GLAS "G" icon at runtime (no binary asset needed) and exposes
/// it both as a WinForms icon for the tray and as an ImageSource for windows.
/// </summary>
public static class AppIcon
{
    private static Icon? _cachedIcon;
    private static ImageSource? _cachedSource;

    public static Icon GetTrayIcon() => _cachedIcon ??= CreateIcon();

    public static ImageSource GetWindowIcon() => _cachedSource ??= CreateWindowIcon();

    private static Icon CreateIcon()
    {
        using Bitmap bitmap = DrawBitmap();
        IntPtr handle = bitmap.GetHicon();
        return (Icon)Icon.FromHandle(handle).Clone();
    }

    private static ImageSource CreateWindowIcon()
    {
        using Bitmap bitmap = DrawBitmap();
        IntPtr handle = bitmap.GetHicon();
        using Icon icon = Icon.FromHandle(handle);
        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static Bitmap DrawBitmap()
    {
        var bitmap = new Bitmap(32, 32);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);

        // Rounded square with a subtle diagonal.
        using var path = new GraphicsPath();
        int r = 8;
        path.AddArc(0, 0, r * 2, r * 2, 180, 90);
        path.AddArc(32 - r * 2, 0, r * 2, r * 2, 270, 90);
        path.AddArc(32 - r * 2, 32 - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(0, 32 - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();

        using var background = new LinearGradientBrush(
            new System.Drawing.Point(0, 0),
            new System.Drawing.Point(32, 32),
            System.Drawing.Color.FromArgb(255, 46, 54, 67),
            System.Drawing.Color.FromArgb(255, 24, 28, 36));
        graphics.FillPath(background, path);

        using var pen = new Pen(System.Drawing.Color.FromArgb(255, 102, 192, 244), 3.5f);
        // Open "G": circle with a gap and a crossbar.
        graphics.DrawArc(pen, 8, 8, 16, 16, -35, 270);
        graphics.DrawLine(pen, 16, 16, 23, 16);

        return bitmap;
    }
}
