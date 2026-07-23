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
        var vm = new CookBookDetailViewModel(book, new FakeNotYetWired());
        Assert.Equal("VaporPets", vm.Name);
        Assert.Equal("8x8", vm.CanvasText);
        Assert.Equal(2, vm.RecipeCount);
        Assert.Equal(3, vm.LayerCount);      // bg, aura, body
        Assert.Equal(3, vm.VariantCount);    // one variant each
        Assert.Equal(2, vm.Recipes.Count);
        Assert.Contains(vm.Recipes, r => r.Name == "cat");
        // custom-only, single variants → unique DNA space is small and exact
        Assert.False(string.IsNullOrEmpty(vm.UniqueDnaText));
    }

    [Fact]
    public void Cook_still_reports_not_yet_wired()
    {
        var n = new FakeNotYetWired();
        new CookBookDetailViewModel(ExplorerViewModelTests.TwoRecipeBook(), n).CookCommand.Execute(null);
        Assert.Equal("Cook", n.Last);
    }
}
