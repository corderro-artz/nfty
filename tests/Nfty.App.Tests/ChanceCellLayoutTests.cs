using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The ABSENT column's width, asserted from a laid-out frame.
///
/// <para>The cell was 96px — roughly double what anything in it measures — and read as a hole in the
/// row rather than as a field. Narrowing it is only safe if the narrowing is a budget rather than a
/// guess, because <b>a width overrun in this app is silent</b>: a clipped control still reports the
/// bounds it asked for, so the markup keeps describing a frame that is not being drawn. The widest
/// thing this cell ever holds is <c>100</c>, three digits, which no capture fixture shows.</para>
/// </summary>
public class ChanceCellLayoutTests
{
    /// <summary>Renders the Recipe pane over a recipe whose last layer is at the given chance,
    /// unlocked so the editable field is the control on screen.</summary>
    private static (Window Window, Views.RecipeDetailView View, RecipeDetailViewModel Vm)
        Render(double percent)
    {
        var (book, _) = VisualCapture.RecipeWithRules();
        var recipe = book.Recipes[0];
        var optional = new LoadedRecipe
        {
            Manifest = recipe.Manifest with
            {
                AbsentPercent = new Dictionary<string, double>
                {
                    [recipe.Manifest.LayerOrder[^1]] = percent,
                },
            },
            Ingredients = recipe.Ingredients,
        };

        var vm = new RecipeDetailViewModel(optional, book, new ImageBridge(), _ => { },
            moveLayer: (_, _) => Task.FromResult<LoadedCookBook?>(null), canReorder: true,
            editRules: (_, _) => Task.FromResult<LoadedCookBook?>(null), dialogs: new FakeDialogs());

        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static List<Panel> Cells(Visual view) => view.GetVisualDescendants()
        .OfType<Panel>()
        .Where(p => p.Classes.Contains("chancecell"))
        .ToList();

    /// <summary>
    /// The three-digit value fits, with slack. Asserted as CONTAINMENT inside the cell rather than
    /// as a non-zero width, because a clipped control reports the size it wanted.
    /// </summary>
    /// <remarks>
    /// The inner <c>TextBox</c> is what draws, and the chevron column takes 17 of the field before a
    /// digit is placed — so the assertion is on the text box's own laid-out width against the ink it
    /// has to carry, measured with the same typeface the box uses.
    /// </remarks>
    [AvaloniaFact]
    public void A_three_digit_chance_fits_the_cell_with_room_to_spare()
    {
        var (window, view, _) = Render(100);
        try
        {
            // LAST, not First: the rows follow layerOrder and Render puts the chance on the top
            // layer. First is the bottom row, sitting at 0 — a single digit, which is exactly the
            // width this test is not about.
            var field = view.GetVisualDescendants().OfType<NumericUpDown>()
                .Last(n => n.Classes.Contains("qnt"));
            Assert.Equal(100m, field.Value);

            var box = field.GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal("100", box.Text);

            var ink = new FormattedText(box.Text!, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(box.FontFamily), box.FontSize, Brushes.Black);

            // The box's own content box, less the padding it declares.
            double room = box.Bounds.Width - box.Padding.Left - box.Padding.Right;
            Assert.True(room >= ink.Width + 4,
                $"'100' needs {ink.Width:0.#}px and the box offers {room:0.#}px — the cell is too "
                + "narrow, and a clipped field reports the width it asked for rather than failing.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The header word fits too. It is the other half of the same budget, and it is the half that
    /// binds first: ABSENT is wider than any value the column shows.
    /// </summary>
    [AvaloniaFact]
    public void The_header_word_fits_the_same_cell()
    {
        var (window, view, _) = Render(85);
        try
        {
            var header = Cells(view)
                .SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>())
                .First(t => t.Text == "ABSENT");

            // The INK, not the Bounds. A TextBlock is arranged to its parent's width and clips what
            // does not fit, so Bounds.Width <= cell.Bounds.Width is true at every size including the
            // ones that cut the word in half — the vacuous form of this assertion, and the reason
            // the width-is-a-budget rule says to measure against what the content needs.
            var ink = new FormattedText("ABSENT", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(header.FontFamily), header.FontSize,
                Brushes.Black);
            double needed = ink.Width + header.LetterSpacing * 6;   // per character, ABSENT is six

            var cell = header.FindAncestorOfType<Panel>()!;
            Assert.True(cell.Bounds.Width >= needed,
                $"'ABSENT' needs {needed:0.#}px and the cell offers {cell.Bounds.Width:0.#}px — it "
                + "is being clipped, which neither the markup nor Bounds.Width can tell you.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The header cell and every row cell are the same width.
    /// </summary>
    /// <remarks>
    /// This is the invariant that makes an <c>Auto</c> column admissible in a two-Grid table at all —
    /// a header Grid and a row Grid size their <c>Auto</c> columns independently, which this app has
    /// been bitten by twice (the ingredient's WEIGHT column, then the Set browser's VALUE column,
    /// which had been misaligned since it shipped). It holds here only because both cells are the
    /// same styled wrapper, so it is asserted rather than assumed.
    /// </remarks>
    [AvaloniaFact]
    public void Every_chance_cell_is_the_one_width_the_style_states()
    {
        var (window, view, vm) = Render(85);
        try
        {
            var cells = Cells(view);
            Assert.Equal(vm.Layers.Count + 1, cells.Count);   // one header cell plus one per row

            var widths = cells.Select(c => c.Bounds.Width).Distinct().ToList();
            Assert.True(widths.Count == 1,
                "header and rows disagree about the column width: "
                + string.Join(", ", widths.Select(w => w.ToString("0.#"))));

            // And it is the width the style declares — a declared size being the real size, the
            // same claim the TextBox MinWidth rule exists for.
            Assert.Equal(58, widths[0]);
        }
        finally { window.Close(); }
    }
}
