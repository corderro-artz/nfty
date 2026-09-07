using System.IO.Compression;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Publishing a cooked Set: what leaves the machine, and in what shape.
/// </summary>
public class SetExporterTests
{
    private const string Pass = "correct-horse-battery-staple";

    /// <summary>Eight variants on one Custom layer, so the unique space comfortably exceeds every
    /// asset count asked for below - a two-variant book allows exactly two unique DNA and the
    /// generator, correctly, refuses to mint a third.</summary>
    private static LoadedCookBook Book()
    {
        var ids = new[] { "a", "b", "c", "d", "e", "f", "g", "h" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v,
                v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        return new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "d", "CHST"),
                new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" },
                        Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { ing },
                },
            },
        };
    }

    /// <summary>A real cooked Set on disk, in a folder named for the collection.</summary>
    private static string CookedSet(int count = 2)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-src-").FullName, "ChestDemo");
        using var book = Book();
        using var set = Generator.Generate(book, new GenerateOptions(count, "launch"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    /// <summary>A real .cbk on disk, so the "include the book" path copies actual archive bytes.</summary>
    private static string CookBookFile()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-book-").FullName, "ChestDemo.cbk");
        using var book = Book();
        CookBookArchive.Write(path, book.Manifest, book.Recipes);
        return path;
    }

    private static string OutDir() => Directory.CreateTempSubdirectory("nfty-out-").FullName;

    private static List<string> EntriesOf(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    private static List<string> FilesUnder(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dir, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ the content matrix

    [Fact]
    public void The_marketplace_preset_leaves_out_the_file_that_says_how_it_was_made()
    {
        // The whole reason this preset exists. nfty/NNNN.json carries the seed and the color rolled
        // on every layer of every asset; a listing site has no use for it and an author has every
        // reason not to publish it. The standard fields and the art still go.
        var result = SetExporter.Export(CookedSet(), OutDir(),
            ExportOptions.For(ExportPreset.Marketplace));

        var names = EntriesOf(result.Path);
        Assert.Contains("images/0001.png", names);
        Assert.Contains("metadata/0001.json", names);
        Assert.DoesNotContain(names, n => n.StartsWith("nfty/", StringComparison.Ordinal));
        Assert.Contains(SetLayout.ManifestFile, names);
    }

    [Fact]
    public void Even_the_leanest_export_still_carries_the_seed_and_the_books_hash()
    {
        // Stated as a test because it is what stops the Marketplace preset being described as
        // "nothing that says how it was made", which an early draft of its own summary claimed.
        // set.json is not one of the axes, and it holds both - a seed is inert without the book,
        // but it is there, and a preset that promised otherwise would be lying to a seller.
        var result = SetExporter.Export(CookedSet(), OutDir(),
            ExportOptions.For(ExportPreset.Marketplace));

        using var reopened = SetReader.Read(result.Path);
        Assert.Equal("launch", reopened.Manifest.Seed);
        Assert.DoesNotContain("Nothing", ExportOptions.SummaryOf(ExportPreset.Marketplace));
    }

    [Fact]
    public void The_set_manifest_ships_even_when_everything_optional_is_dropped()
    {
        // set.json is not one of the axes. It is what makes the result a Set rather than a folder of
        // pictures, it is what `inspect` reads, and it carries the collection-wide rarity table - so
        // even the leanest export still answers "what is this, and how rare is what".
        var result = SetExporter.Export(CookedSet(), OutDir(), new ExportOptions
        {
            Images = false,
            OpenSeaMetadata = false,
            NftyMetadata = false,
        });

        Assert.Equal(new[] { SetLayout.ManifestFile }, EntriesOf(result.Path));
    }

    [Theory]
    [InlineData(true, true, true, 3)]
    [InlineData(true, true, false, 2)]
    [InlineData(true, false, false, 1)]
    [InlineData(false, false, true, 1)]
    public void Each_content_axis_moves_only_its_own_directory(
        bool images, bool openSea, bool nfty, int expectedDirectories)
    {
        // The axes are independent, so the test is too - checking a preset would prove the presets
        // are wired up, not that the axes are separable.
        var plan = SetExporter.Plan(CookedSet(), new ExportOptions
        {
            Images = images,
            OpenSeaMetadata = openSea,
            NftyMetadata = nfty,
        });

        var dirs = plan.Entries.Select(e => e.Name.Split('/')[0]).Where(d => d != SetLayout.ManifestFile)
            .Distinct().ToList();
        Assert.Equal(expectedDirectories, dirs.Count);
        Assert.Equal(images, dirs.Contains(SetLayout.ImagesDir));
        Assert.Equal(openSea, dirs.Contains(SetLayout.MetadataDir));
        Assert.Equal(nfty, dirs.Contains(SetLayout.NftyDir));
    }

    // ------------------------------------------------------------------ the source book

    [Fact]
    public void The_cookbook_ships_only_when_it_is_asked_for()
    {
        var set = CookedSet();
        var book = CookBookFile();

        Assert.False(SetExporter.Plan(set, ExportOptions.For(ExportPreset.AssetPack)).CarriesCookBook);

        var full = SetExporter.Export(set, OutDir(),
            ExportOptions.For(ExportPreset.FullProject) with { Shape = ExportShape.Archive }, book);
        Assert.True(full.Plan.CarriesCookBook);
        Assert.Contains("ChestDemo.cbk", EntriesOf(full.Path));
    }

    [Fact]
    public void Asking_for_the_cookbook_without_saying_which_one_is_refused()
    {
        // A Set records its book's HASH, never the book, so nothing here can go and find it. Silently
        // shipping no book would be the worst outcome: the export would look complete and the
        // collaborator it was addressed to could not open it.
        var ex = Assert.Throws<ArgumentException>(() =>
            SetExporter.Plan(CookedSet(), ExportOptions.For(ExportPreset.FullProject)));

        Assert.Contains("no .cbk was given", ex.Message);
    }

    [Fact]
    public void Something_that_is_not_a_cookbook_is_refused_before_anything_is_written()
    {
        var set = CookedSet();
        var notABook = Path.Combine(OutDir(), "notes.txt");
        File.WriteAllText(notABook, "hello");
        var options = ExportOptions.For(ExportPreset.FullProject);

        Assert.Throws<ArgumentException>(() => SetExporter.Plan(set, options, notABook));
        Assert.Throws<ArgumentException>(() =>
            SetExporter.Plan(set, options, Path.Combine(OutDir(), "missing.cbk")));
    }

    // ------------------------------------------------------------------ shape

    [Fact]
    public void The_export_is_named_after_the_collection_not_the_folder_it_sat_in()
    {
        // A working folder is called whatever the author typed into a picker, and the recipient sees
        // that rather than the collection. Worse, a Set opened from a packed .set is read out of a
        // TEMPORARY directory - so exporting an already-open Set would have produced something like
        // "nfty-set-a3f9c1.set".
        var packed = SetExporter.Export(CookedSet(), OutDir(),
            ExportOptions.For(ExportPreset.AssetPack)).Path;

        var again = SetExporter.Export(packed, OutDir(), ExportOptions.For(ExportPreset.AssetPack));

        Assert.Equal("Chest Demo" + Archives.SetExtension, Path.GetFileName(again.Path));
    }

    [Fact]
    public void A_folder_export_lands_as_a_folder_and_an_archive_as_one_file()
    {
        var set = CookedSet();

        var folder = SetExporter.Export(set, OutDir(),
            ExportOptions.For(ExportPreset.AssetPack) with { Shape = ExportShape.Folder });
        Assert.True(Directory.Exists(folder.Path));
        Assert.EndsWith("Chest Demo", folder.Path);
        Assert.Contains(SetLayout.ManifestFile, FilesUnder(folder.Path));

        var archive = SetExporter.Export(set, OutDir(), ExportOptions.For(ExportPreset.AssetPack));
        Assert.True(File.Exists(archive.Path));
        Assert.EndsWith("Chest Demo" + Archives.SetExtension, archive.Path);
    }

    [Fact]
    public void An_exported_archive_is_still_a_readable_set()
    {
        // The point of exporting at all: the thing that comes out is a Set the recipient's nfty
        // opens, not a zip of loose files that happens to contain one.
        var archive = SetExporter.Export(CookedSet(3), OutDir(),
            ExportOptions.For(ExportPreset.AssetPack)).Path;

        using var reopened = SetReader.Read(archive);
        Assert.Equal(3, reopened.Manifest.Count);
        Assert.Equal(3, reopened.Items.Count);
    }

    [Fact]
    public void A_packed_set_can_itself_be_the_source()
    {
        // Somebody hands you a .set; you re-cut it for a marketplace. It has no folder to read.
        var packed = SetExporter.Export(CookedSet(), OutDir(),
            ExportOptions.For(ExportPreset.AssetPack)).Path;

        var result = SetExporter.Export(packed, OutDir(),
            ExportOptions.For(ExportPreset.Marketplace));

        Assert.DoesNotContain(EntriesOf(result.Path),
            n => n.StartsWith("nfty/", StringComparison.Ordinal));
    }

    [Fact]
    public void Exporting_a_folder_onto_itself_is_refused_rather_than_emptying_it()
    {
        // File.Copy source-to-source would delete the Set one file at a time, which is the worst
        // possible outcome of pressing Export. The collision is reachable because an export is named
        // after the COLLECTION: a folder-shape export into the parent of a folder already called
        // "Chest Demo" resolves to that same folder. Set up explicitly rather than relying on the
        // fixture's folder name, which is deliberately not the collection's.
        var parent = OutDir();
        var set = Path.Combine(parent, "Chest Demo");
        Directory.CreateDirectory(set);
        using (var book = Book())
        using (var generated = Generator.Generate(book, new GenerateOptions(2, "launch")))
            SetWriter.Write(generated, set, pack: false);

        var ex = Assert.Throws<ArgumentException>(() => SetExporter.Export(set, parent,
            ExportOptions.For(ExportPreset.AssetPack) with { Shape = ExportShape.Folder }));

        Assert.Contains("the Set being exported", ex.Message);
        Assert.Contains(SetLayout.ManifestFile, FilesUnder(set));   // still all there
    }

    // ------------------------------------------------------------------ sealing

    [Fact]
    public void A_sealed_export_opens_with_the_passphrase_and_holds_the_whole_set()
    {
        var result = SetExporter.Export(CookedSet(3), OutDir(),
            ExportOptions.For(ExportPreset.SealedCritique) with { Note = "Draft for review" },
            passphrase: Pass);

        Assert.EndsWith("Chest Demo" + Archives.SealedExtension, result.Path);

        using var opened = SealedSetReader.Open(result.Path, Pass);
        Assert.Equal(3, opened.Manifest.Count);
        Assert.Equal(3, opened.Items.Count);
        Assert.Equal("Draft for review", opened.Header.Note);
        Assert.False(opened.Header.Policy.AllowExport);
    }

    [Fact]
    public void A_sealed_export_says_what_it_holds_without_the_passphrase()
    {
        var result = SetExporter.Export(CookedSet(3), OutDir(),
            ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass);

        var header = Seal.Peek(result.Path);
        Assert.Equal("Chest Demo", header.Collection);
        Assert.Equal(3, header.Count);
    }

    [Fact]
    public void The_decrypted_copy_is_not_left_lying_around_after_the_set_is_closed()
    {
        // Opening a seal writes the plaintext .set to a temp directory and unpacks it there. The
        // packed copy is deleted the instant it is unpacked, and the whole directory goes on
        // Dispose - otherwise the export the seal exists to withhold sits on disk under a
        // predictable name for as long as the Set is open, and after it is not.
        var result = SetExporter.Export(CookedSet(), OutDir(),
            ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass);

        string temp;
        using (var opened = SealedSetReader.Open(result.Path, Pass))
        {
            temp = Path.GetDirectoryName(opened.Items[0].ImagePath)!;
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(temp)!, "*.set"));
        }

        Assert.False(Directory.Exists(temp));
    }

    [Fact]
    public void Re_exporting_a_sealed_file_is_refused()
    {
        // Where the view-only policy actually lives. In a front-end it would be advisory - the CLI
        // could launder a sealed Set back into an open one in a single command.
        var tin = SetExporter.Export(CookedSet(), OutDir(),
            ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass).Path;

        var ex = Assert.Throws<SealedSetException>(() =>
            SetExporter.Export(tin, OutDir(), ExportOptions.For(ExportPreset.AssetPack)));

        Assert.Contains("what the seal declines", ex.Message);
    }

    [Fact]
    public void Sealing_a_folder_is_refused_rather_than_quietly_made_an_archive()
    {
        // A sealed folder would be a folder of unreadable files. Refused instead of corrected,
        // because silently shipping a different shape than the one chosen is its own defect - and a
        // front-end that disables the radio is not a reason for Core to guess.
        var ex = Assert.Throws<ArgumentException>(() => SetExporter.Plan(CookedSet(),
            new ExportOptions { Sealed = true, Shape = ExportShape.Folder }));

        Assert.Contains("single file", ex.Message);
    }

    [Fact]
    public void Sealing_without_a_usable_passphrase_is_refused_before_anything_is_written()
    {
        var set = CookedSet();
        var outDir = OutDir();
        var options = ExportOptions.For(ExportPreset.SealedCritique);

        Assert.Throws<ArgumentException>(() => SetExporter.Export(set, outDir, options));
        Assert.Throws<ArgumentException>(() => SetExporter.Export(set, outDir, options, passphrase: "short"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
    }

    // ------------------------------------------------------------------ the plan

    [Fact]
    public void The_plan_names_exactly_the_files_the_export_writes()
    {
        // The plan is what a front-end shows the user before they press Export, so the two agreeing
        // is the whole point of it existing. A summary assembled separately from the same checkboxes
        // is how a screen comes to promise one thing and ship another.
        var set = CookedSet(4);
        var options = ExportOptions.For(ExportPreset.AssetPack);

        var plan = SetExporter.Plan(set, options);
        var result = SetExporter.Export(set, OutDir(), options);

        Assert.Equal(plan.Entries.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal),
            EntriesOf(result.Path).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new FileInfo(plan.Entries[0].Source).Length, plan.Entries[0].Bytes);
        Assert.True(plan.Bytes > 0);
        Assert.Equal("Chest Demo", plan.Collection);
        Assert.Equal(4, plan.Count);
    }

    [Fact]
    public void The_plan_writes_nothing()
    {
        var outDir = OutDir();
        SetExporter.Plan(CookedSet(), ExportOptions.For(ExportPreset.AssetPack));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
    }

    [Fact]
    public void Options_report_which_preset_they_are_and_stop_when_they_stop_being_it()
    {
        // Derived from the content rather than remembered from a click, so a screen cannot go on
        // highlighting "Marketplace" after a box below it was ticked.
        foreach (ExportPreset p in Enum.GetValues<ExportPreset>())
            Assert.Equal(p, ExportOptions.For(p).MatchingPreset());

        Assert.Null((ExportOptions.For(ExportPreset.Marketplace) with { Images = false })
            .MatchingPreset());

        // The note is a message to a person, not part of the export's shape.
        Assert.Equal(ExportPreset.SealedCritique,
            (ExportOptions.For(ExportPreset.SealedCritique) with { Note = "hi" }).MatchingPreset());
    }

    [Fact]
    public void Every_preset_has_a_title_and_a_sentence_saying_who_it_is_for()
    {
        // Stated in Core so the CLI's help and the GUI's tiles cannot describe the same choice
        // differently - the reason every report in Stats/ is rendered here too.
        foreach (ExportPreset p in Enum.GetValues<ExportPreset>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ExportOptions.TitleOf(p)));
            Assert.False(string.IsNullOrWhiteSpace(ExportOptions.SummaryOf(p)));
        }
    }

    [Fact]
    public void A_folder_that_is_not_a_set_is_reported_as_that_rather_than_exported_empty()
    {
        var ex = Assert.Throws<CorruptSetException>(() =>
            SetExporter.Plan(OutDir(), ExportOptions.For(ExportPreset.AssetPack)));

        Assert.Contains(SetLayout.ManifestFile, ex.Message);
    }
}
