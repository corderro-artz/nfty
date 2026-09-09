using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The CookBook card's DNA-space table: it pages rather than scrolls, and how many rows a page
/// holds is measured from a row the app actually drew.
/// </summary>
/// <remarks>
/// <para>The card used to scroll, which is how it survived being ~130px taller than the smallest
/// window the app opens allows: the detail card gets a few hundred pixels either way, and everything
/// below the fold — the mint bar and the Cook button among it — was simply out of sight. Paging the
/// one unbounded thing on the card is what let the scroller go, and with it the possibility of
/// scrolling the collection's own summary off the screen.</para>
///
/// <para>Every assertion here is measured off a laid-out frame inside the real
/// <see cref="Views.ExplorerView"/>, at page areas derived from <see cref="ShellViewModel"/>'s own
/// minimum — a card measured on its own in a window 200px wider than the page ever is passed while
/// the running app clipped, which is the mistake this file exists not to repeat.</para>
/// </remarks>
public class DnaSpaceLayoutTests
{
    /// <summary>Eight recipes, six layers each, and one with ten so the +N slot is exercised.</summary>
    private static LoadedCookBook ManyRecipes()
    {
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
                [new Variant("a", "A", 1), new Variant("b", "B", 1), new Variant("c", "C", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["a"] = new Image<Rgba32>(8, 8),
                ["b"] = new Image<Rgba32>(8, 8),
                ["c"] = new Image<Rgba32>(8, 8),
            },
        };
        string[] names =
        [
            "Chest", "Strongbox", "Coffer", "Casket",
            "Lockbox", "Reliquary", "Footlocker", "Reinforced Strongbox",
        ];
        var recipes = names.Select((n, i) =>
        {
            // The last recipe is deliberately deeper than the badge strip has slots.
            string[] layers = Enumerable.Range(0, i == names.Length - 1 ? 10 : 6)
                .Select(k => $"r{i}l{k}").ToArray();
            return new LoadedRecipe
            {
                Manifest = new RecipeManifest($"r{i}", n, layers, []),
                Ingredients = layers.Select(Ing).ToArray(),
            };
        }).ToArray();

        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "8 Recipes", new Dimensions(8, 8),
                // A description that WRAPS TO TWO LINES, because the identity card sizes to it and
                // the table gets what is left. The first version of this fixture left it empty,
                // which bought the card 33px it does not have in the app - so the harness said the
                // table fit while the running app sliced its only row in half.
                new Collection("8 Recipes",
                    "A demo collection that ships with nfty: layered chests to open, edit and "
                    + "cook. Nothing here is precious - break it and rebuild it.", "EGT"),
                names.Select((_, i) => $"r{i}").ToDictionary(id => id, _ => 1d),
                TargetSupply: 1200),
            Recipes = recipes,
        };
    }

    /// <summary>Renders the real Explorer at a given WINDOW size, page area derived from it.</summary>
    /// <param name="book">The book to open.</param>
    /// <param name="windowWidth">Outer window width.</param>
    /// <param name="windowHeight">Outer window height.</param>
    /// <param name="window">The host, for the caller to close.</param>
    /// <returns>The laid-out view and its CookBook view model.</returns>
    private static (Views.ExplorerView View, CookBookDetailViewModel Vm) Render(
        LoadedCookBook book, double windowWidth, double windowHeight, out Window window)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
            session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        var view = new Views.ExplorerView { DataContext = explorer };
        window = new Window
        {
            Content = view,
            Width = (windowWidth - 24) / ShellViewModel.BaseScale,
            Height = (windowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
        return (view, (CookBookDetailViewModel)card.DataContext!);
    }

    // ---- it pages, and the page is measured ------------------------------------------------------

    [AvaloniaFact]
    public void A_taller_window_puts_more_rows_on_a_page()
    {
        // The whole "breathes with the window" behaviour, and the reason PageSize is measured rather
        // than declared. A constant would be right at one window size and wrong at every other.
        using var small = ManyRecipes();
        var (_, tight) = Render(small, ShellViewModel.MinWindowWidth, ShellViewModel.MinWindowHeight,
            out var w1);
        int atMinimum = tight.PageSize;
        w1.Close();

        using var big = ManyRecipes();
        var (_, roomy) = Render(big, 1920, 1080, out var w2);
        int fullScreen = roomy.PageSize;
        w2.Close();

        Assert.True(atMinimum >= 1, $"the smallest window showed {atMinimum} rows");
        Assert.True(fullScreen > atMinimum,
            $"full screen showed {fullScreen} rows against {atMinimum} at the minimum");
    }

    [AvaloniaFact]
    public void The_card_does_not_scroll_and_the_pinned_block_stays_on_screen()
    {
        // The card carries no ScrollViewer at all — that is what makes "the mint bar is always
        // visible" a property of the layout rather than a habit of small books.
        using var book = ManyRecipes();
        var (view, _) = Render(book, ShellViewModel.MinWindowWidth, ShellViewModel.MinWindowHeight,
            out var window);
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();

            // There is one ScrollViewer, and it is there to report a fixed height rather than to
            // scroll: both bars are off and the table pages so its content never exceeds the
            // viewport.
            var host = Assert.Single(card.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(ScrollBarVisibility.Disabled, host.VerticalScrollBarVisibility);

            // MEASURED AGAINST THE ROW, not against the host's own Extent. Extent was the first
            // version of this assertion and it is VACUOUS: with both bars disabled a ScrollViewer
            // measures its child at the height it was given, so Extent reports the viewport back
            // whatever the rows do. It read as green while the app drew 26px of a 35px row, sliced
            // across the middle by the mint bar underneath.
            var vm = (CookBookDetailViewModel)card.DataContext!;
            var row = card.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("crow"));
            Assert.True(row.Bounds.Height > 0);
            Assert.True(vm.PageSize * row.Bounds.Height <= host.Viewport.Height + 0.5,
                $"{vm.PageSize} rows of {row.Bounds.Height:F1}px in a {host.Viewport.Height:F1}px "
                + "viewport - a page is showing a row it cannot draw whole");

            // And a whole row must fit AT ALL at the smallest window, which is the claim that stops
            // PageSize's Math.Max(1, ...) floor from papering over a card that has outgrown the
            // window. The slack is thin by design - the card is nearly full here - so state it:
            // furniture that grows by more than this has taken the table's last row.
            Assert.True(host.Viewport.Height >= row.Bounds.Height,
                $"the rows host is {host.Viewport.Height:F1}px and a row is {row.Bounds.Height:F1}px");

            var bar = card.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("distbar"));
            double barBottom = bar.TranslatePoint(new Avalonia.Point(0, bar.Bounds.Height), card)!.Value.Y;
            Assert.True(barBottom <= card.Bounds.Height + 0.5,
                $"the mint bar ends at {barBottom:F1} in a card {card.Bounds.Height:F1} tall");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_mint_bar_describes_the_whole_book_while_the_table_shows_a_page()
    {
        // A filter narrows the view, never the count. The page is what you are reading; the bar is
        // what the collection IS, and binding it to the page would tell a reader an eight-recipe
        // book has two.
        using var book = ManyRecipes();
        var (view, vm) = Render(book, ShellViewModel.MinWindowWidth, ShellViewModel.MinWindowHeight,
            out var window);
        try
        {
            Assert.True(vm.PageSize < vm.Recipes.Count, "this fixture should need more than one page");
            Assert.Equal(vm.PageSize, vm.VisibleRecipes.Count);

            var bar = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("distbar"));
            int segments = bar.GetVisualDescendants().OfType<Border>()
                .Count(b => b.Classes.Contains("series"));
            Assert.Equal(vm.Recipes.Count, segments);
        }
        finally { window.Close(); }
    }

    // ---- what lines up ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_header_and_every_row_share_one_set_of_column_edges()
    {
        // Avalonia has no SharedSizeGroup, so across two grids a stated pixel column is the only
        // thing that aligns — the rule the recipe layer table already follows. The header states the
        // same columns the rows do, and this is what proves the two copies agree.
        using var book = ManyRecipes();
        var (view, _) = Render(book, 1920, 1080, out var window);
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            double Right(Visual v) => Math.Round(
                v.TranslatePoint(new Avalonia.Point(v.Bounds.Width, 0), card)!.Value.X);

            var figures = card.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("cnum")).ToList();
            var shares = card.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("cshare")).ToList();
            var header = card.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Classes.Contains("th")).ToList();

            Assert.True(figures.Count > 1, "needs several rows to have anything to align");
            Assert.Single(figures.Select(Right).Distinct());
            Assert.Single(shares.Select(Right).Distinct());

            // And the header's UNIQUE DNA sits over the figures it names.
            var dnaHeader = header.First(t => t.Text == "UNIQUE DNA");
            Assert.Equal(Right(figures[0]), Right(dnaHeader));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_badge_strip_is_evenly_spaced_and_nothing_overlaps()
    {
        // The slots were 34 for content that needs 35 - a separator (7), its gap (4) and a badge
        // (24) - so every right-aligned slot overflowed its LEADING edge: each x began 1px INSIDE
        // the badge to its left, the last badge ran 1px past the strip into the arrow column, and
        // the gaps alternated 0 and 4, which is what made the row read as unevenly spaced.
        //
        // Asserted as ONE distance repeated rather than as a number: the point is that a reader sees
        // a rhythm, and a strip whose separators hug the badge on their left has none.
        using var book = ManyRecipes();
        var (view, _) = Render(book, 1920, 1080, out var window);
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            var row = card.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("crow"));
            var strip = row.GetVisualDescendants().OfType<UniformGrid>().First();

            var marks = new List<(double Left, double Right)>();
            foreach (var t in strip.GetVisualDescendants().OfType<TextBlock>()
                         .Where(t => t.Classes.Contains("ftimes") && t.IsVisible && t.Bounds.Width > 0))
            {
                double l = t.TranslatePoint(new Avalonia.Point(0, 0), strip)!.Value.X;
                marks.Add((l, l + t.Bounds.Width));
            }
            foreach (var b in strip.GetVisualDescendants().OfType<Border>()
                         .Where(b => b.Classes.Contains("fchip")))
            {
                double l = b.TranslatePoint(new Avalonia.Point(0, 0), strip)!.Value.X;
                marks.Add((l, l + b.Bounds.Width));
            }

            var ordered = marks.OrderBy(m => m.Left).ToList();
            Assert.True(ordered.Count >= 4, "needs several marks to have a rhythm at all");

            var gaps = Enumerable.Range(1, ordered.Count - 1)
                .Select(i => Math.Round(ordered[i].Left - ordered[i - 1].Right, 1))
                .ToList();

            Assert.DoesNotContain(gaps, g => g < 0);          // nothing sits inside its neighbour
            Assert.Single(gaps.Distinct());                    // one rhythm, not two alternating

            // And the strip's own content stays inside it, which is what the last badge did not do.
            Assert.True(ordered[^1].Right <= strip.Bounds.Width + 0.5,
                $"the last badge runs to {ordered[^1].Right:F1} in a {strip.Bounds.Width:F1}px strip");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Badge_column_n_is_in_the_same_place_on_every_row_including_the_overflow()
    {
        // Laid out as a RUN, a six-layer recipe put its first badge where a ten-layer one put its
        // second and nothing lined up down the table. One slot per column fixes badge n's position,
        // and the +N takes the LAST slot — so it lines up with the final badge of the rows that need
        // none, rather than sitting wherever its row's run happened to end.
        using var book = ManyRecipes();
        var (view, vm) = Render(book, 1920, 1080, out var window);
        try
        {
            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            Assert.Equal(vm.Recipes.Count, vm.VisibleRecipes.Count);   // one page at this size

            var rows = card.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("crow")).ToList();
            Assert.Equal(vm.Recipes.Count, rows.Count);

            // Every badge in a column shares a left edge AND a width: the slots align the columns,
            // and one stated badge width stops a two-character +N drawing a wider box than a digit.
            for (int col = 0; col < CookBookDetailViewModel.FactorSlots; col++)
            {
                var boxes = rows
                    .Select(r => r.GetVisualDescendants().OfType<Border>()
                        .Where(b => b.Classes.Contains("fchip")).ElementAtOrDefault(col))
                    .OfType<Border>()
                    .Select(b => (
                        Left: Math.Round(b.TranslatePoint(new Avalonia.Point(0, 0), card)!.Value.X),
                        b.Bounds.Width))
                    .ToList();

                Assert.True(boxes.Count > 1, $"column {col} should appear on several rows");
                Assert.Single(boxes.Distinct());
            }

            // The deep recipe spends its last slot on a +N rather than pushing anything sideways.
            var overflow = card.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("more")).ToList();
            var more = Assert.Single(overflow);
            var deepRow = more.GetVisualAncestors().OfType<Border>()
                .First(b => b.Classes.Contains("crow"));
            Assert.Equal(CookBookDetailViewModel.FactorSlots,
                deepRow.GetVisualDescendants().OfType<Border>()
                    .Count(b => b.Classes.Contains("fchip")));
        }
        finally { window.Close(); }
    }
}
