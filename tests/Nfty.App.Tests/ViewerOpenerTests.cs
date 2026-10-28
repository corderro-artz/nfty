using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Viewer;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Routing a path to what the viewer shows: pictures, asset metadata, and the refusals.</summary>
public class ViewerOpenerTests
{
    private static string Dir() => Directory.CreateTempSubdirectory("nfty-open-").FullName;

    /// <summary>A Set layout by hand: <c>nfty/0001.json</c>, <c>metadata/0001.json</c> and, when asked, the image.</summary>
    private static string SetRoot(bool withImage)
    {
        string root = Dir();
        Directory.CreateDirectory(Path.Combine(root, "nfty"));
        Directory.CreateDirectory(Path.Combine(root, "metadata"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllText(Path.Combine(root, "nfty", "0001.json"), JsonSerializer.Serialize(new
        {
            setNumber = 1, recipe = "chest", dna = "abc123", seed = "s",
            rarity = new[] { new { trait_type = "Lock", value = "Brass", rarityPct = 12.5 } },
            layers = System.Array.Empty<object>(),
        }));
        File.WriteAllText(Path.Combine(root, "metadata", "0001.json"), JsonSerializer.Serialize(new
        {
            name = "Chest #1", description = "d", image = "ipfs://x/0001.png",
            attributes = new[] { new { trait_type = "Lock", value = "Brass" } },
        }));
        if (withImage)
        {
            using var img = new Image<Rgba32>(6, 5, new Rgba32(1, 2, 3, 255));
            img.SaveAsPng(Path.Combine(root, "images", "0001.png"));
        }
        return root;
    }

    [AvaloniaTheory]
    [InlineData("nfty")]
    [InlineData("metadata")]
    public async Task A_metadata_json_opens_its_image_and_traits(string folder)
    {
        string root = SetRoot(withImage: true);
        var dialogs = new ArchiveGalleryTests.Scripted();

        using var subject = await ViewerOpener.OpenAsync(Path.Combine(root, folder, "0001.json"), dialogs);

        Assert.NotNull(subject);
        Assert.False(subject!.ImageMissing);
        Assert.Equal(6, subject.Art!.Width);                       // resolved through images/ beside it
        Assert.Contains(subject.Facts, f => f.Label == "LOCK" && f.Value.StartsWith("Brass"));
        Assert.Empty(dialogs.Errors);
    }

    [AvaloniaFact]
    public async Task A_metadata_json_whose_image_is_gone_shows_the_missing_mark()
    {
        string root = SetRoot(withImage: false);

        using var subject = await ViewerOpener.OpenAsync(Path.Combine(root, "nfty", "0001.json"),
            new ArchiveGalleryTests.Scripted());

        Assert.NotNull(subject);                                   // still opens: the traits are real
        Assert.True(subject!.ImageMissing);
        Assert.Null(subject.Art);
        Assert.Contains(subject.Facts, f => f.Label == "RECIPE");
    }

    [AvaloniaFact]
    public async Task Any_other_json_is_an_error_card_and_not_a_window()
    {
        string file = Path.Combine(Dir(), "package.json");
        File.WriteAllText(file, "{\"name\":\"x\",\"version\":\"1\"}");
        var dialogs = new ArchiveGalleryTests.Scripted();

        var subject = await ViewerOpener.OpenAsync(file, dialogs);

        Assert.Null(subject);
        Assert.Equal(ViewerOpener.NotMetadata, Assert.Single(dialogs.Errors));
    }

    [AvaloniaFact]
    public async Task An_unknown_extension_opens_the_error_card_and_not_a_window()
    {
        string file = Path.Combine(Dir(), "notes.txt");
        File.WriteAllText(file, "hi");
        var dialogs = new ArchiveGalleryTests.Scripted();

        var subject = await ViewerOpener.OpenAsync(file, dialogs);

        Assert.Null(subject);
        Assert.Contains("notes.txt", Assert.Single(dialogs.Errors));
    }

    [AvaloniaFact]
    public async Task A_picture_opens_as_a_single_subject()
    {
        string file = Path.Combine(Dir(), "a.png");
        using (var img = new Image<Rgba32>(3, 3)) img.SaveAsPng(file);

        using var subject = await ViewerOpener.OpenAsync(file, new ArchiveGalleryTests.Scripted());

        Assert.IsType<SnapshotSubject>(subject);
        Assert.Equal(3, subject!.Art!.Width);
    }
}
