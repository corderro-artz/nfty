using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// <see cref="ShellViewModel.ChromeReserve"/> is what a modal does NOT get to use, and every
/// modal-fit number is computed from it.
/// </summary>
/// <remarks>
/// <para><b>It was three literals restating the markup, and one of them was wrong.</b> The constant
/// read <c>40 + 30 + 24</c> while the status bar is 34 tall with a 1px top border — so it
/// under-reserved by five pixels, and every "does this modal fit" calculation in the app was five
/// pixels optimistic. Its own summary said it was "named so the minimum above and the test that
/// checks it agree by construction"; nothing checked it, and they did not agree.</para>
///
/// <para>This measures the real chrome off a rendered shell instead. A bar that changes height now
/// fails here with the number the constant should be, rather than silently making every other
/// measurement in the app slightly wrong.</para>
/// </remarks>
public class ChromeReserveTests
{
    /// <summary>The gutter the frame's shadow needs outside the page area. Not measurable off the
    /// chrome — it is the window's own drop shadow — so it stays a stated number, and it is the only
    /// part of the reserve that is.</summary>
    private const double ShadowGutter = 24;

    private static (double Titlebar, double StatusBar) MeasureChrome()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService());
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1416, Height = 864 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The two bars are the only Grids in the chrome with a pinned Height; everything else
            // sizes to content. Read off the laid-out control rather than the markup, so a border
            // or a padding that adds to the real height is counted.
            var pinned = view.GetVisualDescendants().OfType<Grid>()
                .Where(g => !double.IsNaN(g.Height))
                .OrderBy(g => ((Visual)g).TranslatePoint(default, view)?.Y ?? 0)
                .ToList();

            Assert.True(pinned.Count >= 2, "expected a pinned titlebar and status bar in the chrome");

            var top = pinned.First();
            var bottom = pinned.Last();

            // The status bar's border is outside its Grid; take the bar's own Border where there is
            // one, so the reserve counts what is actually painted.
            double statusHeight = bottom.FindAncestorOfType<Border>() is { } band
                && band.Bounds.Height >= bottom.Bounds.Height
                    ? band.Bounds.Height
                    : bottom.Bounds.Height;

            return (top.Bounds.Height, statusHeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_reserve_matches_the_chrome_that_is_actually_drawn()
    {
        var (titlebar, statusBar) = MeasureChrome();

        Assert.Equal(ShellViewModel.ChromeReserve, titlebar + statusBar + ShadowGutter, 1);
    }

    [AvaloniaFact]
    public void The_titlebar_stays_taller_than_the_status_bar()
    {
        // A deliberate asymmetry, and a user-directed one: the titlebar carries the brand, the
        // document name and the window buttons, and reads as the top of the frame rather than as a
        // matching pair of rails. Equal heights make the window look like it has two footers.
        var (titlebar, statusBar) = MeasureChrome();

        Assert.True(titlebar > statusBar,
            $"the titlebar is {titlebar:0} and the status bar {statusBar:0}; the top bar carries "
            + "more and should read as heavier");
    }

    [AvaloniaFact]
    public void The_minimum_window_still_clears_the_tallest_modal()
    {
        // The reserve feeds every modal-fit calculation, so a change to it has to be checked against
        // the tallest card rather than only against itself. The help sheet is that card; the export
        // dialog is adaptive and does not demand a height.
        var sheet = new Views.HelpView { DataContext = new HelpViewModel(new FakeDialogs()) };
        var window = new Window { Content = sheet, Width = 1600, Height = 1400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        double needed;
        try
        {
            sheet.Measure(Size.Infinity);
            needed = sheet.DesiredSize.Height * ShellViewModel.BaseScale + ShellViewModel.ChromeReserve;
        }
        finally { window.Close(); }

        Assert.True(ShellViewModel.MinWindowHeight >= needed,
            $"the quick-reference sheet needs {needed:0} and the minimum window is "
            + $"{ShellViewModel.MinWindowHeight:0}");
    }
}
