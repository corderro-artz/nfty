using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class SetRiseSourceTests
{
    private const string Pass = "correct-horse-battery-staple";

    private static DepthMap FlatDepth(byte front) =>
        DepthMap.FromBytes(8, 8, Enumerable.Range(0, 64).SelectMany(_ => new[] { front, (byte)0 }).ToArray());

    /// <summary>A Chest book on disk with oak at depth 2, six assets cooked into a folder beside it.</summary>
    private static string CookedBesideBook(out string bookPath)
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(FlatDepth(2)));
        bookPath = path;
        var setDir = Path.Combine(Path.GetDirectoryName(path)!, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(20, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        return setDir;
    }

    private static RiseItem WithBody(SetRiseSource src, LoadedSet set, string body) =>
        src.Items.First(i =>
            set.Items.Single(x => x.Number == int.Parse(i.Id)).Rarity.Any(r => r.Trait_type == "Body" && r.Value == body));

    [AvaloniaFact]
    public void An_open_set_says_which_asset_it_is_viewing_and_that_it_is_read_only()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        var chosen = set.Items[3].Number;
        using var src = new SetRiseSource(set, chosen, () => Array.Empty<string>(), new ImageBridge());
        Assert.Equal($"Viewing #{chosen:D4} of a cooked Set. Read-only.", src.OpeningLine);
        src.InitialItem.IsSelected = false;
        src.Items.Single(i => int.Parse(i.Id) == set.Items[0].Number).IsSelected = true;
        Assert.Equal($"Viewing #{set.Items[0].Number:D4} of a cooked Set. Read-only.", src.OpeningLine);
    }

    [AvaloniaFact]
    public async Task Export_readiness_is_published_even_when_publishing_the_book_throws()
    {
        // An export waits for ExportReady behind a busy card nothing but the job can close: if the
        // arrival threw before saying so, every later export would wait forever.
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        src.Changed += () => throw new InvalidOperationException("a listener failed");
        await src.BookSearch;
        Assert.ThrowsAny<Exception>(() => Dispatcher.UIThread.RunJobs());
        Assert.True(src.ExportReady.IsCompleted);
    }

    [AvaloniaFact]
    public async Task A_found_book_gives_each_asset_its_combined_depth()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());

        Assert.StartsWith("Looking for", src.DepthNote);
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Chest.cbk", src.BookName);
        Assert.StartsWith("Depth comes from Chest.cbk", src.DepthNote);
        var depth = src.Open(WithBody(src, set, "Oak")).Depth;
        Assert.Equal(new DepthPixel(2, 0), depth.Get(1, 2));    // oak's own depth
        Assert.Equal(new DepthPixel(0, 0), depth.Get(3, 3));    // gold paints over it and carries none
    }

    [AvaloniaFact]
    public async Task A_book_arriving_after_an_asset_is_loaded_reseeds_it()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        var oak = WithBody(src, set, "Oak");
        var subject = src.Open(oak);               // opened while the search is still out: paper
        Assert.Equal("paper", oak.DepthChip);
        int raised = 0;
        src.Changed += () => raised++;

        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(subject, oak.Subject);
        Assert.Equal(new DepthPixel(2, 0), subject.Depth.Get(1, 2));
        Assert.False(subject.IsDirty);
        Assert.Equal("varied", oak.DepthChip);
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public async Task No_book_means_paper_and_the_note_says_so()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(FlatDepth(2)));
        var elsewhere = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-set-").FullName, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(3, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, elsewhere, pack: false);
        s.Dispose();
        using var set = SetReader.Read(elsewhere);
        using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());

        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(src.BookName);
        Assert.Contains("was not found, so every asset is paper", src.DepthNote);
        Assert.True(src.Open(src.Items[0]).Depth.SameDepthAs(null));
    }

    [AvaloniaFact]
    public async Task A_source_disposed_before_the_search_lands_drops_the_book()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        LoadedCookBook? read = null;
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge(),
            p => read = CookBookArchive.Read(p));
        int raised = 0;
        src.Changed += () => raised++;

        src.Dispose();
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(src.BookName);
        Assert.Equal(0, raised);
        // The late book is given back, not leaked: its decoded images are disposed.
        Assert.NotNull(read);
        var image = read!.Recipes[0].Ingredients[0].VariantImages.Values.First();
        Assert.Throws<ObjectDisposedException>(() => _ = image[0, 0]);
    }

    [AvaloniaFact]
    public async Task A_book_that_reads_as_corrupt_is_not_found_and_never_faults_the_search()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge(),
            _ => throw new System.Text.Json.JsonException("bad manifest"));

        await src.BookSearch;     // would throw here if the search faulted
        Dispatcher.UIThread.RunJobs();

        Assert.Null(src.BookName);
        Assert.Contains("was not found", src.DepthNote);
    }

    [AvaloniaFact]
    public void Constructing_a_source_decodes_no_thumbnail_until_one_is_read()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, null, new ImageBridge());

        PumpFor(150);
        Assert.All(src.Items, i => Assert.False(src.RowOf(i).IsThumbnailDecoded));

        _ = src.Items[3].Thumbnail;                 // a view realizing one tile
        PumpUntilDecoded(() => src.RowOf(src.Items[3]).IsThumbnailDecoded);

        Assert.True(src.RowOf(src.Items[3]).IsThumbnailDecoded);
        Assert.Equal(1, src.Items.Count(i => src.RowOf(i).IsThumbnailDecoded));
    }

    [AvaloniaFact]
    public void A_landed_thumbnail_is_announced_so_a_bound_tile_repaints()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, null, new ImageBridge());
        var item = src.Items[2];
        int told = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RiseItem.Thumbnail)) told++; };

        _ = item.Thumbnail;
        PumpUntilDecoded(() => src.RowOf(item).IsThumbnailDecoded);

        Assert.True(told >= 1);
        Assert.NotNull(item.Thumbnail);
        Assert.Same(src.RowOf(item).Thumbnail, item.Thumbnail);
    }

    private static void PumpFor(int ms) => PumpUntilDecoded(() => false, ms);

    private static void PumpUntilDecoded(Func<bool> done, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && sw.ElapsedMilliseconds < timeoutMs)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task The_unmatched_note_follows_the_selected_asset_and_selection_is_announced()
    {
        var setDir = CookedBesideBook(out _);
        using var real = SetReader.Read(setDir);
        var odd = real.Items[0] with
        {
            Rarity = real.Items[0].Rarity.Select(r => r.Trait_type == "Body" ? r with { Value = "Mahogany" } : r).ToList(),
        };
        using var set = new LoadedSet
        {
            Manifest = real.Manifest, SourceDirectory = real.SourceDirectory,
            Items = new[] { odd }.Concat(real.Items.Skip(1)).ToList(),
        };
        using var src = new SetRiseSource(set, odd.Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        src.Open(src.Items[0]);
        Assert.Contains("could not be matched", src.DepthNote);
        int raised = 0;
        src.Changed += () => raised++;

        src.Items[0].IsSelected = false;
        src.Items[1].IsSelected = true;           // selection moves; nothing is opened

        Assert.DoesNotContain("could not be matched", src.DepthNote);
        Assert.True(raised >= 1);
    }

    [AvaloniaFact]
    public async Task An_asset_with_a_partial_alpha_layer_says_which_layer_in_the_note()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        var ghost = set.Items.First(i => i.Rarity.Any(r => r.Trait_type == "Body" && r.Value == "Ghost"));
        using var src = new SetRiseSource(set, ghost.Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        src.Open(src.InitialItem);

        Assert.Contains("cannot be risen", src.DepthNote);
        Assert.Contains("partly transparent", src.DepthNote);
        Assert.Contains("so it is paper", src.DepthNote);
    }

    [AvaloniaFact]
    public async Task An_all_absent_roll_rises_as_nothing_and_never_throws()
    {
        var setDir = CookedBesideBook(out _);
        using var real = SetReader.Read(setDir);
        var bare = real.Items[0] with
        {
            ImagePath = Path.Combine(setDir, "gone.png"),
            Rarity = real.Items[0].Rarity.Where(r => r.Trait_type == "Type").ToList(),
            AbsentLayers = new[] { "Body", "Lock" },
        };
        using var set = new LoadedSet
        {
            Manifest = real.Manifest, SourceDirectory = real.SourceDirectory,
            Items = new[] { bare }.Concat(real.Items.Skip(1)).ToList(),
        };
        using var src = new SetRiseSource(set, bare.Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        var subject = src.Open(src.InitialItem);

        Assert.False(subject.IsRisable);
        Assert.Equal("empty", src.InitialItem.DepthChip);
        Assert.DoesNotContain("could not be matched", src.DepthNote);
    }

    [AvaloniaFact]
    public async Task An_asset_whose_layers_match_two_variants_by_name_is_paper_and_the_note_says_so()
    {
        var setDir = CookedBesideBook(out _);
        using var real = SetReader.Read(setDir);
        var odd = real.Items[0] with
        {
            Rarity = real.Items[0].Rarity.Select(r => r.Trait_type == "Body" ? r with { Value = "Mahogany" } : r).ToList(),
        };
        using var set = new LoadedSet
        {
            Manifest = real.Manifest, SourceDirectory = real.SourceDirectory,
            Items = new[] { odd }.Concat(real.Items.Skip(1)).ToList(),
        };
        using var src = new SetRiseSource(set, odd.Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        var subject = src.Open(src.InitialItem);

        Assert.True(subject.Depth.SameDepthAs(null));
        Assert.Contains("could not be matched to that book by name, so it is paper", src.DepthNote);
    }

    private static SealedSet OpenSealed()
    {
        var ids = new[] { "a", "b", "c", "d" };
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
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-seal-").FullName, "Chest Demo");
        using (var cooked = Generator.Generate(book, new GenerateOptions(3, "launch")))
            SetWriter.Write(cooked, dir, pack: false);
        var outDir = Directory.CreateTempSubdirectory("nfty-tin-").FullName;
        var result = SetExporter.Export(dir, outDir, ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass);
        return SealedSetReader.Open(result.Path, Pass);
    }

    [AvaloniaFact]
    public void A_sealed_set_is_viewable_and_refuses_export()
    {
        using var set = OpenSealed();
        using var src = new SetRiseSource(set, set.Items[0].Number, null, new ImageBridge());

        Assert.True(src.IsSealed);
        Assert.Equal($"Viewing #{set.Items[0].Number:D4} of a cooked Set. Read-only. Sealed: it can be viewed here, not exported.",
            src.OpeningLine);
        Assert.NotNull(src.ExportRefusal);
        var art = src.Open(src.InitialItem).Art;
        using var onDisk = Image.Load<Rgba32>(set.Items[0].ImagePath);
        Assert.Equal(onDisk[0, 0], art[0, 0]);
        // Refused by the source itself, not only where a button is enabled.
        Assert.Throws<InvalidOperationException>(() => src.SnapshotExport(src.Items));
    }

    [AvaloniaFact]
    public async Task Unloaded_assets_are_decoded_for_export_and_not_kept()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();
        var picked = new[] { src.Items[1], src.Items[2] };

        var seen = new List<string>();
        using (var snapshot = src.SnapshotExport(picked))
        {
            for (int i = 0; i < snapshot.Count; i++)
            {
                var input = snapshot.Open(i, i == 0 ? "one" : "two");
                seen.Add(input.BaseName);
                Assert.Equal(8, input.Art.Width);   // alive while it is being consumed
                RiseExportSnapshot.Release(input);
            }
        }

        Assert.Equal(new[] { "one", "two" }, seen);
        Assert.All(picked, i => Assert.Null(i.Subject));
    }

    [AvaloniaFact]
    public void The_rail_facts_pill_and_export_names_follow_the_set()
    {
        var setDir = CookedBesideBook(out _);
        using var set = SetReader.Read(setDir);
        using var src = new SetRiseSource(set, set.Items[2].Number, null, new ImageBridge());

        Assert.Equal(set.Items.Select(i => i.Number.ToString("D4")), src.Items.Select(i => i.Id));
        Assert.Equal(set.Items[2].Number.ToString("D4"), src.InitialItem.Id);
        Assert.Equal("Chest-0003", src.Items[2].ExportBaseName);
        Assert.Equal("cooked Set · read-only", src.ReadOnlyPill);
        Assert.Equal("Chest-models", src.BatchStem);
        Assert.True(src.CanBatch);
        Assert.False(src.CanSave);
        Assert.False(src.IsDirty);
        Assert.Null(src.ExportRefusal);
    }
}
