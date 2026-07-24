using Avalonia.Controls;
using Avalonia.Threading;

namespace Nfty.App.Tests;

/// <summary>Shows a control under the themed headless app and lays it out, so applied style
/// values (fonts, brushes) can be read back in tests.
///
/// <c>Window.LayoutManager</c> is not publicly accessible in Avalonia 11.2.3 (its getter is
/// non-public), so instead of calling <c>ExecuteInitialLayoutPass</c> directly we flush the
/// dispatcher queue, which runs the layout pass Avalonia already scheduled when the window
/// was shown — see "Flushing async operations" in the Avalonia headless testing docs.</summary>
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
