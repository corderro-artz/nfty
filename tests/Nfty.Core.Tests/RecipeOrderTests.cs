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
/// A BOOK MAY SAY WHAT ORDER ITS RECIPES ARE LISTED IN, AND THAT ORDER CHANGES NO ASSET.
/// </summary>
/// <remarks>
/// <para>Recipes used to be listed in whatever order their ZIP entry names sorted into — which is
/// their ids, ordinally — so a book had no say in how its own contents were presented and no field
/// to say it with. <see cref="CookBookManifest.RecipeOrder"/> is that field: additive, optional, and
/// therefore no schema bump, exactly as <c>TargetSupply</c> and <c>Palette</c> were.</para>
///
/// <para>The load-bearing claim is the last test here. <c>WeightedRoller.Prepare</c> sorts its keys
/// ordinally before building the cumulative table, so the listing cannot reach the roll — which is
/// what makes reordering Recipes safe in a way reordering LAYERS is explicitly not. If that ever
/// stops being true, this is the test that says so.</para>
/// </remarks>
public class RecipeOrderTests
{
    private static LoadedIngredient Ing(string id) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            new[] { new Variant(id + "-a", "A", 1), new Variant(id + "-b", "B", 1) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            [id + "-a"] = Solid(4, 4, 200),
            [id + "-b"] = Solid(4, 4, 90),
        },
    };

    private static Image<Rgba32> Solid(int w, int h, byte v)
    {
        var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(a =>
        {
            for (int y = 0; y < a.Height; y++)
            {
                var row = a.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++) row[x] = new Rgba32(v, v, v, 255);
            }
        });
        return img;
    }

    private static LoadedRecipe Rec(string id) => new()
    {
        Manifest = new RecipeManifest(id, id.ToUpperInvariant(), new[] { id + "-layer" },
            Array.Empty<IncompatibilityRule>()),
        Ingredients = new[] { Ing(id + "-layer") },
    };

    /// <summary>Three recipes whose ids sort ordinally as apple, mango, pear.</summary>
    private static LoadedCookBook Book(IReadOnlyList<string>? order = null) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(4, 4),
            new Collection("Book", "", "B"),
            new Dictionary<string, double> { ["apple"] = 1, ["mango"] = 1, ["pear"] = 1 },
            RecipeOrder: order),
        Recipes = new[] { Rec("apple"), Rec("mango"), Rec("pear") },
    };

    private static string[] Ids(LoadedCookBook b) => b.Recipes.Select(r => r.Manifest.Id).ToArray();

    private static string WriteTo(LoadedCookBook book)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "book.cbk");
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
        return path;
    }

    // ---- the edit -------------------------------------------------------------------------------

    [Fact]
    public void Moving_a_recipe_reorders_the_list_and_records_the_whole_order()
    {
        using var book = Book();
        using var moved = CookBookEdits.MoveRecipe(book, "pear", 0);

        Assert.Equal(new[] { "pear", "apple", "mango" }, Ids(moved));
        Assert.Equal(new[] { "pear", "apple", "mango" }, moved.Manifest.RecipeOrder);
    }

    [Fact]
    public void A_move_past_either_end_is_clamped_rather_than_refused()
    {
        using var book = Book();
        using var up = CookBookEdits.MoveRecipe(book, "apple", -5);
        using var down = CookBookEdits.MoveRecipe(book, "pear", 99);

        Assert.Equal(new[] { "apple", "mango", "pear" }, Ids(up));
        Assert.Equal(new[] { "apple", "mango", "pear" }, Ids(down));
    }

    [Fact]
    public void Moving_a_recipe_that_is_not_there_throws()
    {
        using var book = Book();
        Assert.Throws<KeyNotFoundException>(() => CookBookEdits.MoveRecipe(book, "quince", 0));
    }

    // ---- the file -------------------------------------------------------------------------------

    [Fact]
    public void A_book_with_no_order_still_lists_its_recipes_ordinally()
    {
        var path = WriteTo(Book());
        try
        {
            using var read = CookBookArchive.Read(path);
            Assert.Null(read.Manifest.RecipeOrder);
            Assert.Equal(new[] { "apple", "mango", "pear" }, Ids(read));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void The_order_survives_a_round_trip()
    {
        using var book = Book();
        using var moved = CookBookEdits.MoveRecipe(book, "pear", 0);
        var path = WriteTo(moved);
        try
        {
            using var read = CookBookArchive.Read(path);
            Assert.Equal(new[] { "pear", "apple", "mango" }, Ids(read));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>A listing is a preference, not a schema. One that has gone stale — an id left behind
    /// by a deleted recipe, a recipe added since — must not stop a book opening, and must not hide a
    /// recipe that is really in the archive.</summary>
    [Fact]
    public void A_stale_order_is_tolerated_and_hides_nothing()
    {
        // "quince" no longer exists; "mango" was never listed.
        using var book = Book(new[] { "quince", "pear", "apple" });
        var path = WriteTo(book);
        try
        {
            using var read = CookBookArchive.Read(path);
            // Listed ones first in their stated order, then whatever is left, ordinally.
            Assert.Equal(new[] { "pear", "apple", "mango" }, Ids(read));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    // ---- the promise ----------------------------------------------------------------------------

    /// <summary>
    /// REORDERING RECIPES IS NOT A REROLL. Reordering a recipe's LAYERS is, and this is the test
    /// that keeps the two apart: the same seed over a reordered book must produce the same assets,
    /// byte for byte and DNA for DNA.
    /// </summary>
    [Fact]
    public void Reordering_recipes_generates_the_identical_collection()
    {
        using var book = Book();
        using var reordered = CookBookEdits.MoveRecipe(book, "pear", 0);

        // Unique DNA OFF and a count well past the space: every draw is kept, so this compares the
        // ROLL SEQUENCE itself rather than a filtered view of it — and a rejection is the one thing
        // that could mask a changed sequence by quietly re-rolling into the same set.
        var opts = new GenerateOptions(Count: 24, Seed: "hello", EnforceUniqueDna: false);
        using var before = Generator.Generate(book, opts);
        using var after = Generator.Generate(reordered, opts);

        Assert.Equal(before.Assets.Select(a => a.Dna), after.Assets.Select(a => a.Dna));
        Assert.Equal(before.Assets.Select(a => a.RecipeId), after.Assets.Select(a => a.RecipeId));
        for (int i = 0; i < before.Assets.Count; i++)
            Assert.True(SamePixels(before.Assets[i].Image, after.Assets[i].Image),
                $"asset {i} differs after a recipe reorder — the listing has reached the roll");
    }

    private static bool SamePixels(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a[x, y] != b[x, y]) return false;
        return true;
    }
}
