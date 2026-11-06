using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Nfty.Core.Imaging;
using Nfty.Model.Gltf;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class ModelFormatAuditTests
{
    private static byte[] Written() => Models.Glb(["##", ".#"]);

    private static byte[] Edited(Action<JsonNode> edit)
    {
        var (json, bin) = GltfValidator.ReadGlb(Written());
        edit(json);
        return Frame(json, bin);
    }

    private static byte[] Frame(JsonNode json, byte[] bin) =>
        GlbContainer.Frame(Encoding.UTF8.GetBytes(json.ToJsonString()), bin);

    public static TheoryData<string> SemanticMutations => new()
    {
        "asset.version", "asset.missing", "scene.missing", "scene.reference", "scene.empty",
        "scene.extra", "node.mesh", "node.extra", "material.missing", "material.reference",
        "material.alpha", "material.cutoff", "pbr.missing", "pbr.metallic", "pbr.roughness",
        "pbr.factor", "pbr.texture", "texture.source", "texture.sampler", "sampler.filter",
        "sampler.wrap", "sampler.extra", "texture.extra", "texture.texCoord", "accessor.missing",
        "accessor.null", "accessor.bounds", "view.target", "view.alignment", "primitive.missing",
        "sampler.missing", "texture.missing", "pbr.null", "scene.null", "view.unused", "accessor.falseBounds",
        "indices.min", "indices.max",
    };

    [Theory]
    [MemberData(nameof(SemanticMutations))]
    public void Unsupported_or_missing_semantics_are_refused(string mutation)
    {
        byte[] bytes = Edited(j =>
        {
            switch (mutation)
            {
                case "asset.version": j["asset"]!["version"] = "999"; break;
                case "asset.missing": j.AsObject().Remove("asset"); break;
                case "scene.missing": j.AsObject().Remove("scene"); break;
                case "scene.reference": j["scene"] = 1; break;
                case "scene.empty": j["scenes"]![0]!["nodes"] = new JsonArray(); break;
                case "scene.extra": j["scenes"]![0]!["name"] = "ignored"; break;
                case "node.mesh": j["nodes"]![0]!["mesh"] = 1; break;
                case "node.extra": j["nodes"]!.AsArray().Add(j["nodes"]![0]!.DeepClone()); break;
                case "material.missing": j.AsObject().Remove("materials"); break;
                case "material.reference": j["meshes"]![0]!["primitives"]![0]!["material"] = 1; break;
                case "material.alpha": j["materials"]![0]!["alphaMode"] = "BLEND"; break;
                case "material.cutoff": j["materials"]![0]!["alphaCutoff"] = 0.2; break;
                case "pbr.missing": j["materials"]![0]!.AsObject().Remove("pbrMetallicRoughness"); break;
                case "pbr.metallic": j["materials"]![0]!["pbrMetallicRoughness"]!["metallicFactor"] = 1; break;
                case "pbr.roughness": j["materials"]![0]!["pbrMetallicRoughness"]!["roughnessFactor"] = 0; break;
                case "pbr.factor": j["materials"]![0]!["pbrMetallicRoughness"]!["baseColorFactor"] = new JsonArray(0, 0, 0, 1); break;
                case "pbr.texture": j["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["index"] = 1; break;
                case "texture.source": j["textures"]![0]!["source"] = 1; break;
                case "texture.sampler": j["textures"]![0]!["sampler"] = 1; break;
                case "sampler.filter": j["samplers"]![0]!["magFilter"] = 9729; break;
                case "sampler.wrap": j["samplers"]![0]!["wrapT"] = 10497; break;
                case "sampler.extra": j["samplers"]![0]!["extensions"] = new JsonObject(); break;
                case "texture.extra": j["textures"]![0]!["extensions"] = new JsonObject(); break;
                case "texture.texCoord": j["materials"]![0]!["pbrMetallicRoughness"]!["baseColorTexture"]!["texCoord"] = 1; break;
                case "accessor.missing": j["accessors"]![0]!.AsObject().Remove("bufferView"); break;
                case "accessor.null": j["accessors"]![0] = null; break;
                case "accessor.bounds": j["accessors"]![0]!["min"] = new JsonArray(0); break;
                case "view.target": j["bufferViews"]![0]!["target"] = 34963; break;
                case "view.alignment": j["bufferViews"]![0]!["byteOffset"] = 1; break;
                case "primitive.missing": j["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject().Remove("POSITION"); break;
                case "sampler.missing": j["samplers"]![0]!.AsObject().Remove("magFilter"); break;
                case "texture.missing": j["textures"]![0]!.AsObject().Remove("source"); break;
                case "pbr.null": j["materials"]![0]!["pbrMetallicRoughness"] = null; break;
                case "scene.null": j["scenes"]![0] = null; break;
                case "view.unused": j["bufferViews"]!.AsArray().Add(new JsonObject { ["buffer"] = 1, ["byteOffset"] = 0, ["byteLength"] = 4 }); break;
                case "accessor.falseBounds": j["accessors"]![0]!["max"]![0] = 1000; break;
                case "indices.min": j["accessors"]![3]!["min"] = new JsonArray(0); break;
                case "indices.max": j["accessors"]![3]!["max"] = new JsonArray(0); break;
            }
        });
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1)]
    public void Buffer_views_cannot_use_storage_beyond_the_declaration(int declared)
    {
        byte[] bytes = Edited(j => j["buffers"]![0]!["byteLength"] = declared);
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
    }

    [Fact]
    public void One_byte_beyond_the_declaration_is_refused_but_legal_glb_padding_reads()
    {
        var (json, bin) = GltfValidator.ReadGlb(Written());
        int end = json["bufferViews"]![4]!["byteOffset"]!.GetValue<int>()
            + json["bufferViews"]![4]!["byteLength"]!.GetValue<int>();
        json["buffers"]![0]!["byteLength"] = end;
        var read = GltfReader.Read(Frame(json, bin[..end]));
        Assert.NotEmpty(read.TexturePng);
        json["buffers"]![0]!["byteLength"] = end - 1;
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(Frame(json, bin)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Textures_require_actual_decodable_png_bytes(bool jpeg)
    {
        var (json, bin) = GltfValidator.ReadGlb(Written());
        int offset = json["bufferViews"]![4]!["byteOffset"]!.GetValue<int>();
        byte[] texture;
        if (jpeg)
        {
            using var art = new Image<Rgba32>(5, 2);
            using var stream = new MemoryStream();
            art.Save(stream, new JpegEncoder());
            texture = stream.ToArray();
        }
        else texture = bin.AsSpan(offset, 33).ToArray();
        var replaced = new byte[offset + texture.Length];
        bin.AsSpan(0, offset).CopyTo(replaced);
        texture.CopyTo(replaced, offset);
        json["bufferViews"]![4]!["byteLength"] = texture.Length;
        json["buffers"]![0]!["byteLength"] = replaced.Length;
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(Frame(json, replaced)));
    }

    [Theory]
    [InlineData("trailing header")]
    [InlineData("physical trailer")]
    [InlineData("reversed")]
    [InlineData("unaligned")]
    [InlineData("unaligned BIN")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public void Malformed_glb_chunk_framing_is_refused(string mutation)
    {
        byte[] bytes = Written();
        int jsonLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12));
        if (mutation is "trailing header" or "physical trailer" or "duplicate" or "unknown")
        {
            int extra = mutation is "duplicate" or "unknown" ? 8 : 1;
            Array.Resize(ref bytes, bytes.Length + extra);
            if (mutation != "physical trailer") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
            if (mutation == "duplicate") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), GlbContainer.JsonChunk);
        }
        else if (mutation == "reversed")
        {
            byte[] original = bytes.ToArray();
            original.AsSpan(20 + jsonLength).CopyTo(bytes.AsSpan(12));
            original.AsSpan(12, 8 + jsonLength).CopyTo(bytes.AsSpan(bytes.Length - 8 - jsonLength));
        }
        else if (mutation == "unaligned BIN")
        {
            Array.Resize(ref bytes, bytes.Length - 1);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20 + jsonLength), (uint)(bytes.Length - 28 - jsonLength));
        }
        else BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)jsonLength - 1);
        Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(bytes));
    }

    [Theory]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("a\0b")]
    [InlineData("CON")]
    [InlineData("con.txt")]
    [InlineData("COM¹")]
    [InlineData("LPT².txt")]
    [InlineData("a.")]
    [InlineData("a ")]
    [InlineData(".")]
    [InlineData("..")]
    public void Both_writer_entry_points_refuse_unsafe_names_before_encoding(string name)
    {
        using var art = Art.FromAscii(["#"]);
        var options = new RiseOptions { Format = ModelFormat.GltfWithBin };
        var mesh = Riser.TryBuild(art, null, options)!;
        using var atlas = FaceAtlas.Build(art);
        Assert.Throws<ArgumentException>(() => ModelWriter.Write(mesh, art, options, name));
        Assert.Throws<ArgumentException>(() => ModelWriter.WriteAtlas(mesh, atlas, options, name));
    }

    [Theory]
    [InlineData(".sprite")]
    [InlineData("my sprite")]
    [InlineData("é猫")]
    [InlineData("sprite#1")]
    [InlineData("a&b")]
    public void Valid_names_and_optional_labels_survive_all_supported_formats(string name)
    {
        using var art = Art.FromAscii(["##", ".#"]);
        foreach (var format in Enum.GetValues<ModelFormat>())
        {
            var options = new RiseOptions { Format = format };
            var mesh = Riser.TryBuild(art, null, options)!;
            var files = ModelWriter.Write(mesh, art, options, name);
            var dir = Directory.CreateTempSubdirectory();
            try
            {
                foreach (var file in files) File.WriteAllBytes(Path.Combine(dir.FullName, file.FileName), file.Bytes);
                var read = GltfReader.Read(Path.Combine(dir.FullName, files[0].FileName));
                using var texture = ImageFormats.Load(read.TexturePng);
                var rewritten = ModelWriter.WriteAtlas(read.Mesh, texture, options, name);
                for (int i = 0; i < files.Count; i++) Assert.Equal(files[i].Bytes, rewritten[i].Bytes);
            }
            finally { dir.Delete(true); }
        }
        byte[] renamed = Edited(j =>
        {
            j["nodes"]![0]!["name"] = name;
            j["meshes"]![0]!["name"] = name;
            j["materials"]![0]!["name"] = name;
            j["asset"]!["generator"] = name;
        });
        Assert.NotNull(GltfReader.Read(renamed));
    }

    [Fact]
    public void Missing_optional_record_names_and_generator_do_not_change_the_model()
    {
        byte[] bytes = Edited(j =>
        {
            j["asset"]!.AsObject().Remove("generator");
            foreach (string array in new[] { "nodes", "meshes", "materials" })
                j[array]![0]!.AsObject().Remove("name");
        });
        Assert.NotNull(GltfReader.Read(bytes));
    }

    [Fact]
    public void File_io_failures_remain_distinct_from_invalid_content()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.glb");
        Assert.Throws<DirectoryNotFoundException>(() => GltfReader.Read(missing));
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "locked.glb");
            File.WriteAllBytes(path, Written());
            using var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.Throws<IOException>(() => GltfReader.Read(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_trailing_dot_companion_uri_is_refused_before_file_io()
    {
        var files = Models.Write(["#"], options: new RiseOptions { Format = ModelFormat.GltfWithBin });
        var json = JsonNode.Parse(files[0].Bytes)!;
        json["buffers"]![0]!["uri"] = "sprite.bin.";
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "sprite.gltf");
            File.WriteAllText(path, json.ToJsonString());
            File.WriteAllBytes(Path.Combine(dir.FullName, "sprite.bin."), files[1].Bytes);
            Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(path));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void A_raw_uri_fragment_is_not_treated_as_part_of_a_companion_file_name()
    {
        var files = Models.Write(["#"], options: new RiseOptions { Format = ModelFormat.GltfWithBin });
        var json = JsonNode.Parse(files[0].Bytes)!;
        json["buffers"]![0]!["uri"] = "sprite.bin#fragment";
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(dir.FullName, "sprite.gltf");
            File.WriteAllText(path, json.ToJsonString());
            File.WriteAllBytes(Path.Combine(dir.FullName, "sprite.bin#fragment"), files[1].Bytes);
            Assert.Throws<UnsupportedModelException>(() => GltfReader.Read(path));
        }
        finally { dir.Delete(true); }
    }
}
