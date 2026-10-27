using System.CommandLine;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary><c>nfty model x.rcp --seed S</c>: a Recipe rolled as <c>preview</c> rolls it and risen as a stack.</summary>
public class ModelRecipeTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    private static LoadedIngredient Layer(string id, int variants, Func<int, int, int, Rgba32> pixel, byte front, byte back)
    {
        var list = Enumerable.Range(0, variants).Select(i => new Variant($"{id}{i}", $"{id}{i}", 1)).ToArray();
        var images = new Dictionary<string, Image<Rgba32>>();
        var depths = new Dictionary<string, DepthMap>();
        for (int v = 0; v < variants; v++)
        {
            var img = new Image<Rgba32>(4, 4);
            var bytes = new byte[4 * 4 * 2];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    img[x, y] = pixel(v, x, y);
                    if (img[x, y].A == 255) { bytes[(y * 4 + x) * 2] = (byte)(front + v); bytes[(y * 4 + x) * 2 + 1] = back; }
                }
            images[$"{id}{v}"] = img;
            depths[$"{id}{v}"] = DepthMap.FromBytes(4, 4, bytes);
        }
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, id, LayerKind.Custom, null, list),
            VariantImages = images,
            Depths = depths,
        };
    }

    private static string WriteRecipe(bool partial = false)
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "chest.rcp");
        var body = Layer("body", 3, (v, _, _) => new Rgba32((byte)(60 * (v + 1)), 90, 40, 255), 1, 3);
        var trim = Layer("trim", 3, (v, x, y) =>
            x >= 1 && x <= 2 && y >= 1 ? new Rgba32(220, (byte)(50 * v), 30, partial && x == 1 && y == 1 ? (byte)128 : (byte)255) : default, 3, 1);
        RecipeArchive.Write(path,
            new RecipeManifest("chest", "Chest", new[] { "body", "trim" }, Array.Empty<IncompatibilityRule>()),
            new[] { body, trim });
        return path;
    }

    [Fact]
    public void Model_of_a_recipe_matches_the_stack_preview_draws()
    {
        string rcp = WriteRecipe();
        string outPath = Path.Combine(Path.GetDirectoryName(rcp)!, "chest.glb");

        Assert.Equal(0, Run("model", rcp, "--seed", "s", "--out", outPath));

        // The same draw `preview chest.rcp --seed s` makes, combined by the one stack rule.
        using var recipe = RecipeArchive.Read(rcp);
        var rolled = StackRoll.ForRecipe(recipe, StackRoll.RngFor("s"));
        var images = rolled.Select(l => VariantPreview.Render(l.Ingredient, l.VariantId, l.ColorSpec)).ToList();
        try
        {
            using var rise = StackDepth.Combine(
                rolled.Select((l, i) => (images[i], l.Ingredient.Depths.GetValueOrDefault(l.VariantId))).ToList(),
                "the Chest recipe (seed 's')");
            var options = new RiseOptions { VoxelSize = 1f, Pivot = Pivot.BottomCenter, Format = ModelFormat.Glb };
            var mesh = Riser.TryBuild(rise.Art, rise.Depth, options, "x", rise.Faces)!;
            var expected = ModelWriter.Write(mesh, rise.Art, options, "chest", rise.Faces).Single();

            Assert.Equal(expected.Bytes, File.ReadAllBytes(outPath));
        }
        finally { foreach (var i in images) i.Dispose(); }
    }

    [Fact]
    public void A_different_seed_can_roll_a_different_model()
    {
        string rcp = WriteRecipe();
        string dir = Path.GetDirectoryName(rcp)!;
        var files = new HashSet<string>();
        foreach (var seed in new[] { "a", "b", "c", "d", "e", "f" })
        {
            string o = Path.Combine(dir, seed + ".glb");
            Assert.Equal(0, Run("model", rcp, "--seed", seed, "--out", o));
            files.Add(Convert.ToHexString(File.ReadAllBytes(o)));
        }
        Assert.True(files.Count > 1);
    }

    [Fact]
    public void Model_of_a_recipe_requires_a_seed()
    {
        var errors = Parse("model chest.rcp --out chest.glb").Errors;
        Assert.Contains(errors, e => e.Message.Contains("--seed"));
        Assert.Empty(Parse("model chest.rcp --seed s --out chest.glb").Errors);
    }

    [Fact]
    public void A_seed_is_refused_for_a_picture_and_a_variant_and_the_other_form_s_options_for_a_recipe()
    {
        Assert.Contains(Parse("model s.png --seed s --out s.glb").Errors, e => e.Message.Contains("--seed"));
        Assert.Contains(Parse("model hat.igt --variant v --seed s --out s.glb").Errors, e => e.Message.Contains("--seed"));
        Assert.NotEmpty(Parse("model chest.rcp --seed s --variant v --out c.glb").Errors);
        Assert.NotEmpty(Parse("model chest.rcp --seed s --front 1 --back 1 --out c.glb").Errors);
    }

    [Theory]
    [InlineData("model s.png --out s.glb --voxel-size 0.0005", false)]
    [InlineData("model s.png --out s.glb --voxel-size 0.001", true)]
    [InlineData("model s.png --out s.glb --voxel-size 1000", true)]
    [InlineData("model s.png --out s.glb --voxel-size 1000.5", false)]
    [InlineData("generate b.cbk --count 1 --out o --models --voxel-size 2000", false)]
    [InlineData("generate b.cbk --count 1 --out o --models --voxel-size 0.001", true)]
    public void The_cli_keeps_the_one_voxel_size_range(string line, bool ok)
    {
        var errors = Parse(line).Errors;
        Assert.Equal(ok, errors.Count == 0);
        if (!ok) Assert.Contains(errors, e => e.Message.StartsWith("Voxel size must be between 0.001 and 1000"));
    }

    [Fact]
    public void A_partly_transparent_layer_is_refused_naming_it()
    {
        string rcp = WriteRecipe(partial: true);
        string outPath = Path.Combine(Path.GetDirectoryName(rcp)!, "chest.glb");

        var ex = Assert.Throws<PartialAlphaException>(() => Run("model", rcp, "--seed", "s", "--out", outPath));

        Assert.Contains("layer 2", ex.Message);
        Assert.False(File.Exists(outPath));
    }
}
