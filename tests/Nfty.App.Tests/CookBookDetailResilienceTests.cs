using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Reading an archive does not validate it, so the Explorer can be handed a book that
/// <see cref="Nfty.Core.Formats.Validator"/> would reject. The detail pane must degrade rather than
/// take the whole screen down with it — the Explorer now selects the cookbook on open, so a throw
/// here escapes the ExplorerViewModel constructor itself.
/// </summary>
public class CookBookDetailResilienceTests
{
    /// <summary>A book Validator rejects: kind "dynamic" with no colorization block. Writable and
    /// readable as an archive (a hand-edited manifest reaches exactly this state).</summary>
    private static LoadedCookBook Unmeasurable() => new()
    {
        Manifest = new CookBookManifest("cb", "Broken", new Dimensions(8, 8),
            new Collection("Broken", "", "BR"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                Ingredients = new[]
                {
                    new LoadedIngredient
                    {
                        Manifest = new IngredientManifest("bg", "Bg", LayerKind.Dynamic, null,
                            new[] { new Variant("day", "Day", 1) }),
                        VariantImages = new Dictionary<string, Image<Rgba32>> { ["day"] = new(8, 8) },
                    },
                },
            },
        },
    };

    [AvaloniaFact]
    public void A_book_whose_dna_space_cannot_be_counted_still_opens()
    {
        using var book = Unmeasurable();
        var vm = new ViewModels.CookBookDetailViewModel(book, () => { });

        Assert.Equal("—", vm.UniqueDnaText);            // not a crash, and not a fabricated number
        Assert.Equal("—", Assert.Single(vm.Recipes).DnaSpaceText);
        Assert.Equal(1, vm.RecipeCount);                // structure it CAN report is still reported
        Assert.Equal(1, vm.VariantCount);
    }
}
