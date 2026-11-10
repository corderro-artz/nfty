using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Desktop;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Window zoom (Ctrl +/−/0) at the smallest window. Every page is built to fit
/// <see cref="ShellViewModel.MinPageArea"/>; zooming in must not squeeze it below that (the page would clip),
/// so the shell lays it out at no less than that area and scrolls the rest. At 100% nothing scrolls.
/// </summary>
public class ZoomFitTests
{
    private static (MainWindow Window, ShellViewModel Shell, ExplorerViewModel Page) Open()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var explorer = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs,
            new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService());
        nav.To(explorer);
        var window = new MainWindow { DataContext = shell, Width = ShellViewModel.MinWindowWidth, Height = ShellViewModel.MinWindowHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, shell, explorer);
    }

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaTheory]
    [InlineData(50)]
    [InlineData(80)]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(300)]
    public void Every_zoom_gives_the_page_at_least_the_room_it_was_built_for(int zoom)
    {
        var (window, shell, explorer) = Open();
        try
        {
            shell.Zoom = zoom;
            Dispatcher.UIThread.RunJobs();
            var host = Named<ContentControl>(window, "PageHost");
            var scroller = Named<ScrollViewer>(window, "PageScroller");
            var min = ShellViewModel.MinPageArea;
            double scale = zoom / 100.0;

            if (zoom > 100)
            {
                Assert.True(host.Bounds.Width >= min.Width - 0.5, $"page {host.Bounds.Width:0} wide at {zoom}%, needs {min.Width:0}");
                Assert.True(host.Bounds.Height >= min.Height - 0.5, $"page {host.Bounds.Height:0} tall at {zoom}%, needs {min.Height:0}");
            }
            else
            {
                // At 100% and below the page is exactly the viewport and nothing scrolls.
                Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
                Assert.Equal(ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);
                Assert.InRange(scroller.Bounds.Width - host.Bounds.Width * scale, -1, 1);    // layout rounding only
                Assert.InRange(scroller.Bounds.Height - host.Bounds.Height * scale, -1, 1);
            }

            // The page lays out inside what it is given: nothing is cut off by the zoom.
            var page = host.GetVisualDescendants().OfType<Views.ExplorerView>().Single();
            Assert.True(page.Bounds.Width <= host.Bounds.Width + 0.5 && page.Bounds.Height <= host.Bounds.Height + 0.5);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Zooming_in_scrolls_the_window_and_zooming_back_stops_it()
    {
        var (window, shell, _) = Open();
        try
        {
            var scroller = Named<ScrollViewer>(window, "PageScroller");
            shell.Zoom = 200;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ScrollBarVisibility.Auto, scroller.VerticalScrollBarVisibility);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height);

            shell.ZoomResetCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, scroller.HorizontalScrollBarVisibility);
        }
        finally { window.Close(); }
    }
}
