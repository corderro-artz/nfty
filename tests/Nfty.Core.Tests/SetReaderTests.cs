using System;
using System.IO;
using System.Linq;
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
    public void A_SET_PAST_TEN_THOUSAND_ASSETS_LOADS_IN_NUMBER_ORDER()
    {
        // THE STEM IS PADDED TO FOUR, SO IT STOPS PADDING AT 9,999. The reader used to order its
        // items by the FILENAME, ordinally - and "10000.json" sorts before "9999.json", so a
        // collection past ten thousand assets loaded with its last ten thousand in front. That is
        // the order the Set browser's grid shows and the order this list carries, so it was a real
        // ordering bug rather than a cosmetic one about how a file manager lists a folder.
        //
        // Fixed by sorting on the number rather than by widening the pad. The stem is part of a
        // layout that has shipped: metadata/NNNN.json records its own image path, so renaming would
        // strand every URL already published from a cooked Set, and extend adds assets to a Set
        // whose existing files are already named - a collection-wide width would leave one Set
        // holding two paddings.
        var dir = CookTo(pack: false);
        try
        {
            string nfty = Path.Combine(dir, "nfty");
            string template = File.ReadAllText(Path.Combine(nfty, "0001.json"));
            foreach (var f in Directory.EnumerateFiles(nfty, "*.json")) File.Delete(f);

            // Written in the order that makes the ordinal sort WRONG, so a reader that kept the
            // enumeration order by accident still fails this.
            foreach (int n in new[] { 10_000, 9_999, 10_001 })
            {
                File.WriteAllText(Path.Combine(nfty, $"{n:D4}.json"),
                    template.Replace("\"setNumber\": 1", $"\"setNumber\": {n}",
                        StringComparison.Ordinal));
            }

            using var loaded = SetReader.Read(dir);

            Assert.Equal(new[] { 9_999, 10_000, 10_001 }, loaded.Items.Select(i => i.Number));
        }
        finally { Directory.Delete(dir, recursive: true); }
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
        string archive = Path.Combine(dir, Path.GetFileName(dir) + ".set");
        string? tempSeen;
        using (var loaded = SetReader.Read(archive))
        {
            Assert.Equal(2, loaded.Items.Count);
            tempSeen = Path.GetDirectoryName(loaded.Items[0].ImagePath);   // inside the extracted temp dir
            Assert.True(File.Exists(loaded.Items[0].ImagePath));
        }
        // after Dispose, the extracted temp dir is gone (the archive + original dir remain)
        Assert.False(Directory.Exists(Path.GetDirectoryName(tempSeen!)));
        Assert.True(File.Exists(archive));
        // One delete, not two: the archive lives INSIDE the Set folder now, so removing the folder
        // takes it with it and a following File.Delete throws DirectoryNotFoundException.
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Missing_set_json_throws()
    {
        var empty = Directory.CreateTempSubdirectory().FullName;
        Assert.ThrowsAny<System.Exception>(() => SetReader.Read(empty));
        Directory.Delete(empty, recursive: true);
    }
}
