using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
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
/// The DNA-space row height is STATED, so the page-size arithmetic cannot feed on its own output.
/// </summary>
/// <remarks>
/// <para><b>The app died a few seconds into dragging a window edge.</b> Avalonia throws
/// <c>InvalidOperationException: Infinite layout loop detected</c> when one render pass runs too
/// many layout iterations, and the CookBook card produced exactly that: its page-size handler ran on
/// <c>LayoutUpdated</c> and MEASURED a drawn row, whose height wobbles for a pass after the
/// <c>ItemsControl</c> rebuilds its containers — so each pass computed a slightly different answer,
/// wrote <c>PageSize</c>, and forced another pass inside the same render. Reproduced by resizing the
/// real window: dead at the fourth step.</para>
///
/// <para><b>This does not reproduce the loop, and saying so is the point.</b> A resize test was
/// written first and passed with the crashing code restored — headless layout does not run the
/// render loop the detector lives in, so it could not fail for the thing it named, which makes it
/// decoration. What CAN be asserted is the property that makes the loop impossible: a handler that
/// writes during layout may only read inputs it cannot change, so the row height is stated once in
/// <c>Tokens.axaml</c> and read from there by both the <c>.crow</c> style and the arithmetic.
/// Unpinning it is the edit that brings the crash back, and that is what fails here.</para>
/// </remarks>
public class ResizeStabilityTests
{
    [AvaloniaFact]
    public void The_row_height_is_stated_rather_than_measured()
    {
        using var book = ManyRecipes();
        var (window, view) = Render(book);
        try
        {
            Assert.True(Application.Current!.TryGetResource("DnaRowHeight", null, out object? token));
            double stated = Assert.IsType<double>(token);

            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            var row = card.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("crow"));

            // The style pins it, so the drawn row IS the token. Remove that Height setter and the
            // page-size handler goes back to reading a number the ItemsControl can move under it.
            Assert.False(double.IsNaN(row.Height),
                "the row height is not stated - the page-size arithmetic can loop again");
            Assert.Equal(stated, row.Height);
            Assert.Equal(stated, row.Bounds.Height, 1);

            // And the page size is that arithmetic's answer, so it has to agree with the rows drawn.
            var vm = (CookBookDetailViewModel)card.DataContext!;
            Assert.Equal(vm.PageSize, vm.VisibleRecipes.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Many_resizes_leave_the_card_in_a_working_state()
    {
        // Weaker than it looks, deliberately: headless layout cannot raise the loop detector, so this
        // is not the crash's regression test - the one above is. It is here because a page that ends
        // a drag showing the wrong number of rows would be a real defect this does catch.
        using var book = ManyRecipes();
        var (window, view) = Render(book);
        double w = window.Width;
        double h = window.Height;
        try
        {
            for (int i = 0; i < 120; i++)
            {
                window.Width = w + (i % 17) * 9;
                window.Height = h + (i % 11) * 7;
                Dispatcher.UIThread.RunJobs();
            }

            var card = view.GetVisualDescendants().OfType<Views.CookBookDetailView>().First();
            var vm = (CookBookDetailViewModel)card.DataContext!;
            Assert.True(vm.PageSize >= 1);
            Assert.Equal(vm.PageSize, vm.VisibleRecipes.Count);

            var host = card.GetVisualDescendants().OfType<ScrollViewer>().First(s => s.Name == "RowsHost");
            var row = card.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("crow"));
            Assert.True(vm.PageSize * row.Bounds.Height <= host.Viewport.Height + 0.5,
                "after resizing, a page is showing a row it cannot draw whole");
        }
        finally { window.Close(); }
    }

    private static (Window Window, Views.ExplorerView View) Render(LoadedCookBook book)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
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
        return (window, view);
    }

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
        string[] names = { "Chest", "Strongbox", "Coffer", "Casket", "Lockbox", "Reliquary", "Footlocker", "Reinforced Strongbox" };
        var recipes = names.Select((n, i) =>
        {
            string[] layers = Enumerable.Range(0, 6).Select(k => $"r{i}l{k}").ToArray();
            return new LoadedRecipe
            {
                Manifest = new RecipeManifest($"r{i}", n, layers, []),
                Ingredients = layers.Select(Ing).ToArray(),
            };
        }).ToArray();

        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "8 Recipes", new Dimensions(8, 8),
                new Collection("8 Recipes",
                    "A demo collection that ships with nfty: layered chests to open, edit and cook. "
                    + "Nothing here is precious - break it and rebuild it.", "CHST"),
                names.Select((_, i) => $"r{i}").ToDictionary(id => id, _ => 1d),
                TargetSupply: 1200),
            Recipes = recipes,
        };
    }
}
