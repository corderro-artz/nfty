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
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace Nfty.App.Tests;

/// <summary>
/// THE RECIPE'S LAYER ROWS SCROLL, AND THE HERO AND THE COLUMN HEADER DO NOT.
/// </summary>
/// <remarks>
/// <para>The pane's main column was one <c>StackPanel</c> — hero, LAYERS heading, then the whole
/// table — so a recipe simply ran off the bottom of the pane it is drawn in. At the window this app
/// OPENS at, a six-layer recipe showed four and a half rows: no scrollbar, nothing saying there was
/// more, and the fifth row sliced across the middle by the status bar. A page must fit the smallest
/// window; a table whose length is the author's to choose is the standing exception, and it scrolls
/// its ROWS rather than its screen — the split the Ingredient pane and the Set browser's rarity rail
/// already keep.</para>
///
/// <para>A two- or three-layer fixture cannot see any of this, which is the blind spot
/// <c>RarityRailSnapTests</c> had to build a twelve-trait book to escape. This one builds twelve
/// layers and asserts against the pane's REAL area, derived from <see cref="ShellViewModel"/> rather
/// than stated — a layout test that names its own width is testing a window the app does not have.
/// </para>
/// </remarks>
public class RecipeLayerScrollTests
{
    /// <summary>The detail pane's own area at the smallest window the app allows: the page less the
    /// Contents tree column.</summary>
    private const double TreeColumn = 286;
    private static double PaneWidth => ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale - TreeColumn;
    private static double PaneHeight =>
        (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale;

    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id.ToUpperInvariant(), LayerKind.Custom, null,
            new[] { new Variant("a", "A", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = new(8, 8) },
    };

    private static LoadedCookBook Book(int layers)
    {
        var stack = Enumerable.Range(0, layers).Select(i => $"l{i:00}").ToArray();
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", stack, Array.Empty<IncompatibilityRule>()),
            Ingredients = stack.Select(Ing).ToArray(),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    private static (Window Window, Views.RecipeDetailView View, RecipeDetailViewModel Vm) Show(int layers)
    {
        var book = Book(layers);
        var vm = new RecipeDetailViewModel(book.Recipes[0], book, new ImageBridge(), _ => { }, null, false);
        var view = new Views.RecipeDetailView { DataContext = vm };
        var window = new Window { Content = view, Width = PaneWidth, Height = PaneHeight };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    /// <summary>The scroller the layer rows live in — the one whose content is the drag Panel.</summary>
    private static ScrollViewer RowScroller(Visual view) => view.GetVisualDescendants()
        .OfType<ScrollViewer>()
        .First(s => s.GetVisualDescendants().OfType<Panel>().Any(p => p.Name == "LayerStack"));

    [AvaloniaFact]
    public void A_long_stack_scrolls_its_rows_at_the_smallest_window()
    {
        var (window, view, vm) = Show(12);
        try
        {
            var scroller = RowScroller(view);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                $"twelve layers wanted {scroller.Extent.Height:0} in a {scroller.Viewport.Height:0}px "
                + "viewport and the scroller reports no overflow — the rows are being clipped, not scrolled");
            Assert.True(scroller.Viewport.Height > 0, "the rows were given no height at all");
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>The probe for the assertion above: a stack that fits must NOT scroll, or the test
    /// would pass on a pane that scrolls at every size.</summary>
    [AvaloniaFact]
    public void A_short_stack_does_not_scroll()
    {
        var (window, view, vm) = Show(3);
        try
        {
            var scroller = RowScroller(view);
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"three layers wanted {scroller.Extent.Height:0} of {scroller.Viewport.Height:0}px");
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>Every row is REACHABLE. Clipping and scrolling look identical until you try to get to
    /// the last one — a control that overruns is still arranged and still reports a sensible Bounds.</summary>
    [AvaloniaFact]
    public void The_last_layer_can_be_scrolled_into_view()
    {
        var (window, view, vm) = Show(12);
        try
        {
            var scroller = RowScroller(view);
            scroller.Offset = new Vector(0, scroller.Extent.Height - scroller.Viewport.Height);
            Dispatcher.UIThread.RunJobs();

            var rows = view.GetVisualDescendants().OfType<ItemsControl>()
                .First(c => c.Name == "LayerRows");
            var last = rows.GetRealizedContainers().OfType<Control>().Last();
            var bottom = last.TranslatePoint(new Point(0, last.Bounds.Height), scroller);

            Assert.NotNull(bottom);
            Assert.True(bottom!.Value.Y <= scroller.Viewport.Height + 0.5,
                $"the last layer still ends at {bottom.Value.Y:0} in a {scroller.Viewport.Height:0}px viewport");
            Assert.True(bottom.Value.Y > 0, "the last layer scrolled clean past the top");
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>
    /// The column header is PINNED. The percentages, kinds and absent chances below are read against
    /// it, and a header that scrolls away from the numbers it names is worse than no header — the
    /// same argument the Set browser's rarity rail and the Ingredient pane's variant table settled.
    /// </summary>
    [AvaloniaFact]
    public void The_column_header_stays_out_of_the_scroller()
    {
        var (window, view, vm) = Show(12);
        try
        {
            var scroller = RowScroller(view);
            var header = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("data-h"));

            Assert.False(header.GetVisualAncestors().Contains(scroller),
                "the LAYER / KIND / VARIANTS header is inside the row scroller and will scroll away");

            // And it is still above the rows, not merely elsewhere.
            var top = header.TranslatePoint(default, view)!.Value.Y;
            var rowsTop = scroller.TranslatePoint(default, view)!.Value.Y;
            Assert.True(top < rowsTop, $"header at {top:0} is not above the rows at {rowsTop:0}");
        }
        finally { vm.Dispose(); window.Close(); }
    }

    /// <summary>The pane itself still fits: scrolling the rows is what buys that, and a column that
    /// grew instead would just move the overflow one level out.</summary>
    [AvaloniaFact]
    public void The_pane_fits_the_smallest_window_whatever_the_stack_depth()
    {
        var (window, view, vm) = Show(12);
        try
        {
            view.Measure(new Size(PaneWidth, PaneHeight));
            Assert.True(view.DesiredSize.Height <= PaneHeight + 0.5,
                $"the recipe pane wants {view.DesiredSize.Height:0} of {PaneHeight:0}px");
        }
        finally { vm.Dispose(); window.Close(); }
    }
}
