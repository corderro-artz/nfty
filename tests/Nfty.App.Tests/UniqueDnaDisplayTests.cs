using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The UNIQUE DNA tile is about 90px wide and the figure it names is unbounded. It rounds; the
/// tooltip does not.
/// </summary>
/// <remarks>
/// <para><b>The tile is allowed to round only because the exact figure is one hover away.</b> This
/// is the number an author tunes quantize steps against — a product they cannot read is a product
/// they cannot tune — so the two halves have to be tested together. A test of the compact form alone
/// would pass on a card that had quietly stopped being able to show the real figure at all.</para>
///
/// <para>Both are asserted off a laid-out frame rather than off the ViewModel, because the ViewModel
/// was already right the whole time the card had no tooltip on it.</para>
/// </remarks>
public class UniqueDnaDisplayTests
{
    /// <summary>
    /// A book whose space is past a billion: four custom layers of 200 variants each, which is
    /// 1,600,000,000 and needs no rules walk and no color buckets to get there.
    /// </summary>
    private static LoadedCookBook HugeBook()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                Enumerable.Range(0, 200).Select(i => new Variant($"v{i}", $"V{i}", 1)).ToArray()),
            VariantImages = Enumerable.Range(0, 200)
                .ToDictionary(i => $"v{i}", _ => new Image<Rgba32>(8, 8)),
        };
        string[] layers = ["a", "b", "c", "d"];
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Huge", new Dimensions(8, 8),
                new Collection("Huge", "", "HG"),
                new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("cat", "cat", layers, []),
                    Ingredients = layers.Select(Ing).ToArray(),
                },
            ],
        };
    }

    [Fact]
    public void A_figure_past_a_billion_rounds_on_the_tile_and_stays_whole_on_the_tooltip()
    {
        using var book = HugeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("1.60 billion", vm.UniqueDnaText);
        Assert.Equal("1,600,000,000 unique DNA", vm.UniqueDnaTip);

        // The cookbar is a bar, not a tile: it exists to compare an intended supply against the
        // space, and a comparison between a rounded number and an exact one is not one.
        Assert.Contains("1,600,000,000", vm.CookBarText);
    }

    [Fact]
    public void A_figure_that_fits_is_shown_in_full_on_both()
    {
        // Degrade only when you must. Essentially every real book lands here, so essentially every
        // book shows its true figure on the card — the rounded form is the exception, not the rule.
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("2", vm.UniqueDnaText);
        Assert.Equal("2 unique DNA", vm.UniqueDnaTip);
    }

    [AvaloniaFact]
    public void The_tile_actually_carries_the_tooltip()
    {
        // A tooltip the ViewModel exposes and the markup never binds is the reason the tile is
        // allowed to round, silently absent. Read it off the rendered control.
        using var book = HugeBook();
        var view = new Views.CookBookDetailView
        {
            DataContext = new CookBookDetailViewModel(book, () => { }, () => { }),
        };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "UNIQUE DNA");
        var tile = label.GetVisualAncestors().OfType<StackPanel>().First();

        Assert.Equal("1,600,000,000 unique DNA", ToolTip.GetTip(tile));

        // And the figure beside it is the rounded one, so the pair is doing what it claims.
        var figure = tile.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("mv"));
        Assert.Equal("1.60 billion", figure.Text);
    }

    [AvaloniaFact]
    public void The_widest_figure_the_compact_form_can_produce_still_fits_the_tile()
    {
        // Width is a budget and an overrun is SILENT — a TextBlock is arranged to its parent and
        // clips, reporting the width it was asked for either way. So measure the ink, against the
        // tile's own content box, for the worst string this formatter can emit: long.MaxValue is
        // "9.22 quintillion", sixteen characters and the ceiling of the type.
        //
        // MEASURED INSIDE THE REAL EXPLORER AT THE SMALLEST PAGE THE APP ALLOWS, which is the whole
        // point. The first version of this test rendered the detail view alone in a 1180px window —
        // 200px wider than the page ever is, and with no tree pane taking a third of what is left —
        // so it passed with room to spare while the running app drew "30.64 trilli". Driving the app
        // found that; no assertion could, because the assertion was measuring a card nothing hosts.
        using var book = HugeBook();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = explorer };

        // The page area at ShellViewModel's minimum window: (1200 - 24) / 1.2 wide, and
        // (712 - ChromeReserve) / 1.2 tall. Derived rather than typed, so a change to the minimum
        // moves this measurement with it instead of leaving it describing an old window.
        var window = new Window
        {
            Content = view,
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "UNIQUE DNA");
        var figure = label.GetVisualAncestors().OfType<StackPanel>().First()
            .GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("mv"));
        var tile = label.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("metric"));

        string worst = SpaceText.Compact(long.MaxValue);
        var ink = new FormattedText(worst, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(figure.FontFamily, figure.FontStyle, figure.FontWeight),
            figure.FontSize, Brushes.Black);

        double room = tile.Bounds.Width - tile.Padding.Left - tile.Padding.Right
            - tile.BorderThickness.Left - tile.BorderThickness.Right;

        Assert.True(ink.Width <= room,
            $"\"{worst}\" measures {ink.Width:F1}px in a tile with {room:F1}px of room");
    }

    [AvaloniaFact]
    public void Widening_the_figure_did_not_narrow_the_three_counts_past_their_labels()
    {
        // The other half of the trade. UNIQUE DNA took the row, so RECIPES / LAYERS / VARIANTS went
        // from half a column each to a third — and a metric label is the widest thing in its tile,
        // not the number. Same measurement, same window: the ink of every label against the box it
        // sits in. Without this the fix above is one clipped control traded for three.
        using var book = HugeBook();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = explorer };
        var window = new Window
        {
            Content = view,
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        foreach (string name in new[] { "RECIPES", "LAYERS", "VARIANTS" })
        {
            var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name);
            var tile = label.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains("metric"));

            // LetterSpacing is a control property and FormattedText has no setter for it, so it is
            // added back by hand: the class states 1.0 per character, which is what makes these
            // labels wider than they look in the markup.
            var ink = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(label.FontFamily, label.FontStyle, label.FontWeight),
                label.FontSize, Brushes.Black);
            double width = ink.Width + label.LetterSpacing * name.Length;

            double room = tile.Bounds.Width - tile.Padding.Left - tile.Padding.Right
                - tile.BorderThickness.Left - tile.BorderThickness.Right;

            Assert.True(width <= room - 4,
                $"\"{name}\" measures {width:F1}px in a tile with {room:F1}px of room");
        }

        // And the column the width came FROM still holds its own content. Widening the metrics band
        // narrows the DNA SPACE column beside it, whose rows are the densest thing on this card - a
        // name, six factor chips and a figure, all on one line. Trading a clipped tile for a clipped
        // row would be no trade at all, so both sides of the split are measured at the same window.
        var heading = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "DNA SPACE");
        var column = heading.GetVisualAncestors().OfType<StackPanel>().First();
        var number = view.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Classes.Contains("cnum"));
        var chipRow = number.GetVisualAncestors().OfType<StackPanel>().First();

        double rowLeft = chipRow.TranslatePoint(new Avalonia.Point(0, 0), column)!.Value.X;
        double rowRight = chipRow.TranslatePoint(new Avalonia.Point(chipRow.Bounds.Width, 0), column)!.Value.X;
        Assert.True(rowLeft >= 0 && rowRight <= column.Bounds.Width,
            $"the recipe row spans {rowLeft:F1}..{rowRight:F1} in a {column.Bounds.Width:F1}px column");

        // A star column whose children outgrow it is arranged at ZERO width, silently. The chips are
        // the first thing that would go, so their presence is the real evidence the row still fits.
        var chips = chipRow.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("fchip")).ToList();
        Assert.NotEmpty(chips);
        Assert.All(chips, c => Assert.True(c.Bounds.Width > 0, "a factor chip was arranged at zero width"));
    }

    /// <summary>
    /// A book whose recipe name is long and whose stack is deep — the shape that clipped.
    /// </summary>
    private static LoadedCookBook LongNamedBook()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                [new Variant("a", "A", 1), new Variant("b", "B", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(8, 8),
                ["b"] = new Image<Rgba32>(8, 8),
            },
        };
        string[] layers = ["a", "b", "c", "d", "e", "f"];
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Long", new Dimensions(8, 8),
                new Collection("Long", "", "LN"),
                new Dictionary<string, double> { ["strongbox"] = 1 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("strongbox", "Reinforced Strongbox", layers, []),
                    Ingredients = layers.Select(Ing).ToArray(),
                },
            ],
        };
    }

    [AvaloniaFact]
    public void A_long_recipe_name_does_not_clip_the_badges_beside_it()
    {
        // The row is [dot][name] ... [chips][arrow][figure], and it was laid out Auto,Auto,* — the
        // name took its natural width and everything that carries a NUMBER shared the remainder.
        // A right-aligned block given less than it needs overflows its LEADING edge, so the first
        // factor chip was half drawn: "Strongbox" lost a badge where "Chest" kept one, in the same
        // table at the same window. Found in the running app; every layout assertion passed.
        using var book = LongNamedBook();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        using var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = explorer };
        var window = new Window
        {
            Content = view,
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var number = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("cnum"));
        var row = number.GetVisualAncestors().OfType<Grid>().First();
        var chips = row.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("fchip")).ToList();

        Assert.Equal(6, chips.Count);
        foreach (var chip in chips)
        {
            double left = chip.TranslatePoint(new Avalonia.Point(0, 0), row)!.Value.X;
            double right = chip.TranslatePoint(new Avalonia.Point(chip.Bounds.Width, 0), row)!.Value.X;
            Assert.True(left >= 0 && right <= row.Bounds.Width,
                $"a badge spans {left:F1}..{right:F1} in a {row.Bounds.Width:F1}px row");
        }

        // And the figure itself, which sits past them all.
        double numRight = number.TranslatePoint(new Avalonia.Point(number.Bounds.Width, 0), row)!.Value.X;
        Assert.True(numRight <= row.Bounds.Width,
            $"the figure ends at {numRight:F1} in a {row.Bounds.Width:F1}px row");
    }

    [AvaloniaFact]
    public void A_badge_gives_its_digit_a_line_box_the_font_can_draw_in()
    {
        // The chip's TextBlock said FontSize 11 and LineHeight 11 - a line box the exact height of
        // the em, which is shorter than the face needs, so the glyph lost its ascender and its
        // descender. Whether that SHOWED depended on where the row landed: the shell renders at 1.2,
        // so one row falls on a whole device pixel and the next does not, and the same "3" drew
        // whole in the first row of this table and broken in the second. It read as a font problem
        // rather than a layout one.
        //
        // Measured as a relation between the face and the box, not as a pixel: ask the typeface how
        // tall a line of this text is and require the box to be at least that.
        using var book = LongNamedBook();
        var view = new Views.CookBookDetailView
        {
            DataContext = new CookBookDetailViewModel(book, () => { }, () => { }),
        };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var digit = view.GetVisualDescendants().OfType<Border>()
            .First(b => b.Classes.Contains("fchip"))
            .GetVisualDescendants().OfType<TextBlock>().First();

        var natural = new FormattedText(digit.Text ?? "8", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(digit.FontFamily, digit.FontStyle, digit.FontWeight),
            digit.FontSize, Brushes.Black);

        Assert.True(digit.Bounds.Height >= natural.Height,
            $"the badge draws its digit in {digit.Bounds.Height:F1}px where the face needs "
            + $"{natural.Height:F1}px");
    }

    [Fact]
    public void Each_recipe_row_carries_its_own_exact_figure()
    {
        // The per-recipe rows are narrower than the tile and carry the same kind of number. Their
        // tooltip also has to keep the derivation the arrow between the chips and the figure leaves
        // implicit — one control has one tooltip, so it holds both facts or loses one.
        using var book = HugeBook();
        var vm = new CookBookDetailViewModel(book, () => { });
        var row = Assert.Single(vm.Recipes);

        Assert.Equal("1.60 billion", row.DnaSpaceText);
        Assert.Contains("1,600,000,000 unique DNA", row.DnaSpaceTip);
        Assert.Contains("quantized colors", row.DnaSpaceTip);
    }
}
