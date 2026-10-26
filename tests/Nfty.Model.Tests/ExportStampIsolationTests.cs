using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Model.Tests;

/// <summary>The debug mark stamps art on the way out; a model's texture must stay the unstamped asset.</summary>
public class ExportStampIsolationTests
{
    private const int Size = 16;

    private static LoadedCookBook Book()
    {
        // A solid square: the stamp's corner lands on solid art, so the stamped image differs.
        Image<Rgba32> Solid() => new(Size, Size, new Rgba32(30, 120, 200, 255));
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("body", "Body", LayerKind.Custom, null,
                [new Variant("v", "Blue", 1), new Variant("w", "Blue2", 1)]),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["v"] = Solid(), ["w"] = Solid() },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", ["body"], []),
            Ingredients = [ing],
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Box", new Dimensions(Size, Size),
                new Collection("Box", "d", "BX"), new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = [recipe],
        };
    }

    private static bool PixelsEqual(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height) return false;
        for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
                if (a[x, y] != b[x, y]) return false;
        return true;
    }

    [Fact]
    public void A_model_exported_with_the_debug_mark_armed_matches_the_unstamped_asset()
    {
        string setDir = Path.Combine(Directory.CreateTempSubdirectory("nfty-iso-").FullName, "Box");
        string outDir = Directory.CreateTempSubdirectory("nfty-iso-out-").FullName;
        using (var book = Book())
        {
            var modeler = new SetModeler(book, new RiseOptions { Format = ModelFormat.Glb });
            using var set = Generator.Generate(book, new GenerateOptions(2, "iso", EnforceUniqueDna: false));
            SetWriter.Write(set, setDir, pack: false, modeler);
        }

        var result = SetExporter.Export(setDir, outDir,
            new ExportOptions { Shape = ExportShape.Folder, NumberWatermark = true, Models = true });
        string exported = result.Path;

        var (json, bin) = GltfValidator.ReadGlb(File.ReadAllBytes(Path.Combine(exported, "models", "0001.glb")));
        using var texture = Image.Load<Rgba32>(GltfValidator.ReadImage(json, bin));
        using var unstamped = Image.Load<Rgba32>(Path.Combine(setDir, "images", "0001.png"));
        using var stamped = Image.Load<Rgba32>(Path.Combine(exported, "images", "0001.png"));

        // The atlas is [front][gutter][back]; its front half is the unstamped asset.
        Assert.Equal(FaceAtlas.WidthFor(unstamped.Width), texture.Width);
        using var frontHalf = texture.Clone(c => c.Crop(new Rectangle(0, 0, unstamped.Width, unstamped.Height)));
        Assert.True(PixelsEqual(frontHalf, unstamped));
        Assert.False(PixelsEqual(stamped, unstamped)); // the stamp really ran
    }
}
