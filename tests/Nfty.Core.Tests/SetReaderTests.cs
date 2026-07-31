using System.IO;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

public class SetReaderTests
{
    // Minimal 1-recipe, 2-variant custom cookbook (custom = no colorization) with an 8x8 canvas.
    private static LoadedCookBook TinyBook()
    {
        LoadedIngredient Ing() => new()
        {
            Manifest = new IngredientManifest("bg", "bg", LayerKind.Custom, null,
                new[] { new Variant("a", "A", 1), new Variant("b", "B", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>>
                { ["a"] = new(8, 8), ["b"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, System.Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { Ing() },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
                new Collection("VaporCats", "desc", "VC"), new Dictionary<string, double> { ["cat"] = 100 }),
            Recipes = new[] { recipe },
        };
    }

    private static string CookTo(bool pack)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(TinyBook(), new GenerateOptions(Count: 2, Seed: "seed1"));
        SetWriter.Write(set, dir, pack);
        return dir;
    }

    [Fact]
    public void Reads_a_cooked_folder()
    {
        var dir = CookTo(pack: false);
        using var loaded = SetReader.Read(dir);
        Assert.Equal("VaporCats", loaded.Manifest.Name);
        Assert.Equal(2, loaded.Manifest.Count);
        Assert.Equal(2, loaded.Items.Count);
        Assert.All(loaded.Items, i => Assert.True(File.Exists(i.ImagePath)));
        Assert.All(loaded.Items, i => Assert.False(string.IsNullOrEmpty(i.Dna)));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Reads_a_packed_set_and_cleans_up_temp_on_dispose()
    {
        var dir = CookTo(pack: true);
        string archive = dir + ".set";
        string? tempSeen;
        using (var loaded = SetReader.Read(archive))
        {
            Assert.Equal(2, loaded.Items.Count);
            tempSeen = Path.GetDirectoryName(loaded.Items[0].ImagePath);   // inside the extracted temp dir
            Assert.True(File.Exists(loaded.Items[0].ImagePath));
        }
        // after Dispose, the extracted temp dir is gone (the archive + original dir remain)
        Assert.False(Directory.Exists(Path.GetDirectoryName(tempSeen!)));
        Directory.Delete(dir, recursive: true); File.Delete(archive);
    }

    [Fact]
    public void Missing_set_json_throws()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        Assert.ThrowsAny<System.Exception>(() => SetReader.Read(empty));
        Directory.Delete(empty, recursive: true);
    }
}
