using Avalonia.Controls;
using Avalonia.Threading;

namespace Nfty.App.Tests;

/// <summary>Shows a control under the themed headless app and lays it out, so applied style
/// values (fonts, brushes) can be read back in tests.
///
/// <c>Window.LayoutManager</c> is not publicly accessible in Avalonia 11.2.3 (its getter is
/// non-public), so instead of calling <c>ExecuteInitialLayoutPass</c> directly we flush the
/// dispatcher queue, which runs the layout pass Avalonia already scheduled when the window
/// was shown — see "Flushing async operations" in the Avalonia headless testing docs.
///
/// Each call opens its own headless <c>Window</c> and never closes it — callers read applied
/// style values (e.g. <c>FontFamily</c>) off the returned control *after* <c>Show</c> returns,
/// and some tests call <c>Show</c> more than once per test to compare two controls side by
/// side; closing (or reusing a single shared window, swapping its <c>Content</c>) detaches the
/// earlier control from the visual tree and reverts its style-applied values to unset before
/// the assertion runs. So the window is intentionally leaked for the test process's lifetime —
/// headless windows are cheap, and this keeps every existing call site correct.</summary>
public static class StyledHost
{
    public static T Show<T>(T control) where T : Control
    {
        var window = new Window { Content = control, Width = 200, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return control;
    }
}
