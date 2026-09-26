using System.Text.Json.Nodes;

namespace Nfty.Model.Tests;

public class GltfWriterTests
{
    private static readonly string[] Plus = [".#.", "###", ".#."];

    private static IReadOnlyList<ModelFile> Write(ModelFormat format, string baseName = "sprite") =>
        Models.Write(Plus, (x, y) => (x, y), new RiseOptions { Format = format }, baseName);

    public static TheoryData<string, string, ModelFormat> EveryShapePatternAndTextFormat()
    {
        var data = new TheoryData<string, string, ModelFormat>();
        foreach (string shape in Shapes.All.Keys)
            foreach (string pattern in Shapes.Patterns.Keys)
                foreach (var format in new[] { ModelFormat.GltfWithBin, ModelFormat.GltfEmbedded })
                    data.Add(shape, pattern, format);
        return data;
    }

    [Fact]
    public void Gltf_with_bin_writes_the_text_document_and_its_buffer_beside_it()
    {
        var files = Write(ModelFormat.GltfWithBin);
        Assert.Equal(new[] { "sprite.gltf", "sprite.bin" }, files.Select(f => f.FileName));

        var (json, bin) = GltfValidator.ReadGltf(files);
        Assert.Equal("sprite.bin", json["buffers"]![0]!["uri"]!.GetValue<string>());
        GltfValidator.AssertValid(json, bin);
    }

    [Fact]
    public void Embedded_gltf_is_one_text_file_carrying_its_buffer_inline()
    {
        var file = Assert.Single(Write(ModelFormat.GltfEmbedded));
        Assert.Equal("sprite.gltf", file.FileName);

        var (json, bin) = GltfValidator.ReadGltf([file]);
        Assert.StartsWith(GltfValidator.DataUriPrefix, json["buffers"]![0]!["uri"]!.GetValue<string>(), StringComparison.Ordinal);
        GltfValidator.AssertValid(json, bin);
    }

    [Theory]
    [MemberData(nameof(EveryShapePatternAndTextFormat))]
    public void Every_text_form_passes_the_structure_validator(string shape, string pattern, ModelFormat format)
    {
        var files = Models.Write(Shapes.All[shape], Shapes.Patterns[pattern], new RiseOptions { Format = format });
        var (json, bin) = GltfValidator.ReadGltf(files);
        GltfValidator.AssertValid(json, bin);
    }

    [Fact]
    public void The_embedded_buffer_decodes_to_exactly_the_bin_file_and_the_glb_chunk()
    {
        byte[] sibling = Assert.Single(Write(ModelFormat.GltfWithBin), f => f.FileName == "sprite.bin").Bytes;
        var (_, embedded) = GltfValidator.ReadGltf(Write(ModelFormat.GltfEmbedded));
        var (_, chunk) = GltfValidator.ReadGlb(Assert.Single(Write(ModelFormat.Glb)).Bytes);

        Assert.Equal(sibling, embedded);
        Assert.Equal(sibling, chunk);
    }

    [Fact]
    public void All_three_formats_are_one_document_differing_only_in_the_buffer_uri()
    {
        // The rule D2's lesson became: one object model, the location of the buffer set IN it.
        JsonNode Document(IReadOnlyList<ModelFile> files, bool glb)
        {
            var json = glb ? GltfValidator.ReadGlb(Assert.Single(files).Bytes).Json : GltfValidator.ReadGltf(files).Json;
            json["buffers"]![0]!.AsObject().Remove("uri");
            return json;
        }

        var fromGlb = Document(Write(ModelFormat.Glb), glb: true);
        Assert.True(JsonNode.DeepEquals(fromGlb, Document(Write(ModelFormat.GltfWithBin), glb: false)));
        Assert.True(JsonNode.DeepEquals(fromGlb, Document(Write(ModelFormat.GltfEmbedded), glb: false)));
    }

    [Fact]
    public void A_base_name_with_a_space_is_escaped_in_the_uri_and_not_in_the_file_name()
    {
        var files = Write(ModelFormat.GltfWithBin, "my sprite");
        Assert.Equal(new[] { "my sprite.gltf", "my sprite.bin" }, files.Select(f => f.FileName));

        var (json, bin) = GltfValidator.ReadGltf(files);
        Assert.Equal("my%20sprite.bin", json["buffers"]![0]!["uri"]!.GetValue<string>());
        GltfValidator.AssertValid(json, bin);
    }

    [Theory]
    [InlineData(ModelFormat.Glb)]
    [InlineData(ModelFormat.GltfWithBin)]
    [InlineData(ModelFormat.GltfEmbedded)]
    public void Every_format_is_byte_identical_when_written_twice(ModelFormat format)
    {
        var a = Write(format);
        var b = Write(format);
        Assert.Equal(a.Select(f => f.FileName), b.Select(f => f.FileName));
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i].Bytes, b[i].Bytes);
    }
}
