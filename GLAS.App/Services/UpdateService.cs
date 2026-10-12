using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GLAS.App.Localization;

namespace GLAS.App.Services;

/// <summary>
/// Update flow shared by the startup check and the Settings button:
///   1. ask ("download and install now?"),
///   2. download the release's portable exe with a tiny progress window,
///   3. swap it in place (current exe → .old),
///   4. offer to restart right away.
/// Development builds (run from a bin\ folder) never self-replace; they
/// just open the release page instead.
/// </summary>
public static class UpdateService
{
    public static async Task CheckAsync(bool manual)
    {
        UpdateChecker.ReleaseInfo? release = await UpdateChecker.GetLatestReleaseAsync();
        if (release is null)
        {
            if (manual)
            {
                UiDialogs.Info(Loc.Instance["Update.Title"], Loc.Instance["Update.Unavailable"]);
            }

            return;
        }

        if (!UpdateChecker.IsNewer(release.Tag))
        {
            if (manual)
            {
                UiDialogs.Info(
                    Loc.Instance["Update.Title"],
                    string.Format(Loc.Instance["Update.UpToDate"], AppInfo.Version));
            }

            return;
        }

        if (!UiDialogs.Confirm(
                Loc.Instance["Update.Title"],
                string.Format(Loc.Instance["Update.Available"], release.Tag)))
        {
            return;
        }

        UpdateChecker.Asset? asset = UpdateChecker.FindExeAsset(release);
        if (asset is null || !IsPortableInstall())
        {
            // No downloadable exe attached, or a dev build: open the page.
            OpenPage(release.HtmlUrl);
            return;
        }

        string downloaded;
        var progress = new ProgressWindow(Loc.Instance["Update.Downloading"]);
        try
        {
            downloaded = await UpdateChecker.DownloadAssetAsync(asset);
        }
        catch (Exception exception)
        {
            UiDialogs.Error(Loc.Instance["Common.Error"], exception.Message);
            return;
        }
        finally
        {
            progress.Close();
        }

        ApplyUpdate(downloaded);

        if (UiDialogs.Confirm(
                Loc.Instance["Update.Title"],
                string.Format(Loc.Instance["Update.Ready"], release.Tag)))
        {
            RestartApp();
        }
        else
        {
            UiDialogs.Info(Loc.Instance["Update.Title"], Loc.Instance["Update.ReadyNextStart"]);
        }
    }

    /// <summary>True when the running exe is a published build, not bin\Debug output.</summary>
    private static bool IsPortableInstall() =>
        Environment.ProcessPath is { } exe
        && !exe.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Swaps the new exe in: running.exe → running.exe.old, new file → running.exe.
    /// Windows allows renaming a running exe, so this works without a helper
    /// process. The .old file is cleaned up on the next startup.
    /// </summary>
    private static void ApplyUpdate(string newExe)
    {
        string current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot locate the running executable.");
        string old = current + ".old";

        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            File.Move(current, old);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                Loc.Instance["Update.ApplyFailed"] + " " + exception.Message, exception);
        }

        try
        {
            File.Copy(newExe, current, overwrite: true);
        }
        catch
        {
            // Put the original back; the .old file stays for the next start.
            File.Move(old, current);
            throw;
        }
    }

    private static void RestartApp()
    {
        if (Environment.ProcessPath is { } exe)
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }

        App.RealExitRequested = true;
        Application.Current.Shutdown();
    }

    private static void OpenPage(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>Removes the leftover backup from a previous self-update.</summary>
    public static void CleanupPreviousUpdate()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        try
        {
            string old = exe + ".old";
            if (File.Exists(old))
            {
                File.Delete(old);
            }
        }
        catch
        {
            // Best effort: a locked .old disappears eventually.
        }
    }

    /// <summary>Small non-cancellable progress dialog for the download.</summary>
    private sealed class ProgressWindow : Window
    {
        public ProgressWindow(string text)
        {
            Title = Loc.Instance["Update.Title"];
            Width = 340;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current?.MainWindow;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = TryFindResource("WindowBackground") as System.Windows.Media.Brush
                ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x24, 0x2B));

            var panel = new StackPanel { Margin = new Thickness(24) };
            panel.Children.Add(new TextBlock { Text = text, FontSize = 13 });
            var bar = new ProgressBar
            {
                Margin = new Thickness(0, 14, 0, 0),
                Height = 12,
                IsIndeterminate = true,
            };
            panel.Children.Add(bar);
            Content = panel;

            Show();
        }
    }
}
