using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests.Community;

public class ShareCheckSetTests : IDisposable
{
    private const string Pass = "correct-horse-battery-staple";
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-shareset-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Fresh(string leaf = "") => Path.Combine(_dir.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName, leaf);

    /// <summary>Custom layers of solid fills: <paramref name="layers"/> layers of <paramref name="variants"/> variants.</summary>
    private static LoadedCookBook Book(int side = 4, int layers = 1, int variants = 8)
    {
        var ingredients = new List<LoadedIngredient>();
        for (int l = 0; l < layers; l++)
        {
            var ids = Enumerable.Range(0, variants).Select(v => $"v{v}").ToList();
            ingredients.Add(new LoadedIngredient
            {
                Manifest = new IngredientManifest($"l{l}", $"Layer {l}", LayerKind.Custom, null,
                    ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
                VariantImages = ids.Select((v, i) => (v, i)).ToDictionary(t => t.v,
                    t => new Image<Rgba32>(side, side, new Rgba32((byte)(t.i * 20), (byte)l, 3, 255))),
            });
        }
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(side, side),
                new Collection("Chest Demo", "d", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", ingredients.Select(i => i.Manifest.Id).ToList(),
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = ingredients,
                },
            ],
        };
    }

    private string CookFolder(LoadedCookBook book, int count)
    {
        string dir = Path.Combine(Fresh(), "ChestDemo");
        using var set = Generator.Generate(book, new GenerateOptions(count, "launch"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    private string Packed(int count = 2, int side = 4, int layers = 1)
    {
        using var book = Book(side, layers, layers == 1 ? 8 : 6);
        string dir = CookFolder(book, count);
        return Export(dir, new ExportOptions());
    }

    private string Export(string setDir, ExportOptions options, string? cookBook = null, string? pass = null) =>
        SetExporter.Export(setDir, Fresh(), options, cookBook, pass).Path;

    private static void Replace(string zipPath, string entry, byte[] bytes)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        zip.GetEntry(entry)!.Delete();
        using var s = zip.CreateEntry(entry).Open();
        s.Write(bytes);
    }

    private static byte[] Read(string zipPath, string entry)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        using var s = zip.GetEntry(entry)!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task A_tiny_cooked_set_passes_with_its_facts()
    {
        string path = Packed(2);
        var facts = await ShareCheck.CheckAsync(path);

        Assert.Empty(facts.Problems);
        Assert.Equal(2, facts.Assets);
        Assert.Equal(4, facts.Width);
        Assert.Equal(4, facts.Height);
        Assert.False(facts.Models);
        Assert.False(facts.IncludesCookBook);
        Assert.Null(facts.CookbookSha256);
        Assert.Equal("Chest Demo", facts.Collection);
        Assert.Equal(Hex(File.ReadAllBytes(path)), facts.FileSha256);

        using var set = SetReader.Read(path);
        string expected = Hex(Encoding.UTF8.GetBytes(string.Join('\n', set.Items.OrderBy(i => i.Number).Select(i => i.Dna))));
        Assert.Equal(expected, facts.Fingerprint);
        Assert.Matches("^[0-9a-f]{64}$", facts.Fingerprint);
    }

    [Fact]
    public async Task A_sealed_payload_renamed_set_is_refused()
    {
        using var book = Book();
        string dir = CookFolder(book, 2);
        string tin = Export(dir, ExportOptions.For(ExportPreset.SealedCritique), pass: Pass);
        string renamed = Path.Combine(Fresh(), "looks-innocent.set");
        File.Copy(tin, renamed);

        Assert.Equal([ShareRules.Sealed], (await ShareCheck.CheckAsync(renamed)).Problems);
        Assert.Equal([ShareRules.Sealed], (await ShareCheck.CheckAsync(tin)).Problems);
    }

    [Fact]
    public async Task A_tin_that_is_not_even_a_zip_is_still_called_sealed()
    {
        string path = Path.Combine(Fresh(), "x.tin");
        File.WriteAllText(path, "not a zip");
        Assert.Equal([ShareRules.Sealed], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task A_missing_file_is_not_a_set()
    {
        var facts = await ShareCheck.CheckAsync(Path.Combine(Fresh(), "nope.set"));
        Assert.Equal([ShareRules.NotASet], facts.Problems);
    }

    [Fact]
    public async Task A_513_canvas_is_refused()
    {
        using var book = Book(side: 513, variants: 2);
        string path = Export(CookFolder(book, 2), new ExportOptions());
        Assert.Equal([ShareRules.CanvasTooLarge(513, 513)], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task A_512_canvas_passes()
    {
        using var book = Book(side: 512, variants: 2);
        string path = Export(CookFolder(book, 2), new ExportOptions());
        var facts = await ShareCheck.CheckAsync(path);
        Assert.Empty(facts.Problems);
        Assert.Equal(512, facts.Width);
    }

    [Fact]
    public async Task A_1001_asset_set_is_refused()
    {
        string path = Packed(1001, side: 1, layers: 4);
        Assert.Equal([ShareRules.TooManyAssets(1001)], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task A_renamed_marketplace_export_is_refused()
    {
        using var book = Book();
        string path = Export(CookFolder(book, 2), new ExportOptions { TokenNaming = TokenNaming.TokenId });
        Assert.Equal([ShareRules.RenamedExport], (await ShareCheck.CheckAsync(path)).Problems);
    }

    private string BookFile(string name, int variants)
    {
        string path = Path.Combine(Fresh(), name + ".cbk");
        using var b = Book(variants: variants);
        CookBookArchive.Write(path, b.Manifest, b.Recipes);
        return path;
    }

    [Fact]
    public async Task A_foreign_cbk_is_refused()
    {
        string own = BookFile("own", 8);
        string other = BookFile("other", 7);
        using var loaded = CookBookArchive.Read(own);
        string dir = CookFolder(loaded, 2);

        string path = Export(dir, new ExportOptions { IncludeCookBook = true }, other);
        Assert.Equal([ShareRules.ForeignBook], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task A_cbk_beside_a_set_that_recorded_no_book_is_refused()
    {
        string other = BookFile("other", 7);
        using var book = Book();
        string path = Export(CookFolder(book, 2), new ExportOptions { IncludeCookBook = true }, other);
        Assert.Equal([ShareRules.ForeignBook], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task The_sets_own_cbk_is_allowed()
    {
        string own = BookFile("own", 8);
        using var loaded = CookBookArchive.Read(own);
        string path = Export(CookFolder(loaded, 2), new ExportOptions { IncludeCookBook = true }, own);

        var facts = await ShareCheck.CheckAsync(path);
        Assert.Empty(facts.Problems);
        Assert.True(facts.IncludesCookBook);
        Assert.Equal(loaded.SourceSha256, facts.CookbookSha256);
    }

    [Fact]
    public async Task A_damaged_image_is_named()
    {
        string path = Packed(3);
        Replace(path, "images/0002.png", [1, 2, 3, 4, 5]);

        var facts = await ShareCheck.CheckAsync(path);
        Assert.Equal([ShareRules.ImageUnreadable("0002.png")], facts.Problems);
    }

    [Fact]
    public async Task An_image_of_another_size_is_named()
    {
        string path = Packed(3);
        using var odd = new Image<Rgba32>(5, 4, new Rgba32(1, 2, 3, 255));
        using var ms = new MemoryStream();
        odd.SaveAsPng(ms);
        Replace(path, "images/0003.png", ms.ToArray());

        Assert.Equal([ShareRules.ImageWrongSize("0003.png")], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task Count_in_set_json_must_match_images()
    {
        string path = Packed(2);
        var json = JsonNode.Parse(Read(path, "set.json"))!.AsObject();
        json["count"] = 3;
        Replace(path, "set.json", Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal([ShareRules.CountMismatch], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task An_extra_picture_the_metadata_does_not_know_is_a_count_mismatch()
    {
        string path = Packed(2);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var s = zip.CreateEntry("images/0009.png").Open();
            s.Write([1, 2, 3]);
        }
        Assert.Equal([ShareRules.CountMismatch], (await ShareCheck.CheckAsync(path)).Problems);
    }

    [Fact]
    public async Task An_archive_problem_stops_the_set_checks()
    {
        string path = Packed(2);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        {
            using var s = zip.CreateEntry("notes.txt").Open();
            s.Write([1]);
        }
        var facts = await ShareCheck.CheckAsync(path);
        Assert.Equal([ShareRules.UnexpectedFile("notes.txt")], facts.Problems);
    }

    [Fact]
    public async Task Fingerprint_survives_repacking_and_leaving_the_book_out()
    {
        string own = BookFile("own", 8);
        using var loaded = CookBookArchive.Read(own);
        string dir = CookFolder(loaded, 3);

        string plain = Export(dir, new ExportOptions { Models = false });
        string full = Export(dir, new ExportOptions { IncludeCookBook = true }, own);

        var a = await ShareCheck.CheckAsync(plain);
        var b = await ShareCheck.CheckAsync(full);
        Assert.Empty(a.Problems);
        Assert.Empty(b.Problems);
        Assert.Equal(a.Fingerprint, b.Fingerprint);
        Assert.NotEqual(a.FileSha256, b.FileSha256);
    }

    [Fact]
    public async Task Fingerprint_changes_when_the_set_is_extended()
    {
        using var book = Book();
        string dir = CookFolder(book, 2);
        var before = await ShareCheck.CheckAsync(Export(dir, new ExportOptions()));

        var existing = SetWriter.ReadExisting(dir);
        using (var more = Generator.Generate(book, new GenerateOptions(1, "more"),
                   existingDnas: existing.Dnas, startNumber: existing.NextNumber))
            SetWriter.Write(more, dir, pack: false);
        var after = await ShareCheck.CheckAsync(Export(dir, new ExportOptions()));

        Assert.Empty(after.Problems);
        Assert.Equal(3, after.Assets);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
    }

    [Fact]
    public async Task A_cancelled_check_throws_cancellation_not_a_problem()
    {
        string path = Packed(2);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ShareCheck.CheckAsync(path, cts.Token));
    }
}
