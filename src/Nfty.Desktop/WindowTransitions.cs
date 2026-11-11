using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Nfty.Desktop;

/// <summary>
/// Minimizes a window with no animation, for putting several windows away at once.
/// </summary>
/// <remarks>Windows animates every minimize: the window shrinks toward the taskbar, drawn above
/// everything. When the badge (or Ctrl+`) puts all the viewers away, the click has just raised the
/// main window over them, so each viewer flashed back into view for that animation before it went:
/// a flicker per window. Turning the transition off for the one minimize hides them at once. The
/// attribute is put back afterwards, so the window's own minimize button keeps the system's
/// animation. Elsewhere this is an ordinary minimize.</remarks>
internal static class WindowTransitions
{
    private const int DwmwaTransitionsForceDisabled = 3;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    /// <summary>Minimizes <paramref name="window"/> without the system's minimize animation.</summary>
    /// <param name="window">The window to put away.</param>
    public static void MinimizeInstantly(Window window)
    {
        nint handle = OperatingSystem.IsWindows() ? window.TryGetPlatformHandle()?.Handle ?? 0 : 0;
        if (handle == 0)
        {
            window.WindowState = WindowState.Minimized;
            return;
        }
        Set(handle, disabled: true);
        window.WindowState = WindowState.Minimized;
        // After the minimize has been processed, not inside it: the animation is chosen as it starts.
        Dispatcher.UIThread.Post(() => Set(handle, disabled: false), DispatcherPriority.Background);
    }

    private static void Set(nint handle, bool disabled)
    {
        int value = disabled ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, DwmwaTransitionsForceDisabled, ref value, sizeof(int));
    }
}
