using System.CommandLine;
using System.Text.Json;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>nfty set depth</c>: stores one variant's depth map from a picture. The archive is written by
/// the same writer the Rise page saves with, and every other variant must come through untouched.
/// </summary>
public class SetDepthCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) =>
        CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static string Capture(Func<int> run, out int code)
    {
        var original = Console.Out;
        var sw = new StringWriter();
        Console.SetOut(sw);
        try { code = run(); }
        finally { Console.SetOut(original); }
        return sw.ToString();
    }

    private static Image<Rgba32> Art(int size, byte seed, bool clearLastPixel)
    {
        var img = new Image<Rgba32>(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                img[x, y] = new Rgba32((byte)(seed + x), (byte)(seed + y), seed, 255);
        if (clearLastPixel) img[size - 1, size - 1] = new Rgba32(0, 0, 0, 0);
        return img;
    }

    /// <summary>A 2x2 Custom ingredient "hat": variant "v" and variant "w" (w carries its own depth).</summary>
    private static string MakeIgt(string dir, bool clearV11 = false, string name = "hat")
    {
        string igt = Path.Combine(dir, name + ".igt");
        using var v = Art(2, 10, clearV11);
        using var w = Art(2, 90, false);
        IngredientArchive.Write(igt,
            new IngredientManifest(name, "Hat", LayerKind.Custom, null,
                [new Variant("v", "V", 1), new Variant("w", "W", 1)]),
            new Dictionary<string, Image<Rgba32>> { ["v"] = v, ["w"] = w },
            new Dictionary<string, DepthMap> { ["w"] = DepthMap.FromBytes(2, 2, [1, 2, 3, 4, 5, 6, 7, 8]) });
        return igt;
    }

    private static string MakeMap(string dir, int size, string file, params (int x, int y, Rgba32 px)[] pixels)
    {
        string path = Path.Combine(dir, file);
        using var map = new Image<Rgba32>(size, size);
        foreach (var (x, y, px) in pixels) map[x, y] = px;
        map.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void Set_depth_stores_the_map_and_reads_back()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir);
        string map = MakeMap(dir, 2, "d.png", (0, 0, new Rgba32(3, 5, 99, 7)));

        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", map));

        using var read = IngredientArchive.Read(igt);
        Assert.Equal(3, read.Depths["v"].Front(0, 0));
        Assert.Equal(5, read.Depths["v"].Back(0, 0));
    }

    [Fact]
    public void Other_variants_keep_their_art_and_depth_byte_for_byte()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir);
        string map = MakeMap(dir, 2, "d.png", (0, 0, new Rgba32(3, 5, 0, 255)));
        using var before = IngredientArchive.Read(igt);
        var wDepthBefore = before.Depths["w"].ToBytes();
        var wArtBefore = new Rgba32[4];
        before.VariantImages["w"].CopyPixelDataTo(wArtBefore);
        var vArtBefore = new Rgba32[4];
        before.VariantImages["v"].CopyPixelDataTo(vArtBefore);

        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", map));

        using var after = IngredientArchive.Read(igt);
        Assert.Equal(wDepthBefore, after.Depths["w"].ToBytes());
        var wArtAfter = new Rgba32[4];
        after.VariantImages["w"].CopyPixelDataTo(wArtAfter);
        Assert.Equal(wArtBefore, wArtAfter);
        var vArtAfter = new Rgba32[4];
        after.VariantImages["v"].CopyPixelDataTo(vArtAfter);
        Assert.Equal(vArtBefore, vArtAfter);
    }

    [Fact]
    public void Set_depth_survives_new_recipe()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igtDir = Directory.CreateDirectory(Path.Combine(dir, "igts")).FullName;
        string igt = MakeIgt(igtDir);
        string map = MakeMap(dir, 2, "d.png", (0, 0, new Rgba32(3, 5, 0, 255)));
        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", map));

        string manifest = Path.Combine(dir, "cat.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(
            new RecipeManifest("cat", "Cat", ["hat"], Array.Empty<IncompatibilityRule>()), Json.Options));
        string rcp = Path.Combine(dir, "cat.rcp");
        Assert.Equal(0, Run("new", "recipe", rcp, "--manifest", manifest, "--ingredients", igtDir));

        using var recipe = RecipeArchive.Read(rcp);
        Assert.Equal(3, recipe.Ingredients[0].Depths["v"].Front(0, 0));
    }

    [Fact]
    public void A_map_of_the_wrong_size_is_refused_and_the_igt_is_untouched()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir);
        string map = MakeMap(dir, 3, "big.png", (0, 0, new Rgba32(3, 5, 0, 255)));
        var before = File.ReadAllBytes(igt);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Run("set", "depth", igt, "--variant", "v", "--map", map));

        Assert.Contains("2x2", ex.Message, StringComparison.Ordinal);
        Assert.Contains("3x3", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(igt));
    }

    [Fact]
    public void An_unknown_variant_names_the_ingredients_variants()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir);
        string map = MakeMap(dir, 2, "d.png");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Run("set", "depth", igt, "--variant", "nope", "--map", map));

        Assert.Contains("'nope'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'v'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'w'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Depth_over_a_clear_pixel_is_not_stored()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir, clearV11: true);
        string map = MakeMap(dir, 2, "d.png",
            (0, 0, new Rgba32(2, 2, 0, 255)), (1, 1, new Rgba32(9, 0, 0, 255)));

        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", map));

        using var read = IngredientArchive.Read(igt);
        Assert.Equal(0, read.Depths["v"].Front(1, 1));
        Assert.Equal(2, read.Depths["v"].Front(0, 0));
    }

    [Fact]
    public void An_all_zero_map_clears_the_depth_and_says_so()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir);
        string set = MakeMap(dir, 2, "d.png", (0, 0, new Rgba32(3, 5, 0, 255)));
        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", set));
        string zero = MakeMap(dir, 2, "zero.png");

        string output = Capture(() => Run("set", "depth", igt, "--variant", "v", "--map", zero), out int code);

        Assert.Equal(0, code);
        Assert.Contains("cleared", output, StringComparison.Ordinal);
        using var read = IngredientArchive.Read(igt);
        Assert.False(read.Depths.ContainsKey("v"));
        Assert.True(read.Depths.ContainsKey("w"));
    }

    [Fact]
    public void Set_depth_twice_with_the_same_map_stores_identical_depth_bytes()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string igt = MakeIgt(dir);
        string map = MakeMap(dir, 2, "d.png", (0, 0, new Rgba32(3, 5, 0, 255)), (1, 0, new Rgba32(8, 1, 0, 255)));

        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", map));
        using var first = IngredientArchive.Read(igt);
        var firstBytes = first.Depths["v"].ToBytes();
        Assert.Equal(0, Run("set", "depth", igt, "--variant", "v", "--map", map));

        using var second = IngredientArchive.Read(igt);
        Assert.Equal(firstBytes, second.Depths["v"].ToBytes());
    }
}
