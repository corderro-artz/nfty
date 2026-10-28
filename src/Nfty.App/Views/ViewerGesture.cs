using Avalonia.Input;

namespace Nfty.App.Views;

/// <summary>
/// The one definition of "open this in a viewer" as a pointer gesture: a middle-button press, or a
/// left press with Ctrl held. Every opening point asks here, so the gesture cannot mean two things.
/// </summary>
public static class ViewerGesture
{
    /// <summary>Whether a press is the viewer gesture.</summary>
    /// <param name="e">The press, read relative to <paramref name="relativeTo"/>.</param>
    /// <param name="relativeTo">The control the handler is on.</param>
    /// <returns>True for middle button, or left button with Ctrl.</returns>
    public static bool IsViewerPress(PointerPressedEventArgs e, Avalonia.Visual relativeTo)
    {
        var p = e.GetCurrentPoint(relativeTo);
        return p.Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed
            || (p.Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed
                && (e.KeyModifiers & KeyModifiers.Control) != 0);
    }
}
