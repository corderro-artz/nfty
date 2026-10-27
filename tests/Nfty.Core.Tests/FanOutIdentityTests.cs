using System.IO.Compression;
using System.Security.Cryptography;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Stats;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// Every fan-out that writes or reads a file gives the same bytes at one thread and at many. Each
/// theory pins the machine to eight logical processors first, so a request for 2, 3 or 8 workers is
/// really that many whatever box runs the suite; one thread is the plain-loop oracle.
/// </summary>
public class FanOutIdentityTests
{
    public static TheoryData<int> Degrees => new() { 2, 3, 8 };

    private static T OnEightProcessors<T>(Func<T> act)
    {
        Parallelism.ProcessorOverride = () => 8;
        try { return act(); }
        finally { Parallelism.ProcessorOverride = null; }
    }

    private static void OnEightProcessors(Action act) => OnEightProcessors<object?>(() => { act(); return null; });

    // ---------------------------------------------------------------- fixtures

    private static GeneratedSet MakeSet(params int[] numbers)
    {
        var assets = new List<GeneratedAsset>();
        foreach (var n in numbers)
        {
            var img = new Image<Rgba32>(32, 32);     // a stamp draws nothing rather than clip, so room for one
            for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                    img[x, y] = new Rgba32((byte)(n * 7 + x * 5), (byte)(y * 20 + n), (byte)(n + x), 255);
            assets.Add(new GeneratedAsset
            {
                SetNumber = n, Dna = $"dna-{n:D5}", RecipeId = n % 2 == 0 ? "cat" : "dog",
                RecipeName = n % 2 == 0 ? "Cat" : "Dog", Image = img,
                Traits = new[] { new TraitSelection("bg", "Background", $"v{n % 3}", $"V{n % 3}") },
                ColorRolls = new[] { new ColorRoll("bg", LayerKind.Dynamic, ColorModel.Hsv, n % 360 + 0.5, 0.5) },
            });
        }
        return new GeneratedSet("Pets", "desc", "PT", "seed", assets);
    }

