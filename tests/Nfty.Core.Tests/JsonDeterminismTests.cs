using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;

namespace Nfty.Core.Tests;

public class JsonDeterminismTests
{
    [Fact]
    public void Nested_metadata_serializes_to_canonical_LF_bytes()
    {
        var metadata = new OpenSeaMetadata("Chest #1", "First\nSecond", "1.png",
            new[] { new MetadataAttribute("Body", "Oak") });

        var bytes = JsonSerializer.SerializeToUtf8Bytes(metadata, Json.Options);

        var expected = "{\n  \"name\": \"Chest #1\",\n  \"description\": \"First\\nSecond\",\n"
            + "  \"image\": \"1.png\",\n  \"attributes\": [\n    {\n"
            + "      \"trait_type\": \"Body\",\n      \"value\": \"Oak\"\n    }\n  ]\n}";
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cookbook_archive_manifest_uses_LF_on_every_platform(bool asynchronous)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "book.cbk");
            var manifest = new CookBookManifest("book", "Chest", new Dimensions(2, 2),
                new Collection("Chest", "First\nSecond", "CH"),
                new Dictionary<string, double>(), Palette: new[] { "hex:123456" });
            if (asynchronous)
                await CookBookArchive.WriteAsync(path, manifest, Array.Empty<LoadedRecipe>());
            else
                CookBookArchive.Write(path, manifest, Array.Empty<LoadedRecipe>());

            using var zip = ZipFile.OpenRead(path);
            using var stream = zip.GetEntry("manifest.json")!.Open();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            Assert.Contains((byte)'\n', bytes);
            Assert.DoesNotContain((byte)'\r', bytes);
            using var json = JsonDocument.Parse(bytes);
            Assert.Equal(2, json.RootElement.GetProperty("canvas").GetProperty("width").GetInt32());
            Assert.Equal("First\nSecond", json.RootElement.GetProperty("collection").GetProperty("description").GetString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
