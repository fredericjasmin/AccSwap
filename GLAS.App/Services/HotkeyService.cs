using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GLAS.App.Services;

/// <summary>
/// Global hotkey Ctrl+Alt+G that shows/toggles the main window.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyIdValue = 0x4753; // 'GS'

    [Flags]
    private enum Modifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
    }

    private HwndSource? _source;
    private IntPtr _windowHandle;

    public event Action? HotkeyPressed;

    public void Register(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WndProc);

        RegisterHotKey(_windowHandle, HotkeyIdValue, (uint)(Modifiers.Control | Modifiers.Alt), (uint)'G');
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyIdValue)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(_windowHandle, HotkeyIdValue);
        }

        _source?.RemoveHook(WndProc);
        _source = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
