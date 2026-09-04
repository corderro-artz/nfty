using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The editor's two horizontal strips, measured from a laid-out frame: does each row actually sit on
/// one line, and does the colorize rail fit inside the 300px it is given.
/// </summary>
/// <remarks>
/// Every failure these pin was reported from a screenshot, not from a test. Fluent's horizontal
/// Slider reserves a tick lane <i>below</i> its track, so its handle renders 5px under the control's
/// own middle — which put the value ramp's thumb off its band and the alpha handle under its "A"
/// rather than beside it. And the rail's quantize row was 12px wider than the rail, so the second
/// stepper's chevron was drawn outside it. None of that is visible in markup.
/// </remarks>
public class EditorStripAlignmentTests
{
    // The pane track's own minimum, as PaletteStripLayoutTests and ToolstripLayoutTests use it.
    private const double MinimumWindowWidth = 1180;

    private static (Window window, IngredientEditorViewModel vm, Views.IngredientEditorView view) Render()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        var view = new Views.IngredientEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = MinimumWindowWidth, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    private static double CenterY(Visual root, Control c) =>
        c.TranslatePoint(default, root)!.Value.Y + c.Bounds.Height / 2;

    /// <summary>The value ramp's handle sits on the band it is dragging along, not below it.</summary>
    [AvaloniaFact]
    public void The_value_ramps_handle_is_centered_on_its_band()
    {
        var (window, vm, view) = Render();
        try
        {
            var band = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("ramp"));
            var panel = (Panel)band.GetVisualParent()!;
            var thumb = panel.GetVisualDescendants().OfType<Thumb>().First();

            Assert.True(band.Bounds.Height > 0, "the ramp band was not laid out");
            Assert.Equal(CenterY(view, band), CenterY(view, thumb), 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>The alpha handle sits on the same line as the "A" that labels it.</summary>
    [AvaloniaFact]
    public void The_alpha_handle_is_centered_on_its_own_row()
    {
        var (window, vm, view) = Render();
        try
        {
            var cell = view.GetVisualDescendants().OfType<StackPanel>().First(p => p.Classes.Contains("alphacell"));
            var label = cell.GetVisualDescendants().OfType<TextBlock>().First();
            var thumb = cell.GetVisualDescendants().OfType<Thumb>().First();

            Assert.Equal(CenterY(view, label), CenterY(view, thumb), 1.5);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The brush-size box is the same sort of cell as the swatch beside it, not half again its
    /// height — and its border is whole, which pinning the outer control's height alone destroys.
    /// </summary>
    [AvaloniaFact]
    public void The_brush_size_box_matches_the_row_it_sits_in()
    {
        var (window, vm, view) = Render();
        try
        {
            var nud = view.GetVisualDescendants().OfType<NumericUpDown>().First(n => n.Classes.Contains("nin"));
            var box = nud.GetVisualDescendants().OfType<TextBox>().First();
            var swatch = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("swatch"));

            Assert.Equal(24, box.Bounds.Height, 1);                       // what actually draws
            Assert.Equal(CenterY(view, swatch), CenterY(view, box), 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// Nothing in the colorize rail is drawn outside it. The rail is a fixed 300 and its body is
    /// inset 13 on each side, so anything wider than 274 is simply painted past the edge — silently,
    /// which is how the quantize steppers shipped with a chevron hanging off.
    /// </summary>
    [AvaloniaFact]
    public void Every_colorize_rail_control_fits_inside_the_rail()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.SetModeDynamicCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>()
                .First(s => s.Name == "ColorizeScroll");
            var body = (StackPanel)scroll.Content!;
            double edge = body.Bounds.Width;
            Assert.True(edge > 0, "the rail body was not laid out");

            // Skip what is inside a Slider's Track: Fluent sizes its two RepeatButtons from the
            // thumb's center, so the trailing one lands half a pixel past the track's own edge. That
            // is the framework's arithmetic, not this app's layout, and it is what this test is not
            // about.
            foreach (var c in body.GetVisualDescendants().OfType<Control>()
                         .Where(c => c.IsVisible && c.Bounds.Width > 0)
                         .Where(c => !c.GetVisualAncestors().OfType<Track>().Any()))
            {
                var o = c.TranslatePoint(default, body);
                if (o is null) continue;
                if (o.Value.X >= -0.5 && o.Value.X + c.Bounds.Width <= edge + 0.5) continue;
                var chain = string.Join(" < ", c.GetVisualAncestors().OfType<Control>().Take(6)
                    .Select(a => a.GetType().Name + "[" + string.Join('.', a.Classes) + "]"));
                Assert.Fail($"{c.GetType().Name} spans {o.Value.X:0.#}..{o.Value.X + c.Bounds.Width:0.#} " +
                    $"in a {edge:0.#}px rail :: {chain}");
            }
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The quantize steppers show their whole value. Three digits is the real ceiling on both -- hue
    /// quantize reaches 360 and saturation 100 -- so that is what has to fit.
    /// </summary>
    /// <remarks>
    /// This shipped clipped: Fluent's two spinner buttons take ~64 of the control's 94 and its stock
    /// TextBox padding ate most of the rest, so "30" drew as "3" plus a sliver. Nothing caught it,
    /// because a clipped TextBox still reports the width it was asked for and every ViewModel test
    /// still passed. Found by looking at a captured frame of the running app.
    /// </remarks>
    [AvaloniaFact]
    public void The_quantize_steppers_show_their_whole_value()
    {
        var (window, vm, view) = Render();
        try
        {
            vm.SetModeDynamicCommand.Execute(null);
            vm.HueQuantize = 360;
            vm.SatQuantize = 100;
            Dispatcher.UIThread.RunJobs();

            var steppers = view.GetVisualDescendants().OfType<NumericUpDown>()
                .Where(n => n.Classes.Contains("qnt")).ToList();
            Assert.Equal(2, steppers.Count);

            foreach (var nud in steppers)
            {
                var box = nud.GetVisualDescendants().OfType<TextBox>().First();
                double content = box.Bounds.Width - box.Padding.Left - box.Padding.Right;
                var text = new FormattedText(box.Text ?? "", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface(box.FontFamily), box.FontSize, Brushes.Black);

                // A 2px floor, not a bare fit: a box that clears its text exactly is one font or
                // padding tweak from clipping again, which is how this shipped the first time.
                Assert.True(content >= text.Width + 2,
                    $"'{box.Text}' needs {text.Width:0.#}px but the box offers {content:0.#}px");
            }
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
