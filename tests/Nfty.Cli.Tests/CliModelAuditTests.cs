using System.CommandLine;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public class CliModelAuditTests
{
    [Theory]
    [InlineData("--format", new string[0])]
    [InlineData("--front", new[] { "abc", "--back", "0" })]
    [InlineData("--front", new[] { "2147483648", "--back", "0" })]
    [InlineData("--front", new string[0])]
    [InlineData("--back", new[] { "abc", "--front", "0" })]
    [InlineData("--back", new[] { "2147483648", "--front", "0" })]
    [InlineData("--back", new string[0])]
    [InlineData("--voxel-size", new[] { "abc" })]
    [InlineData("--voxel-size", new string[0])]
    [InlineData("--out", new string[0])]
    public void Invalid_model_options_remain_parser_errors(string option, string[] values)
    {
        string[] args = option == "--out"
            ? ["model", "x.png", option, .. values]
            : ["model", "x.png", "--out", "x.glb", option, .. values];
        var parsed = CommandFactory.Build().Parse(args);
        Assert.Contains(parsed.Errors, e => e.Message.Contains(option, StringComparison.Ordinal));
        var (code, stdout, stderr) = ProgramTests.Run(args);
        Assert.Equal(1, code);
        Assert.Contains(option, stdout + stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Unhandled exception", stdout + stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("at Nfty.", stdout + stderr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a.GLTF")]
    [InlineData("a.bin")]
    public void A_locked_model_file_preserves_both_existing_payloads(string heldName)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows sharing-violation regression.");
        string root = Directory.CreateTempSubdirectory("nfty-cli-model-audit-").FullName;
        try
        {
            string png = Path.Combine(root, "picture.png"), output = Path.Combine(root, "a.GLTF");
            using (var art = new Image<Rgba32>(2, 2, new Rgba32(10, 20, 30, 255))) art.SaveAsPng(png);
            Assert.Equal(0, ProgramTests.Run("model", png, "--out", output, "--format", "gltf").Code);
            byte[] json = File.ReadAllBytes(output), bin = File.ReadAllBytes(Path.Combine(root, "a.bin"));
            using (var art = new Image<Rgba32>(16, 16, new Rgba32(40, 50, 60, 255))) art.SaveAsPng(png);
            using (var held = new FileStream(Path.Combine(root, heldName), FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(1, ProgramTests.Run("model", png, "--out", output, "--format", "gltf").Code);
            Assert.Equal(json, File.ReadAllBytes(output));
            Assert.Equal(bin, File.ReadAllBytes(Path.Combine(root, "a.bin")));
            Assert.Empty(Directory.GetDirectories(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("glb", ModelFormat.Glb, ".GLB")]
    [InlineData("gltf", ModelFormat.GltfWithBin, ".GLTF")]
    [InlineData("gltf-embedded", ModelFormat.GltfEmbedded, ".GLTF")]
    public void Successful_models_preserve_writer_bytes_and_requested_extension_casing(string format, ModelFormat kind, string extension)
    {
        string root = Directory.CreateTempSubdirectory("nfty-cli-model-bytes-").FullName;
        try
        {
            string png = Path.Combine(root, "picture.png"), output = Path.Combine(root, "Portrait" + extension);
            using var art = new Image<Rgba32>(2, 2, new Rgba32(40, 80, 120, 255));
            art.SaveAsPng(png);
            var options = new RiseOptions { Format = kind };
            var expected = ModelWriter.Write(Riser.TryBuild(art, null, options)!, art, options, "Portrait");
            Assert.Equal(0, ProgramTests.Run("model", png, "--out", output, "--format", format).Code);
            Assert.Contains(Path.GetFileName(output), Directory.GetFiles(root).Select(Path.GetFileName));
            foreach (var file in expected)
            {
                string target = Path.GetExtension(file.FileName) == ".bin"
                    ? Path.Combine(root, file.FileName) : output;
                Assert.Equal(file.Bytes, File.ReadAllBytes(target));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
