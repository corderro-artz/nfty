using System.Globalization;
using Nfty.Core.Diagnostics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// MEASURED, NEVER ASSERTED AS WALL-CLOCK — <c>generate --models</c> builds one model per asset of a
/// whole Set, so the cost the earlier steps measured one picture at a time is now paid fifty or
/// fifty thousand times. This cooks 50 assets of a 64px and a 512px book, then times only
/// <see cref="SetModeler.Model"/> over them, with the scopes under it. The last scope is the three
/// <c>ToArray</c> copies plus the clones <c>Mesh</c> makes (BACKLOG M5): the decision rule recorded there
/// is "optimize only if that is more than 10% of <c>SetModeler.Model</c>". Run with
/// <c>dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~SetModelsPerfTests -l "console;verbosity=detailed"</c>.
/// </summary>
public class SetModelsPerfTests(ITestOutputHelper output)
{
    private const int Assets = 50;

    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void Modeling_fifty_assets_is_measured(int size)
    {
        using var book = Book(size);
        using var set = Generator.Generate(book, new GenerateOptions(Assets, "perf", EnforceUniqueDna: false));
        var modeler = new SetModeler(book, new RiseOptions());

        // One unmeasured model (JIT, pools), then the fifty.
        Assert.NotNull(modeler.Model(set.Assets[0], "warm"));

        Perf.Enable();
        try
        {
            Perf.Reset();
            long bytes = 0;
            foreach (var asset in set.Assets)
                bytes += modeler.Model(asset, $"{asset.SetNumber:D4}")!.Sum(f => f.Bytes.Length);

            var scopes = Perf.Snapshot().ToDictionary(e => e.Name);
            foreach (var name in new[] { "SetModeler.Model", "Riser.TryBuild", "ModelWriter.Write", "MeshBuilder.ToMesh.copies" })
                Assert.Contains(name, scopes.Keys);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{size}px book, {set.Assets.Count} assets, {bytes:N0} model bytes in all"));
            output.WriteLine(Perf.Report());
        }
        finally
        {
            Perf.Disable();
        }
    }

    /// <summary>Two recipes' worth of variation in one: a dome-shaped disc body of three colors and a
    /// smaller hat that is absent half the time. Every picture is the book's full size, so the size is the
    /// thing that varies between the two runs.</summary>
    private static LoadedCookBook Book(int size)
    {
        LoadedIngredient Layer(string id, string name, double radiusShare, int domeHeight, params byte[] reds)
        {
            var images = new Dictionary<string, Image<Rgba32>>();
            var depths = new Dictionary<string, DepthMap>();
            var variants = new List<Variant>();
            foreach (var red in reds)
            {
                string v = $"{id}{red}";
                var img = Disc(size, radiusShare, red);
                images[v] = img;
                depths[v] = Dome(img, domeHeight);
                variants.Add(new Variant(v, v, 1));
            }
            return new LoadedIngredient
            {
                Manifest = new IngredientManifest(id, name, LayerKind.Custom, null, variants),
                VariantImages = images,
                Depths = depths,
            };
        }

        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("r", "R", ["body", "hat"], [],
                AbsentPercent: new Dictionary<string, double> { ["hat"] = 50 }),
            Ingredients = [Layer("body", "Body", 0.48, 8, 40, 120, 200), Layer("hat", "Hat", 0.25, 12, 90, 230)],
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("perf", "Perf", new Dimensions(size, size),
                new Collection("Perf", "d", "PF"), new Dictionary<string, double> { ["r"] = 100 }),
            Recipes = [recipe],
        };
    }

    private static Image<Rgba32> Disc(int size, double radiusShare, byte red)
    {
        var img = new Image<Rgba32>(size, size);
        float c = (size - 1) / 2f, r = (float)(size * radiusShare);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
                img[x, y] = d <= r
                    ? new Rgba32(red, (byte)(x * 255 / size), (byte)(y * 255 / size), 255)
                    : new Rgba32(0, 0, 0, 0);
            }
        return img;
    }

    private static DepthMap Dome(Image<Rgba32> art, int height)
    {
        float c = (art.Width - 1) / 2f, r = art.Width / 2f;
        return Art.Depth(art, (x, y) =>
        {
            float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            return (Math.Clamp(height - (int)(d * height / r), 0, height), 1);
        });
    }
}
