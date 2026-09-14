using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
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
/// The export dialog's two remaining lies: a preset that stopped being lit for the wrong reason,
/// and a "source CookBook" the screen could not find.
/// </summary>
public class ExportPresetAndBookTests
{
    /// <summary>A real cooked Set on disk, plus the <c>.cbk</c> it was cooked from — written as a
    /// real file, because the whole point is that the Set records that file's hash.</summary>
    /// <remarks>
    /// <paramref name="folder"/> also changes the book's CONTENT, and it has to. A ZIP stores entry
    /// timestamps at two-second resolution, so two books written from identical content inside the
    /// same test run hash the SAME - which made a "this is not the source book" test pass the wrong
    /// way round on its first cut.
    /// </remarks>
    private static (string SetDir, string BookPath) CookedWithItsBook(string folder = "out")
    {
        var root = Directory.CreateTempSubdirectory("nfty-src-").FullName;
        string bookPath = Path.Combine(root, "Chest Demo.cbk");

        string[] ids = folder == "out" ? ["a", "b", "c", "d"] : ["w", "x", "y"];
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v,
                v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255)), StringComparer.Ordinal),
        };
        var manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
            new Collection("Chest Demo", "d", "CHST"),
            new Dictionary<string, double> { ["chest"] = 100 });
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("chest", "Chest", new[] { "bg" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        using (var authored = new LoadedCookBook { Manifest = manifest, Recipes = new[] { recipe } })
            CookBookArchive.Write(bookPath, authored.Manifest, authored.Recipes);

        // Cooked from the FILE, so the Set records that file's hash - which is the thread the whole
        // feature pulls on.
        using var book = CookBookArchive.Read(bookPath);
        using var set = Generator.Generate(book, new GenerateOptions(3, "launch"));
        string setDir = Path.Combine(root, folder);
        SetWriter.Write(set, setDir, pack: false);
        return (setDir, bookPath);
    }

    private static ExportDialogViewModel Dialog(string setDir, string? sha = null,
        IEnumerable<string>? candidates = null) =>
        new(setDir, new FilePickerService(), new NoopFolderRevealer(), new FakeDialogs(),
            sha, candidates);

    // ---- the preset that came un-named ----------------------------------------------------------

    /// <summary>
    /// A preset names WHAT LEAVES the machine. Folder-or-file is packaging, and changing it must not
    /// un-name the preset.
    /// </summary>
    /// <remarks>
    /// Reported as: tick all four content boxes, choose Single file, and Full project goes dark with
    /// nothing lit in its place. Marketplace did the same in the other direction and nobody noticed,
    /// because its default shape is the one people leave alone.
    /// </remarks>
    [Fact]
    public void Changing_the_shape_does_not_un_name_the_preset()
    {
        var (setDir, _) = CookedWithItsBook();

        var full = ExportOptions.For(ExportPreset.FullProject);
        Assert.Equal(ExportShape.Folder, full.Shape);
        Assert.Equal(ExportPreset.FullProject, full.MatchingPreset());
        Assert.Equal(ExportPreset.FullProject,
            (full with { Shape = ExportShape.Archive }).MatchingPreset());

        var market = ExportOptions.For(ExportPreset.Marketplace);
        Assert.Equal(ExportPreset.Marketplace,
            (market with { Shape = ExportShape.Folder }).MatchingPreset());
    }

    /// <summary>And unticking a CONTENT box still does, which is what the derivation is for.</summary>
    [Fact]
    public void Unticking_a_content_box_still_un_names_the_preset()
    {
        var pack = ExportOptions.For(ExportPreset.AssetPack);
        Assert.Equal(ExportPreset.AssetPack, pack.MatchingPreset());
        Assert.Null((pack with { Images = false }).MatchingPreset());
    }

    /// <summary>The four stay distinguishable without the shape: none of them differs in shape
    /// alone.</summary>
    [Fact]
    public void Every_preset_is_still_told_apart_from_every_other()
    {
        foreach (var p in Enum.GetValues<ExportPreset>())
            Assert.Equal(p, ExportOptions.For(p).MatchingPreset());
    }

    /// <summary>On the live dialog, which is where it was seen.</summary>
    [Fact]
    public void The_full_project_tile_stays_lit_when_single_file_is_chosen()
    {
        var (setDir, book) = CookedWithItsBook();
        var vm = Dialog(setDir, candidates: new[] { book });

        vm.ApplyPresetCommand.Execute(vm.Presets.Single(t => t.Preset == ExportPreset.FullProject));
        Assert.True(vm.Presets.Single(t => t.Preset == ExportPreset.FullProject).IsSelected);

        vm.Packed = true;                                   // "Single file"
        Assert.True(vm.Presets.Single(t => t.Preset == ExportPreset.FullProject).IsSelected);
        Assert.Single(vm.Presets, t => t.IsSelected);
    }

    // ---- the shape radios actually write back ----------------------------------------------------

    /// <summary>
    /// Clicking Folder sets the shape.
    /// </summary>
    /// <remarks>
    /// The two radios are <c>IsChecked="{Binding Packed}"</c> and <c>IsChecked="{Binding !Packed}"</c>.
    /// A negated binding that does not write back would leave the second radio looking chosen while
    /// the export went on being an archive — a screen promising one thing and shipping another,
    /// which is the exact defect this dialog exists to prevent. Driven from the control rather than
    /// asserted on the property, because the property was never in doubt.
    /// </remarks>
    [AvaloniaFact]
    public void Clicking_folder_really_chooses_a_folder()
    {
        var (setDir, book) = CookedWithItsBook();
        var vm = Dialog(setDir, candidates: new[] { book });
        var view = new Views.ExportDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var radios = view.GetVisualDescendants().OfType<RadioButton>()
                .Where(r => r.GroupName == "shape").ToList();
            Assert.Equal(2, radios.Count);
            var folder = radios.Single(r => r.Content as string == "Folder");
            var single = radios.Single(r => r.Content as string == "Single file");

            Assert.True(vm.Packed);
            folder.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.Packed);
            Assert.Equal(ExportShape.Folder, vm.Options.Shape);

            single.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.Packed);
            Assert.Equal(ExportShape.Archive, vm.Options.Shape);
        }
        finally { window.Close(); }
    }

    // ---- the source CookBook ---------------------------------------------------------------------

    /// <summary>
    /// The book is FOUND rather than asked for.
    /// </summary>
    /// <remarks>
    /// "Include the source CookBook" used to mean "and now go and find the file", and leaving the
    /// box ticked without one made the whole dialog refuse: Core throws, the manifest empties and
    /// Export greys out. The Set records the book's hash, so a candidate can be checked.
    /// </remarks>
    [Fact]
    public void The_source_book_is_found_from_the_hash_the_set_recorded()
    {
        var (setDir, book) = CookedWithItsBook();
        using var read = SetReader.Read(setDir);
        var vm = Dialog(setDir, read.Manifest.CookbookSha256, new[] { book });

        Assert.Equal(book, vm.CookBookPath);
        Assert.True(vm.CookBookIsTheSource);

        vm.IncludeCookBook = true;
        Assert.Equal("", vm.Problem);
        Assert.Contains(vm.Parts, p => p.EndsWith(".cbk", StringComparison.Ordinal));
    }

    /// <summary>A book sitting beside the Set is found without anybody listing it.</summary>
    [Fact]
    public void A_book_next_to_the_set_is_found_too()
    {
        var (setDir, book) = CookedWithItsBook();
        using var read = SetReader.Read(setDir);
        var vm = Dialog(setDir, read.Manifest.CookbookSha256);   // no candidates at all

        Assert.Equal(book, vm.CookBookPath);
    }

    /// <summary>
    /// A candidate that is not the book is not accepted.
    /// </summary>
    /// <remarks>
    /// Matching on the hash is what makes looking around safe: a folder of a dozen books cannot
    /// produce a wrong answer, it produces no answer, and the picker is still there. Shipping the
    /// wrong book labelled "source CookBook" is worse than shipping none.
    /// </remarks>
    [Fact]
    public void A_book_that_is_not_the_source_is_not_picked_up()
    {
        var (setDir, _) = CookedWithItsBook();
        var (_, otherBook) = CookedWithItsBook("other");
        using var read = SetReader.Read(setDir);

        var vm = new ExportDialogViewModel(setDir, new FilePickerService(), new NoopFolderRevealer(),
            new FakeDialogs(), read.Manifest.CookbookSha256, new[] { otherBook });

        // The one beside the Set is still found; what must not happen is the WRONG one winning.
        Assert.NotEqual(otherBook, vm.CookBookPath);
    }

    /// <summary>
    /// Choosing a book that did not cook this Set is warned about, not refused.
    /// </summary>
    /// <remarks>
    /// The same rule <c>extend</c> keeps about the same comparison: deliberately shipping an edited
    /// book is a legitimate thing to want and the author is the one who knows. Believing it is the
    /// source when it is not is the thing nothing else on the screen would have said.
    /// </remarks>
    [Fact]
    public void A_book_that_is_not_the_source_is_warned_about()
    {
        var (setDir, _) = CookedWithItsBook();
        var (_, otherBook) = CookedWithItsBook("other");
        using var read = SetReader.Read(setDir);

        var vm = Dialog(setDir, read.Manifest.CookbookSha256);
        vm.CookBookPath = otherBook;
        vm.IncludeCookBook = true;

        Assert.False(vm.CookBookIsTheSource);
        Assert.Contains(vm.Consequences, c => c.Contains("not the one this Set was cooked from"));
        Assert.Equal("", vm.Problem);                 // warned, never refused
        Assert.Contains(vm.Parts, p => p.EndsWith(".cbk", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Set that records no hash cannot be checked, and "cannot tell" is not "wrong".
    /// </summary>
    [Fact]
    public void A_set_with_no_recorded_hash_says_nothing_either_way()
    {
        var (setDir, book) = CookedWithItsBook();
        var vm = Dialog(setDir, sha: null, candidates: new[] { book });

        Assert.Null(vm.CookBookPath);                 // nothing to match against
        vm.CookBookPath = book;
        vm.IncludeCookBook = true;
        Assert.Null(vm.CookBookIsTheSource);
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("not the one this Set was cooked from"));
    }
}
