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
    /// The row carries a series INDEX, not a color. It used to carry a <c>Color</c> hashed from the
    /// recipe id, which was deterministic but off-palette by construction and — the part that
    /// mattered — structurally unable to change with the theme, so the distribution bar rendered
    /// identically bright in dark mode where it is the heaviest element on the card. The paint is
    /// now a token resolved by the view, and all this level has to guarantee is that adjacent
    /// recipes differ and the assignment is stable.
    /// </summary>
    [AvaloniaFact]
    public void Recipe_series_assignment_is_stable_and_adjacent_recipes_differ()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm1 = new CookBookDetailViewModel(book, () => { });
        var vm2 = new CookBookDetailViewModel(book, () => { });

        Assert.Equal(vm1.Recipes.Select(r => r.Series), vm2.Recipes.Select(r => r.Series));
        Assert.NotEqual(vm1.Recipes[0].Series, vm1.Recipes[1].Series);

        // Exactly one series flag is set per row, so the view's six class bindings cannot both
        // paint nothing (an unset Background reads as transparent, which looks like a layout bug)
        // or paint twice.
        foreach (var r in vm1.Recipes)
        {
            var flags = new[] { r.IsSeries1, r.IsSeries2, r.IsSeries3, r.IsSeries4, r.IsSeries5, r.IsSeries6 };
            Assert.Single(flags, f => f);
            Assert.InRange(r.Series, 1, 6);
        }
    }
}
