using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Controls;

namespace Nfty.Desktop;

/// <summary>
/// Attaches the window behavior the shared <see cref="WindowFrame"/> cannot own - move, resize,
/// maximize - to a real <see cref="Window"/>. One wiring for the main window and the viewer, so the
/// two behave alike as well as look alike.
/// </summary>
internal static class ChromeWiring
{
    /// <summary>Wires a window to its frame.</summary>
    /// <param name="window">The window the frame fills (decorations off).</param>
    /// <param name="frame">The frame.</param>
    /// <param name="captionCommands">True to give the caption buttons window-level commands; false
    /// when the frame's commands are bound to a view model (the shell's are).</param>
    public static void Attach(Window window, WindowFrame frame, bool captionCommands)
    {
        AppIcon.Apply(window);
        // The state full screen came from, so a drag out of it lands where the toggle would.
        var beforeFullScreen = WindowState.Normal;
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property != Window.WindowStateProperty || e.GetNewValue<WindowState>() != WindowState.FullScreen) return;
            var old = e.GetOldValue<WindowState>();
            beforeFullScreen = old is WindowState.Minimized or WindowState.FullScreen ? WindowState.Normal : old;
        };
        frame.DragRequested += e => BeginDrag(window, e, beforeFullScreen);
        frame.ToggleMaximizeRequested += () => Toggle(window);
        frame.ResizeRequested += (edge, e) =>
        {
            if (window.WindowState == WindowState.Normal && window.CanResize) window.BeginResizeDrag(edge, e);
        };

        if (!captionCommands) return;
        frame.MinimizeCommand = new RelayCommand(() => window.WindowState = WindowState.Minimized);
        frame.ToggleMaximizeCommand = new RelayCommand(() => Toggle(window));
        frame.CloseCommand = new RelayCommand(window.Close);
    }

    /// <summary>
    /// Starts a titlebar drag. A drag from full screen LEAVES full screen first, the way a drag
    /// from maximized restores the window: moving the full screen window as it was left a
    /// screen-sized window wherever it was dropped, still reported as full screen, so the
    /// titlebar's full screen button kept showing the wrong glyph.
    /// </summary>
    /// <remarks>Back to maximized, the system's own drag-from-maximized takes over. Back to normal,
    /// the window is placed so the pointer holds the same spot of the titlebar, proportionally
    /// across its width, and the drag carries on from there.</remarks>
    private static void BeginDrag(Window window, PointerPressedEventArgs e, WindowState restoreTo)
    {
        if (window.WindowState != WindowState.FullScreen)
        {
            window.BeginMoveDrag(e);
            return;
        }

        var local = e.GetPosition(window);
        double across = window.Bounds.Width > 0 ? Math.Clamp(local.X / window.Bounds.Width, 0, 1) : 0.5;
        var pointer = window.PointToScreen(local);

        window.WindowState = restoreTo;
        if (restoreTo == WindowState.Normal)
        {
            double scale = window.DesktopScaling;
            double width = window.ClientSize.Width * scale;
            window.Position = new PixelPoint(
                (int)Math.Round(pointer.X - across * width),
                (int)Math.Round(pointer.Y - local.Y * scale));
        }
        window.BeginMoveDrag(e);
    }

    private static void Toggle(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
