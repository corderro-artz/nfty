using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Converters;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Ticket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The pop-out viewer's archive galleries: what each archive lists, that listing decodes nothing,
/// that a tile decodes only when realized, and that a sealed export stays view-only.
/// </summary>
public class ArchiveGalleryTests
{
    /// <summary>Plays the dialog layer: answers the passphrase card from a script, and records every
    /// error card and what the passphrase card said when it was shown.</summary>
    internal sealed class Scripted(params string?[] passphrases) : IDialogService
    {
        private readonly Queue<string?> _answers = new(passphrases);
        public List<string> Errors { get; } = new();
        public List<string> PromptErrors { get; } = new();
        public int PromptsShown { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }

        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            switch (dialog)
            {
                case PassphraseDialogViewModel p:
                    PromptsShown++;
                    PromptErrors.Add(p.Error);
                    return Task.FromResult((TResult?)(object?)(_answers.Count > 0 ? _answers.Dequeue() : null));
                case ErrorDialogViewModel e:
                    Errors.Add(e.Message);
                    break;
            }
            return Task.FromResult<TResult?>(default);
        }

        public void Close(object? result) { }
    }

    internal sealed class Host : IViewerHost
    {
        public List<ViewerSubject> Opened { get; } = new();
        public void Open(ViewerSubject subject) => Opened.Add(subject);
    }

    private static string Dir() => Directory.CreateTempSubdirectory("nfty-gal-").FullName;

    private static string Demo() => DemoCookBook.WriteTo(Dir());

    /// <summary>A real cooked 4x4 Set on disk, with <paramref name="count"/> assets.</summary>
    private static string CookedSet(int count = 4)
    {
        var dir = Path.Combine(Dir(), "Chest Demo");
        var ids = new[] { "a", "b", "c", "d", "e", "f" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v, v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "d", "CHST"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
        using var set = Generator.Generate(book, new GenerateOptions(count, "launch"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    /// <summary>Seals a Set folder the way export does, at a cheap iteration count.</summary>
    private static string Sealed(string setDir, string pass)
    {
        string zip = Path.Combine(Dir(), "payload.zip");
        ZipFile.CreateFromDirectory(setDir, zip);
        string tin = Path.Combine(Dir(), "x" + Archives.SealedExtension);
        using var plain = File.OpenRead(zip);
        Seal.Write(tin, plain, "Chest Demo", 4, null, SealPolicy.ViewOnly, pass, 1000);
        return tin;
    }

    /// <summary>A gallery of real demo pictures, already decoded, plus one tile whose picture is gone:
    /// the state the contrast sweep and the capture need, deterministic (nothing decodes later).</summary>
    internal static ArchiveGalleryViewModel MixedGallery()
    {
        var gate = new ThumbnailGate(1);
        var source = GalleryBuilder.ForCookBook(Demo(), gate);   // not disposed: its tiles move to the new gallery
        var items = source.Items.Take(14).ToList();
        items.Add(new GalleryItem("Lock / gone", () => throw new FileNotFoundException(),
            () => Task.FromResult<ViewerSubject>(null!), gate));
        foreach (var i in items) i.DecodeNow();
        return new ArchiveGalleryViewModel("Chest Demo", source.Facts, items, gate);
    }

    // ------------------------------------------------------------------ cookbook

    [AvaloniaFact]
    public void A_cookbook_lists_its_recipes_and_variants_without_decoding_any_image()
    {
        string path = Demo();
        var gate = new ThumbnailGate(2);
        using var gallery = GalleryBuilder.ForCookBook(path, gate);

        var tree = ArchivePeek.CookBookTree(path);
        int expected = tree.Recipes.Sum(r => r.Ingredients.Sum(i => i.Variants.Count));
        Assert.Equal(expected, gallery.Items.Count);
        Assert.True(gallery.Items.Count > 20);                  // the demo is a real, varied book
        Assert.Equal(0, gate.Decodes);                          // listing opened no picture
        Assert.All(gallery.Items, i => Assert.Equal(0, i.DecodesRun));
        Assert.Contains(gallery.Facts, f => f.Label == "RECIPES" && f.Value == tree.Recipes.Count.ToString());
    }

    [AvaloniaFact]
    public void A_tile_decodes_its_image_only_when_realized()
    {
        using var gallery = GalleryBuilder.ForCookBook(Demo(), new ThumbnailGate(2));
        var tile = gallery.Items[3];
        var other = gallery.Items[4];

        Assert.Equal(0, gallery.Gate.Decodes);
        var bmp = tile.DecodeNow();

        Assert.NotNull(bmp);
        Assert.False(tile.ImageMissing);
        Assert.Equal(1, gallery.Gate.Decodes);                  // one tile realized, one decode
        Assert.Equal(0, other.DecodesRun);                      // its neighbour paid nothing
    }

    [AvaloniaFact]
    public async Task A_cookbook_tile_opens_its_variant_as_the_stored_gray_map()
    {
        string path = Demo();
        using var gallery = GalleryBuilder.ForCookBook(path, new ThumbnailGate(1));
        var tree = ArchivePeek.CookBookTree(path);
        var dyn = tree.Recipes.SelectMany(r => r.Ingredients).First(i => i.Kind != LayerKind.Custom);
        var tile = gallery.Items.First(i => i.Caption.Contains(dyn.Name));

        using var subject = await tile.Open();

        Assert.NotNull(subject.Art);
        Assert.Contains(subject.Facts, f => f.Label == "SHOWN AS");
        Assert.Contains(subject.Facts, f => f.Label == "KIND" && f.Value == dyn.Kind.ToString());
    }

    [AvaloniaFact]
    public async Task A_recipe_and_an_ingredient_list_and_open_their_variants_too()
    {
        string book = Demo();
        string dir = Dir();
        using var loaded = CookBookArchive.Read(book);
        var recipe = loaded.Recipes[0];
        string rcp = Path.Combine(dir, "r" + Archives.RecipeExtension);
        string igt = Path.Combine(dir, "i" + Archives.IngredientExtension);
        RecipeArchive.Write(rcp, recipe.Manifest, recipe.Ingredients);
        var ing = recipe.Ingredients[0];
        IngredientArchive.Write(igt, ing.Manifest, ing.VariantImages, DepthMaps.None);

        using var fromRecipe = GalleryBuilder.ForRecipe(rcp, new ThumbnailGate(1));
        using var fromIngredient = GalleryBuilder.ForIngredient(igt, new ThumbnailGate(1));

        Assert.Equal(recipe.Ingredients.Sum(i => i.Manifest.Variants.Count), fromRecipe.Items.Count);
        Assert.Equal(ing.Manifest.Variants.Count, fromIngredient.Items.Count);
        Assert.Equal(0, fromRecipe.Gate.Decodes + fromIngredient.Gate.Decodes);
        using var a = await fromRecipe.Items[^1].Open();
        using var b = await fromIngredient.Items[0].Open();
        Assert.NotNull(a.Art);
        Assert.Equal(ing.VariantImages[ing.Manifest.Variants[0].Id].Width, b.Art!.Width);
        Assert.NotNull(fromIngredient.Items[0].DecodeNow());
    }

    [AvaloniaFact]
    public async Task A_missing_tile_image_does_not_fail_the_gallery()
    {
        var gate = new ThumbnailGate(1);
        var good = new GalleryItem("ok", () => Png(), () => Task.FromResult<ViewerSubject>(null!), gate);
        var gone = new GalleryItem("gone", () => throw new FileNotFoundException(), () => Task.FromResult<ViewerSubject>(null!), gate);
        using var gallery = new ArchiveGalleryViewModel("g", [], [good, gone], gate);

        gone.DecodeNow();
        good.DecodeNow();
        await Task.CompletedTask;

        Assert.True(gone.ImageMissing);
        Assert.False(gone.IsLoading);        // missing is a state of its own, not "still coming"
        Assert.False(good.ImageMissing);
        Assert.Equal(2, gallery.Items.Count);
    }

    private static byte[] Png()
    {
        using var img = new Image<Rgba32>(4, 4, new Rgba32(9, 9, 9, 255));
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [AvaloniaFact]
    public async Task Clicking_a_tile_hands_its_subject_to_the_host()
    {
        var host = new Host();
        var gate = new ThumbnailGate(1);
        var tile = new GalleryItem("t", null, () => Task.FromResult<ViewerSubject>(
            new SnapshotSubject("t", [], null, null)), gate);
        using var gallery = new ArchiveGalleryViewModel("g", [], [tile], gate) { Host = host };

        await gallery.OpenTile(tile);

        Assert.Single(host.Opened);
        host.Opened[0].Dispose();
    }

    // ------------------------------------------------------------------ set

    [AvaloniaFact]
    public void A_set_lists_its_assets_in_set_numbering_past_9999()
    {
        // The stems "9999" and "10000" sort the other way round as text, so a list that trusted the
        // file name would put the last ten thousand in front.
        var manifest = new SetManifest("Big", 2, "s", null, "t", [], []);
        var items = new[] { 10000, 9999, 2 }.Select(n => new SetItem(n, $"images/{SetLayout.Stem(n)}.png",
            "dna", "r", [], [])).ToList();
        var set = new LoadedSet { Manifest = manifest, Items = items };

        using var gallery = GalleryBuilder.ForSet(set, new ThumbnailGate(1));

        Assert.Equal(new[] { "#0002", "#9999", "#10000" }, gallery.Items.Select(i => i.Caption));
    }

    [AvaloniaFact]
    public async Task A_set_folder_opens_a_gallery_and_a_tile_opens_that_asset()
    {
        var dialogs = new Scripted();
        using var subject = await ViewerOpener.OpenAsync(CookedSet(5), dialogs);

        var gallery = Assert.IsType<GallerySubject>(subject).Gallery;
        Assert.Equal(5, gallery.Items.Count);
        using var asset = await gallery.Items[1].Open();
        Assert.True(asset.AllowExport);
        Assert.Contains(asset.Facts, f => f.Label == "ASSET" && f.Value == "#0002");
        Assert.NotNull(asset.Art);
    }

    // ------------------------------------------------------------------ sealed

    [AvaloniaFact]
    public async Task A_sealed_set_asks_for_the_passphrase_and_re_asks_when_wrong()
    {
        string tin = Sealed(CookedSet(3), "correct-horse-battery");
        var dialogs = new Scripted("wrong-wrong-wrong", "correct-horse-battery");

        using var subject = await ViewerOpener.OpenAsync(tin, dialogs);

        Assert.IsType<GallerySubject>(subject);
        Assert.Equal(2, dialogs.PromptsShown);
        Assert.Equal("", dialogs.PromptErrors[0]);                 // first ask is clean
        Assert.NotEqual("", dialogs.PromptErrors[1]);              // the second says why it asks again
        Assert.Empty(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Canceling_the_passphrase_opens_nothing()
    {
        string tin = Sealed(CookedSet(3), "correct-horse-battery");
        var dialogs = new Scripted((string?)null);

        var subject = await ViewerOpener.OpenAsync(tin, dialogs);

        Assert.Null(subject);
        Assert.Empty(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task Every_tile_of_a_sealed_set_is_view_only()
    {
        string tin = Sealed(CookedSet(3), "correct-horse-battery");
        using var subject = await ViewerOpener.OpenAsync(tin, new Scripted("correct-horse-battery"));
        var gallery = Assert.IsType<GallerySubject>(subject).Gallery;

        Assert.False(subject!.AllowExport);
        Assert.Equal(3, gallery.Items.Count);
        foreach (var tile in gallery.Items)
        {
            using var asset = await tile.Open();
            Assert.False(asset.AllowExport);
        }
    }

    // ------------------------------------------------------------------ menu / kitchen

    private static string Menu(MenuArtMode mode)
    {
        string book = Demo();
        string mnu = Path.Combine(Dir(), "demo.mnu");
        using var loaded = CookBookArchive.Read(book);
        MenuWriter.Write(loaded, mnu, new MenuWriteOptions { Art = mode });
        return mnu;
    }

    [AvaloniaTheory]
    [InlineData(MenuArtMode.Atlas)]
    [InlineData(MenuArtMode.Loose)]
    public async Task A_menu_tile_shows_its_cropped_cell(MenuArtMode mode)
    {
        string mnu = Menu(mode);
        using var gallery = GalleryBuilder.ForMenu(mnu, new ThumbnailGate(1));
        using var menu = Nfty.Ticket.MenuArchive.Open(mnu);

        Assert.Equal(menu.Book.Art.Cells.Count, gallery.Items.Count);
        var cell = menu.Book.Art.Cells[0];
        using var subject = await gallery.Items[0].Open();

        Assert.Equal(cell.Width, subject.Art!.Width);              // the cell's rect, not the atlas
        Assert.Equal(cell.Height, subject.Art.Height);
        if (mode == MenuArtMode.Atlas)
        {
            using var atlas = Image.Load<Rgba32>(menu.ArtBytes(cell.Key));
            Assert.True(atlas.Width > cell.Width || atlas.Height > cell.Height);
            Assert.Equal(atlas[cell.X + 1, cell.Y + 1], subject.Art[1, 1]);
        }
        Assert.NotNull(gallery.Items[0].DecodeNow());
    }

    [AvaloniaFact]
    public async Task A_kitchen_tile_opens_that_archives_gallery()
    {
        string kitchenDir = Dir();
        Kitchen.Create(Path.Combine(kitchenDir, "studio.ktn"), new KitchenManifest("k", "Studio"));
        string book = DemoCookBook.WriteTo(kitchenDir);
        var dialogs = new Scripted();

        using var subject = await ViewerOpener.OpenAsync(Path.Combine(kitchenDir, "studio.ktn"), dialogs);

        var gallery = Assert.IsType<GallerySubject>(subject).Gallery;
        var tile = Assert.Single(gallery.Items);
        Assert.Equal(Path.GetFileName(book), tile.Caption);
        Assert.False(tile.IsLoading);                              // no picture to wait for
        Assert.False(tile.ImageMissing);
        using var inner = await tile.Open();
        Assert.IsType<GallerySubject>(inner);
    }

    // ------------------------------------------------------------------ chunking

    [AvaloniaFact]
    public void Gallery_rows_are_reused_while_the_row_length_is_unchanged()
    {
        var gate = new ThumbnailGate(1);
        IReadOnlyList<GalleryItem> items = Enumerable.Range(0, 30)
            .Select(i => new GalleryItem("t" + i, null, () => Task.FromResult<ViewerSubject>(null!), gate)).ToList();
        var conv = GalleryRowChunkConverter.Instance;
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        var a = (IReadOnlyList<GalleryRow>)conv.Convert([items, 600.0], typeof(object), null, culture)!;
        var b = (IReadOnlyList<GalleryRow>)conv.Convert([items, 610.0], typeof(object), null, culture)!;
        var c = (IReadOnlyList<GalleryRow>)conv.Convert([items, 300.0], typeof(object), null, culture)!;

        Assert.Same(a, b);
        Assert.NotSame(a, c);
        Assert.Equal(30, a.Sum(r => r.Tiles.Count));
        Assert.True(c.Count > a.Count);
    }
}
