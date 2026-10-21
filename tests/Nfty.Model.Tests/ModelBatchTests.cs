using System.IO.Compression;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class ModelBatchTests
{
    [Theory]
    [InlineData("Body/Oak: v2?", "Body-Oak- v2-")]
    [InlineData("  ..  ", "model")]
    [InlineData("Lid", "Lid")]
    public void Base_names_are_plain_file_names_on_every_platform(string asked, string expected) =>
        Assert.Equal(expected, ModelBatch.SafeBaseName(asked));

    [Fact]
    public void Colliding_names_get_a_counter_ignoring_case() =>
        Assert.Equal(new[] { "Oak", "oak-2", "Oak-3", "Walnut" },
            ModelBatch.UniqueBaseNames(new[] { "Oak", "oak", "Oak", "Walnut" }));

    [Theory]
    [InlineData(ModelFormat.Glb)]
    [InlineData(ModelFormat.GltfWithBin)]
    [InlineData(ModelFormat.GltfEmbedded)]
    public void The_names_the_manifest_lists_are_the_names_the_writer_writes(ModelFormat format)
    {
        using var art = Art.FromAscii("##");
        var options = new RiseOptions { Format = format };
        var mesh = Riser.TryBuild(art, null, options)!;
        Assert.Equal(ModelBatch.FileNamesFor("Body-Oak", format),
            ModelWriter.Write(mesh, art, options, "Body-Oak").Select(f => f.FileName));
    }

    [Fact]
    public void A_batch_skips_and_names_what_cannot_rise_and_writes_the_rest()
    {
        using var ok = Art.FromAscii("##");
        using var see = Art.FromAscii("#~");
        using var none = Art.FromAscii("..");
        var result = ModelBatch.Build(new[]
        {
            new ModelInput("a", ok, null), new ModelInput("b", see, null), new ModelInput("c", none, null),
        }, new RiseOptions());
        Assert.Equal(new[] { "a.glb" }, result.Files.Select(f => f.FileName));
        Assert.Equal(2, result.Skipped.Count);
        Assert.StartsWith("b:", result.Skipped[0].ToString());
        Assert.StartsWith("c:", result.Skipped[1].ToString());
        Assert.Equal("b", result.Skipped[0].Name);
        Assert.Equal(SkipReason.PartialAlpha, result.Skipped[0].Reason);
        Assert.Equal(SkipReason.NothingSolid, result.Skipped[1].Reason);
    }

    [Fact]
    public void The_zip_is_byte_identical_for_identical_input_and_lists_every_file()
    {
        using var art = Art.FromAscii("##");
        var files = ModelBatch.Build(new[] { new ModelInput("b", art, null), new ModelInput("a", art, null) },
            new RiseOptions { Format = ModelFormat.GltfWithBin }).Files;
        var dir = Directory.CreateTempSubdirectory("nfty-zip-").FullName;
        string one = Path.Combine(dir, "1.zip"), two = Path.Combine(dir, "2.zip");
        ModelBatch.WriteZip(one, files);
        ModelBatch.WriteZip(two, files);
        Assert.Equal(File.ReadAllBytes(one), File.ReadAllBytes(two));
        using var zip = ZipFile.OpenRead(one);
        Assert.Equal(new[] { "a.bin", "a.gltf", "b.bin", "b.gltf" }, zip.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void Zip_entries_sort_ordinally_and_carry_the_fixed_1980_stamp()
    {
        using var art = Art.FromAscii("#");
        var files = ModelBatch.Build(new[] { new ModelInput("a", art, null), new ModelInput("B", art, null) },
            new RiseOptions()).Files;
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-zip-").FullName, "o.zip");
        ModelBatch.WriteZip(path, files);
        using var zip = ZipFile.OpenRead(path);
        Assert.Equal(new[] { "B.glb", "a.glb" }, zip.Entries.Select(e => e.FullName));   // 'B' < 'a' ordinally
        Assert.All(zip.Entries, e => Assert.Equal(1980, e.LastWriteTime.Year));
    }

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("nul", "nul_")]
    [InlineData("Com1", "Com1_")]
    [InlineData("LPT9.txt", "LPT9_.txt")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM0", "COM0")]
    public void Windows_device_names_are_neutralized(string asked, string expected) =>
        Assert.Equal(expected, ModelBatch.SafeBaseName(asked));

    [Fact]
    public void Build_sanitizes_every_name_itself_and_never_escapes_the_folder()
    {
        using var art = Art.FromAscii("#");
        var result = ModelBatch.Build(new[]
        {
            new ModelInput("../x", art, null), new ModelInput("CON", art, null), new ModelInput("a/b", art, null),
        }, new RiseOptions());
        Assert.Equal(new[] { "-x.glb", "CON_.glb", "a-b.glb" }, result.Files.Select(f => f.FileName));
    }

    [Fact]
    public void Build_gives_duplicate_names_a_deterministic_suffix_and_names_skips_by_it()
    {
        using var art = Art.FromAscii("#");
        using var see = Art.FromAscii("~");
        var result = ModelBatch.Build(new[]
        {
            new ModelInput("Lock", art, null), new ModelInput("lock", art, null), new ModelInput("Lock", see, null),
        }, new RiseOptions());
        Assert.Equal(new[] { "Lock.glb", "lock-2.glb" }, result.Files.Select(f => f.FileName));
        Assert.StartsWith("Lock-3:", result.Skipped.Single().ToString());
    }

    [Theory]
    [InlineData("../x.glb")]
    [InlineData("a/../../x.glb")]
    [InlineData("/abs.glb")]
    [InlineData("C:\\abs.glb")]
    [InlineData("..\\x.glb")]
    public void The_writers_refuse_a_file_name_that_is_rooted_or_leaves_the_folder(string name)
    {
        var files = new[] { new ModelFile(name, new byte[] { 1 }) };
        var root = Directory.CreateTempSubdirectory("nfty-esc-").FullName;
        var dir = Path.Combine(root, "out");
        var ex = Assert.Throws<ArgumentException>(() => ModelBatch.WriteFolder(dir, files));
        Assert.Contains(name, ex.Message);
        Assert.False(File.Exists(Path.Combine(root, "x.glb")));
        var zip = Path.Combine(root, "o.zip");
        var ez = Assert.Throws<ArgumentException>(() => ModelBatch.WriteZip(zip, files));
        Assert.Contains(name, ez.Message);
    }

    [Fact]
    public void A_folder_receives_every_file()
    {
        using var art = Art.FromAscii("#");
        var files = ModelBatch.Build(new[] { new ModelInput("x", art, null) }, new RiseOptions()).Files;
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-dir-").FullName, "Body-models");
        ModelBatch.WriteFolder(dir, files);
        Assert.True(File.Exists(Path.Combine(dir, "x.glb")));
    }
}
