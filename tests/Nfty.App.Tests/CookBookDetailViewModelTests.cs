using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookDetailViewModelTests
{
    [Fact]
    public void Exposes_identity_counts_and_unique_dna()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();   // cat[bg,aura]+dog[body], custom kind, 1 variant each
        var vm = new CookBookDetailViewModel(book, () => { });
        Assert.Equal("VaporPets", vm.Name);
        // A real multiplication sign with spaces, as explorer.html renders the canvas chip - not "8x8".
        Assert.Equal("8 × 8", vm.CanvasText);
        Assert.Equal(2, vm.RecipeCount);
        Assert.Equal(3, vm.LayerCount);      // bg, aura, body
        Assert.Equal(3, vm.VariantCount);    // one variant each
        Assert.Equal(2, vm.Recipes.Count);
        Assert.Contains(vm.Recipes, r => r.Name == "cat");
        // custom-only, single variants → unique DNA space is small and exact
        Assert.False(string.IsNullOrEmpty(vm.UniqueDnaText));
    }

    [AvaloniaFact]
    public void Cook_invokes_the_cook_action()
    {
        bool cooked = false;
        var vm = new CookBookDetailViewModel(ExplorerViewModelTests.TwoRecipeBook(), () => cooked = true);
        vm.CookCommand.Execute(null);
        Assert.True(cooked);
    }

    /// <summary>
    /// The row carries a hue SHIFT, not a color and not an index into a fixed set. What this level
    /// owes is that the assignment is stable and that no two recipes share a color — the property
    /// six cycling tokens could not keep past six.
    /// </summary>
    /// <remarks>
    /// It used to carry a <c>Color</c> hashed from the recipe id, which was deterministic but
    /// off-palette by construction and — the part that mattered — structurally unable to change
    /// with the theme. The paint is a token turned by this shift now, so both properties hold.
    /// </remarks>
    [AvaloniaFact]
    public void Recipe_series_assignment_is_stable_and_adjacent_recipes_differ()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm1 = new CookBookDetailViewModel(book, () => { });
        var vm2 = new CookBookDetailViewModel(book, () => { });

        // Stable: the same book paints the same way on the next launch. This is what rules out a
        // color actually rolled at random, which would repaint the book differently every time.
        Assert.Equal(vm1.Recipes.Select(r => r.HueShift), vm2.Recipes.Select(r => r.HueShift));
        Assert.NotEqual(vm1.Recipes[0].HueShift, vm1.Recipes[1].HueShift);
        Assert.Equal(0, vm1.Recipes[0].HueShift);          // the first is the anchor itself
    }

    /// <summary>
    /// The cycle is gone: an eight-recipe book carries eight distinguishable colors, where six
    /// tokens assigned by position gave recipes 7 and 8 the colors of 1 and 2.
    /// </summary>
    /// <remarks>
    /// The bar is the surface this was visible on — two pairs of segments in one row that nothing
    /// could tell apart. The floor is stated as an angle rather than as "not equal" because equality
    /// is not the requirement: two hues a degree apart are the same defect as two identical ones.
    /// </remarks>
    [AvaloniaFact]
    public void No_two_recipes_share_a_color_however_many_there_are()
    {
        using var book = ManyRecipeBook(24);
        var vm = new CookBookDetailViewModel(book, () => { });
        var shifts = vm.Recipes.Select(r => r.HueShift).ToList();

        Assert.Equal(24, shifts.Count);
        foreach (double s in shifts) Assert.InRange(s, 0, 360);

        for (int i = 0; i < shifts.Count; i++)
        {
            for (int j = i + 1; j < shifts.Count; j++)
            {
                double d = Math.Abs(shifts[i] - shifts[j]);
                double apart = Math.Min(d, 360 - d);         // it is a wheel: 359° and 1° are close
                Assert.True(apart > 5,
                    $"recipes {i} and {j} are {apart:F1}° apart, which is one color");
            }
        }

        // And consecutive ones — the pair a reader compares — are far more than merely distinct.
        for (int i = 1; i < shifts.Count; i++)
        {
            double d = Math.Abs(shifts[i] - shifts[i - 1]);
            Assert.True(Math.Min(d, 360 - d) > 60, $"rows {i - 1} and {i} are adjacent and close");
        }
    }

    /// <summary>A book of <paramref name="count"/> one-layer recipes, for counting colors.</summary>
    /// <param name="count">How many recipes.</param>
    /// <returns>The book. The caller disposes it.</returns>
    internal static LoadedCookBook ManyRecipeBook(int count)
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
        var recipes = Enumerable.Range(0, count).Select(i => new LoadedRecipe
        {
            Manifest = new RecipeManifest($"r{i}", $"Recipe {i}", [$"r{i}l0"], []),
            Ingredients = [Ing($"r{i}l0")],
        }).ToArray();

        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Many", new Dimensions(8, 8),
                new Collection("Many", "", "MNY"),
                recipes.ToDictionary(r => r.Manifest.Id, _ => 1d)),
            Recipes = recipes,
        };
    }
}
