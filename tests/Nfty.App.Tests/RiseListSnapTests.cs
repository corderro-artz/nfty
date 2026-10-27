using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// V-2 and V-7: the Rise source rail folds on a whole row. At the smallest window the layers list
/// showed five and a half rows with the sixth sliced by the rail foot, and the Set's tile grid did
/// the same across the middle of a tile row.
/// </summary>
/// <remarks>
/// The property is arithmetic, as <see cref="RarityRailSnapTests"/> argues: the host is its top inset
/// plus a WHOLE number of row pitches. A list that happens to end on a row at one window size proves
/// nothing about another, and a fixture that cannot overflow proves nothing about overflow - so the
/// recipe here has twelve layers and the Set sixty assets.
/// </remarks>
public class RiseLayersRailSnapTests
{
    internal static LoadedCookBook TwelveLayerBook()
    {
        var layers = Enumerable.Range(0, 12).Select(i => $"l{i}").ToArray();
        LoadedIngredient Ing(string id) => new()
        {
            Manifest = new IngredientManifest(id, "Layer " + id, LayerKind.Custom, null,
                new[] { new Variant(id + "v", "V", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { [id + "v"] = RiseFixtures.Picture(RiseFixtures.Oak) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("stack", "Stack", layers, Array.Empty<IncompatibilityRule>()),
            Ingredients = layers.Select(Ing).ToArray(),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Stack", new Dimensions(8, 8),
                new Collection("Stack", "", "ST"), new Dictionary<string, double> { ["stack"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    internal static void Assert_whole_rows(string what, double hostHeight, double inset, double pitch)
    {
        Assert.True(pitch > 0, "no row was realized to measure");
        double rows = (hostHeight - inset) / pitch;
        Assert.True(Math.Abs(rows - Math.Round(rows)) < 0.02,
            $"{what}: {hostHeight:F1}px less a {inset:0}px inset is {rows:F2} rows of {pitch:F1}px - "
            + "the fold cuts one in half");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(120)]
    public void Rows_fold_on_a_row_boundary_at_every_size(double extraHeight)
    {
        using var book = TwelveLayerBook();
        var vm = RisePages.Vm(new RecipeRiseSource(book, book.Recipes[0], 1, new ImageBridge()));
        using var page = RisePages.Show(vm, RisePages.MinArea.Width, RisePages.MinArea.Height + extraHeight);
        Dispatcher.UIThread.RunJobs();

        var host = page.View.FindControl<ScrollViewer>("LayersScroll")!;
        var row = host.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("vcard"));
        Assert.True(host.Extent.Height > host.Viewport.Height, "the fixture no longer overflows the rail");
        Assert_whole_rows("layers rail", host.Bounds.Height, 12, row.Bounds.Height + row.Margin.Bottom);
    }
}

/// <summary>V-7: the same fold for the Set's tile grid (see <see cref="RiseLayersRailSnapTests"/>).</summary>
public class RiseAssetGridSnapTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(120)]
    public void Tiles_fold_on_a_row_boundary_at_the_smallest_window(double extraHeight)
    {
        var (vm, owner) = RisePages.DemoSetVm();
        using var page = RisePages.Show(vm, RisePages.MinArea.Width, RisePages.MinArea.Height + extraHeight, owner: owner);
        Dispatcher.UIThread.RunJobs();

        var host = page.View.FindControl<ListBox>("TileList")!;
        var item = host.GetVisualDescendants().OfType<ListBoxItem>().First();
        var scroller = host.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(scroller.Extent.Height > scroller.Viewport.Height, "the fixture no longer overflows the grid");
        RiseLayersRailSnapTests.Assert_whole_rows("tile grid", host.Bounds.Height, 8, item.Bounds.Height);
    }
}
