using Avalonia.Controls;
using Avalonia.Threading;

namespace Nfty.App.Tests;

/// <summary>Shows a control under the themed headless app and lays it out, so applied style
/// values (fonts, brushes) can be read back in tests.
///
/// Rather than calling <c>ExecuteInitialLayoutPass</c> directly, this flushes the dispatcher queue,
/// which runs the layout pass Avalonia already scheduled when the window was shown — see "Flushing
/// async operations" in the Avalonia headless testing docs. The original reason was that
/// <c>Window.LayoutManager</c>'s getter was non-public; this comment named Avalonia <b>11.2.3</b>
/// long after the pin moved to 12.1.1 (<c>Directory.Packages.props</c>), which is the same staleness
/// <c>RowChunkConverter</c> records having hit. <b>Whether that getter is still non-public in 12.1.1
/// has NOT been re-verified</b> — flushing the queue is correct either way, so the note is left as
/// provenance rather than rewritten into a claim nobody checked.
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
