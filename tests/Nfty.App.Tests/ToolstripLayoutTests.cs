using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The editor toolstrip's width budget, asserted from a laid-out frame.
/// </summary>
/// <remarks>
/// Same failure mode as <see cref="PaletteStripLayoutTests"/>, one row up: the toolstrip is a
/// horizontal StackPanel inside a clipping Border, so anything past the pane's right edge is simply
/// not drawn — no exception, no warning. Adding the Line tool cost 36px (a 30px button plus its
/// gap) and pushed the brush-size stepper off the end; the frame showed it and arithmetic in a
/// comment had not. Anything added here has to shrink something else, and this fails the moment it
/// does not.
/// </remarks>
public class ToolstripLayoutTests
{
    // DERIVED from the window minimum, not the mockups' 1180 page. That figure was 200px wider than
    // this page is ever given: the shell renders at BaseScale, so the narrowest page the app can
    // show is (MinWindowWidth - 24) / 1.2 = 980 logical pixels. Measured at 1180 the strip cleared
    // its edge with room to spare while the running app painted the last two controls straight over
    // the colorize rail beside it - the same mistake the UNIQUE DNA cell's first test made, and the
    // same cure: measure the page the app actually hosts.
    private static double PageWidth =>
        (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale;

    // The window the app OPENS at, which is where a wrapped strip is most often looked at and where
    // the old WrapPanel missed one line by a single pixel.
    private static double DefaultPageWidth => (1366 - 24) / ShellViewModel.BaseScale;

    private static (Window window, Views.IngredientEditorView view) Render(double pageWidth)
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = pageWidth, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    // The toolstrip is the one .pane-hrow that holds the tool buttons; the other two head the
    // variants rail and the colorize rail.
    private static Border Strip(Visual view) => view.GetVisualDescendants()
        .OfType<Border>()
        .First(b => b.Classes.Contains("pane-hrow")
                 && b.GetVisualDescendants().OfType<Button>().Any(x => x.Classes.Contains("ttool")));

    [AvaloniaFact]
    public void Every_toolstrip_control_is_inside_the_pane_at_the_minimum_window_width()
    {
        var (window, view) = Render(PageWidth);
        try
        {
            var strip = Strip(view);
            var panel = strip.GetVisualDescendants().OfType<StripPanel>().First();
            Assert.True(strip.Bounds.Width > 0, "the strip itself was arranged at zero width");
            double edge = ContentRight(strip);

            foreach (var child in panel.Children.OfType<Control>())
            {
                var origin = child.TranslatePoint(default, strip);
                Assert.NotNull(origin);
                double right = origin!.Value.X + child.Bounds.Width;
                Assert.True(right <= edge,
                    $"{Describe(child)} runs to {right:0.#} past the strip's content edge at {edge:0.#}");
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The strip GROWS to hold what it wraps, so no line is cut off the bottom either.
    /// </summary>
    /// <remarks>
    /// This replaces a "keeps ten pixels of slack" assertion, which was the right guard while the
    /// strip was a single row that could overrun and the wrong one afterwards: a wrapped line ends
    /// wherever the next control did not fit, so the trailing slack is arbitrary by construction and
    /// dips to a few pixels at some widths without anything being wrong. What can still go wrong is
    /// the other axis — <c>.pane-hrow</c> pins its siblings at 41px, and a second line inside a
    /// fixed-height row would be cut exactly as silently as the horizontal overrun was.
    /// </remarks>
    [AvaloniaFact]
    public void The_strip_is_tall_enough_for_every_line_it_wraps_onto()
    {
        var (window, view) = Render(PageWidth);
        try
        {
            var strip = Strip(view);
            var panel = strip.GetVisualDescendants().OfType<StripPanel>().First();

            double bottom = panel.Children.OfType<Control>()
                .Select(c => c.TranslatePoint(default, strip)!.Value.Y + c.Bounds.Height)
                .DefaultIfEmpty(0).Max();
            double room = strip.Bounds.Height - strip.Padding.Bottom;

            Assert.True(bottom <= room + 0.5,
                $"the strip's controls reach {bottom:0.#} in a row {room:0.#} tall");
            Assert.True(strip.Bounds.Height >= 41, "the row must not be shorter than its siblings");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// At the window the app OPENS at, the strip is ONE line.
    /// </summary>
    /// <remarks>
    /// This is the defect that was reported rather than measured: the strip wanted 557px of a 556px
    /// pane, so it wrapped, and what went over the fold was the brush-size box on its own beside six
    /// hundred pixels of empty row. Missing by one pixel is not a wrapped toolbar. Asserted at 1366
    /// rather than at the minimum because wrapping at the minimum is the DESIGN — the strip does not
    /// fit 1280 and never will — while wrapping at the default is a budget that has been overspent.
    /// </remarks>
    [AvaloniaFact]
    public void The_strip_is_one_line_at_the_window_the_app_opens_at()
    {
        var (window, view) = Render(DefaultPageWidth);
        try
        {
            var panel = Strip(view).GetVisualDescendants().OfType<StripPanel>().First();
            var lines = StripLines.Of(panel);

            Assert.True(lines.Count == 1,
                $"the toolstrip wrapped onto {lines.Count} lines at the window the app opens at - "
                + "its controls have outgrown the pane again");
            Assert.True(lines[0].Count > 1, "the strip laid out nothing to measure");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Where it DOES wrap, a group stays whole and no line is left holding a lone control while the
    /// row beside it is empty.
    /// </summary>
    /// <remarks>
    /// A WrapPanel breaks between any two children, so the break landed wherever the arithmetic put
    /// it; StripPanel takes groups and picks the break that leaves the least visible gap. The
    /// assertion is on the GAP rather than on a particular split: naming the split would restate the
    /// current widths, and the rule is that no line is much emptier than the emptiest it has to be.
    /// </remarks>
    [AvaloniaFact]
    public void A_wrapped_line_is_never_mostly_empty()
    {
        var (window, view) = Render(PageWidth);
        try
        {
            var strip = Strip(view);
            var panel = strip.GetVisualDescendants().OfType<StripPanel>().First();
            var lines = StripLines.Of(panel);
            if (lines.Count < 2) return;      // one line is the best possible answer

            foreach (var line in lines)
            {
                double used = StripLines.Used(line);
                Assert.True(used >= panel.Bounds.Width / 2,
                    $"a wrapped line uses {used:0.#} of {panel.Bounds.Width:0.#} - the break left a "
                    + "control stranded beside an empty row");
            }
        }
        finally { window.Close(); }
    }

    // The strip's own padding counts: a control that ends inside the border but past the padding is
    // touching the pane edge, which is the state this test exists to prevent.
    private static double ContentRight(Border strip) => strip.Bounds.Width - strip.Padding.Right;

    private static string Describe(Control c) =>
        c is Button b && b.Content is Avalonia.Controls.Shapes.Path
            ? $"{c.GetType().Name}({b.GetValue(ToolTip.TipProperty)})"
            : c.GetType().Name;
}
