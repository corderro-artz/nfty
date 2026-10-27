using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class RiseExportTests
{
    private const string Pass = "correct-horse-battery-staple";

    private sealed class ScriptedPicker(string? save = null, string? folder = null, bool throwIfAsked = false, Action? onSave = null) : IFilePickerService
    {
        public int Asked { get; private set; }
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => throw new InvalidOperationException();
        public Task<string?> SaveFileAsync(string title, string defaultExtension)
        {
            Asked++;
            onSave?.Invoke();
            return throwIfAsked ? throw new InvalidOperationException("asked") : Task.FromResult(save);
        }
        public Task<string?> PickFolderAsync(string title)
        {
            Asked++;
            return throwIfAsked ? throw new InvalidOperationException("asked") : Task.FromResult(folder);
        }
    }

    private static string Temp() => Directory.CreateTempSubdirectory("nfty-rise-export-").FullName;

    private static RiseViewModel Vm(RiseSource src, IFilePickerService picker, StatusService? status = null, IDialogService? dialogs = null) =>
        new(src, new ImageBridge(), new FakeNav(), dialogs ?? new FakeDialogs(), picker, status ?? new StatusService());

    private static RiseViewModel BodyVm(IFilePickerService picker, StatusService? status = null, bool pine = true, IDialogService? dialogs = null)
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook(withPine: pine));
        return Vm(RiseFixtures.Variant(s), picker, status, dialogs);
    }

    private static RiseItem Item(RiseViewModel vm, string name) => vm.Items.Single(i => i.Name == name);

    private static string[] Names(string dir) =>
        Directory.GetFiles(dir).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray();

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
        var dir = Path.Combine(Temp(), "Chest Demo");
        using (var cooked = Generator.Generate(book, new GenerateOptions(3, "launch")))
            SetWriter.Write(cooked, dir, pack: false);
        var result = SetExporter.Export(dir, Temp(), ExportOptions.For(ExportPreset.SealedCritique), passphrase: Pass);
        return SealedSetReader.Open(result.Path, Pass);
    }

    [AvaloniaFact]
    public async Task A_sealed_set_refuses_in_words_and_never_opens_the_picker()
    {
        using var set = OpenSealed();
        var picker = new ScriptedPicker(throwIfAsked: true);
        using var vm = Vm(new SetRiseSource(set, set.Items[0].Number, null, new ImageBridge()), picker);
        vm.Items[0].IsChecked = true;

        Assert.True(vm.ExportCount > 0);
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.Equal(vm.Source.ExportRefusal, vm.ExportProblem);
        Assert.Contains("sealed", vm.ExportProblem);
        Assert.Equal("Nothing to export", vm.ManifestTitle);

        // Executing anyway (a disabled button is a label) is still refused, picker untouched.
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(0, picker.Asked);
        Assert.Equal("", vm.ManifestSize);
    }

    [AvaloniaFact]
    public async Task A_single_gltf_with_bin_is_written_under_the_picked_stem()
    {
        var dir = Temp();
        var status = new StatusService();
        using var vm = BodyVm(new ScriptedPicker(save: Path.Combine(dir, "mychest.gltf")), status);
        Item(vm, "Oak").IsChecked = true;
        vm.PickFormatCommand.Execute(vm.FormatCards.Single(c => c.Format == ModelFormat.GltfWithBin));

        Assert.Equal("Body-Oak.gltf", vm.ManifestTitle);
        Assert.Equal(new[] { "Body-Oak.gltf", "Body-Oak.bin" }, vm.ManifestParts);

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "mychest.bin", "mychest.gltf" }, Names(dir));
        Assert.Equal($"Exported mychest.gltf, mychest.bin to {dir}.", status.Last);
        Assert.Contains("mychest.bin", File.ReadAllText(Path.Combine(dir, "mychest.gltf")));
    }

    [AvaloniaFact]
    public async Task A_folder_batch_writes_exactly_the_names_the_manifest_lists()
    {
        var root = Temp();
        using var vm = BodyVm(new ScriptedPicker(folder: root));
        Item(vm, "Oak").IsChecked = true;
        Item(vm, "Pine").IsChecked = true;
        vm.Packed = false;

        Assert.True(vm.IsBatch);
        Assert.Equal("Body-models/", vm.ManifestTitle);
        Assert.Equal(new[] { "2 models", "2 files", "Body-Oak.glb, Body-Pine.glb" }, vm.ManifestParts);
        Assert.Equal("", vm.ManifestSize);       // a batch states names, not a size

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(vm.ManifestParts[2].Split(", "), Names(Path.Combine(root, "Body-models")));
    }

    [AvaloniaFact]
    public async Task A_batch_of_two_file_models_counts_four_files_and_writes_four()
    {
        var root = Temp();
        using var vm = BodyVm(new ScriptedPicker(folder: root));
        Item(vm, "Oak").IsChecked = true;
        Item(vm, "Pine").IsChecked = true;
        vm.Packed = false;
        vm.Format = ModelFormat.GltfWithBin;

        Assert.Equal("4 files", vm.ManifestParts[1]);
        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Body-Oak.bin", "Body-Oak.gltf", "Body-Pine.bin", "Body-Pine.gltf" },
            Names(Path.Combine(root, "Body-models")));
    }

    [AvaloniaFact]
    public async Task A_packed_batch_zip_holds_the_listed_names()
    {
        var root = Temp();
        var zipPath = Path.Combine(root, "out.zip");
        using var vm = BodyVm(new ScriptedPicker(save: zipPath));
        Item(vm, "Oak").IsChecked = true;
        Item(vm, "Pine").IsChecked = true;

        Assert.Equal("Body-models.zip", vm.ManifestTitle);
        await vm.ExportCommand.ExecuteAsync(null);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(vm.ManifestParts[2].Split(", "), zip.Entries.Select(e => e.FullName).ToArray());
    }

    [AvaloniaFact]
    public async Task A_batch_with_the_ghost_ticked_writes_the_others_and_names_the_ghost_as_skipped()
    {
        var root = Temp();
        var status = new StatusService();
        using var vm = BodyVm(new ScriptedPicker(folder: root), status);
        Item(vm, "Oak").IsChecked = true;
        Item(vm, "Pine").IsChecked = true;
        Item(vm, "Ghost").IsChecked = true;      // a variant page loads every variant, so it is already known to be 2D and never reaches the writer
        vm.Packed = false;

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Body-Oak.glb", "Body-Pine.glb" }, Names(Path.Combine(root, "Body-models")));
        Assert.Equal($"Exported 2 models (2 files) to {Path.Combine(root, "Body-models")}. Skipped Ghost (2D only).", status.Last);
    }

    [AvaloniaFact]
    public void A_ghost_already_known_to_be_flat_is_left_out_of_the_batch_and_the_check_all_count()
    {
        using var vm = BodyVm(new ScriptedPicker());
        vm.SelectedItem = Item(vm, "Ghost");           // loads it: now known to be 2D
        vm.SelectedItem = Item(vm, "Oak");
        vm.ToggleCheckAllCommand.Execute(null);

        Assert.Equal("2 of 2 checked for export", vm.CheckedSummary);
        Assert.False(Item(vm, "Ghost").IsChecked);
        Assert.Equal("Clear", vm.CheckAllLabel);
        vm.ToggleCheckAllCommand.Execute(null);
        Assert.Equal("0 of 2 checked for export", vm.CheckedSummary);
        Assert.Equal("Check all", vm.CheckAllLabel);
        Assert.Equal("Check at least one item to export.", vm.ExportProblem);
    }

    [AvaloniaFact]
    public void The_shape_section_is_inked_only_for_a_batch()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Assert.False(vm.IsBatch);
        Item(vm, "Oak").IsChecked = true;
        Assert.False(vm.IsBatch);
        Assert.Equal("Export model…", vm.ExportLabel);
        Item(vm, "Pine").IsChecked = true;
        Assert.True(vm.IsBatch);
        Assert.Equal("Export 2 models…", vm.ExportLabel);
        Assert.Equal("2", vm.ExportBadge);
        Item(vm, "Pine").IsChecked = false;
        Assert.False(vm.IsBatch);
    }

    [AvaloniaFact]
    public void Voxel_size_is_finite_clamped_and_rounded_at_the_property()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Assert.Equal(1f, vm.ExportOptions.VoxelSize);
        vm.VoxelSize = 0; Assert.Equal(Nfty.Core.Publish.ModelVoxelSize.Min, vm.VoxelSize);
        vm.VoxelSize = 0.0005; Assert.Equal(0.001, vm.VoxelSize);
        vm.VoxelSize = double.NaN; Assert.Equal(1, vm.VoxelSize);
        vm.VoxelSize = 1.2344; Assert.Equal(1.234, vm.VoxelSize);
        vm.VoxelSize = 5000; Assert.Equal(1000, vm.VoxelSize);
        vm.VoxelSize = double.PositiveInfinity; Assert.Equal(1, vm.VoxelSize);
        vm.VoxelSize = 2.5;
        Assert.Equal(2.5f, vm.ExportOptions.VoxelSize);
    }

    [AvaloniaFact]
    public void The_model_dimensions_are_the_mesh_extent_times_the_voxel_size()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Assert.Equal("model 6 × 6 × 1", vm.ModelDimensionsText);
        vm.VoxelSize = 2;
        Assert.Equal("model 12 × 12 × 2", vm.ModelDimensionsText);
        vm.VoxelSize = 0.5;
        Assert.Equal("model 3 × 3 × 0.5", vm.ModelDimensionsText);
    }

    [AvaloniaFact]
    public void Pivot_radios_are_two_way_and_the_manifest_names_the_pivot()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Item(vm, "Oak").IsChecked = true;
        Assert.True(vm.IsPivotBottomCenter);
        Assert.Equal(new[] { "texture inside", "pivot bottom center" }, vm.ManifestParts);
        vm.IsPivotMinCorner = true;
        Assert.Equal(Pivot.MinCorner, vm.Pivot);
        Assert.False(vm.IsPivotBottomCenter);
        Assert.Equal("pivot min corner", vm.ManifestParts[1]);
        vm.IsPivotCenter = true;
        Assert.Equal("pivot center", vm.ManifestParts[1]);
        vm.Format = ModelFormat.GltfEmbedded;
        Assert.Equal("buffer inside", vm.ManifestParts[0]);
        Assert.True(vm.FormatCards.Single(c => c.Format == ModelFormat.GltfEmbedded).IsSelected);
        Assert.False(vm.FormatCards.Single(c => c.Format == ModelFormat.Glb).IsSelected);
    }

    [AvaloniaFact]
    public void Rise_and_export_word_sizes_the_same_way()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Item(vm, "Oak").IsChecked = true;
        vm.ShowExportTabCommand.Execute(null);

        using var snapshot = vm.Source.SnapshotExport(new[] { vm.SelectedItem });
        var input = snapshot.Open(0, "x");
        long actual = ModelBatch.Build(new[] { input }, vm.ExportOptions).Files.Sum(f => (long)f.Bytes.Length);
        RiseExportSnapshot.Release(input);

        // The Set export card words a size with SizeText.Of; this card must not have its own wording.
        Assert.Equal(SizeText.Of(actual), vm.ManifestSize);
    }

    [AvaloniaFact]
    public void The_exact_size_is_built_only_on_the_export_tab_and_follows_options_and_depth()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Item(vm, "Oak").IsChecked = true;
        vm.VoxelSize = 2;
        Assert.Equal(0, vm.SizeBuilds);
        Assert.Equal("", vm.ManifestSize);

        vm.ShowExportTabCommand.Execute(null);
        Assert.Equal(1, vm.SizeBuilds);
        var glb = vm.ManifestSize;
        Assert.Matches(@"^\d+(\.\d)? (bytes|KB|MB)$|^\d+ (bytes|KB)$", glb);

        // The size is the bytes the writer produces, not an estimate.
        using var snapshot = vm.Source.SnapshotExport(new[] { vm.SelectedItem });
        var input = snapshot.Open(0, "x");
        long actual = ModelBatch.Build(new[] { input }, vm.ExportOptions).Files.Sum(f => (long)f.Bytes.Length);
        RiseExportSnapshot.Release(input);
        Assert.Equal(SizeText.Of(actual), glb);

        vm.Format = ModelFormat.GltfEmbedded;
        Assert.Equal(2, vm.SizeBuilds);
        Assert.NotEqual(glb, vm.ManifestSize);

        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        Assert.Equal(3, vm.SizeBuilds);

        vm.ShowDepthTabCommand.Execute(null);
        vm.VoxelSize = 3;
        vm.ApplyDepthStroke(new[] { (3, 1) }, DepthSide.Front, false);
        Assert.Equal(3, vm.SizeBuilds);          // nothing built while the DEPTH tab shows
    }

    [AvaloniaFact]
    public async Task A_cancelled_picker_writes_nothing_and_a_failure_is_said_in_a_dialog()
    {
        var picker = new ScriptedPicker(save: null);
        using (var vm = BodyVm(picker))
        {
            Item(vm, "Oak").IsChecked = true;
            var status = new StatusService();
            await vm.ExportCommand.ExecuteAsync(null);
            Assert.Equal(1, picker.Asked);
            Assert.Null(status.Last);
        }

        var dialogs = new FakeDialogs();
        using var failing = BodyVm(new ScriptedPicker(save: Path.Combine(Temp(), "nope", "..", "..", "x\0.glb")), dialogs: dialogs);
        Item(failing, "Oak").IsChecked = true;
        await failing.ExportCommand.ExecuteAsync(null);
        Assert.IsType<ErrorDialogViewModel>(dialogs.Active);
        Assert.True(failing.ExportCommand.CanExecute(null));       // not left stuck exporting
    }

    [AvaloniaFact]
    public async Task A_batch_set_names_what_the_writer_skips_when_the_picture_turns_out_to_be_2D()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var setDir = Path.Combine(Path.GetDirectoryName(path)!, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(20, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        using var set = SetReader.Read(setDir);
        // The lock paints over the ghost's one half-alpha pixel, so no cooked asset is 2D: make one so.
        using (var damaged = Image.Load<Rgba32>(set.Items[5].ImagePath))
        {
            damaged[3, 3] = new Rgba32(200, 40, 40, 128);
            damaged.SaveAsPng(set.Items[5].ImagePath);
        }
        int ghosts = 1;

        var root = Temp();
        var status = new StatusService();
        using var vm = Vm(new SetRiseSource(set, set.Items[0].Number, () => Array.Empty<string>(), new ImageBridge()),
            new ScriptedPicker(folder: root), status);
        vm.Packed = false;
        vm.ToggleCheckAllCommand.Execute(null);          // nothing is loaded but the first, so none is known to be 2D yet
        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(20 - ghosts, Directory.GetFiles(Directory.GetDirectories(root).Single()).Length);
        Assert.Contains("(partial alpha)", status.Last);
        Assert.StartsWith($"Exported {20 - ghosts} models", status.Last);

        // The refused asset is now known to be 2D: the manifest, badge and label stop promising it.
        var refused = vm.Items.Single(i => i.DepthChip == "2D only");
        Assert.False(refused.IsChecked);
        Assert.False(refused.IsRisableOrUnknown);
        Assert.Equal(20 - ghosts, vm.ExportCount);
        Assert.Equal($"{20 - ghosts} models", vm.ManifestParts[0]);
        Assert.Equal($"{20 - ghosts} files", vm.ManifestParts[1]);
        Assert.Equal($"Export {20 - ghosts} models…", vm.ExportLabel);
        Assert.Equal($"{20 - ghosts} of {20 - ghosts} checked for export", vm.CheckedSummary);
    }

    [AvaloniaFact]
    public void The_item_the_page_opened_on_starts_ticked_and_names_one_model()
    {
        using var vm = BodyVm(new ScriptedPicker());
        Assert.Equal(1, vm.ExportCount);
        Assert.Equal("Body-Oak.glb", vm.ManifestTitle);
        Assert.Null(vm.ExportProblem);
        Assert.True(vm.ExportCommand.CanExecute(null));
        Assert.Equal(new[] { "Oak" }, vm.Items.Where(i => i.IsChecked).Select(i => i.Name));

        // Reaching another item by selection does not tick it behind the user's back.
        vm.SelectedItem = Item(vm, "Pine");
        Assert.False(Item(vm, "Pine").IsChecked);
        Assert.Equal(1, vm.ExportCount);
    }

    [AvaloniaFact]
    public void A_set_opens_with_its_opening_asset_ticked()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var setDir = Path.Combine(Path.GetDirectoryName(path)!, "Chest Set");
        using (var cooked = Generator.Generate(s.Current!, new GenerateOptions(6, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, setDir, pack: false);
        s.Dispose();
        using var set = SetReader.Read(setDir);
        using var vm = Vm(new SetRiseSource(set, set.Items[2].Number, () => Array.Empty<string>(), new ImageBridge()),
            new ScriptedPicker());
        Assert.Equal(1, vm.ExportCount);
        Assert.True(vm.SelectedItem.IsChecked);
        Assert.Equal(1, vm.Items.Count(i => i.IsChecked));
    }

    [AvaloniaFact]
    public async Task A_format_changed_while_the_picker_is_open_does_not_change_what_the_file_was_named_for()
    {
        var dir = Temp();
        RiseViewModel? vm = null;
        var picker = new ScriptedPicker(save: Path.Combine(dir, "pick.glb"), onSave: () => vm!.Format = ModelFormat.GltfEmbedded);
        vm = BodyVm(picker);
        using (vm)
        {
            await vm.ExportCommand.ExecuteAsync(null);
            Assert.Equal(new[] { "pick.glb" }, Names(dir));
            Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "pick.glb")), 0, 4));
        }
    }

    [AvaloniaFact]
    public void The_gltf_with_bin_size_is_marked_approximate_because_the_stem_is_not_chosen_yet()
    {
        using var vm = BodyVm(new ScriptedPicker());
        vm.Format = ModelFormat.GltfWithBin;
        vm.ShowExportTabCommand.Execute(null);
        Assert.StartsWith("~", vm.ManifestSize);
        vm.Format = ModelFormat.Glb;
        Assert.DoesNotContain("~", vm.ManifestSize);
    }

    [AvaloniaFact]
    public async Task A_recipe_exports_its_one_roll_and_follows_a_reroll()
    {
        using var book = RiseFixtures.ChestBook();
        var recipe = book.Recipes.Single(r => r.Manifest.Id == "chest");
        var src = new RecipeRiseSource(book, recipe, seed: 7, new ImageBridge());
        var dir = Temp();
        using var vm = Vm(src, new ScriptedPicker(save: Path.Combine(dir, "roll.glb")));

        Assert.Equal(1, vm.ExportCount);
        Assert.Equal("Chest-7.glb", vm.ManifestTitle);
        src.Reroll();
        Assert.Equal("Chest-8.glb", vm.ManifestTitle);

        await vm.ExportCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "roll.glb" }, Names(dir));
    }

    [AvaloniaFact]
    public void A_2d_only_selection_says_why_in_words()
    {
        using var vm = BodyVm(new ScriptedPicker(), pine: false);
        Item(vm, "Oak").IsChecked = false;
        Item(vm, "Ghost").IsChecked = true;
        Assert.Equal(0, vm.ExportCount);
        Assert.False(vm.ExportCommand.CanExecute(null));
        Assert.Contains("stays 2D", vm.ExportProblem);
        Assert.Equal("Nothing to export", vm.ManifestTitle);
    }
    private static readonly Rgba32 GoldColor = new(240, 204, 98, 255);
    private static readonly Rgba32 OakColor = new(178, 122, 70, 255);

    /// <summary>The embedded PNG texture of a GLB, decoded.</summary>
    private static Image<Rgba32> GlbTexture(byte[] glb)
    {
        int jsonLen = BitConverter.ToInt32(glb, 12);
        using var json = System.Text.Json.JsonDocument.Parse(new ReadOnlyMemory<byte>(glb, 20, jsonLen));
        int binStart = 20 + jsonLen + 8;
        int view = json.RootElement.GetProperty("images")[0].GetProperty("bufferView").GetInt32();
        var v = json.RootElement.GetProperty("bufferViews")[view];
        int offset = v.TryGetProperty("byteOffset", out var o) ? o.GetInt32() : 0;
        return Image.Load<Rgba32>(new ReadOnlySpan<byte>(glb, binStart + offset, v.GetProperty("byteLength").GetInt32()));
    }

    [AvaloniaFact]
    public async Task A_recipe_export_writes_the_back_owner_color()
    {
        using var book = RiseFixtures.StrapOverBody();
        var recipe = book.Recipes.Single();
        var dir = Temp();
        using var vm = Vm(new RecipeRiseSource(book, recipe, RiseFixtures.OakSeed(book, recipe), new ImageBridge()),
            new ScriptedPicker(save: Path.Combine(dir, "roll.glb")));

        await vm.ExportCommand.ExecuteAsync(null);

        using var tex = GlbTexture(File.ReadAllBytes(Path.Combine(dir, "roll.glb")));
        Assert.Equal(GoldColor, tex[3, 3]);
        Assert.Equal(OakColor, tex[8 + 1 + 3, 3]);      // right half of the atlas: the body under the strap
    }

    [AvaloniaFact]
    public async Task The_page_export_is_the_cooked_sets_model_byte_for_byte()
    {
        // One definition of the model: what the page exports for a roll is what a cook writes to models/.
        using var book = RiseFixtures.StrapOverBody(withGhost: false);
        var recipe = book.Recipes.Single();
        const int seed = 7;
        var dir = Temp();
        using var vm = Vm(new RecipeRiseSource(book, recipe, seed, new ImageBridge()),
            new ScriptedPicker(save: Path.Combine(dir, "roll.glb")));
        await vm.ExportCommand.ExecuteAsync(null);

        using var asset = Generator.GenerateStreaming(book,
            new GenerateOptions(1, seed.ToString(), "chest", EnforceUniqueDna: false)).First();
        var file = Assert.Single(new SetModeler(book, new RiseOptions()).Model(asset, "roll")!);

        Assert.Equal("roll.glb", file.FileName);
        Assert.Equal(file.Bytes, File.ReadAllBytes(Path.Combine(dir, "roll.glb")));
    }

    [AvaloniaFact]
    public void An_export_snapshot_owns_its_faces()
    {
        using var book = RiseFixtures.StrapOverBody();
        var recipe = book.Recipes.Single();
        var src = new RecipeRiseSource(book, recipe, RiseFixtures.OakSeed(book, recipe), new ImageBridge());
        using var vm = Vm(src, new ScriptedPicker());
        var live = src.Items[0].Subject!.Faces!;

        using var snapshot = src.SnapshotExport(src.Items);
        src.Reroll();                                       // disposes the old subject and the faces it held
        Assert.Throws<ObjectDisposedException>(() => _ = live.Back[3, 3]);

        var input = snapshot.Open(0, "x");
        Assert.Equal(OakColor, input.Faces!.Back[3, 3]);    // the snapshot kept its own copy
        RiseExportSnapshot.Release(input);
        Assert.Throws<ObjectDisposedException>(() => _ = input.Faces.Back[3, 3]);   // and gave it back
    }
    private static async Task<(byte[] Expected, byte[] Page)> SetPageVsCooked(
        LoadedCookBook source, bool stripExactColors = false, string seed = "launch")
    {
        var (session, bookPath) = RiseFixtures.OnDisk(source);
        var setDir = Path.Combine(Path.GetDirectoryName(bookPath)!, "Chest Set");
        byte[] expected;
        using (var cooked = Generator.Generate(session.Current!, new GenerateOptions(3, seed, EnforceUniqueDna: false)))
        {
            SetWriter.Write(cooked, setDir, pack: false);
            var asset = cooked.Assets[1];
            expected = Assert.Single(new SetModeler(session.Current!, new RiseOptions()).Model(asset, "m")!).Bytes;
        }
        session.Dispose();
        if (stripExactColors)
            foreach (var f in Directory.GetFiles(Path.Combine(setDir, "nfty"), "*.json"))
                File.WriteAllText(f, System.Text.RegularExpressions.Regex.Replace(
                    File.ReadAllText(f), @",\s*""exact[HS]"":\s*[-0-9.eE+]+", ""));

        using var set = SetReader.Read(setDir);
        var dir = Temp();
        using var vm = Vm(new SetRiseSource(set, set.Items[1].Number, () => Array.Empty<string>(), new ImageBridge()),
            new ScriptedPicker(save: Path.Combine(dir, "m.glb")));
        await ((SetRiseSource)vm.Source).BookSearch;
        await vm.Source.ExportReady;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        await vm.ExportCommand.ExecuteAsync(null);
        return (expected, File.ReadAllBytes(Path.Combine(dir, "m.glb")));
    }

    [AvaloniaFact]
    public async Task A_set_pages_export_is_the_cooked_sets_model_byte_for_byte()
    {
        var (expected, page) = await SetPageVsCooked(RiseFixtures.StrapOverBody(withGhost: false));
        Assert.Equal(expected, page);
    }

    [AvaloniaFact]
    public async Task A_set_pages_export_matches_with_a_colorized_back_owner_byte_for_byte()
    {
        // Rounding a rolled color moves an 8-bit channel in only SOME assets, so one asset can't pin it.
        foreach (var seed in new[] { "s1", "s2", "s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10" })
        {
            var (expected, page) = await SetPageVsCooked(RiseFixtures.DynamicBodyUnderStrap(), seed: seed);
            Assert.True(expected.AsSpan().SequenceEqual(page), $"seed {seed}: the page's model is not the cook's");
        }
    }

    [AvaloniaFact]
    public async Task A_set_cooked_before_exact_colors_still_exports_from_the_rounded_ones()
    {
        var (expected, page) = await SetPageVsCooked(RiseFixtures.DynamicBodyUnderStrap(), stripExactColors: true);
        Assert.Equal(expected.Length, page.Length);   // same model, colors within 1 LSB: not byte-identical by contract
    }
}
