using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class CookBookSessionTests
{
    private static (LoadedCookBook book, Image<Rgba32> variantImage) MiniBook(string id)
    {
        var img = new Image<Rgba32>(4, 4, new Rgba32(120, 120, 120, 255));
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = img },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest(id, id, new Dimensions(4, 4),
                new Collection(id, "", "X"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
        return (book, img);
    }

    [Fact]
    public void Opening_a_second_book_disposes_the_first()
    {
        var session = new CookBookSession();
        var (a, aImg) = MiniBook("A");
        var (b, _) = MiniBook("B");
        session.Open(a);
        session.Open(b);
        Assert.Same(b, session.Current);
        Assert.Throws<ObjectDisposedException>(() => aImg.ProcessPixelRows(_ => { }));  // A's image freed
        session.Dispose();
    }

    [Fact]
    public void Close_disposes_and_clears_and_raises_changed()
    {
        var session = new CookBookSession();
        var (a, _) = MiniBook("A");
        int changes = 0; session.Changed += () => changes++;
        session.Open(a);
        session.Close();
        Assert.Null(session.Current);
        Assert.Equal(2, changes);   // open + close
        session.Dispose();
    }

    private static LoadedCookBook OneRecipeBook()
    {
        var img = new Image<Rgba32>(2, 2, new Rgba32(80, 80, 80, 255));
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "BG", LayerKind.Custom, null, new[] { new Variant("a", "A", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["a"] = img },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "CB", new Dimensions(2, 2),
                new Collection("CB", "", "X"), new Dictionary<string, double> { ["cat"] = 1 }),
            Recipes = new[] { recipe },
        };
    }

    [Fact]
    public void Open_records_the_source_path_and_Close_clears_it()
    {
        using var session = new CookBookSession();
        using var a = OneRecipeBook();           // existing helper in this test file
        session.Open(a, "C:/books/a.cbk");
        Assert.Equal("C:/books/a.cbk", session.SourcePath);
        session.Close();
        Assert.Null(session.SourcePath);
    }

    [Fact]
    public void Replace_swaps_current_without_disposing_the_previous_book()
    {
        using var session = new CookBookSession();
        var a = OneRecipeBook();
        var b = OneRecipeBook();
        session.Open(a, "C:/books/a.cbk");
        int changed = 0; session.Changed += () => changed++;
        session.Replace(b);
        Assert.Same(b, session.Current);
        Assert.Equal("C:/books/a.cbk", session.SourcePath);   // path preserved
        Assert.Equal(1, changed);
        // `a` was NOT disposed: its variant images are still usable.
        var img = a.Recipes[0].Ingredients[0].VariantImages.Values.First();
        Assert.True(img.Width > 0);                            // throws ObjectDisposedException if disposed
        a.Dispose(); b.Dispose();
    }
}
