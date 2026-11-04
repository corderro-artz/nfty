using Avalonia.Controls;
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
        frame.DragRequested += window.BeginMoveDrag;
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

    private static void Toggle(Window window) =>
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
