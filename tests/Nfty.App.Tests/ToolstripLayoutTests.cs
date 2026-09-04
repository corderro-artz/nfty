using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    // The pane track's own minimum, as PaletteStripLayoutTests uses it: the mockups' 1180 page less
    // the 262 variants rail and the 300 colorize rail. Below this the panes scroll rather than
    // compress, so this is the narrowest the strip is ever asked to fit into.
    private const double MinimumWindowWidth = 1180;

    private static (Window window, Views.IngredientEditorView view) Render()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = MinimumWindowWidth, Height = 720 };
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
        var (window, view) = Render();
        try
        {
            var strip = Strip(view);
            var panel = strip.GetVisualDescendants().OfType<StackPanel>().First();
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
    /// The budget with its slack stated. Fitting exactly is not fitting: a tooltip's font, a
    /// stepper's spinner column or a one-pixel border can move the total, and a strip that clears
    /// the edge by a hair today is one style tweak from clipping again.
    /// </summary>
    [AvaloniaFact]
    public void The_toolstrip_keeps_at_least_ten_pixels_of_slack()
    {
        var (window, view) = Render();
        try
        {
            var strip = Strip(view);
            var panel = strip.GetVisualDescendants().OfType<StackPanel>().First();
            var last = panel.Children.OfType<Control>().Last();
            double right = last.TranslatePoint(default, strip)!.Value.X + last.Bounds.Width;
            double slack = ContentRight(strip) - right;
            Assert.True(slack >= 10, $"only {slack:0.#}px of slack left in the toolstrip");
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
