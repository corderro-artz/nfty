using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// A BOOK'S IDENTITY, RECIPE WEIGHTS AND PALETTE ARE EDITABLE; ONLY THE WEIGHTS REACH THE ROLL.
/// </summary>
public class CookBookIdentityEditTests
{
    private static Image<Rgba32> Solid(byte v)
    {
        var img = new Image<Rgba32>(4, 4);
        img.ProcessPixelRows(a =>
        {
            for (int y = 0; y < a.Height; y++)
                a.GetRowSpan(y).Fill(new Rgba32(v, v, v, 255));
        });
        return img;
    }

    private static LoadedRecipe Rec(string id) => new()
    {
        Manifest = new RecipeManifest(id, id.ToUpperInvariant(), new[] { id + "-layer" },
            Array.Empty<IncompatibilityRule>()),
        Ingredients = new[]
        {
            new LoadedIngredient
            {
                Manifest = new IngredientManifest(id + "-layer", id, LayerKind.Custom, null,
                    new[] { new Variant(id + "-a", "A", 1), new Variant(id + "-b", "B", 1) }),
                VariantImages = new Dictionary<string, Image<Rgba32>>
                {
                    [id + "-a"] = Solid(200),
                    [id + "-b"] = Solid(90),
                },
            },
        },
    };

    private static LoadedCookBook Book() => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
            new Collection("Coll", "desc", "SYM"),
            new Dictionary<string, double> { ["apple"] = 1, ["pear"] = 3 },
            TargetSupply: 50, Palette: new[] { "hex:112233" }),
        Recipes = new[] { Rec("apple"), Rec("pear") },
    };

    [Fact]
    public void EditIdentity_changes_only_the_named_fields()
    {
        using var book = Book();
        var coll = new Collection("New", "d2", "N");
        using var edited = CookBookEdits.EditIdentity(book, "Renamed", coll, null);

        Assert.Equal("Renamed", edited.Manifest.Name);
        Assert.Equal(coll, edited.Manifest.Collection);
        Assert.Null(edited.Manifest.TargetSupply);
        Assert.Equal(book.Manifest with { Name = "Renamed", Collection = coll, TargetSupply = null },
            edited.Manifest);
        Assert.Same(book.Recipes, edited.Recipes);
    }

    [Fact]
    public void SetRecipeWeight_zero_shelves_and_keeps_the_recipe()
    {
        using var book = Book();
        using var edited = CookBookEdits.SetRecipeWeight(book, "pear", 0);

        Assert.Equal(0, edited.Manifest.RecipeWeights["pear"]);
        Assert.Equal(1, edited.Manifest.RecipeWeights["apple"]);
        Assert.Equal(2, edited.Recipes.Count);
        Assert.Equal(3, book.Manifest.RecipeWeights["pear"]);
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.SetRecipeWeight(book, "quince", 1));
    }

    [Fact]
    public void SetPalette_round_trips_through_the_archive()
    {
        using var book = Book();
        using var edited = CookBookEdits.SetPalette(book, new[] { "hex:aabbcc", "hsv:322,83,84" });
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "b.cbk");
            CookBookArchive.Write(path, edited.Manifest, edited.Recipes);
            using var read = CookBookArchive.Read(path);
            Assert.Equal(new[] { "hex:aabbcc", "hsv:322,83,84" }, read.Manifest.Palette);

            using var cleared = CookBookEdits.SetPalette(edited, Array.Empty<string>());
            Assert.Null(cleared.Manifest.Palette);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Identity_edits_do_not_change_the_collection_for_a_seed()
    {
        using var book = Book();
        using var edited = CookBookEdits.EditIdentity(
            CookBookEdits.SetPalette(book, new[] { "hex:000000" }), "Other",
            new Collection("Other", "x", "O"), 9);
        var opts = new GenerateOptions(Count: 8, Seed: "hello", EnforceUniqueDna: false);
        using var before = Generator.Generate(book, opts);
        using var after = Generator.Generate(edited, opts);

        Assert.Equal(before.Assets.Select(a => a.Dna), after.Assets.Select(a => a.Dna));
    }
}
