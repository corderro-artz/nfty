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
using Nfty.Core.Model;
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

    /// <summary>
    /// EVERY band row, not just the value ramp: each handle sits on the bar it drags along.
    /// </summary>
    /// <remarks>
    /// The test above checked one band and passed while the four dual-range handles were 5px high,
    /// because the compensation <c>Slider.overlay</c> carries is only correct for a 40px row and the
    /// range rows measured 50. The rows are pinned now (<c>Panel.band</c>), but the number is not
    /// what this asserts — it asserts the thing that has to be true, so any future change to the
    /// row height, the slider template or the handle size fails here rather than shipping.
    /// The wizard is swept too: it draws the same four range rows.
    /// </remarks>
    [AvaloniaFact]
    public void Every_handle_is_centered_on_the_band_it_drags_along()
    {
        var (window, vm, view) = Render();
        var wizard = new Views.NewIngredientView
        {
            DataContext = new NewIngredientViewModel(new FakeDialogs()) { Kind = LayerKind.Dynamic },
        };
        var wizWindow = new Window { Content = wizard, Width = MinimumWindowWidth, Height = 900 };
        wizWindow.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            int checked_ = 0;
            foreach (var root in new Control[] { view, wizard })
                foreach (var panel in root.GetVisualDescendants().OfType<Panel>()
                             .Where(p => p.Classes.Contains("band") && p.Bounds.Height > 0))
                {
                    var band = panel.Children.OfType<Border>().Single();
                    foreach (var thumb in panel.GetVisualDescendants().OfType<Thumb>())
                    {
                        Assert.True(band.Bounds.Height > 0, "a band was not laid out");
                        Assert.Equal(CenterY(root, band), CenterY(root, thumb), 1);
                        checked_++;
                    }
                }

            // Both screens' rows really were reached — an empty sweep would pass vacuously, which is
            // exactly how the four range handles went unchecked for so long.
            Assert.True(checked_ >= 9, $"only {checked_} handles were swept");
        }
        finally { wizWindow.Close(); window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The variant sidebar's three actions are one row: two equal, and Import the small one.
    /// </summary>
    /// <remarks>Duplicate and Delete over a full-width Import read as two different kinds of
    /// control. Asserts containment with slack rather than exact fit — the labels are the budget
    /// here, and fitting exactly is one style tweak from not fitting.</remarks>
    [AvaloniaFact]
    public void The_variant_actions_are_one_row_and_all_fit()
    {
        var (window, vm, view) = Render();
        try
        {
            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Where(b => (b.Content as string) is "Duplicate" or "Delete" or "Import…")
                .OrderBy(b => b.Bounds.X)
                .ToList();
            Assert.Equal(3, buttons.Count);

            // One row: same top, same height.
            Assert.Equal(buttons[0].Bounds.Y, buttons[1].Bounds.Y, 1);
            Assert.Equal(buttons[0].Bounds.Y, buttons[2].Bounds.Y, 1);
            Assert.All(buttons, b => Assert.Equal(buttons[0].Bounds.Height, b.Bounds.Height, 1));

            // Duplicate and Delete share the slack; Import takes only its label.
            Assert.Equal(buttons[0].Bounds.Width, buttons[1].Bounds.Width, 1);
            Assert.True(buttons[2].Bounds.Width < buttons[0].Bounds.Width,
                $"Import ({buttons[2].Bounds.Width}) should be the small one");

            // The two share a fixed budget, so they need real slack: fitting exactly is one style
            // tweak from not fitting. Import sits in an Auto column and is its own content by
            // definition - there the only thing to check is that nothing squeezed it below that.
            foreach (var (b, floor) in new[] { (buttons[0], 3.0), (buttons[1], 3.0), (buttons[2], 0.0) })
            {
                var text = b.GetVisualDescendants().OfType<TextBlock>().Single();
                double slack = b.Bounds.Width - b.Padding.Left - b.Padding.Right - text.Bounds.Width;
                Assert.True(slack >= floor, $"\"{text.Text}\" has {slack:0.0}px of slack");
            }
        }
        finally { window.Close(); vm.Dispose(); }
    }

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

    /// <summary>The compact numeric cell's height, read from the token rather than restated — a
    /// size stated twice will drift, and this one is now shared by two strips.</summary>
    private static double CompactFieldHeight(Window window)
    {
        Assert.True(Application.Current!.TryGetResource(
            "FieldHeightSm", window.ActualThemeVariant, out var v));
        return Assert.IsType<double>(v);
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

            Assert.Equal(CompactFieldHeight(window), box.Bounds.Height, 1);   // what actually draws
            Assert.Equal(CenterY(view, swatch), CenterY(view, box), 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// EVERY compact numeric cell in the editor is one height. The rail used to stack 24px range
    /// boxes directly above 32px quantize steppers, which read as two unrelated kinds of control in
    /// one panel; the height is a token now, and this asserts that every call site took it. The
    /// measurement is of the INNER TextBox in each case, since that is the box that draws the border
    /// — an outer NumericUpDown can report whatever height it likes with nothing painting there.
    /// </summary>
    [AvaloniaFact]
    public void Every_compact_numeric_cell_is_the_same_height()
    {
        var (window, vm, view) = Render();
        try
        {
            var expected = CompactFieldHeight(window);

            var boxes = view.GetVisualDescendants().OfType<TextBox>()
                .Where(b => b.Classes.Contains("nin")).ToList();
            foreach (var nud in view.GetVisualDescendants().OfType<NumericUpDown>()
                         .Where(n => n.Classes.Contains("nin") || n.Classes.Contains("qnt")))
                boxes.AddRange(nud.GetVisualDescendants().OfType<TextBox>());

            Assert.True(boxes.Count >= 7, $"expected the four range boxes, the two quantize steppers "
                                        + $"and the brush size; found {boxes.Count}");
            foreach (var b in boxes)
                Assert.Equal(expected, b.Bounds.Height, 1);

            // And a declared width is the real width. Fluent's TextBox carries a 64px MinWidth that
            // beats any smaller Width, so `Width="58"` drew at 64 and the markup said one thing
            // while the frame did another — silently, since the boxes still matched each other.
            foreach (var b in view.GetVisualDescendants().OfType<TextBox>()
                         .Where(b => b.Classes.Contains("nin") && !double.IsNaN(b.Width)))
                Assert.Equal(b.Width, b.Bounds.Width, 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The stepper column is a narrow chevron rail, not most of the field. Fluent's own
    /// ButtonSpinner draws two side-by-side buttons ~32px each, which left a 94px quantize control
    /// with about 8px of text room; the hand-authored template in Controls.axaml gives them one
    /// StepperWidth column. Asserted as a RATIO of the control rather than a number, so it stays
    /// true if either the token or the field width moves.
    /// </summary>
    [AvaloniaFact]
    public void The_spinner_chevrons_take_a_narrow_column_not_half_the_field()
    {
        var (window, vm, view) = Render();
        try
        {
            var nud = view.GetVisualDescendants().OfType<NumericUpDown>()
                .First(n => n.Classes.Contains("qnt"));
            var box = nud.GetVisualDescendants().OfType<TextBox>().First();

            Assert.True(nud.Bounds.Width > 0, "the stepper is laid out at all");
            var share = box.Bounds.Width / nud.Bounds.Width;
            Assert.True(share > 0.7,
                $"the value cell is {box.Bounds.Width:0.#} of {nud.Bounds.Width:0.#} ({share:P0}) — "
                + "the chevrons are eating the field again");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    /// <summary>
    /// The corner preview tile obeys BOTH of its buttons, on a laid-out frame. Each was broken in a
    /// different way and neither was visible in a ViewModel test: the size was hard-coded in the
    /// markup, so "enlarge" moved a number nothing read, and the tile hid itself whenever fill-pane
    /// was on — taking fill-pane's own off switch off the screen with it. Measured off the tile's
    /// own Bounds, since a Width the markup ignores still binds and still reports.
    /// </summary>
    [AvaloniaFact]
    public void The_preview_tile_resizes_and_stays_reachable_in_both_states()
    {
        var (window, vm, view) = Render();
        try
        {
            static Border Tile(Visual root) => root.GetVisualDescendants().OfType<Border>()
                .First(b => b.Name == "PreviewTile");

            var inset = Tile(view).Bounds.Height;
            Assert.Equal(vm.PreviewHeight, inset, 1);

            vm.EnlargePreviewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(Tile(view).Bounds.Height > inset + 20,
                $"enlarge moved the tile from {inset:0.#} to {Tile(view).Bounds.Height:0.#}");

            vm.EnlargePreviewCommand.Execute(null);
            vm.FillPanePreviewCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            // The way back out has to still be on screen — this is the whole bug.
            var tile = Tile(view);
            Assert.True(tile.IsEffectivelyVisible, "the tile carrying the buttons vanished");
            var buttons = tile.GetVisualDescendants().OfType<Button>().ToList();
            Assert.Equal(3, buttons.Count);
            Assert.All(buttons, b => Assert.True(b.IsEffectivelyVisible && b.Bounds.Width > 0));
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
