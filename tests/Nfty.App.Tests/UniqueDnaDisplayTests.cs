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
    public void The_headline_figure_is_never_rounded()
    {
        // It USED to round past a billion and put the exact digits on a tooltip. The cell it lives
        // in was widened until the widest figure a long can hold fits at the smallest window the app
        // opens, so both the compact form and the tooltip are gone from this surface: a number you
        // have to hover to read is a number you cannot compare at a glance, and this is the one an
        // author tunes quantize steps against.
        using var book = HugeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("1,600,000,000", vm.UniqueDnaText);
    }

    [Fact]
    public void A_figure_that_fits_is_shown_in_full_on_both()
    {
        // Degrade only when you must. Essentially every real book lands here, so essentially every
        // book shows its true figure on the card — the rounded form is the exception, not the rule.
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new CookBookDetailViewModel(book, () => { });

        Assert.Equal("2", vm.UniqueDnaText);
    }

    [AvaloniaFact]
    public void The_headline_figure_is_shown_whole_and_carries_no_tooltip()
    {
        // The tile used to round and hang the exact digits on a tooltip. Both are gone, and the
        // ABSENCE is the assertion: a tooltip left behind on a control that no longer needs one is
        // how a surface comes to state the same number twice.
        using var book = HugeBook();
        var view = Render(HugeBook(), out var window);
        try
        {
            var figure = Figure(view);
            Assert.Equal("1,600,000,000", figure.Text);
            Assert.Null(ToolTip.GetTip(figure));
            Assert.Null(ToolTip.GetTip(figure.GetVisualAncestors().OfType<StackPanel>().First()));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_widest_figure_a_long_can_hold_fits_the_cell_at_the_smallest_window()
    {
        // THE MEASUREMENT THE WHOLE CELL EXISTS FOR. Width is a budget and an overrun is silent — a
        // TextBlock is arranged to its parent and clips, reporting the width it was asked for either
        // way — so this measures the INK of the worst string the formatter can emit against the
        // cell's real content box, inside the real Explorer, at the real page area.
        //
        // It is the EXACT form now, not the compact one: the cell was widened until every digit
        // fits, which is what let the compact form and the tooltip come off this surface. If this
        // ever fails, the figure has to start rounding again — it must not start clipping.
        var view = Render(HugeBook(), out var window);
        try
        {
            var figure = Figure(view);
            var cell = figure.GetVisualAncestors().OfType<Border>()
                .First(b => b.Classes.Contains("metric"));

            string worst = SpaceText.Exact(long.MaxValue);
            var ink = new FormattedText(worst, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(figure.FontFamily, figure.FontStyle, figure.FontWeight),
                figure.FontSize, Brushes.Black);

            double room = cell.Bounds.Width - cell.Padding.Left - cell.Padding.Right
                - cell.BorderThickness.Left - cell.BorderThickness.Right;

            Assert.True(ink.Width <= room,
                $"\"{worst}\" measures {ink.Width:F1}px in a cell with {room:F1}px of room");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_three_counts_still_hold_their_own_labels()
    {
        // The other side of the trade. The counts size to their content now, so they cannot clip —
        // but "cannot" is exactly the kind of claim that stops being true, and a metric label is the
        // widest thing in its cell, not the number.
        var view = Render(HugeBook(), out var window);
        try
        {
            foreach (string name in new[] { "RECIPES", "LAYERS", "VARIANTS" })
            {
                var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == name);
                var cell = label.GetVisualAncestors().OfType<Border>()
                    .First(b => b.Classes.Contains("metric"));

                // LetterSpacing is a control property and FormattedText has no setter for it, so it
                // is added back by hand: the class states it per character, which is what makes
                // these labels wider than they look in the markup.
                var ink = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(label.FontFamily, label.FontStyle, label.FontWeight),
                    label.FontSize, Brushes.Black);
                double width = ink.Width + label.LetterSpacing * name.Length;
                double room = cell.Bounds.Width - cell.Padding.Left - cell.Padding.Right
                    - cell.BorderThickness.Left - cell.BorderThickness.Right;

                Assert.True(width <= room + 0.5,
                    $"\"{name}\" measures {width:F1}px in a cell with {room:F1}px of room");
            }
        }
        finally { window.Close(); }
    }

    /// <summary>Renders the real Explorer at the smallest page the app allows.</summary>
    /// <param name="book">The book to open. Disposed by the caller closing the window.</param>
    /// <param name="window">The host window, for the caller to close.</param>
    /// <returns>The laid-out Explorer view.</returns>
    private static Views.ExplorerView Render(LoadedCookBook book, out Window window)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = explorer };

        // The page area at ShellViewModel's minimum window: (1200 - 24) / 1.2 wide and
        // (712 - ChromeReserve) / 1.2 tall. Derived rather than typed, so a change to the minimum
        // moves this measurement with it instead of leaving it describing an old window.
        window = new Window
        {
            Content = view,
            Width = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale,
            Height = (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    /// <summary>The headline figure's TextBlock, found by the label beside it.</summary>
    /// <param name="view">A laid-out Explorer.</param>
    /// <returns>The UNIQUE DNA figure.</returns>
    private static TextBlock Figure(Visual view) =>
        view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "UNIQUE DNA")
            .GetVisualAncestors().OfType<StackPanel>().First()
            .GetVisualDescendants().OfType<TextBlock>().First(t => t.Classes.Contains("mv"));

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
