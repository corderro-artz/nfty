using System.Text.Json.Nodes;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>A .gltf with a separate .bin keeps working after an export renames the pair to token ids.</summary>
public class ExportGltfRenameTests
{
    [Fact]
    public void A_renamed_gltf_still_parses_and_its_buffer_resolves()
    {
        string setDir = Path.Combine(Directory.CreateTempSubdirectory("nfty-gltf-").FullName, "Box");
        string outDir = Directory.CreateTempSubdirectory("nfty-gltf-out-").FullName;
        using (var book = ModelBook())
        {
            var modeler = new SetModeler(book, new RiseOptions { Format = ModelFormat.GltfWithBin });
            using var set = Generator.Generate(book, new GenerateOptions(2, "g", EnforceUniqueDna: false));
            SetWriter.Write(set, setDir, pack: false, modeler);
        }

        var result = SetExporter.Export(setDir, outDir, new ExportOptions
        {
            Shape = ExportShape.Folder, Models = true, TokenNaming = TokenNaming.TokenId, TokenStart = 0,
        });

        string gltf = Path.Combine(result.Path, "models", "1.gltf");
        Assert.True(File.Exists(Path.Combine(result.Path, "models", "1.bin")));
        Assert.False(File.Exists(Path.Combine(result.Path, "models", "0002.bin")));
        var read = GltfReader.Read(gltf);   // resolves buffers[0].uri beside the file
        Assert.True(read.Mesh.TriangleCount > 0);

        // Only the buffer link moved: the original document differs from the renamed one in that field alone.
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(setDir, "models", "0002.gltf")))!;
        var renamed = JsonNode.Parse(File.ReadAllText(gltf))!;
        Assert.Equal("0002.bin", original["buffers"]![0]!["uri"]!.GetValue<string>());
        Assert.Equal("1.bin", renamed["buffers"]![0]!["uri"]!.GetValue<string>());
        renamed["buffers"]![0]!["uri"] = "0002.bin";
        Assert.Equal(original.ToJsonString(), renamed.ToJsonString());

        // The renamed export is still a Set, and its model is found by the Set's own naming.
        using var reopened = SetReader.Read(result.Path);
        Assert.Equal(2, reopened.Items.Count);
        Assert.Equal(2, SetLayout.ModelCountIn(result.Path));
    }

    private static LoadedCookBook ModelBook()
    {
        Image<Rgba32> Solid() => new(8, 8, new Rgba32(30, 120, 200, 255));
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                [new Variant("v", "Blue", 1), new Variant("w", "Blue2", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = Solid(), ["w"] = Solid() },
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Box", new Dimensions(8, 8),
                new Collection("Box", "d", "BX"), new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = [new LoadedRecipe { Manifest = new RecipeManifest("r", "R", ["body"], []), Ingredients = [ing] }],
        };
    }
}
