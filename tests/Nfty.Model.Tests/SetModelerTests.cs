using System.Text.Json.Nodes;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class SetModelerTests
{
    private const int Size = 4;

    /// <summary>A body that covers everything (front 1) and an optional hat on the top row (front 3).</summary>
    private static LoadedCookBook Book(bool partialHat = false)
    {
        var body = Art.FromAscii("####", "####", "####", "####");
        var hat = Art.FromAscii(".##.", "....", "....", "....");
        if (partialHat) hat[0, 3] = new Rgba32(200, 40, 40, 128);

        LoadedIngredient Layer(string id, string name, Image<Rgba32> img, int front) => new()
        {
            Manifest = new IngredientManifest(id, name, LayerKind.Custom, null, [new Variant("v", "Ghost", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = img },
            Depths = new Dictionary<string, DepthMap> { ["v"] = Art.Depth(img, (_, _) => (front, 0)) },
        };

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", ["body", "hat"], [],
                AbsentPercent: new Dictionary<string, double> { ["hat"] = 50 }),
            Ingredients = [Layer("body", "Body", body, 1), Layer("hat", "Hat", hat, 3)],
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("chest", "Chest", new Dimensions(Size, Size),
                new Collection("Chest", "d", "CH"), new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = [recipe],
        };
    }

    private static GeneratedSet Cook(LoadedCookBook book, int count = 12) =>
        Generator.Generate(book, new GenerateOptions(count, "models", EnforceUniqueDna: false));

    private static bool HasHat(GeneratedAsset a) => a.Traits.Any(t => t.IngredientId == "hat");

    [Fact]
    public void ForAsset_matches_ForSelection_over_the_assets_traits()
    {
        using var book = Book();
        using var set = Cook(book);
        var recipe = book.Recipes[0];
        Assert.Contains(set.Assets, HasHat);
        foreach (var asset in set.Assets)
        {
            var viaSelection = StackDepth.ForSelection(recipe,
                asset.Traits.Select(t => (t.IngredientId, t.VariantId)).ToList());
            Assert.Equal(viaSelection.ToBytes(), StackDepth.ForAsset(recipe, asset)!.ToBytes());
        }
    }

    [Fact]
    public void ForAsset_takes_the_traits_in_paint_order()
    {
        // The hat paints over the body, so at a hat pixel the HAT's depth wins. Reversed traits would
        // let the body win there.
        using var book = Book();
        using var set = Cook(book);
        var asset = set.Assets.First(HasHat);
        var depth = StackDepth.ForAsset(book.Recipes[0], asset)!;
        Assert.Equal(3, depth.Front(1, 0));
        Assert.Equal(1, depth.Front(0, 0));
    }

    [Fact]
    public void ForAsset_is_null_for_an_all_absent_roll()
    {
        using var book = Book();
        using var picture = new Image<Rgba32>(Size, Size);
        using var asset = new GeneratedAsset
        {
            SetNumber = 1, Dna = "d", RecipeId = "r", RecipeName = "R",
            Image = picture.Clone(), Traits = [], ColorRolls = [],
        };
        Assert.Null(StackDepth.ForAsset(book.Recipes[0], asset));
    }

    [Fact]
    public void A_two_d_only_book_is_refused_by_name()
    {
        using var book = Book(partialHat: true);
        var ex = Assert.Throws<PartialAlphaBookException>(() => new SetModeler(book, new RiseOptions()));
        Assert.Contains("Chest", ex.Message);
        Assert.Contains("Hat / Ghost", ex.Message);
        Assert.Equal("Chest", ex.BookName);
        Assert.Single(ex.Variants);
    }

    [Fact]
    public void The_refusal_lists_at_most_five_names_then_counts_the_rest()
    {
        var variants = Enumerable.Range(1, 8)
            .Select(i => new Nfty.Core.Stats.VoxelVariant($"L{i}", "V", "v", 16, 1)).ToList();
        var message = new PartialAlphaBookException("Big", variants).Message;
        Assert.Contains("L5 / V", message);
        Assert.DoesNotContain("L6 / V", message);
        Assert.Contains("and 3 more", message);
    }

    [Fact]
    public void The_model_of_an_asset_is_the_one_nfty_model_writes_for_its_picture()
    {
        using var book = Book();
        using var set = Cook(book);
        var options = new RiseOptions { Format = ModelFormat.Glb, VoxelSize = 2f };
        var modeler = new SetModeler(book, options);
        var asset = set.Assets.First(HasHat);

        var viaSeam = Assert.Single(modeler.Model(asset, "0007")!);

        var depth = StackDepth.ForAsset(book.Recipes[0], asset);
        var mesh = Riser.TryBuild(asset.Image, depth, options, "x")!;
        var direct = Assert.Single(ModelWriter.Write(mesh, asset.Image, options, "0007"));
        Assert.Equal(direct.FileName, viaSeam.FileName);
        Assert.Equal(direct.Bytes, viaSeam.Bytes);
    }

    [Fact]
    public void A_fully_clear_asset_has_no_model()
    {
        using var book = Book();
        var modeler = new SetModeler(book, new RiseOptions());
        using var asset = new GeneratedAsset
        {
            SetNumber = 1, Dna = "d", RecipeId = "r", RecipeName = "R",
            Image = new Image<Rgba32>(Size, Size), Traits = [], ColorRolls = [],
        };
        Assert.Null(modeler.Model(asset, "0001"));
    }

    private static (float Min, float Max) ZExtent(byte[] glb)
    {
        var (json, _) = GltfValidator.ReadGlb(glb);
        int accessor = json["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"]!.GetValue<int>();
        var node = json["accessors"]![accessor]!;
        return (node["min"]![2]!.GetValue<float>(), node["max"]![2]!.GetValue<float>());
    }

    [Fact]
    public void Each_asset_rises_with_its_own_stack_depth()
    {
        using var book = Book();
        using var set = Cook(book);
        var modeler = new SetModeler(book, new RiseOptions { Format = ModelFormat.Glb, VoxelSize = 2f });

        var withHat = set.Assets.First(HasHat);
        var without = set.Assets.First(a => !HasHat(a));

        // Depth in z = 1 (paper) + front + back voxels; hat pixel front 3, body only front 1.
        var (loHat, hiHat) = ZExtent(modeler.Model(withHat, "a")![0].Bytes);
        var (loPlain, hiPlain) = ZExtent(modeler.Model(without, "b")![0].Bytes);
        Assert.Equal((1 + 3) * 2f, hiHat - loHat);
        Assert.Equal((1 + 1) * 2f, hiPlain - loPlain);
    }

    [Fact]
    public void Models_are_byte_identical_at_any_thread_count()
    {
        using var book = Book();
        using var set = Cook(book);
        var modeler = new SetModeler(book, new RiseOptions());

        Dictionary<string, byte[]> WriteAt(int threads)
        {
            string dir = Directory.CreateTempSubdirectory().FullName;
            ThreadPool.GetMinThreads(out var w, out var io);
            ThreadPool.SetMinThreads(threads, io);
            try { SetWriter.Write(set, dir, pack: false, modeler); }
            finally { ThreadPool.SetMinThreads(w, io); }
            string models = Path.Combine(dir, "models");
            return Directory.EnumerateFiles(models).ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes);
        }

        var baseline = WriteAt(1);
        Assert.Equal(set.Assets.Count, baseline.Count);
        foreach (int threads in new[] { 2, 8 })
        {
            var run = WriteAt(threads);
            Assert.Equal(baseline.Keys.Order(StringComparer.Ordinal), run.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, bytes) in baseline) Assert.Equal(bytes, run[name]);
        }
    }

    [Theory]
    [InlineData(ModelFormat.Glb)]
    [InlineData(ModelFormat.GltfWithBin)]
    [InlineData(ModelFormat.GltfEmbedded)]
    public void Every_format_writes_valid_files_into_the_set(ModelFormat format)
    {
        using var book = Book();
        using var set = Cook(book, 4);
        var modeler = new SetModeler(book, new RiseOptions { Format = format });
        string dir = Directory.CreateTempSubdirectory().FullName;
        SetWriter.Write(set, dir, pack: false, modeler);

        foreach (var asset in set.Assets)
        {
            string stem = asset.SetNumber.ToString("D4");
            var names = modeler.FileNames(stem);
            var files = names.Select(n =>
            {
                string path = Path.Combine(dir, "models", n);
                Assert.True(File.Exists(path), path);
                return new ModelFile(n, File.ReadAllBytes(path));
            }).ToList();

            var (json, bin) = format == ModelFormat.Glb
                ? GltfValidator.ReadGlb(files[0].Bytes)
                : GltfValidator.ReadGltf(files);
            GltfValidator.AssertValid(json, bin);
        }
        Assert.Equal(set.Assets.Count * modeler.FileNames("0001").Count,
            Directory.EnumerateFiles(Path.Combine(dir, "models")).Count());
    }

    [Fact]
    public void File_names_are_known_without_building_a_model()
    {
        using var book = Book();
        Assert.Equal(new[] { "0001.glb" }, new SetModeler(book, new RiseOptions()).FileNames("0001"));
        Assert.Equal(new[] { "0001.gltf", "0001.bin" },
            new SetModeler(book, new RiseOptions { Format = ModelFormat.GltfWithBin }).FileNames("0001"));
        Assert.Equal(new[] { "0001.gltf" },
            new SetModeler(book, new RiseOptions { Format = ModelFormat.GltfEmbedded }).FileNames("0001"));
    }
}
