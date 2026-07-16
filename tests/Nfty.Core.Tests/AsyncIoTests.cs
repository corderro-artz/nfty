using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The async twins of the archive and set-writing I/O. Each must land byte-identical results
/// to its sync counterpart — they are the same formats, just awaited.
/// </summary>
public class AsyncIoTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory().FullName;

    private static LoadedIngredient Ingredient() => new()
    {
        Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
            new[] { new Variant("sunset", "Sunset", 1), new Variant("grid", "Grid", 2) }),
        VariantImages = new Dictionary<string, Image<Rgba32>>
        {
            ["sunset"] = new Image<Rgba32>(2, 2, new Rgba32(255, 128, 0, 255)),
            ["grid"] = new Image<Rgba32>(2, 2, new Rgba32(0, 128, 255, 255)),
        },
    };

    private static LoadedRecipe Recipe() => new()
    {
        Manifest = new RecipeManifest("cat", "Cat", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
        Ingredients = new[] { Ingredient() },
    };

    private static CookBookManifest BookManifest() =>
        new("cb", "VaporPets", new Dimensions(2, 2), new Collection("VaporPets", "d", "VP"),
            new Dictionary<string, double> { ["cat"] = 1 });

    [Fact]
    public async Task Ingredient_round_trips_asynchronously()
    {
        var path = Path.Combine(TempDir(), "bg.igt");
        var ing = Ingredient();

        await IngredientArchive.WriteAsync(path, ing.Manifest, ing.VariantImages);
        var loaded = await IngredientArchive.ReadAsync(path);

        Assert.Equal(2, loaded.Manifest.Variants.Count);
        Assert.Equal(new Rgba32(255, 128, 0, 255), loaded.VariantImages["sunset"][0, 0]);
    }

    [Fact]
    public async Task Recipe_round_trips_asynchronously()
    {
        var path = Path.Combine(TempDir(), "cat.rcp");
        var recipe = Recipe();

        await RecipeArchive.WriteAsync(path, recipe.Manifest, recipe.Ingredients);
        var loaded = await RecipeArchive.ReadAsync(path);

        Assert.Equal("Cat", loaded.Manifest.Name);
        Assert.Equal(new[] { "bg" }, loaded.Manifest.LayerOrder);
        Assert.Equal(2, loaded.Ingredients.Single().Manifest.Variants.Count);
    }

    [Fact]
    public async Task CookBook_round_trips_asynchronously()
    {
        var path = Path.Combine(TempDir(), "VaporPets.cbk");

        await CookBookArchive.WriteAsync(path, BookManifest(), new[] { Recipe() });
        var loaded = await CookBookArchive.ReadAsync(path);

        Assert.Equal("VaporPets", loaded.Manifest.Name);
        Assert.Equal(new Dimensions(2, 2), loaded.Manifest.Canvas);
        Assert.Equal(new Rgba32(0, 128, 255, 255),
            loaded.Recipes.Single().Ingredients.Single().VariantImages["grid"][0, 0]);
    }

    [Fact]
    public async Task Async_written_cookbook_reads_back_through_the_sync_path()
    {
        // The async writer must produce the same archive the sync reader expects.
        var path = Path.Combine(TempDir(), "VaporPets.cbk");
        await CookBookArchive.WriteAsync(path, BookManifest(), new[] { Recipe() });

        var loaded = CookBookArchive.Read(path);

        Assert.Equal("VaporPets", loaded.Manifest.Name);
    }

    [Fact]
    public async Task Async_read_records_the_same_source_hash_as_the_sync_read()
    {
        var path = Path.Combine(TempDir(), "VaporPets.cbk");
        CookBookArchive.Write(path, BookManifest(), new[] { Recipe() });

        var async = await CookBookArchive.ReadAsync(path);
        var sync = CookBookArchive.Read(path);

        Assert.NotNull(async.SourceSha256);
        Assert.Equal(sync.SourceSha256, async.SourceSha256);
    }

    [Fact]
    public async Task Future_schema_version_is_rejected_by_the_async_reader()
    {
        var path = Path.Combine(TempDir(), "future.igt");
        var ing = Ingredient();
        var future = ing.Manifest with { SchemaVersion = 2 };
        IngredientArchive.Write(path, future, ing.VariantImages);

        await Assert.ThrowsAsync<UnsupportedSchemaVersionException>(
            () => IngredientArchive.ReadAsync(path));
    }

    [Fact]
    public async Task WriteAsync_produces_the_same_files_as_Write()
    {
        var dir = TempDir();
        var cbk = Path.Combine(dir, "VaporPets.cbk");
        CookBookArchive.Write(cbk, BookManifest(), new[] { Recipe() });
        var book = CookBookArchive.Read(cbk);

        var syncDir = Path.Combine(dir, "sync");
        var asyncDir = Path.Combine(dir, "async");

        using (var a = Generator.Generate(book, new GenerateOptions(2, "seed-1")))
            SetWriter.Write(a, syncDir, pack: false);
        using (var b = Generator.Generate(book, new GenerateOptions(2, "seed-1")))
            await SetWriter.WriteAsync(b, asyncDir, pack: false);

        Assert.Equal(
            File.ReadAllText(Path.Combine(syncDir, "set.json")),
            File.ReadAllText(Path.Combine(asyncDir, "set.json")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(syncDir, "images", "0001.png")),
            File.ReadAllBytes(Path.Combine(asyncDir, "images", "0001.png")));
        Assert.Equal(
            File.ReadAllText(Path.Combine(syncDir, "nfty", "0002.json")),
            File.ReadAllText(Path.Combine(asyncDir, "nfty", "0002.json")));
    }

    [Fact]
    public async Task WriteAsync_reports_progress_per_asset()
    {
        var dir = TempDir();
        var cbk = Path.Combine(dir, "VaporPets.cbk");
        CookBookArchive.Write(cbk, BookManifest(), new[] { Recipe() });
        var book = CookBookArchive.Read(cbk);

        var seen = new List<WriteProgress>();
        using var set = Generator.Generate(book, new GenerateOptions(2, "seed-1"));
        await SetWriter.WriteAsync(set, Path.Combine(dir, "out"), pack: false,
            progress: new InlineProgress<WriteProgress>(seen.Add));

        Assert.Equal(new[] { 1, 2 }, seen.Select(p => p.Completed));
        Assert.All(seen, p => Assert.Equal(2, p.Total));
    }

    [Fact]
    public async Task ReadExistingAsync_matches_the_sync_reader()
    {
        var dir = TempDir();
        var cbk = Path.Combine(dir, "VaporPets.cbk");
        CookBookArchive.Write(cbk, BookManifest(), new[] { Recipe() });
        var book = CookBookArchive.Read(cbk);
        var outDir = Path.Combine(dir, "out");

        using (var set = Generator.Generate(book, new GenerateOptions(2, "seed-1")))
            SetWriter.Write(set, outDir, pack: false);

        var async = await SetWriter.ReadExistingAsync(outDir);
        var sync = SetWriter.ReadExisting(outDir);

        Assert.Equal(sync.NextNumber, async.NextNumber);
        Assert.Equal(sync.Dnas.OrderBy(d => d), async.Dnas.OrderBy(d => d));
    }

    [Fact]
    public async Task Packed_async_write_produces_a_readable_set_archive()
    {
        var dir = TempDir();
        var cbk = Path.Combine(dir, "VaporPets.cbk");
        CookBookArchive.Write(cbk, BookManifest(), new[] { Recipe() });
        var book = CookBookArchive.Read(cbk);
        var outDir = Path.Combine(dir, "out");

        using (var set = Generator.Generate(book, new GenerateOptions(1, "seed-1")))
            await SetWriter.WriteAsync(set, outDir, pack: true);

        using var zip = System.IO.Compression.ZipFile.OpenRead(outDir + ".set");
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("set.json"));
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith("0001.png"));
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
