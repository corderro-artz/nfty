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
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Color mode in the editor: the palette strip, the opacity lock, and what Save does with color
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

    /// <summary>Answers the color-save dialog with a fixed choice and counts how often it was asked;
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

    /// <summary>Confirms every dialog, and counts them. Needed wherever a test crosses a gate that
    /// asks — <see cref="FakeDialogs"/> answers <c>default</c>, which for a confirm is NO.</summary>
    private sealed class Confirming : IDialogService
    {
        public int Asked { get; private set; }
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }
        public Task<TResult?> ShowAsync<TResult>(ViewModelBase d)
        {
            Asked++;
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

    private sealed class OnePicker(string? open) : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult(open);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }

    private static string WriteGrayPng(int w, int h, byte value)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "in.png");
        using var img = new Image<Rgba32>(w, h, new Rgba32(value, value, value, 255));
        img.Save(path);
        return path;
    }

    private static IngredientEditorViewModel EditorWithPicker(
        (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) f,
        IFilePickerService picker) =>
        new(f.ing, f.recipe, f.session.Current!, new ImageBridge(), new FakeNav(),
            f.session, new FakeDialogs(), picker);

    private static IngredientEditorViewModel Editor(
        (string path, CookBookSession session, LoadedRecipe recipe, LoadedIngredient ing) f,
        IDialogService? dialogs = null, IPaletteService? palette = null) =>
        new(f.ing, f.recipe, f.session.Current!, new ImageBridge(), new FakeNav(),
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

    /// <summary>A value-map stores lightness and nothing else, so a color swatch picked in grayscale
    /// mode has to become its lightness. Refusing the click instead would leave saved swatches
    /// visibly present and silently inert.</summary>
    [AvaloniaFact]
    public void A_color_swatch_picked_in_grayscale_mode_becomes_its_lightness()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f);
            Assert.False(vm.IsColorMode);

            vm.PickSwatchCommand.Execute(new PaletteSwatch(new RgbColor(0, 255, 0)));

            // BT.709 luminance of pure green, the same reduction a color PNG import goes through.
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
            Assert.Contains(armed, palette.SwatchesIn(PaletteMode.Color));
            var cell = Assert.Single(vm.SavedSwatches);
            Assert.Equal(armed, cell.Rgb);
            Assert.True(cell.CanForget);

            vm.SaveSwatchCommand.Execute(null);          // re-saving is a no-op, not a duplicate
            Assert.Single(vm.SavedSwatches);

            vm.ForgetSwatchCommand.Execute(cell);
            Assert.Empty(vm.SavedSwatches);
            Assert.Empty(palette.SwatchesIn(PaletteMode.Color));
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
        palette.Add(new RgbColor(0xAA, 0xBB, 0xCC), PaletteMode.Color);
        try
        {
            using var vm = Editor(f, palette: palette);
            vm.PaintMode = PaletteMode.Color;      // both swatches carry a hue

            Assert.Equal(2, vm.SavedSwatches.Count);
            Assert.Equal(new RgbColor(0x11, 0x22, 0x33), vm.SavedSwatches[0].Rgb);
            Assert.False(vm.SavedSwatches[0].CanForget);
            Assert.True(vm.SavedSwatches[1].CanForget);
            Assert.Null(vm.SavedSwatches[0].ForgetCommand);
            Assert.NotNull(vm.SavedSwatches[1].ForgetCommand);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>
    /// THE SAVED RUN SWAPS WITH THE MODE, and a book's own palette is routed the same way.
    /// </summary>
    /// <remarks>
    /// It did not, and the ramp above it did: the strip changed half its colors on a mode switch and
    /// kept the other half, so painting a value-map was done over a row of saturated cells that each
    /// silently armed a gray — several of them the SAME gray. A book records no mode with its
    /// palette, so <c>Palette.InMode</c> routes it by grayness, which is where saving it in either
    /// mode would have put it.
    /// </remarks>
    [AvaloniaFact]
    public void The_saved_run_holds_only_what_the_mode_can_paint_and_swaps_with_it()
    {
        var f = OnDiskWithPalette(new[] { "hex:112233", "hex:646464" });
        var palette = new PaletteService(StateStore.InMemory());
        palette.Add(new RgbColor(0xAA, 0xBB, 0xCC), PaletteMode.Color);
        palette.Add(new RgbColor(0x20, 0x20, 0x20), PaletteMode.Grayscale);
        try
        {
            using var vm = Editor(f, palette: palette);

            Assert.False(vm.IsColorMode);
            Assert.Equal(new[] { new RgbColor(0x64, 0x64, 0x64), new RgbColor(0x20, 0x20, 0x20) },
                vm.SavedSwatches.Select(s => s.Rgb));

            vm.SetPaintColorCommand.Execute(null);

            Assert.Equal(new[] { new RgbColor(0x11, 0x22, 0x33), new RgbColor(0xAA, 0xBB, 0xCC) },
                vm.SavedSwatches.Select(s => s.Rgb));
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>Saving writes to the palette for the mode in force, so a color mixed in one mode
    /// never turns up in the other's row.</summary>
    [AvaloniaFact]
    public void A_swatch_is_saved_into_the_palette_for_the_mode_it_was_mixed_in()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var palette = new PaletteService(StateStore.InMemory());
        try
        {
            using var vm = Editor(f, dialogs: new Confirming(), palette: palette);

            vm.BrushValue = 90;                       // grayscale: the armed color is gray 90
            vm.SaveSwatchCommand.Execute(null);

            vm.SetPaintColorCommand.Execute(null);
            vm.BrushHue = 200; vm.BrushSat = 80; vm.BrushValue = 255;
            var mixed = vm.CurrentRgb;
            vm.SaveSwatchCommand.Execute(null);

            Assert.Equal(new[] { new RgbColor(90, 90, 90) }, palette.SwatchesIn(PaletteMode.Grayscale));
            Assert.Equal(new[] { mixed }, palette.SwatchesIn(PaletteMode.Color));
            Assert.Equal(new[] { mixed }, vm.SavedSwatches.Select(s => s.Rgb));
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>
    /// Leaving color mode with color strokes on the canvas asks first, and CANCELING keeps color.
    /// </summary>
    /// <remarks>
    /// The direction is the point. Gray to color is a widening and loses nothing, so it asks
    /// nothing; color to gray loses no pixel either — which is exactly why it needed saying, because
    /// the layer is a value-map again and Save writes the value-map. Nothing on the screen said so
    /// once the canvas was back in grays.
    /// </remarks>
    [AvaloniaFact]
    public async Task Leaving_color_with_color_art_asks_first_and_canceling_stays_in_color()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var refusing = new RefusingDialogs();
        try
        {
            using var vm = Editor(f, dialogs: refusing);
            vm.SetPaintColorCommand.Execute(null);
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });

            await vm.SetPaintGrayscaleCommand.ExecuteAsync(null);

            Assert.Equal(1, refusing.Asked);
            Assert.True(vm.IsColorMode);              // a GATE, not a notice after the fact
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>With nothing painted in color there is nothing to leave behind, so the switch is
    /// silent — and entering color mode never asks at all, because widening loses nothing.</summary>
    [AvaloniaFact]
    public async Task Switching_modes_with_no_color_art_asks_nothing_in_either_direction()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new Confirming();
        try
        {
            using var vm = Editor(f, dialogs: dialogs);

            vm.SetPaintColorCommand.Execute(null);
            await vm.SetPaintGrayscaleCommand.ExecuteAsync(null);

            Assert.Equal(0, dialogs.Asked);
            Assert.False(vm.IsColorMode);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>Asked ONCE per editor session, like the partial-alpha warning: what Save writes does
    /// not become more true the second time the mode is flipped.</summary>
    [AvaloniaFact]
    public async Task The_warning_about_leaving_color_is_asked_at_most_once()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var dialogs = new Confirming();
        try
        {
            using var vm = Editor(f, dialogs: dialogs);
            vm.SetPaintColorCommand.Execute(null);
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });

            await vm.SetPaintGrayscaleCommand.ExecuteAsync(null);
            Assert.False(vm.IsColorMode);

            vm.SetPaintColorCommand.Execute(null);
            await vm.SetPaintGrayscaleCommand.ExecuteAsync(null);

            Assert.Equal(1, dialogs.Asked);
            Assert.False(vm.IsColorMode);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); }
    }

    /// <summary>
    /// And the note stays after the warning is gone: back in gray mode with color art pending, the
    /// footer says what Save is about to write.
    /// </summary>
    /// <remarks>
    /// The one-off dialog cannot carry this — it is shown once and dismissed, and the state it
    /// describes lasts for the rest of the session. The note is the line that persists.
    /// </remarks>
    [AvaloniaFact]
    public async Task The_save_note_says_the_value_map_does_not_carry_the_color_strokes()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        try
        {
            using var vm = Editor(f, dialogs: new Confirming());
            Assert.Null(vm.SaveNoteText);

            vm.SetPaintColorCommand.Execute(null);
            Assert.Contains("Custom ingredient", vm.SaveNoteText);

            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            await vm.SetPaintGrayscaleCommand.ExecuteAsync(null);

            Assert.False(vm.IsColorMode);
            Assert.Contains("value-map", vm.SaveNoteText);
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

    // ---------------- saving color art ----------------

    [AvaloniaFact]
    public void Switching_to_color_carries_the_existing_drawing_over_as_gray()
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

    /// <summary>A save writes the WHOLE ingredient, so entering color mode has to widen every
    /// variant — not only the one on screen. A variant the author never visited would otherwise reach
    /// the exporter with no color raster and take the save down with it.</summary>
    [AvaloniaFact]
    public async Task Every_variant_gets_color_art_even_the_ones_never_visited()
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
    public async Task Canceling_the_dialog_writes_nothing_and_leaves_the_draft_editable()
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

    /// <summary>An auto-widened color raster is a copy of a value-map that no longer exists once
    /// the value-map is replaced by an import — so it must not survive to be shown as artwork.</summary>
    [AvaloniaFact]
    public async Task Importing_into_the_value_map_drops_a_color_raster_nobody_painted_on()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var png = WriteGrayPng(8, 8, 90);
        try
        {
            using var vm = EditorWithPicker(f, new OnePicker(png));

            vm.ActiveTool = EditorTool.Fill; vm.BrushValue = 200;
            vm.ApplyToolStroke(new[] { (0, 0) });
            vm.SetPaintColorCommand.Execute(null);       // widens: the color raster is gray 200
            Assert.Equal(200, vm.ColorAt(4, 4).R);

            vm.SetPaintGrayscaleCommand.Execute(null);
            await vm.ImportImageCommand.ExecuteAsync(null);   // the value-map becomes gray 90
            Assert.Equal(90, vm.ValueAt(4, 4));

            vm.SetPaintColorCommand.Execute(null);
            Assert.Equal(90, vm.ColorAt(4, 4).R);        // re-widened, not the stale 200
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); File.Delete(png); }
    }

    /// <summary>The other half of the rule: color art the author actually painted is their work, and
    /// importing a value-map — a different surface entirely — is no reason to throw it away.</summary>
    [AvaloniaFact]
    public async Task Importing_into_the_value_map_keeps_color_art_that_was_painted()
    {
        var f = IngredientEditorSaveTests.OnDisk(LayerKind.Dynamic);
        var png = WriteGrayPng(8, 8, 90);
        try
        {
            // Confirming, not FakeDialogs: there IS color art here, so leaving color mode asks, and
            // a fake that answers default would refuse the switch and leave the test in color mode.
            using var vm = new IngredientEditorViewModel(f.ing, f.recipe, f.session.Current!,
                new ImageBridge(), new FakeNav(), f.session, new Confirming(), new OnePicker(png));

            vm.SetPaintColorCommand.Execute(null);
            vm.ActiveTool = EditorTool.Fill;
            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });        // a real color stroke
            Assert.Equal(255, vm.ColorAt(4, 4).R);

            await vm.SetPaintGrayscaleCommand.ExecuteAsync(null);
            await vm.ImportImageCommand.ExecuteAsync(null);
            Assert.Equal(90, vm.ValueAt(4, 4));

            vm.SetPaintColorCommand.Execute(null);
            var kept = vm.ColorAt(4, 4);
            Assert.Equal(255, kept.R); Assert.Equal(0, kept.G); Assert.Equal(0, kept.B);
        }
        finally { f.session.Dispose(); Directory.Delete(Path.GetDirectoryName(f.path)!, true); File.Delete(png); }
    }

    /// <summary>Each surface keeps its own stack, so undoing in color must not walk back value-map
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
            Assert.False(vm.UndoCommand.CanExecute(null)); // the color stack is empty

            vm.BrushHue = 0; vm.BrushSat = 100; vm.BrushValue = 255;
            vm.ApplyToolStroke(new[] { (0, 0) });
            Assert.True(vm.UndoCommand.CanExecute(null));
            vm.UndoCommand.Execute(null);

            Assert.Equal(200, vm.ColorAt(4, 4).R);         // back to the lifted gray, not further
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
