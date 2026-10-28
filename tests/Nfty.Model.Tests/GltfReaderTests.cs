using System.Text.Json.Nodes;
using Nfty.Core.Model;
using Nfty.Model.Gltf;

namespace Nfty.Model.Tests;

public class GltfReaderTests
{
    private static readonly string[] Plus = [".#.", "###", ".#."];

    private static (Mesh Mesh, IReadOnlyList<ModelFile> Files) Written(ModelFormat format)
    {
        var options = new RiseOptions { Format = format };
        using var art = Art.FromAscii(Plus);
        var mesh = Riser.TryBuild(art, Art.Depth(art, (x, y) => (x + 1, y)), options)!;
        return (mesh, ModelWriter.Write(mesh, art, options, "sprite"));
    }

    private static void AssertSameMesh(Mesh expected, Mesh actual)
    {
        Assert.Equal(expected.Positions, actual.Positions);
        Assert.Equal(expected.Normals, actual.Normals);
        Assert.Equal(expected.TexCoords, actual.TexCoords);
        Assert.Equal(expected.Indices, actual.Indices);
        Assert.Equal(expected.TextureWidth, actual.TextureWidth);
        Assert.Equal(expected.TextureHeight, actual.TextureHeight);
    }

    [Fact]
    public void A_glb_nfty_wrote_reads_back_the_same_mesh_and_texture()
    {
        var (mesh, files) = Written(ModelFormat.Glb);
        var read = GltfReader.Read(files[0].Bytes);
        AssertSameMesh(mesh, read.Mesh);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, read.TexturePng[..4]);
    }

    [Fact]
    public void A_gltf_with_a_bin_file_reads_back_the_same_mesh()
    {
        var (mesh, files) = Written(ModelFormat.GltfWithBin);
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            foreach (var f in files) File.WriteAllBytes(Path.Combine(dir.FullName, f.FileName), f.Bytes);
            AssertSameMesh(mesh, GltfReader.Read(Path.Combine(dir.FullName, "sprite.gltf")).Mesh);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_data_uri_gltf_reads_back_the_same_mesh()
    {
        var (mesh, files) = Written(ModelFormat.GltfEmbedded);
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "sprite.gltf");
            File.WriteAllBytes(path, files[0].Bytes);
            AssertSameMesh(mesh, GltfReader.Read(path).Mesh);
        }
        finally { dir.Delete(true); }
    }

    // Edits the JSON chunk of a GLB nfty wrote and frames it again.
    private static byte[] Edited(Action<JsonNode> edit)
    {
        var (_, files) = Written(ModelFormat.Glb);
        var (json, bin) = GltfValidator.ReadGlb(files[0].Bytes);
        edit(json);
        return GlbContainer.Frame(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), bin);
    }

    [Fact]
    public void A_model_with_two_meshes_is_refused_by_name()
    {
        var bytes = Edited(j => j["meshes"]!.AsArray().Add(JsonNode.Parse(j["meshes"]![0]!.ToJsonString())));
        var ex = Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
        Assert.Contains("more than one mesh", ex.Message);
    }

    [Fact]
    public void A_model_with_a_sparse_accessor_is_refused_by_name()
    {
        var bytes = Edited(j => j["accessors"]![1]!["sparse"] = new JsonObject());
        var ex = Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
        Assert.Contains("sparse accessors", ex.Message);
    }

    // Hostile input never escapes as anything but UnsupportedModelException: a viewer that catches
    // that one type falls back to rising the asset instead of failing the tile.
    [Fact]
    public void A_chunk_length_past_int_range_is_refused_not_overflowed()
    {
        var (_, files) = Written(ModelFormat.Glb);
        var bytes = files[0].Bytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0xFFFFFFF0);
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
    }

    [Theory]
    [InlineData("""{"asset":{},"meshes":[{}],"accessors":[],"bufferViews":[],"buffers":[{"byteLength":0}],"images":[]}""")]
    [InlineData("""{"asset":{},"meshes":[{"primitives":[{}]}],"accessors":[],"bufferViews":[],"buffers":[{"byteLength":0}],"images":[]}""")]
    [InlineData("""{"asset":{},"meshes":[null],"accessors":[],"bufferViews":[],"buffers":[{"byteLength":0}],"images":[]}""")]
    [InlineData("""{"asset":{},"meshes":[],"accessors":[],"bufferViews":[],"buffers":[null],"images":[]}""")]
    public void A_structurally_hollow_glb_is_refused(string json)
    {
        var bytes = GlbContainer.Frame(System.Text.Encoding.UTF8.GetBytes(json), new byte[4]);
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
    }

    [Theory]
    [InlineData("data:application/octet-stream;base64,!!!not base64!!!")]
    [InlineData("../outside.bin")]
    [InlineData("sub/inside.bin")]
    [InlineData("missing.bin")]
    public void A_gltf_buffer_uri_nfty_never_writes_is_refused(string uri)
    {
        var (_, files) = Written(ModelFormat.GltfEmbedded);
        var json = JsonNode.Parse(files[0].Bytes)!;
        json["buffers"]![0]!["uri"] = uri;
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "sprite.gltf");
            File.WriteAllText(path, json.ToJsonString());
            Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(path));
        }
        finally { dir.Delete(true); }
    }
}
