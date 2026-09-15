using System;
using System.Collections.Generic;
using System.Linq;
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
/// THE INGREDIENT HERO'S SHARE STRIP IS A 2x2 BLOCK THAT PAGES, AND ITS HEIGHT IS A CONSTANT.
/// </summary>
/// <remarks>
/// Stacked, the strip's height was a property of the layer: one row at two variants, three at five,
/// six at twelve — so everything under it moved whenever a variant was added, and at twelve it took
/// 433px of a 494px pane and pushed the pane's own buttons off the screen. Capping it at six fixed
/// the overflow and left the varying height, and a column of six labelled percentages is the table
/// below drawn as bars. Four in a fixed grid is one height in every book; the rest are a page away.
/// </remarks>
public class HeroBarPagingTests
{
    private static IngredientDetailViewModel Pane(int variants)
    {
        var ids = Enumerable.Range(0, variants).Select(i => $"v{i:00}").ToArray();
        var ing = new LoadedIngredient
        {
            // Descending weights, so "biggest slice first" has something to order.
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Custom, null,
                ids.Select((id, i) => new Variant(id, $"Variant {i}", variants - i)).ToArray()),
            VariantImages = ids.ToDictionary(id => id, _ => new Image<Rgba32>(8, 8)),
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
        return new IngredientDetailViewModel(ing, recipe, book, new ImageBridge(),
            editIngredient: () => { }, isEditing: () => true);
    }

    [AvaloniaFact]
    public void A_page_is_four_bars_biggest_slice_first()
    {
        using var vm = Pane(7);

        Assert.Equal(4, IngredientDetailViewModel.HeroBarsPerPage);
        Assert.Equal(4, vm.VisibleHeroBars.Count);
        Assert.Equal(new[] { "Variant 0", "Variant 1", "Variant 2", "Variant 3" },
            vm.VisibleHeroBars.Select(b => b.Name));
        Assert.Equal("1–4 of 7", vm.HeroPageLabel);
        Assert.Equal(2, vm.HeroPageCount);
        Assert.True(vm.HasHeroPages);
    }

    [AvaloniaFact]
    public void The_last_page_carries_the_remainder_and_stops_there()
    {
        using var vm = Pane(7);

        Assert.False(vm.PreviousHeroPageCommand.CanExecute(null));
        Assert.True(vm.NextHeroPageCommand.CanExecute(null));

        vm.NextHeroPageCommand.Execute(null);

        Assert.Equal(3, vm.VisibleHeroBars.Count);           // 7 - 4
        Assert.Equal("5–7 of 7", vm.HeroPageLabel);
        Assert.False(vm.NextHeroPageCommand.CanExecute(null));
        Assert.True(vm.PreviousHeroPageCommand.CanExecute(null));

        vm.PreviousHeroPageCommand.Execute(null);
        Assert.Equal("1–4 of 7", vm.HeroPageLabel);
    }

    /// <summary>The pager's controls are present at one page too — they drive ink, never geometry —
    /// so adding a variant cannot move the block above them.</summary>
    [AvaloniaFact]
    public void A_layer_that_fits_one_page_still_has_a_pager()
    {
        using var vm = Pane(2);

        Assert.Single(vm.HeroDots);
        Assert.False(vm.HasHeroPages);
        Assert.Equal("1–2 of 2", vm.HeroPageLabel);
        Assert.False(vm.NextHeroPageCommand.CanExecute(null));
        Assert.False(vm.PreviousHeroPageCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void The_dots_count_the_pages_and_light_the_current_one()
    {
        using var vm = Pane(9);

        Assert.Equal(3, vm.HeroDots.Count);
        Assert.Equal(new[] { true, false, false }, vm.HeroDots.Select(d => d.IsCurrent));

        vm.NextHeroPageCommand.Execute(null);
        Assert.Equal(new[] { false, true, false }, vm.HeroDots.Select(d => d.IsCurrent));
    }

    /// <summary>
    /// THE HERO IS ONE HEIGHT WHATEVER THE LAYER. This is the assertion the old strip could not
    /// pass at any cap: a WrapPanel's height is a function of how many items it was given.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(12)]
    public void The_hero_is_the_same_height_for_every_variant_count(int variants)
    {
        // Measured against a one-variant layer, which is the smallest a hero can describe.
        double Height(int n)
        {
            var vm = Pane(n);
            var view = new Views.IngredientDetailView { DataContext = vm };
            var window = new Window { Content = view, Width = 760, Height = 620 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var hero = view.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("vhero"));
                return hero.Bounds.Height;
            }
            finally { vm.Dispose(); window.Close(); }
        }

        Assert.Equal(Height(1), Height(variants), precision: 0);
    }
}