    private static string CookedSet(params int[] numbers)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-fan-").FullName, "Pets");
        using var set = MakeSet(numbers);
        SetWriter.Write(set, dir, pack: false, parallelism: Parallelism.Sequential);
        return dir;
    }

    private static Dictionary<string, string> Hashes(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(dir, f).Replace('\\', '/'),
                f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), StringComparer.Ordinal);

    /// <summary>
    /// Every entry of a zip as "path:hash", in archive order, descending into nested archives. Entry
    /// timestamps are not compared: a zip stamps "now", so two writes differ there by design. Order
    /// and content are what a thread count could change.
    /// </summary>
    private static List<string> Flatten(Stream stream, string prefix = "")
    {
        var list = new List<string>();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var e in zip.Entries)
        {
            using var es = e.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            var ext = Path.GetExtension(e.FullName);
            if (ext is ".igt" or ".rcp")
            {
                list.Add($"{prefix}{e.FullName}:nested");
                list.AddRange(Flatten(ms, $"{prefix}{e.FullName}/"));
            }
            else
            {
                list.Add($"{prefix}{e.FullName}:{Convert.ToHexString(SHA256.HashData(ms.ToArray()))}");
            }
        }
        return list;
    }

    private static List<string> Flatten(string path)
    {
        using var fs = File.OpenRead(path);
        return Flatten(fs);
    }

    // ---------------------------------------------------------------- G1: stamp

    [Theory]
    [MemberData(nameof(Degrees))]
    public void Watermarked_export_files_are_identical_at_one_and_many_threads(int degree)
    {
        var set = CookedSet(Enumerable.Range(1, 30).ToArray());
        var root = Directory.CreateTempSubdirectory("nfty-fan-out-").FullName;
        var options = new ExportOptions { Shape = ExportShape.Folder, NumberWatermark = true };

        var one = SetExporter.Export(set, Path.Combine(root, "one"), options, parallelism: Parallelism.Sequential);
        var many = OnEightProcessors(() =>
            SetExporter.Export(set, Path.Combine(root, "many"), options, parallelism: new Parallelism(degree)));

        var a = Hashes(one.Path);
        var b = Hashes(many.Path);
        Assert.Equal(a.Keys.Order(StringComparer.Ordinal), b.Keys.Order(StringComparer.Ordinal));
        Assert.True(a.Count >= 30 * 3);
        foreach (var (file, hash) in a) Assert.Equal(hash, b[file]);

        // The stamp really ran: a stamped export differs from a plain one in the art.
        var plain = SetExporter.Export(set, Path.Combine(root, "plain"),
            new ExportOptions { Shape = ExportShape.Folder }, parallelism: Parallelism.Sequential);
        Assert.NotEqual(Hashes(plain.Path)["images/0001.png"], a["images/0001.png"]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public void A_failing_stamp_leaves_no_temp_folder_behind(int degree)
    {
        var set = CookedSet(Enumerable.Range(1, 12).ToArray());
        // Two unreadable PNGs: whichever worker reaches one first, the LOWEST-numbered is named.
        File.WriteAllText(Path.Combine(set, "images", "0009.png"), "not a png");
        File.WriteAllText(Path.Combine(set, "images", "0004.png"), "not a png");
        var root = Directory.CreateTempSubdirectory("nfty-fan-out-").FullName;
        var before = StampTemps();

        var ex = Assert.Throws<CorruptSetException>(() => OnEightProcessors(() =>
            SetExporter.Export(set, Path.Combine(root, "out"),
                new ExportOptions { Shape = ExportShape.Folder, NumberWatermark = true },
                parallelism: new Parallelism(degree))));

        Assert.Contains("0004.png", ex.Message);
        Assert.DoesNotContain("0009.png", ex.Message);
        Assert.Equal(before, StampTemps());
    }

    private static List<string> StampTemps() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), "nfty-stamp-*").Order(StringComparer.Ordinal).ToList();

    // ---------------------------------------------------------------- G3: menu

    [Theory]
    [InlineData(MenuArtMode.Atlas, 2)]
    [InlineData(MenuArtMode.Atlas, 8)]
    [InlineData(MenuArtMode.Loose, 3)]
    [InlineData(MenuArtMode.Loose, 8)]
    public void Menu_bytes_are_identical_at_one_and_many_threads(MenuArtMode art, int degree)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-fan-").FullName;
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(dir));
        var options = new MenuWriteOptions { Art = art };

        MenuWriter.Write(book, Path.Combine(dir, "one.mnu"), options, Parallelism.Sequential);
        OnEightProcessors(() => MenuWriter.Write(book, Path.Combine(dir, "many.mnu"), options, new Parallelism(degree)));

        var one = Flatten(Path.Combine(dir, "one.mnu"));
        var many = Flatten(Path.Combine(dir, "many.mnu"));
        Assert.True(one.Count > 10);
        Assert.Contains(one, e => e.StartsWith("depth/", StringComparison.Ordinal));   // the order-sensitive part
        Assert.Equal(one, many);
    }

    // ---------------------------------------------------------------- G4: archive writes and reads

    [Theory]
    [MemberData(nameof(Degrees))]
    public void Ingredient_archive_bytes_are_identical_at_one_and_many_threads(int degree)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-fan-").FullName;
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(dir));
        var ing = book.Recipes.SelectMany(r => r.Ingredients).OrderByDescending(i => i.Manifest.Variants.Count).First();
        Assert.True(ing.Manifest.Variants.Count >= 2);

        IngredientArchive.Write(Path.Combine(dir, "one.igt"), ing.Manifest, ing.VariantImages, ing.Depths, Parallelism.Sequential);
        OnEightProcessors(() => IngredientArchive.Write(Path.Combine(dir, "many.igt"), ing.Manifest,
            ing.VariantImages, ing.Depths, new Parallelism(degree)));
        Assert.Equal(Flatten(Path.Combine(dir, "one.igt")), Flatten(Path.Combine(dir, "many.igt")));
    }

    [Theory]
    [MemberData(nameof(Degrees))]
    public async Task CookBook_archive_bytes_are_identical_at_one_and_many_threads(int degree)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-fan-").FullName;
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(dir));

        CookBookArchive.Write(Path.Combine(dir, "one.cbk"), book.Manifest, book.Recipes, Parallelism.Sequential);
        OnEightProcessors(() => CookBookArchive.Write(Path.Combine(dir, "many.cbk"), book.Manifest, book.Recipes,
            new Parallelism(degree)));
        Parallelism.ProcessorOverride = () => 8;
        try { await CookBookArchive.WriteAsync(Path.Combine(dir, "async.cbk"), book.Manifest, book.Recipes, default, new Parallelism(degree)); }
        finally { Parallelism.ProcessorOverride = null; }

        var one = Flatten(Path.Combine(dir, "one.cbk"));
        Assert.Contains(one, e => e.Contains("depth/", StringComparison.Ordinal));
        Assert.Equal(one, Flatten(Path.Combine(dir, "many.cbk")));
        Assert.Equal(one, Flatten(Path.Combine(dir, "async.cbk")));
    }

    [Theory]
    [MemberData(nameof(Degrees))]
    public async Task Archive_reads_decode_the_same_pixels_in_the_same_order_at_any_thread_count(int degree)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-fan-").FullName;
        var path = DemoCookBook.WriteTo(dir);
        using var one = CookBookArchive.Read(path, Parallelism.Sequential);
        using var many = await ReadWide(path, degree);

        Assert.Equal(Describe(one), Describe(many));
    }

    private static async Task<LoadedCookBook> ReadWide(string path, int degree)
    {
        Parallelism.ProcessorOverride = () => 8;
        try { return await CookBookArchive.ReadAsync(path, default, new Parallelism(degree)); }
        finally { Parallelism.ProcessorOverride = null; }
    }

    private static List<string> Describe(LoadedCookBook book)
    {
        var list = new List<string>();
        foreach (var r in book.Recipes)
            foreach (var i in r.Ingredients)
                foreach (var (id, img) in i.VariantImages)   // dictionary enumeration order is observable
                {
                    var px = new Rgba32[img.Width * img.Height];
                    img.CopyPixelDataTo(px);
                    var bytes = px.SelectMany(p => new[] { p.R, p.G, p.B, p.A }).ToArray();
                    list.Add($"{r.Manifest.Id}/{i.Manifest.Id}/{id}:{Convert.ToHexString(SHA256.HashData(bytes))}");
                }
        return list;
    }

    private static LoadedIngredient SixVariants(string badDepthAt1 = "v2", string badDepthAt2 = "v4")
    {
        var ids = Enumerable.Range(0, 6).Select(i => $"v{i}").ToList();
        var depths = new Dictionary<string, DepthMap>(StringComparer.Ordinal);
        foreach (var id in new[] { badDepthAt1, badDepthAt2 })
            depths[id] = DepthMap.Flat(3, 3);          // the images are 4x4: a mismatch
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v, 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v, v => new Image<Rgba32>(4, 4, new Rgba32(1, 2, 3, 255)),
                StringComparer.Ordinal),
            Depths = depths,
        };
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task A_failing_encode_disposes_nothing_twice_and_writes_no_archive(int degree)
    {
        var dir = Directory.CreateTempSubdirectory("nfty-fan-").FullName;
        using var ing = SixVariants();
        var sync = Path.Combine(dir, "sync.igt");
        var async = Path.Combine(dir, "async.igt");

        // Two variants fail; the lowest-positioned one is named at every thread count.
        var ex = OnEightProcessors(() => Assert.Throws<ArgumentException>(() =>
            IngredientArchive.Write(sync, ing.Manifest, ing.VariantImages, ing.Depths, new Parallelism(degree))));
        Assert.Contains("'v2'", ex.Message);
        Assert.DoesNotContain("'v4'", ex.Message);
        Assert.False(File.Exists(sync));

        Parallelism.ProcessorOverride = () => 8;
        try
        {
            var aex = await Assert.ThrowsAsync<ArgumentException>(() =>
                IngredientArchive.WriteAsync(async, ing.Manifest, ing.VariantImages, ing.Depths, default, new Parallelism(degree)));
            Assert.Contains("'v2'", aex.Message);
        }
        finally { Parallelism.ProcessorOverride = null; }
        Assert.False(File.Exists(async));

        // The writer only borrows the images: every one is still alive (a double dispose or an early
        // one would throw here), and the ingredient disposes them exactly once at the end of the test.
        // Width is plain metadata that survives Dispose; a pixel read is what a disposed image refuses.
        foreach (var img in ing.VariantImages.Values) _ = img[0, 0];
    }

    // ---------------------------------------------------------------- G6: voxel report

    [Theory]
    [MemberData(nameof(Degrees))]
    public void Voxel_report_rows_are_identical_at_one_and_many_threads(int degree)
    {
        // 3 recipes x 3 layers x 4 variants, each variant with its own count of partial pixels, so a
        // row filed under the wrong slot shows as a wrong number rather than hiding in a tie.
        var recipes = new List<LoadedRecipe>();
        int k = 0;
        for (var r = 0; r < 3; r++)
        {
            var ings = new List<LoadedIngredient>();
            for (var l = 0; l < 3; l++)
            {
                var ids = Enumerable.Range(0, 4).Select(v => $"v{v}").ToList();
                var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal);
                foreach (var id in ids)
                {
                    var img = new Image<Rgba32>(8, 8, new Rgba32(1, 2, 3, 255));
                    var partial = ++k % 17;
                    for (var p = 0; p < partial; p++) img[p % 8, p / 8] = new Rgba32(1, 2, 3, 128);
                    images[id] = img;
                }
                ings.Add(new LoadedIngredient
                {
                    Manifest = new IngredientManifest($"l{r}{l}", $"Layer {r}{l}", LayerKind.Custom, null,
                        ids.Select(v => new Variant(v, v, 1)).ToList()),
                    VariantImages = images,
                });
            }
            recipes.Add(new LoadedRecipe
            {
                Manifest = new RecipeManifest($"r{r}", $"R{r}", ings.Select(i => i.Manifest.Id).Reverse().ToList(),
                    Array.Empty<IncompatibilityRule>()),
                Ingredients = ings,
            });
        }
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(8, 8), new Collection("B", "d", "B"),
                recipes.ToDictionary(r => r.Manifest.Id, _ => 1.0)),
            Recipes = recipes,
        };

        var one = VoxelReport.Scan(book, Parallelism.Sequential);
        var many = OnEightProcessors(() => VoxelReport.Scan(book, new Parallelism(degree)));

        Assert.Equal(36, one.Count);
        Assert.True(one.Select(r => r.Partial).Distinct().Count() > 8);
        Assert.Equal(one, many);
        Assert.Equal(VoxelReport.Render("t", one), VoxelReport.Render("t", many));
    }

    // ---------------------------------------------------------------- G8 / G9: reading a Set

    [Theory]
    [MemberData(nameof(Degrees))]
    public async Task Set_items_load_in_number_order_at_one_and_many_threads(int degree)
    {
        // Sparse numbers straddling the point where the four-digit stem stops padding: ordinally
        // "10000.json" sorts before "9999.json", and the load must not.
        var set = CookedSet(1, 2, 9999, 10000);

        using var one = SetReader.Read(set, Parallelism.Sequential);
        using var many = OnEightProcessors(() => SetReader.Read(set, new Parallelism(degree)));
        Parallelism.ProcessorOverride = () => 8;
        LoadedSet viaAsync;
        try { viaAsync = await SetReader.ReadAsync(set, default, new Parallelism(degree)); }
        finally { Parallelism.ProcessorOverride = null; }
        using var __ = viaAsync;

        Assert.Equal(new[] { 1, 2, 9999, 10000 }, one.Items.Select(i => i.Number));
        Assert.Equal(one.Items.Select(i => (i.Number, i.Dna, i.ImagePath)),
            many.Items.Select(i => (i.Number, i.Dna, i.ImagePath)));
        Assert.Equal(one.Items.Select(i => i.Number), viaAsync.Items.Select(i => i.Number));
    }

    [Theory]
    [MemberData(nameof(Degrees))]
    public async Task Existing_dnas_are_read_identically_at_one_and_many_threads(int degree)
    {
        var set = CookedSet(Enumerable.Range(1, 40).Concat(new[] { 10000 }).ToArray());

        var one = SetWriter.ReadExisting(set, Parallelism.Sequential);
        var many = OnEightProcessors(() => SetWriter.ReadExisting(set, new Parallelism(degree)));
        Parallelism.ProcessorOverride = () => 8;
        SetWriter.ExistingSet viaAsync;
        try { viaAsync = await SetWriter.ReadExistingAsync(set, default, new Parallelism(degree)); }
        finally { Parallelism.ProcessorOverride = null; }

        Assert.Equal(41, one.Dnas.Count);
        Assert.Equal(10001, one.NextNumber);
        Assert.Equal(one.Dnas, many.Dnas);                 // enumeration order, not completion order
        Assert.Equal(one.NextNumber, many.NextNumber);
        Assert.Equal(one.Dnas, viaAsync.Dnas);
        Assert.Equal(one.NextNumber, viaAsync.NextNumber);
    }
}
