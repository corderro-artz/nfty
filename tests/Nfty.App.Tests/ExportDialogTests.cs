using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
/// The export dialog: what it promises, and that what it promises is what the engine would write.
/// </summary>
public class ExportDialogTests
{
    private const string Pass = "correct-horse-battery-staple";

    /// <summary>A real cooked Set on disk. The dialog reads a real directory on every change, so a
    /// fake would be testing something the running app never does.</summary>
    private static string CookedSet(int count = 4)
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("nfty-exp-").FullName, "Chest Demo");
        var ids = new[] { "a", "b", "c", "d", "e", "f" };
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("bg", "Background", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.ToDictionary(v => v,
                v => new Image<Rgba32>(4, 4, new Rgba32((byte)v[0], 2, 3, 255))),
        };
        using var book = new LoadedCookBook
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
        using var set = Generator.Generate(book, new GenerateOptions(count, "launch"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    private static ExportDialogViewModel Dialog(string? setDir = null) =>
        new(setDir ?? CookedSet(), new FilePickerService(), new NoopFolderRevealer(),
            new FakeDialogs());

    // ------------------------------------------------------------------ the manifest

    [Fact]
    public void The_manifest_names_what_the_engine_would_actually_write()
    {
        // The one assertion this screen exists for. The dialog's list and the exporter's list are
        // the same call, and this proves it stays that way - a summary assembled separately from
        // the same checkboxes is how a screen comes to promise one thing and ship another.
        var dir = CookedSet();
        var vm = Dialog(dir);
        vm.NftyMetadata = false;

        var plan = SetExporter.Plan(dir, vm.Options);

        Assert.Equal(plan.Parts(), vm.Parts);
        Assert.Equal(plan.OutputName, vm.OutputName);
        Assert.Equal(plan.SizeText(), vm.SizeText);
    }

    [Fact]
    public void Unticking_a_box_removes_its_directory_from_the_manifest_immediately()
    {
        var vm = Dialog();
        Assert.Contains(vm.Parts, p => p.StartsWith("nfty/", StringComparison.Ordinal));

        vm.NftyMetadata = false;

        Assert.DoesNotContain(vm.Parts, p => p.StartsWith("nfty/", StringComparison.Ordinal));
        Assert.Contains(vm.Parts, p => p.StartsWith("images/", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_property_the_manifest_depends_on_is_registered_to_refresh_it()
    {
        // Derived by reflection rather than restated, so an input added later cannot quietly stop
        // updating what the screen promises - which would be invisible in every other test here,
        // since they all read the properties directly.
        var outputs = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ExportDialogViewModel.Parts), nameof(ExportDialogViewModel.OutputName),
            nameof(ExportDialogViewModel.SizeText), nameof(ExportDialogViewModel.Problem),
            nameof(ExportDialogViewModel.Consequences), nameof(ExportDialogViewModel.IsRunning),
            nameof(ExportDialogViewModel.IsDone), nameof(ExportDialogViewModel.OutputPath),
            nameof(ExportDialogViewModel.ResultText),
        };

        var settable = typeof(ExportDialogViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && !outputs.Contains(p.Name))
            .Select(p => p.Name)
            .ToList();

        Assert.NotEmpty(settable);
        Assert.Equal(settable.OrderBy(n => n, StringComparer.Ordinal),
            settable.Where(ExportDialogViewModel.PlanInputs.Contains)
                .OrderBy(n => n, StringComparer.Ordinal));
    }

    // ------------------------------------------------------------------ presets

    [Fact]
    public void There_is_a_tile_for_every_preset_the_engine_defines()
    {
        var vm = Dialog();
        Assert.Equal(Enum.GetValues<ExportPreset>().Length, vm.Presets.Count);
        Assert.All(vm.Presets, t =>
        {
            Assert.Equal(ExportOptions.TitleOf(t.Preset), t.Title);
            Assert.Equal(ExportOptions.SummaryOf(t.Preset), t.Summary);
        });
    }

    [Fact]
    public void Applying_a_preset_lights_it_and_sets_every_box_beneath_it()
    {
        var vm = Dialog();
        var marketplace = vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace);

        vm.ApplyPresetCommand.Execute(marketplace);

        Assert.True(marketplace.IsSelected);
        Assert.False(vm.NftyMetadata);
        Assert.True(vm.Images);
        Assert.Single(vm.Presets, t => t.IsSelected);
    }

    [Fact]
    public void A_tile_goes_out_the_moment_a_box_below_it_stops_agreeing()
    {
        // Derived from the OPTIONS rather than remembered from the click, so the screen cannot go on
        // claiming a preset the export no longer is.
        var vm = Dialog();
        var marketplace = vm.Presets.Single(t => t.Preset == ExportPreset.Marketplace);
        vm.ApplyPresetCommand.Execute(marketplace);
        Assert.True(marketplace.IsSelected);

        vm.Images = false;

        Assert.DoesNotContain(vm.Presets, t => t.IsSelected);
    }

    // ------------------------------------------------------------------ sealing

    [Fact]
    public void Sealing_forces_the_single_file_shape_and_says_so_by_disabling_the_other()
    {
        var vm = Dialog();
        vm.Packed = false;
        Assert.True(vm.CanChooseFolder);

        vm.IsSealed = true;

        Assert.True(vm.Packed);
        Assert.False(vm.CanChooseFolder);
    }

    [Fact]
    public void Export_is_blocked_with_a_reason_until_the_passphrase_is_usable_and_confirmed()
    {
        // Stated rather than only disabled: a control that is dim for a reason the screen does not
        // give is a control the user has to guess at.
        var vm = Dialog();
        Assert.True(vm.ExportCommand.CanExecute(null));

        vm.IsSealed = true;
        Assert.Contains(Seal.MinimumPassphraseLength.ToString(), vm.Problem);
        Assert.False(vm.ExportCommand.CanExecute(null));

        vm.Passphrase = Pass;
        Assert.Contains("do not match", vm.Problem);
        Assert.False(vm.ExportCommand.CanExecute(null));

        vm.PassphraseConfirm = Pass;
        Assert.Equal("", vm.Problem);
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void The_seal_caveat_is_shown_wherever_it_cannot_scroll_away()
    {
        // It sits in the pinned manifest box, not in the seal panel: with the panel open the form is
        // taller than the card, so inside it the one sentence saying what a seal does NOT do sat
        // below the fold at the exact moment it mattered. Found on a rendered frame.
        var vm = Dialog();
        Assert.DoesNotContain(vm.Consequences, c => c.Contains("Sealed", StringComparison.Ordinal));

        vm.IsSealed = true;

        var line = Assert.Single(vm.Consequences, c => c.StartsWith("Sealed", StringComparison.Ordinal));
        Assert.Contains("can still take the art", line);
    }

    [Fact]
    public void Consequences_stack_rather_than_replacing_each_other()
    {
        // An export can carry the source book AND be sealed, which is the combination most in need
        // of both sentences - so this is a list and not one string.
        var vm = Dialog();
        var book = Path.Combine(Directory.CreateTempSubdirectory().FullName, "Chest Demo.cbk");
        File.WriteAllText(book, "not read by Plan, only listed");
        vm.CookBookPath = book;
        vm.IncludeCookBook = true;
        vm.IsSealed = true;

        Assert.Equal(2, vm.Consequences.Count);
        Assert.Contains(vm.Consequences, c => c.Contains("source CookBook", StringComparison.Ordinal));
        Assert.Contains(vm.Consequences, c => c.StartsWith("Sealed", StringComparison.Ordinal));
    }

    [Fact]
    public void Asking_for_the_book_without_choosing_one_states_the_engines_own_refusal()
    {
        // Not restated here, so the dialog and the command line cannot disagree about the same
        // request.
        var vm = Dialog();
        vm.IncludeCookBook = true;

        Assert.Contains("no .cbk was given", vm.Problem);
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.Empty(vm.Parts);
    }

    // ------------------------------------------------------------------ the run

    [Fact]
    public async Task Exporting_writes_what_the_manifest_promised()
    {
        var outDir = Directory.CreateTempSubdirectory("nfty-out-").FullName;
        var vm = Dialog();
        vm.NftyMetadata = false;
        var promised = vm.Parts.ToList();
        var name = vm.OutputName;

        await RunExport(vm, outDir);

        Assert.True(vm.IsDone);
        var written = Path.Combine(outDir, name);
        Assert.True(File.Exists(written));
        using var zip = System.IO.Compression.ZipFile.OpenRead(written);
        var actual = zip.Entries
            .GroupBy(e => e.FullName.Contains('/') ? e.FullName[..e.FullName.IndexOf('/')] + "/" : e.FullName)
            .Select(g => g.Count() == 1 ? g.Key : $"{g.Key}  {g.Count()} files")
            .OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(promised.OrderBy(x => x, StringComparer.Ordinal), actual);
    }

    [Fact]
    public async Task A_sealed_export_opens_again_with_its_passphrase()
    {
        var outDir = Directory.CreateTempSubdirectory("nfty-out-").FullName;
        var vm = Dialog();
        vm.IsSealed = true;
        vm.Passphrase = Pass;
        vm.PassphraseConfirm = Pass;
        vm.Note = "Draft for review";

        await RunExport(vm, outDir);

        var written = Path.Combine(outDir, "Chest Demo" + Archives.SealedExtension);
        Assert.True(File.Exists(written));
        using var opened = SealedSetReader.Open(written, Pass);
        Assert.Equal("Draft for review", opened.Header.Note);
        Assert.Equal(4, opened.Items.Count);
    }

    [Fact]
    public async Task The_passphrase_is_not_kept_after_the_run()
    {
        // The dialog stays alive until it is closed, and there is nothing further it needs the
        // passphrase for.
        var vm = Dialog();
        vm.IsSealed = true;
        vm.Passphrase = Pass;
        vm.PassphraseConfirm = Pass;

        await RunExport(vm, Directory.CreateTempSubdirectory("nfty-out-").FullName);

        Assert.Equal("", vm.Passphrase);
        Assert.Equal("", vm.PassphraseConfirm);
    }

    [AvaloniaFact]
    public void The_preset_tiles_in_the_rendered_view_actually_invoke_the_command()
    {
        // A test that calls ApplyPresetCommand is not a test that the TILE invokes it. The tiles
        // reach past their own DataContext with $parent[ItemsControl], and the worry was the silent
        // failure CLAUDE.md records for a $parent binding inside a ContextMenu - button renders,
        // button is clickable, nothing happens.
        //
        // Probing says that particular worry does not apply HERE: the DataTemplate carries an
        // x:DataType, so a path that does not resolve is a BUILD error (AVLN2000), not a silent
        // null. What this still covers is everything the compiler cannot see - that there is one
        // tile per preset, that each carries itself as the parameter, and that invoking a tile's
        // own bound command moves the ViewModel rather than some other tile's.
        var vm = Dialog();
        var view = new Views.ExportDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tiles = view.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("preset")).ToList();
        Assert.Equal(vm.Presets.Count, tiles.Count);

        var marketplace = tiles.Single(b => b.DataContext is ExportPresetTile
        {
            Preset: ExportPreset.Marketplace,
        });
        Assert.True(vm.NftyMetadata);

        // The gesture a real click performs, through the control's own bound command rather than
        // through the ViewModel's.
        Assert.NotNull(marketplace.Command);
        marketplace.Command!.Execute(marketplace.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.NftyMetadata);
        window.Close();
    }

    /// <summary>Runs the export against a fixed folder, standing in for the folder picker.</summary>
    private static async Task RunExport(ExportDialogViewModel vm, string outDir)
    {
        var picker = new StubFolderPicker(outDir);
        var field = typeof(ExportDialogViewModel)
            .GetField("_picker", BindingFlags.NonPublic | BindingFlags.Instance)!;
        field.SetValue(vm, picker);
        await vm.ExportCommand.ExecuteAsync(null);
    }

    private sealed class StubFolderPicker : IFilePickerService
    {
        private readonly string _dir;
        public StubFolderPicker(string dir) => _dir = dir;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) =>
            Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) =>
            Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(_dir);
    }
}
