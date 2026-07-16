using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Reading an archive eagerly decodes every variant PNG, so a Loaded* tree owns real unmanaged
/// memory. A GUI opens and closes cookbooks repeatedly, so that memory must be reclaimable.
/// </summary>
public class LoadedDisposalTests
{
    private static LoadedIngredient Ing(string id, params string[] variantIds) => new()
    {
        Manifest = new IngredientManifest(id, id, LayerKind.Custom, null,
            variantIds.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = variantIds.ToDictionary(
            v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255))),
    };

    private static LoadedRecipe Rec(string id, params LoadedIngredient[] ings) => new()
    {
        Manifest = new RecipeManifest(id, id, ings.Select(i => i.Manifest.Id).ToList(),
            Array.Empty<IncompatibilityRule>()),
        Ingredients = ings,
    };

    private static LoadedCookBook Book(params LoadedRecipe[] recipes) => new()
    {
        Manifest = new CookBookManifest("cb", "Book", new Dimensions(2, 2),
            new Collection("B", "d", "B"), recipes.ToDictionary(r => r.Manifest.Id, _ => 1.0)),
        Recipes = recipes,
    };

    [Fact]
    public void Disposing_an_ingredient_disposes_its_variant_images()
    {
        var ing = Ing("bg", "a", "b");
        var images = ing.VariantImages.Values.ToList();

        ing.Dispose();

        foreach (var img in images)
            Assert.Throws<ObjectDisposedException>(() => img[0, 0]);
    }

    [Fact]
    public void Disposing_a_recipe_disposes_its_ingredients()
    {
        var recipe = Rec("cat", Ing("bg", "a"), Ing("body", "x"));
        var images = recipe.Ingredients.SelectMany(i => i.VariantImages.Values).ToList();

        recipe.Dispose();

        foreach (var img in images)
            Assert.Throws<ObjectDisposedException>(() => img[0, 0]);
    }

    [Fact]
    public void Disposing_a_cookbook_disposes_every_variant_image_in_every_recipe()
    {
        var book = Book(Rec("cat", Ing("bg", "a", "b")), Rec("robot", Ing("bg", "x")));
        var images = book.Recipes
            .SelectMany(r => r.Ingredients)
            .SelectMany(i => i.VariantImages.Values)
            .ToList();

        book.Dispose();

        Assert.Equal(3, images.Count);
        foreach (var img in images)
            Assert.Throws<ObjectDisposedException>(() => img[0, 0]);
    }

    [Fact]
    public void Disposing_a_cookbook_twice_is_safe()
    {
        var book = Book(Rec("cat", Ing("bg", "a")));

        book.Dispose();

        Assert.Null(Record.Exception(book.Dispose));
    }

    [Fact]
    public void An_image_shared_by_two_ingredients_survives_the_double_dispose()
    {
        // Nothing in the archive readers shares images, but an in-memory book can. ImageSharp's
        // Dispose is idempotent, so the second free must be a no-op rather than a crash.
        var shared = new Image<Rgba32>(2, 2, new Rgba32(1, 2, 3, 255));
        var a = new LoadedIngredient
        {
            Manifest = new IngredientManifest("a", "a", LayerKind.Custom, null,
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = shared },
        };
        var b = new LoadedIngredient
        {
            Manifest = new IngredientManifest("b", "b", LayerKind.Custom, null,
                new[] { new Variant("v", "v", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = shared },
        };
        var book = Book(Rec("cat", a, b));

        Assert.Null(Record.Exception(book.Dispose));
        Assert.Throws<ObjectDisposedException>(() => shared[0, 0]);
    }

    [Fact]
    public void A_cookbook_read_from_an_archive_frees_its_images_on_dispose()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        using (var source = Book(Rec("cat", Ing("bg", "a"))))
            CookBookArchive.Write(path, source.Manifest, source.Recipes);

        var book = CookBookArchive.Read(path);
        var images = book.Recipes.SelectMany(r => r.Ingredients)
            .SelectMany(i => i.VariantImages.Values).ToList();

        book.Dispose();

        Assert.NotEmpty(images);
        foreach (var img in images)
            Assert.Throws<ObjectDisposedException>(() => img[0, 0]);
    }
}
