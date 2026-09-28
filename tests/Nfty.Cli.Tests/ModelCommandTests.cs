using System.Buffers.Binary;
using System.CommandLine;
using System.Text.Json.Nodes;
using Nfty.Cli;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

/// <summary>
/// <c>nfty model</c>: a picture or one Ingredient variant, risen and written. Options belonging to
/// the other form are REJECTED at parse time, never ignored — the rule <c>preview</c> keeps.
/// </summary>
public class ModelCommandTests
{
    private static readonly InvocationConfiguration NonThrowing = new() { EnableDefaultExceptionHandler = false };

    private static int Run(params string[] args) => CommandFactory.Build().Parse(args).Invoke(NonThrowing);

    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    private static string WritePicture(string dir, string name, int width, int height, Rgba32 color)
    {
        string path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(width, height, color);
        img.SaveAsPng(path);
        return path;
    }

    private static (float[] Min, float[] Max) PositionBounds(byte[] glb)
    {
        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(12));
        var json = JsonNode.Parse(glb.AsSpan(20, jsonLength))!;
        int position = json["meshes"]![0]!["primitives"]![0]!["attributes"]!["POSITION"]!.GetValue<int>();
        var accessor = json["accessors"]![position]!;
        return (accessor["min"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray(),
                accessor["max"]!.AsArray().Select(n => n!.GetValue<float>()).ToArray());
    }

    // --- parsing ---

    [Fact]
    public void A_picture_needs_nothing_but_an_out_path()
    {
        Assert.Empty(Parse("model sprite.png --out sprite.glb").Errors);
    }

    [Fact]
    public void An_ingredient_needs_a_variant_and_a_picture_refuses_one()
    {
        Assert.Empty(Parse("model hat.igt --variant red --out hat.glb").Errors);
        Assert.NotEmpty(Parse("model hat.igt --out hat.glb").Errors);
        Assert.NotEmpty(Parse("model sprite.png --variant red --out sprite.glb").Errors);
    }

