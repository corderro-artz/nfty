using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Tests;

/// <summary>Small 8x8 pictures and a one-recipe book of them, for the Rise sources' tests.</summary>
internal static class RiseFixtures
{
    // Body "oak": a rounded slab, rows 1-3 color A, rows 4-6 color B (same-color fill has two regions).
    internal static readonly string[] Oak =
        { "........", "..AAAA..", ".AAAAAA.", ".AAAAAA.", ".BBBBBB.", ".BBBBBB.", "..BBBB..", "........" };

    // Body "ghost": the same slab with one half-alpha pixel at (3,3) — 2D only.
    internal static readonly string[] Ghost =
        { "........", "..AAAA..", ".AAAAAA.", ".AA~AAA.", ".BBBBBB.", ".BBBBBB.", "..BBBB..", "........" };

    // Lock "gold": a 2x2 solid at (3,3)-(4,4).
    internal static readonly string[] Gold =
        { "........", "........", "........", "...GG...", "...GG...", "........", "........", "........" };

    /// <summary>Draws rows of letters: A, B, G are solid colors, '~' is half alpha, anything else is clear.</summary>
    internal static Image<Rgba32> Picture(string[] rows)
    {
        var img = new Image<Rgba32>(rows[0].Length, rows.Length);
        for (int y = 0; y < rows.Length; y++)
            for (int x = 0; x < rows[y].Length; x++)
                img[x, y] = rows[y][x] switch
                {
                    'A' => new Rgba32(178, 122, 70, 255),
                    'B' => new Rgba32(94, 59, 34, 255),
                    'G' => new Rgba32(240, 204, 98, 255),
                    '~' => new Rgba32(200, 40, 40, 128),
                    _ => new Rgba32(0, 0, 0, 0),
                };
        return img;
    }

    /// <summary>The book in memory: recipe "chest" = layers ["body", "lock"]; body variants oak (weight 3),
    /// ghost (1); lock variant gold.</summary>
    internal static LoadedCookBook ChestBook(DepthMap? oakDepth = null, DepthMap? ghostDepth = null,
        DepthMap? goldDepth = null)
    {
        var bodyDepths = new Dictionary<string, DepthMap>();
        if (oakDepth is not null) bodyDepths["oak"] = oakDepth;
        if (ghostDepth is not null) bodyDepths["ghost"] = ghostDepth;
        var body = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                new[] { new Variant("oak", "Oak", 3), new Variant("ghost", "Ghost", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
            {
                ["oak"] = Picture(Oak),
                ["ghost"] = Picture(Ghost),
            },
            Depths = bodyDepths,
        };
        var lck = new LoadedIngredient
        {
            Manifest = new IngredientManifest("lock", "Lock", LayerKind.Custom, null,
                new[] { new Variant("gold", "Gold", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["gold"] = Picture(Gold) },
            Depths = goldDepth is null ? new Dictionary<string, DepthMap>() : new Dictionary<string, DepthMap> { ["gold"] = goldDepth },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("chest", "Chest", new[] { "body", "lock" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { body, lck },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest", new Dimensions(8, 8),
                new Collection("Chest", "", "CH"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    /// <summary>Writes the book to a temp .cbk, reads it back (so SourceSha256 is set) and opens it in a session.</summary>
    internal static (CookBookSession Session, string Path) OnDisk(LoadedCookBook book)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-").FullName, "Chest.cbk");
        CookBookPersistence.WriteNew(path, book.Manifest, book.Recipes);
        book.Dispose();
        var session = new CookBookSession();
        session.Open(CookBookArchive.Read(path), path);
        return (session, path);
    }

    internal static VariantRiseSource Variant(CookBookSession s, string variant = "oak", bool editing = true)
    {
        var recipe = s.Current!.Recipes.Single(r => r.Manifest.Id == "chest");
        var body = recipe.Ingredients.Single(i => i.Manifest.Id == "body");
        return new VariantRiseSource(recipe, body, variant, new ImageBridge(), s, () => editing);
    }
}
