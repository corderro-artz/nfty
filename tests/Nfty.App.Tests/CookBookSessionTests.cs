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
}
