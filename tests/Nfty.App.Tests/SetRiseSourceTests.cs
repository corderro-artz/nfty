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
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(6, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        return setDir;
    }

    private static RiseItem WithBody(SetRiseSource src, LoadedSet set, string body) =>
        src.Items.First(i =>
            set.Items.Single(x => x.Number == int.Parse(i.Id)).Rarity.Any(r => r.Trait_type == "Body" && r.Value == body));

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
        var src = new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge());
        int raised = 0;
        src.Changed += () => raised++;

        src.Dispose();
        await src.BookSearch;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(src.BookName);
        Assert.Equal(0, raised);
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
        Assert.NotNull(src.ExportRefusal);
        var art = src.Open(src.InitialItem).Art;
        using var onDisk = Image.Load<Rgba32>(set.Items[0].ImagePath);
        Assert.Equal(onDisk[0, 0], art[0, 0]);
        // Refused by the source itself, not only where a button is enabled.
        Assert.Throws<InvalidOperationException>(() => src.ExportInputs(src.Items, src.Items.Select(i => i.ExportBaseName).ToList()));
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
        foreach (var input in src.ExportInputs(picked, new[] { "one", "two" }))
        {
            seen.Add(input.BaseName);
            Assert.Equal(8, input.Art.Width);   // alive while it is being consumed
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
