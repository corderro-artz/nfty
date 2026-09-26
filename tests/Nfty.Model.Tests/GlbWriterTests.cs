using System.Reflection;
using System.Text.Json.Nodes;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class GlbWriterTests
{
    private static readonly string[] Plus = [".#.", "###", ".#."];

    public static TheoryData<string, string> EveryShapeAndPattern() => Shapes.EveryShapeAndPattern();

    [Fact]
    public void A_glb_is_one_file_named_after_the_base_name()
    {
        var file = Assert.Single(Models.Write(Plus, baseName: "chest"));
        Assert.Equal("chest.glb", file.FileName);
    }

    [Theory]
    [MemberData(nameof(EveryShapeAndPattern))]
    public void Every_glb_passes_the_structure_validator(string shape, string pattern)
    {
        var (json, bin) = GltfValidator.ReadGlb(Models.Glb(Shapes.All[shape], Shapes.Patterns[pattern]));
        GltfValidator.AssertValid(json, bin);
    }

    [Fact]
    public void Position_min_and_max_are_the_meshs_own_bounds()
    {
        using var art = Art.FromAscii(Plus);
        var mesh = Riser.TryBuild(art, Art.Depth(art, (x, _) => (x, 1)), new RiseOptions())!;
        var (json, _) = GltfValidator.ReadGlb(Assert.Single(ModelWriter.Write(mesh, art, new RiseOptions(), "p")).Bytes);

        var position = json["accessors"]![0]!;
        Assert.Equal(new[] { mesh.Min.X, mesh.Min.Y, mesh.Min.Z }, position["min"]!.AsArray().Select(n => n!.GetValue<float>()));
        Assert.Equal(new[] { mesh.Max.X, mesh.Max.Y, mesh.Max.Z }, position["max"]!.AsArray().Select(n => n!.GetValue<float>()));
        Assert.Equal(mesh.VertexCount, position["count"]!.GetValue<int>());
    }

    [Fact]
    public void Indices_are_unsigned_int_whatever_the_mesh_size()
    {
        var (json, bin) = GltfValidator.ReadGlb(Models.Glb(["#"]));
        Assert.Equal(5125, json["accessors"]![3]!["componentType"]!.GetValue<int>());
        Assert.Equal(36, GltfValidator.ReadUInts(json, bin, 3).Length);
    }

    [Fact]
    public void The_material_is_single_sided_and_named_for_nfty()
    {
        // D3: a closed solid shows no back face; double-sided only disables culling. D4: the
        // reference implementation named its material after itself, baked into every file.
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        var material = json["materials"]![0]!;
        Assert.Null(material["doubleSided"]);
        Assert.Equal("nfty", material["name"]!.GetValue<string>());
        Assert.Equal("nfty", json["meshes"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void The_sampler_is_nearest_and_clamped_so_pixel_art_stays_crisp()
    {
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        var sampler = json["samplers"]![0]!;
        Assert.Equal((9728, 9728, 33071, 33071), (
            sampler["magFilter"]!.GetValue<int>(), sampler["minFilter"]!.GetValue<int>(),
            sampler["wrapS"]!.GetValue<int>(), sampler["wrapT"]!.GetValue<int>()));
    }

    [Fact]
    public void Alpha_is_a_mask_cut_at_one_half()
    {
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        Assert.Equal("MASK", json["materials"]![0]!["alphaMode"]!.GetValue<string>());
        Assert.Equal(0.5f, json["materials"]![0]!["alphaCutoff"]!.GetValue<float>());
    }

    [Fact]
    public void The_png_sits_in_a_buffer_view_with_no_target()
    {
        var (json, _) = GltfValidator.ReadGlb(Models.Glb(Plus));
        var view = json["bufferViews"]![json["images"]![0]!["bufferView"]!.GetValue<int>()]!;
        Assert.Null(view["target"]);
        Assert.Equal("image/png", json["images"]![0]!["mimeType"]!.GetValue<string>());
    }

    [Fact]
    public void Both_chunks_are_padded_to_four_bytes()
    {
        // A 1x1 sprite makes an odd-sized PNG and a JSON of arbitrary length; both must be padded.
        byte[] glb = Models.Glb(["#"]);
        Assert.Equal(0, glb.Length % 4);
        GltfValidator.ReadGlb(glb);
    }

    [Fact]
    public void The_texture_is_the_art_unaltered()
    {
        // nfty resamples, blurs and recolors nothing; the texture is the whole picture, pixel for pixel.
        using var art = Art.FromAscii(".#.", "###");
        var (json, bin) = GltfValidator.ReadGlb(Models.Glb([".#.", "###"]));
        using var texture = Image.Load<Rgba32>(GltfValidator.ReadImage(json, bin));

        Assert.Equal((art.Width, art.Height), (texture.Width, texture.Height));
        for (int y = 0; y < art.Height; y++)
            for (int x = 0; x < art.Width; x++)
                Assert.Equal(art[x, y], texture[x, y]);
    }

    [Fact]
    public void The_same_pixels_give_the_same_file_whatever_metadata_the_picture_carried()
    {
        // A picture decoded from disk carries its PNG's metadata — color type, bit depth, text, a
        // resolution. None of it is the picture, so none of it may reach the model's bytes.
        var options = new RiseOptions();
        using var plain = Art.FromAscii(Plus);
        using var decorated = plain.Clone();
        decorated.Metadata.HorizontalResolution = 300;
        decorated.Metadata.VerticalResolution = 300;
        var png = decorated.Metadata.GetPngMetadata();
        png.ColorType = PngColorType.Palette;
        png.BitDepth = PngBitDepth.Bit4;
        png.TextData.Add(new PngTextData("Comment", "made elsewhere", "", ""));

        var mesh = Riser.TryBuild(plain, null, options)!;
        Assert.Equal(
            Assert.Single(ModelWriter.Write(mesh, plain, options, "a")).Bytes,
            Assert.Single(ModelWriter.Write(mesh, decorated, options, "a")).Bytes);
    }

    [Fact]
    public void The_same_input_gives_byte_identical_files()
    {
        // Owning the writer is what makes this assertable at all: no version stamp, no timestamp,
        // no hash-ordered collection anywhere on the path.
        Func<int, int, (int, int)> depth = (x, y) => (x, y);
        Assert.Equal(Models.Glb(Shapes.All["hole"], depth), Models.Glb(Shapes.All["hole"], depth));
    }

    [Fact]
    public void A_texture_of_another_size_is_refused()
    {
        using var art = Art.FromAscii("##");
        using var other = Art.FromAscii("###");
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        Assert.Throws<ArgumentException>(() => ModelWriter.Write(mesh, other, new RiseOptions(), "a"));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("")]
    [InlineData(" ")]
    public void A_base_name_that_is_not_a_plain_file_name_is_refused(string baseName)
    {
        using var art = Art.FromAscii("#");
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        Assert.ThrowsAny<ArgumentException>(() => ModelWriter.Write(mesh, art, new RiseOptions(), baseName));
    }

    [Fact]
    public void Every_entry_point_takes_live_images_never_bytes()
    {
        // D6: the reference implementation took PNG bytes and decoded per call. At cook time the image
        // is already in memory; a PNG round-trip per asset is pure waste on the hot path.
        var methods = new[] { typeof(Riser), typeof(ModelWriter), typeof(StackDepth), typeof(DepthFill) }
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.All(methods, m => Assert.DoesNotContain(m.GetParameters(), p =>
            p.ParameterType == typeof(byte[]) || p.ParameterType == typeof(ReadOnlySpan<byte>) || p.ParameterType == typeof(Stream)));
        Assert.Equal(typeof(Image<Rgba32>), typeof(Riser).GetMethod(nameof(Riser.TryBuild))!.GetParameters()[0].ParameterType);
    }

    /// <summary>Guards the guard: each planted defect must make the validator fail.</summary>
    [Theory]
    [InlineData("min-missing")]
    [InlineData("min-wrong")]
    [InlineData("sampler-linear")]
    [InlineData("image-target")]
    [InlineData("double-sided")]
    [InlineData("short-indices")]
    [InlineData("misaligned")]
    public void The_validator_is_not_vacuous(string defect)
    {
        byte[] good = Models.Glb(Plus);
        var (goodJson, goodBin) = GltfValidator.ReadGlb(good);
        GltfValidator.AssertValid(goodJson, goodBin);

        byte[] bad = GltfValidator.Reframe(good, json =>
        {
            switch (defect)
            {
                case "min-missing": json["accessors"]![0]!.AsObject().Remove("min"); break;
                case "min-wrong": json["accessors"]![0]!["min"]![0] = -100f; break;
                case "sampler-linear": json["samplers"]![0]!["magFilter"] = 9729; break;
                case "image-target": json["bufferViews"]![4]!["target"] = 34962; break;
                case "double-sided": json["materials"]![0]!["doubleSided"] = true; break;
                case "short-indices": json["accessors"]![3]!["componentType"] = 5123; break;
                case "misaligned":
                    json["bufferViews"]![1]!["byteOffset"] = json["bufferViews"]![1]!["byteOffset"]!.GetValue<int>() + 2;
                    break;
                default: Assert.Fail($"unknown defect {defect}"); break;
            }
        });

        var (json, bin) = GltfValidator.ReadGlb(bad);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => GltfValidator.AssertValid(json, bin));
    }
}
