using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Colour mode in the editor: the palette strip, the opacity lock, and what Save does with colour
/// art painted onto a value-map layer.
/// </summary>
public class IngredientEditorColorModeTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    /// <summary>Answers the colour-save dialog with a fixed choice and counts how often it was asked;
    /// every other dialog is confirmed. Counting matters for "asked once, not on every save".</summary>
    private sealed class ColorSaveDialogs(ColorSaveChoice choice) : IDialogService
    {
        public int Asked { get; private set; }
        public int Confirms { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            if (d is ColorSaveDialogViewModel) { Asked++; return Task.FromResult((TResult?)(object?)choice); }
            Confirms++;
            return Task.FromResult((TResult?)(object?)true);
        }
        public void Close(object? result) { }
    }

    /// <summary>Refuses every confirm — used to prove the partial-alpha warning is a GATE.</summary>
    private sealed class RefusingDialogs : IDialogService
    {
        public int Asked { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Asked++;
            return Task.FromResult((TResult?)(object?)false);
        }
        public void Close(object? result) { }
    }

    private static IngredientEditorViewModel Editor(
        (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) f,
        IDialogService? dialogs = null, IPaletteService? palette = null) =>
        new(f.ing, f.recipe, f.session.Current!, new ImageBridge(), new FakeNav(), new FakeNotYetWired(),
            f.session, dialogs ?? new FakeDialogs(), new NoPicker(), palette: palette);

    // ---------------- the palette strip ----------------

    [AvaloniaFact]
    public void A_value_map_layer_opens_in_grayscale_and_the_ramp_follows_the_mode()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);

            Assert.False(vm.IsColorMode);
            Assert.True(vm.CanPaintGrayscale);
            Assert.Equal(Palette.Slots, vm.Ramp.Count);
            Assert.All(vm.Ramp, s => Assert.True(s.Rgb.R == s.Rgb.G && s.Rgb.G == s.Rgb.B));

            vm.SetPaintColorCommand.Execute(null);

            // The strip's SHAPE never changes — only its contents. That is the no-reflow rule.
            Assert.Equal(Palette.Slots, vm.Ramp.Count);
            Assert.Contains(vm.Ramp, s => s.Rgb.R != s.Rgb.G || s.Rgb.G != s.Rgb.B);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public void Picking_a_ramp_slot_arms_it_and_marks_exactly_that_cell()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            vm.SetPaintColorCommand.Execute(null);
            var slot = vm.Ramp[3];

            vm.PickSwatchCommand.Execute(slot);

            Assert.Equal(slot.Rgb, vm.CurrentRgb);
            Assert.Single(vm.Ramp, s => s.IsSelected);
            Assert.True(slot.IsSelected);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>A value-map stores lightness and nothing else, so a colour swatch picked in grayscale
    /// mode has to become its lightness. Refusing the click instead would leave saved swatches
    /// visibly present and silently inert.</summary>
    [AvaloniaFact]
    public void A_colour_swatch_picked_in_grayscale_mode_becomes_its_lightness()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            Assert.False(vm.IsColorMode);

            vm.PickSwatchCommand.Execute(new PaletteSwatch(new RgbColor(0, 255, 0)));

            // BT.709 luminance of pure green, the same reduction a colour PNG import goes through.
            Assert.Equal(182, vm.BrushValue);
            Assert.Equal(new RgbColor(182, 182, 182), vm.CurrentRgb);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public void Saved_swatches_round_trip_through_the_palette_service_and_can_be_forgotten()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var palette = new PaletteService(StateStore.InMemory());
        try
        {
            using var vm = Editor(f, palette: palette);
            vm.SetPaintColorCommand.Execute(null);
            vm.BrushHue = 200; vm.BrushSat = 50; vm.BrushValue = 255;
            var armed = vm.CurrentRgb;

            vm.SaveSwatchCommand.Execute(null);
            Assert.Contains(armed, palette.Swatches);
            var cell = Assert.Single(vm.SavedSwatches);
            Assert.Equal(armed, cell.Rgb);
            Assert.True(cell.CanForget);

            vm.SaveSwatchCommand.Execute(null);          // re-saving is a no-op, not a duplicate
            Assert.Single(vm.SavedSwatches);

            vm.ForgetSwatchCommand.Execute(cell);
            Assert.Empty(vm.SavedSwatches);
            Assert.Empty(palette.Swatches);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>A CookBook's own swatches travel in its archive and are not this screen's to delete;
    /// the app-wide ones sit beneath them.</summary>
    [AvaloniaFact]
    public void A_books_own_swatches_show_first_and_cannot_be_forgotten_from_the_editor()
    {
        var f = OnDiskWithPalette(new[] { "hex:112233" });
        var palette = new PaletteService(StateStore.InMemory());
        palette.Add(new RgbColor(0xAA, 0xBB, 0xCC));
        try
        {
            using var vm = Editor(f, palette: palette);

            Assert.Equal(2, vm.SavedSwatches.Count);
            Assert.Equal(new RgbColor(0x11, 0x22, 0x33), vm.SavedSwatches[0].Rgb);
            Assert.False(vm.SavedSwatches[0].CanForget);
            Assert.True(vm.SavedSwatches[1].CanForget);
            Assert.Null(vm.SavedSwatches[0].ForgetCommand);
            Assert.NotNull(vm.SavedSwatches[1].ForgetCommand);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    // ---------------- the opacity lock ----------------

    [AvaloniaFact]
    public async Task The_lock_is_on_by_default_and_snaps_a_translucent_stroke_to_opaque()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            Assert.True(vm.IsOpacityLocked);
            Assert.False(vm.IsAlphaEnabled);

            vm.SetPaintColorCommand.Execute(null);
            vm.BrushAlpha = 100;                       // inert while locked
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });

            Assert.Equal(255, vm.ColorAt(4, 4).A);
            await Task.CompletedTask;
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public async Task Unlocking_warns_once_and_then_partial_alpha_lands()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new ColorSaveDialogs(ColorSaveChoice.Cancel);
        try
        {
            using var vm = Editor(f, dialogs);

            await vm.ToggleOpacityLockCommand.ExecuteAsync(null);
            Assert.Equal(1, dialogs.Confirms);
            Assert.True(vm.IsAlphaEnabled);

            vm.SetPaintColorCommand.Execute(null);
            vm.BrushAlpha = 100;
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.Equal(100, vm.ColorAt(4, 4).A);

            // Re-locking and unlocking again must NOT warn a second time: the warning is about what
            // partial alpha does downstream, which does not become more true on the second stroke.
            await vm.ToggleOpacityLockCommand.ExecuteAsync(null);
            Assert.True(vm.IsOpacityLocked);
            await vm.ToggleOpacityLockCommand.ExecuteAsync(null);
            Assert.True(vm.IsAlphaEnabled);
            Assert.Equal(1, dialogs.Confirms);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public async Task Declining_the_warning_leaves_the_lock_on()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new RefusingDialogs();
        try
        {
            using var vm = Editor(f, dialogs);

            await vm.ToggleOpacityLockCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.Asked);
            Assert.True(vm.IsOpacityLocked);      // a gate, not a notice shown after the fact
            Assert.False(vm.IsAlphaEnabled);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    // ---------------- saving colour art ----------------

    [AvaloniaFact]
    public void Switching_to_colour_carries_the_existing_drawing_over_as_grey()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.Equal(200, vm.ValueAt(4, 4));

            vm.SetPaintColorCommand.Execute(null);

            var lifted = vm.ColorAt(4, 4);
            Assert.Equal(200, lifted.R);
            Assert.Equal(200, lifted.G);
            Assert.Equal(200, lifted.B);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public async Task Save_as_new_adds_a_custom_layer_and_leaves_the_original_exactly_as_it_was()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new ColorSaveDialogs(ColorSaveChoice.NewIngredient);
        try
        {
            using (var vm = Editor(f, dialogs))
            {
                vm.SetPaintColorCommand.Execute(null);
                vm.ActiveTool = EditorTool.Fill;
                vm.BrushHue = 120; vm.BrushSat = 100; vm.BrushValue = 255;
                vm.ApplyToolStroke(new[] { (0, 0) });
                Assert.NotNull(vm.SaveNoteText);

                await vm.SaveCommand.ExecuteAsync(null);
                Assert.Equal(1, dialogs.Asked);

                // Asked once, not per save: the draft is Custom now, so a second save just writes.
                vm.ApplyToolStroke(new[] { (1, 1) });
                await vm.SaveCommand.ExecuteAsync(null);
                Assert.Equal(1, dialogs.Asked);
                Assert.Null(vm.SaveNoteText);
            }

            // The LIVE graph first: the archive on disk was written before any disposal, so only the
            // in-memory book can catch a save that freed images the original layer still points at.
            // Reading a pixel off a disposed ImageSharp image throws.
            var liveOriginal = f.session.Current!.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(0, liveOriginal.VariantImages["glow"][0, 0].A);

            using var book = CookBookArchive.Read(f.path);
            var recipe = book.Recipes[0];
            Assert.Equal(2, recipe.Ingredients.Count);

            var original = recipe.Ingredients.Single(i => i.Manifest.Id == "aura");
            Assert.Equal(LayerKind.Dynamic, original.Manifest.Kind);
            Assert.NotNull(original.Manifest.Colorization);
            Assert.Equal(0, original.VariantImages["glow"][4, 4].A);   // never painted on

            var made = recipe.Ingredients.Single(i => i.Manifest.Id != "aura");
            Assert.Equal(LayerKind.Custom, made.Manifest.Kind);
            Assert.Null(made.Manifest.Colorization);
            Assert.NotEqual(original.Manifest.Name, made.Manifest.Name);   // trait_type must be unique
            var px = made.VariantImages[made.Manifest.Variants[0].Id][4, 4];
            Assert.Equal(0, px.R); Assert.Equal(255, px.G); Assert.Equal(0, px.B);

            // The new layer paints last, on top of the stack it was added to.
            Assert.Equal(made.Manifest.Id, recipe.Manifest.LayerOrder[^1]);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>A save writes the WHOLE ingredient, so entering colour mode has to widen every
    /// variant — not only the one on screen. A variant the author never visited would otherwise reach
    /// the exporter with no colour raster and take the save down with it.</summary>
    [AvaloniaFact]
    public async Task Every_variant_gets_colour_art_even_the_ones_never_visited()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new ColorSaveDialogs(ColorSaveChoice.NewIngredient);
        try
        {
            using (var vm = Editor(f, dialogs))
            {
                vm.AddVariantCommand.Execute(null);          // a second variant, selected
                vm.SelectedVariant = vm.Variants[0];         // back to the first; #2 is never visited
                vm.SetPaintColorCommand.Execute(null);
                vm.ActiveTool = EditorTool.Fill;
                vm.BrushHue = 120; vm.BrushSat = 100; vm.BrushValue = 255;
                vm.ApplyToolStroke(new[] { (0, 0) });

                await vm.SaveCommand.ExecuteAsync(null);
                Assert.False(vm.IsDirty);                    // the save actually completed
            }

            using var book = CookBookArchive.Read(f.path);
            var made = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id != "aura");
            Assert.Equal(2, made.Manifest.Variants.Count);
            Assert.Equal(2, made.VariantImages.Count);       // both were written, blank or not
            Assert.Equal(255, made.VariantImages[made.Manifest.Variants[0].Id][4, 4].G);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public async Task Overwrite_converts_the_layer_in_place_and_discards_its_colorization()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new ColorSaveDialogs(ColorSaveChoice.Overwrite);
        try
        {
            using (var vm = Editor(f, dialogs))
            {
                vm.SetPaintColorCommand.Execute(null);
                vm.ActiveTool = EditorTool.Fill;
                vm.BrushHue = 240; vm.BrushSat = 100; vm.BrushValue = 255;
                vm.ApplyToolStroke(new[] { (0, 0) });
                await vm.SaveCommand.ExecuteAsync(null);
            }

            using var book = CookBookArchive.Read(f.path);
            var ing = Assert.Single(book.Recipes[0].Ingredients);
            Assert.Equal("aura", ing.Manifest.Id);
            Assert.Equal(LayerKind.Custom, ing.Manifest.Kind);
            Assert.Null(ing.Manifest.Colorization);
            var px = ing.VariantImages["glow"][4, 4];
            Assert.Equal(0, px.R); Assert.Equal(0, px.G); Assert.Equal(255, px.B);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    [AvaloniaFact]
    public async Task Cancelling_the_dialog_writes_nothing_and_leaves_the_draft_editable()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new ColorSaveDialogs(ColorSaveChoice.Cancel);
        try
        {
            using (var vm = Editor(f, dialogs))
            {
                vm.SetPaintColorCommand.Execute(null);
                vm.ActiveTool = EditorTool.Fill;
                vm.BrushHue = 120; vm.BrushSat = 100; vm.BrushValue = 255;
                vm.ApplyToolStroke(new[] { (0, 0) });

                await vm.SaveCommand.ExecuteAsync(null);

                Assert.Equal(1, dialogs.Asked);
                Assert.True(vm.IsDirty);                 // nothing was written, so nothing is clean
                Assert.True(vm.CanPaintGrayscale);       // and the draft was NOT half-converted
                Assert.NotNull(vm.SaveNoteText);
            }

            using var book = CookBookArchive.Read(f.path);
            var ing = Assert.Single(book.Recipes[0].Ingredients);
            Assert.Equal(LayerKind.Dynamic, ing.Manifest.Kind);
            Assert.Equal(0, ing.VariantImages["glow"][4, 4].A);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>Each surface keeps its own stack, so undoing in colour must not walk back value-map
    /// strokes made before the mode was switched.</summary>
    [AvaloniaFact]
    public void Undo_follows_the_mode_and_never_crosses_between_the_two_surfaces()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });          // one grayscale stroke

            vm.SetPaintColorCommand.Execute(null);
            Assert.False(vm.UndoCommand.CanExecute(null)); // the colour stack is empty

            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.UndoCommand.CanExecute(null));
            vm.UndoCommand.Execute(null);

            Assert.Equal(200, vm.ColorAt(4, 4).R);         // back to the lifted grey, not further
            Assert.Equal(200, vm.ValueAt(4, 4));           // and the value-map never moved

            vm.SetPaintGrayscaleCommand.Execute(null);
            Assert.True(vm.UndoCommand.CanExecute(null));  // the grayscale stroke is still undoable
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    // A book whose manifest carries its own palette, so the two scopes can be told apart.
    private static (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing)
        OnDiskWithPalette(IReadOnlyList<string> specs)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "book.cbk");
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("aura", "Aura", LayerKind.Dynamic,
                new Colorization(ColorModel.Hsv, 12, 4,
                    new[] { new ColorEntry(1, new ColorRange(0, 360, 40, 100), null) }),
                new[] { new Variant("glow", "Glow", 1) }),
            VariantImages = new Dictionary<string, SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>>
            { ["glow"] = new(8, 8) },
        };
        var recipe = new LoadedRecipe
        {
            Manifest = new RecipeManifest("cat", "Cat", new[] { "aura" }, Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { ing },
        };
        var manifest = new CookBookManifest("cb", "Book", new Dimensions(8, 8),
            new Collection("Book", "", "B"), new Dictionary<string, double> { ["cat"] = 100 },
            Palette: specs);
        CookBookArchive.Write(path, manifest, new[] { recipe });
        var book = CookBookArchive.Read(path);
        var session = new CookBookSession();
        session.Open(book, path);
        var r = book.Recipes[0];
        return (path, session, r, r.Ingredients[0]);
    }
}