    [Fact]
    public void Anything_else_is_refused_naming_both_forms()
    {
        var error = Assert.Single(Parse("model book.cbk --out a.glb").Errors);
        Assert.Contains(".png", error.Message, StringComparison.Ordinal);
        Assert.Contains(".igt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Front_and_back_are_given_together_or_not_at_all()
    {
        Assert.Empty(Parse("model s.png --out s.glb --front 2 --back 0").Errors);
        Assert.NotEmpty(Parse("model s.png --out s.glb --front 2").Errors);
        Assert.NotEmpty(Parse("model s.png --out s.glb --back 2").Errors);
    }

    [Theory]
    [InlineData("--front 256 --back 0")]
    [InlineData("--front 0 --back 300")]
    public void A_depth_past_one_byte_is_refused(string fill)
    {
        Assert.NotEmpty(Parse($"model s.png --out s.glb {fill}").Errors);
    }

    [Fact]
    public void A_voxel_size_that_is_not_positive_is_refused()
    {
        Assert.NotEmpty(Parse("model s.png --out s.glb --voxel-size 0").Errors);
    }

    [Theory]
    [InlineData("glb", "a.gltf")]
    [InlineData("gltf", "a.glb")]
    [InlineData("gltf-embedded", "a.glb")]
    [InlineData("glb", "a")]
    public void The_out_extension_must_match_the_format(string format, string outPath)
    {
        Assert.NotEmpty(Parse($"model s.png --out {outPath} --format {format}").Errors);
    }

    [Fact]
    public void Unknown_formats_and_pivots_are_refused()
    {
        Assert.NotEmpty(Parse("model s.png --out s.glb --format obj").Errors);
        Assert.NotEmpty(Parse("model s.png --out s.glb --pivot top").Errors);
    }

    [Theory]
    [InlineData(".glb")]
    [InlineData(".gltf")]
    public void An_out_path_with_no_file_name_before_the_extension_is_refused(string outPath)
    {
        var error = Assert.Single(Parse($"model s.png --out {outPath}").Errors);
        Assert.DoesNotContain("baseName", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cannot be an empty string", error.Message, StringComparison.Ordinal);
        Assert.Contains("--out", error.Message, StringComparison.Ordinal);
        Assert.Contains("file name", error.Message, StringComparison.Ordinal);
    }

    // --- running ---

    [Fact]
    public void A_picture_rises_into_a_glb_one_voxel_thick()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 2, 1, new Rgba32(200, 40, 40, 255));
            string glb = Path.Combine(dir, "sprite.glb");

            Assert.Equal(0, Run("model", png, "--out", glb));

            byte[] bytes = File.ReadAllBytes(glb);
            Assert.Equal(0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            var (min, max) = PositionBounds(bytes);
            Assert.Equal(new[] { -1f, 0f, -0.5f }, min);   // bottom-center, two wide, paper
            Assert.Equal(new[] { 1f, 1f, 0.5f }, max);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Fill_all_raises_every_opaque_pixel_and_voxel_size_scales_it()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 1, 1, new Rgba32(200, 40, 40, 255));
            string glb = Path.Combine(dir, "sprite.glb");

            Assert.Equal(0, Run("model", png, "--out", glb, "--front", "3", "--back", "1",
                "--pivot", "min-corner", "--voxel-size", "0.5"));

            var (min, max) = PositionBounds(File.ReadAllBytes(glb));
            Assert.Equal(new[] { 0f, 0f, 0f }, min);
            Assert.Equal(new[] { 0.5f, 0.5f, 2.5f }, max);   // 1 + 3 + 1 voxels deep, at half a unit
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_ingredient_variant_rises_with_its_stored_depth_and_fill_all_replaces_it()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string igt = Path.Combine(dir, "hat.igt");
            using (var red = new Image<Rgba32>(1, 1, new Rgba32(200, 40, 40, 255)))
                IngredientArchive.Write(igt,
                    new IngredientManifest("hat", "Hat", LayerKind.Custom, null, [new Variant("red", "Red", 1)]),
                    new Dictionary<string, Image<Rgba32>> { ["red"] = red },
                    new Dictionary<string, DepthMap> { ["red"] = DepthMap.FromBytes(1, 1, [2, 1]) });

            string stored = Path.Combine(dir, "stored.glb");
            Assert.Equal(0, Run("model", igt, "--variant", "red", "--out", stored));
            var (min, max) = PositionBounds(File.ReadAllBytes(stored));
            Assert.Equal(4f, max[2] - min[2]);   // 1 + front 2 + back 1

            string flat = Path.Combine(dir, "flat.glb");
            Assert.Equal(0, Run("model", igt, "--variant", "red", "--out", flat, "--front", "0", "--back", "0"));
            (min, max) = PositionBounds(File.ReadAllBytes(flat));
            Assert.Equal(1f, max[2] - min[2]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_gltf_format_writes_its_bin_beside_it()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 2, 2, new Rgba32(200, 40, 40, 255));
            string gltf = Path.Combine(dir, "sprite.gltf");

            Assert.Equal(0, Run("model", png, "--out", gltf, "--format", "gltf"));

            Assert.True(File.Exists(Path.Combine(dir, "sprite.bin")));
            var json = JsonNode.Parse(File.ReadAllBytes(gltf))!;
            Assert.Equal("sprite.bin", json["buffers"]![0]!["uri"]!.GetValue<string>());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_upper_case_out_extension_is_kept_not_re_cased_to_lower()
    {
        // The validator compares --out's extension to --format case-insensitively, but the writer
        // must not silently canonicalize the casing the user typed: --out Thing.GLB writes
        // Thing.GLB, not Thing.glb, and the printed line names the exact path written.
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "sprite.png", 1, 1, new Rgba32(200, 40, 40, 255));
            string glb = Path.Combine(dir, "Thing.GLB");

            var previous = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            int exit;
            try { exit = Run("model", png, "--out", glb); }
            finally { Console.SetOut(previous); }

            Assert.Equal(0, exit);
            Assert.Contains("Thing.GLB", sw.ToString(), StringComparison.Ordinal);
            // NTFS itself is case-insensitive but preserves the case a file was created with, so the
            // directory's own entry is the portable way to check the writer didn't re-case it.
            Assert.Contains(glb, Directory.GetFiles(dir), StringComparer.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_partly_transparent_picture_is_refused_by_name_and_nothing_is_written()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "ghost.png", 1, 1, new Rgba32(200, 40, 40, 128));
            string glb = Path.Combine(dir, "ghost.glb");

            var ex = Assert.Throws<PartialAlphaException>(() => Run("model", png, "--out", glb));
            Assert.Contains(png, ex.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(glb));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_fully_transparent_picture_has_nothing_to_rise()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string png = WritePicture(dir, "empty.png", 2, 2, new Rgba32(0, 0, 0, 0));
            var ex = Assert.Throws<InvalidOperationException>(() =>
                Run("model", png, "--out", Path.Combine(dir, "empty.glb")));
            Assert.Contains("nothing to rise", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void An_unknown_variant_is_refused_naming_the_ones_there_are()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string igt = Path.Combine(dir, "hat.igt");
            using (var red = new Image<Rgba32>(1, 1, new Rgba32(200, 40, 40, 255)))
                IngredientArchive.Write(igt,
                    new IngredientManifest("hat", "Hat", LayerKind.Custom, null, [new Variant("red", "Red", 1)]),
                    new Dictionary<string, Image<Rgba32>> { ["red"] = red },
                    DepthMaps.None);

            var ex = Assert.Throws<ArgumentException>(() =>
                Run("model", igt, "--variant", "blue", "--out", Path.Combine(dir, "hat.glb")));
            Assert.Contains("'blue'", ex.Message, StringComparison.Ordinal);
            Assert.Contains("red", ex.Message, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
