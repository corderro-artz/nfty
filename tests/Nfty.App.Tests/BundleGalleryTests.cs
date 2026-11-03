using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A marketplace upload bundle (images/ + metadata/ + collection.json, no set.json) opens read-only
/// in the viewer's gallery, marked as a bundle; an export that kept set.json reopens as a full Set
/// whatever its names.
/// </summary>
public class BundleGalleryTests
{
    internal static string CookedSet(int count = 4)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-bun-").FullName, "Chest Demo");
        var ids = new[] { "a", "b", "c", "d", "e", "f" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v, v => new Image<Rgba32>(4, 4, new Rgba32((byte)(v[0] * 2), 80, 120, 255))),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "Chests of all kinds", "CHST"), new Dictionary<string, double> { ["chest"] = 100 }),
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

    internal static string Export(string set, ExportOptions options) =>
        SetExporter.Export(set, Directory.CreateTempSubdirectory("nfty-bun-out-").FullName,
            options with { Shape = ExportShape.Folder }).Path;

    internal static ExportOptions MarketplaceOptions() => ExportOptions.For(ExportPreset.Marketplace) with
    {
        BaseUri = "ipfs://bafyexample/", TokenNaming = TokenNaming.TokenId, RoyaltyPercent = 5,
        FeeRecipient = "0x1234567890abcdef1234567890ABCDEF12345678", ExternalLink = "https://chests.example/",
    };

    [AvaloniaFact]
    public async Task A_bundle_folder_opens_in_the_gallery_with_traits_and_collection_facts()
    {
        string bundle = Export(CookedSet(), MarketplaceOptions());
        var dialogs = new ArchiveGalleryTests.Scripted();

        using var subject = await ViewerOpener.OpenAsync(bundle, dialogs);

        var gallery = Assert.IsType<GallerySubject>(subject).Gallery;
        Assert.Empty(dialogs.Errors);
        Assert.Equal(4, gallery.Items.Count);
        Assert.Contains(gallery.Facts, f => f.Label == "BUNDLE" && f.Banner && f.Value.Contains("Upload bundle"));
        Assert.Contains(gallery.Facts, f => f.Label == "COLLECTION" && f.Value == "Chest Demo");
        Assert.Contains(gallery.Facts, f => f.Label == "SYMBOL" && f.Value == "CHST");
        Assert.Contains(gallery.Facts, f => f.Label == "ROYALTY" && f.Value == "5%");
        Assert.Contains(gallery.Facts, f => f.Label == "LINK" && f.Value == "https://chests.example/");
        Assert.Contains(gallery.Facts, f => f.Label == "IMAGES" && f.Value == "4 of 4");

        var tile = gallery.Items[1];
        Assert.Equal("#2", tile.Caption);
        var bmp = tile.DecodeNow();
        Assert.NotNull(bmp);
        Assert.False(tile.ImageMissing);

        using var opened = await tile.Open();
        Assert.False(opened.ImageMissing);
        Assert.NotNull(opened.Art);
        Assert.Contains(opened.Facts, f => f.Label == "BUNDLE");
        Assert.Contains(opened.Facts, f => f.Label == "NAME" && f.Value == "Chest Demo #2");
        Assert.Contains(opened.Facts, f => f.Label == "BACKGROUND");        // the trait, as a fact
        Assert.Contains(opened.Facts, f => f.Label == "IMAGE" && f.Value == "ipfs://bafyexample/images/2.png");
    }

    [AvaloniaFact]
    public async Task A_bundle_picture_that_is_not_in_the_bundle_shows_the_missing_mark()
    {
        string bundle = Export(CookedSet(), MarketplaceOptions());
        File.Delete(Path.Combine(bundle, "images", "3.png"));

        using var subject = await ViewerOpener.OpenAsync(bundle, new ArchiveGalleryTests.Scripted());

        var gallery = ((GallerySubject)subject!).Gallery;
        gallery.Items[2].DecodeNow();
        Assert.True(gallery.Items[2].ImageMissing);
        Assert.False(gallery.Items[1].DecodeNow() is null);
        Assert.Contains(gallery.Facts, f => f.Label == "IMAGES" && f.Value == "3 of 4");
        using var opened = await gallery.Items[2].Open();
        Assert.True(opened.ImageMissing);
    }

    [AvaloniaFact]
    public async Task A_packed_bundle_opens_the_same_way_and_a_plain_folder_still_does_not()
    {
        var options = MarketplaceOptions() with { Shape = ExportShape.Archive };
        var packed = SetExporter.Export(CookedSet(), Directory.CreateTempSubdirectory("nfty-bun-out-").FullName, options);

        using var subject = await ViewerOpener.OpenAsync(packed.Path, new ArchiveGalleryTests.Scripted());
        Assert.Equal(4, ((GallerySubject)subject!).Gallery.Items.Count);

        var dialogs = new ArchiveGalleryTests.Scripted();
        string other = Directory.CreateTempSubdirectory("nfty-plain-").FullName;
        Assert.Null(await ViewerOpener.OpenAsync(other, dialogs));
        Assert.Single(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task A_token_id_export_with_set_json_opens_as_a_full_set_with_its_pictures()
    {
        string export = Export(CookedSet(), new ExportOptions { TokenNaming = TokenNaming.TokenId, TokenStart = 0 });

        using var subject = await ViewerOpener.OpenAsync(export, new ArchiveGalleryTests.Scripted());

        var gallery = ((GallerySubject)subject!).Gallery;
        Assert.Equal(4, gallery.Items.Count);
        Assert.Contains(gallery.Facts, f => f.Label == "SEED");              // a Set, not a bundle
        Assert.DoesNotContain(gallery.Facts, f => f.Label == "BUNDLE");
        Assert.All(gallery.Items, t => { t.DecodeNow(); Assert.False(t.ImageMissing); });
    }

    [AvaloniaFact]
    public async Task A_metadata_file_of_an_export_finds_its_renamed_picture()
    {
        string export = Export(CookedSet(), new ExportOptions { TokenNaming = TokenNaming.Padded, TokenStart = 0 });

        // set.json present: padded names starting at 0, so metadata/0000.json is set number 1.
        using var subject = await ViewerOpener.OpenAsync(Path.Combine(export, "metadata", "0000.json"),
            new ArchiveGalleryTests.Scripted());
        Assert.False(subject!.ImageMissing);
        Assert.NotNull(subject.Art);

        // And a bundle (no set.json): the image field decides.
        string bundle = Export(CookedSet(), ExportOptions.For(ExportPreset.Marketplace) with { BaseUri = "ipfs://CID/" });
        using var viaField = await ViewerOpener.OpenAsync(Path.Combine(bundle, "metadata", "0002.json"),
            new ArchiveGalleryTests.Scripted());
        Assert.False(viaField!.ImageMissing);
        Assert.NotNull(viaField.Art);
    }

    private sealed class NoPicker : Nfty.App.Services.IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    [AvaloniaFact]
    public async Task Landing_opens_a_bundle_in_the_viewer_not_the_set_browser_and_remembers_it()
    {
        string bundle = Export(CookedSet(), MarketplaceOptions());
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var host = new ArchiveGalleryTests.Host();
        var recents = new Nfty.App.Services.RecentsService(Directory.CreateTempSubdirectory().FullName);
        var vm = new LandingViewModel(nav, dialogs, new NoPicker(), recents, new Nfty.App.Services.CookBookSession(),
            book => throw new InvalidOperationException(), set => throw new InvalidOperationException(),
            (_, _, _) => null!, viewer: host);

        vm.OpenRecentCommand.Execute(new Nfty.App.Models.RecentItem("Chest Demo", "x", bundle, false));
        await vm.PendingOpen!;

        Assert.Null(nav.Current);                                   // not the Set browser
        Assert.IsType<GallerySubject>(Assert.Single(host.Opened));
        Assert.Contains(vm.Recents, r => r.Path == bundle && r.Meta.StartsWith("upload bundle", StringComparison.Ordinal));
    }
}
