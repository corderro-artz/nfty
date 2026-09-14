using System;
using System.IO;
using Nfty.App.Services;
using Nfty.Core.Imaging;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The app-wide palette, persisted in the <c>.nfty</c> store.
///
/// Convenience state throughout: a corrupt file loads empty, a failed save is swallowed, and a store
/// with nowhere to write keeps the swatches for the session rather than refusing them. None of it
/// may ever block or crash the editor — the discipline RecentsService already applied.
///
/// <para>There is one palette PER MODE. A saved vermilion offered while painting a value-map is a
/// cell that silently arms a mid gray, so the two are kept apart and swap with the mode.</para></summary>
public class PaletteServiceTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory().FullName;

    private static readonly RgbColor Pink = new(214, 36, 159);
    private static readonly RgbColor Teal = new(61, 127, 143);
    private static readonly RgbColor Gray = new(61, 61, 61);
    private static readonly RgbColor Gray2 = new(200, 200, 200);

    [Fact]
    public void A_first_run_has_no_swatches_in_either_mode()
    {
        var dir = TempDir();
        try
        {
            var svc = new PaletteService(StateStore.At(dir));
            Assert.Empty(svc.SwatchesIn(PaletteMode.Grayscale));
            Assert.Empty(svc.SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Swatches_round_trip_through_the_store()
    {
        var dir = TempDir();
        try
        {
            var first = new PaletteService(StateStore.At(dir));
            first.Add(Pink, PaletteMode.Color);
            first.Add(Teal, PaletteMode.Color);
            first.Add(Gray, PaletteMode.Grayscale);

            var second = new PaletteService(StateStore.At(dir));

            Assert.Equal(new[] { Pink, Teal }, second.SwatchesIn(PaletteMode.Color));
            Assert.Equal(new[] { Gray }, second.SwatchesIn(PaletteMode.Grayscale));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>The two palettes are SEPARATE stores, not one list filtered two ways: saving in one
    /// mode must not put anything in the other, whatever the color happens to be.</summary>
    [Fact]
    public void A_swatch_saved_in_one_mode_is_not_in_the_other()
    {
        var dir = TempDir();
        try
        {
            var svc = new PaletteService(StateStore.At(dir));
            svc.Add(Pink, PaletteMode.Color);
            svc.Add(Gray, PaletteMode.Grayscale);

            Assert.DoesNotContain(Pink, svc.SwatchesIn(PaletteMode.Grayscale));
            Assert.DoesNotContain(Gray, svc.SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Forgetting_a_swatch_leaves_the_other_mode_alone()
    {
        var dir = TempDir();
        try
        {
            var svc = new PaletteService(StateStore.At(dir));
            svc.Add(Gray, PaletteMode.Grayscale);
            svc.Add(Pink, PaletteMode.Color);

            svc.Remove(Gray, PaletteMode.Color);          // not there; a no-op, not a cross-mode hit

            Assert.Equal(new[] { Gray }, svc.SwatchesIn(PaletteMode.Grayscale));
            Assert.Equal(new[] { Pink }, svc.SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_file_holds_prefixed_specs_so_it_stays_readable_by_hand()
    {
        var dir = TempDir();
        try
        {
            new PaletteService(StateStore.At(dir)).Add(Pink, PaletteMode.Color);

            var text = File.ReadAllText(Path.Combine(dir, PaletteService.FileName));
            Assert.Contains("hex:d6249f", text);
            Assert.Contains("color", text);               // and which palette it is in
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Adding_a_swatch_twice_neither_duplicates_nor_reorders_it()
    {
        var dir = TempDir();
        try
        {
            var svc = new PaletteService(StateStore.At(dir));
            svc.Add(Pink, PaletteMode.Color);
            svc.Add(Teal, PaletteMode.Color);
            svc.Add(Pink, PaletteMode.Color);

            Assert.Equal(new[] { Pink, Teal }, svc.SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Removing_a_swatch_persists_and_removing_an_absent_one_is_a_no_op()
    {
        var dir = TempDir();
        try
        {
            var svc = new PaletteService(StateStore.At(dir));
            svc.Add(Pink, PaletteMode.Color);
            svc.Add(Teal, PaletteMode.Color);

            svc.Remove(Pink, PaletteMode.Color);
            svc.Remove(new RgbColor(9, 9, 10), PaletteMode.Color);

            Assert.Equal(new[] { Teal }, svc.SwatchesIn(PaletteMode.Color));
            Assert.Equal(new[] { Teal },
                new PaletteService(StateStore.At(dir)).SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// A palette written before the modes existed was a bare JSON ARRAY, and it still loads.
    /// </summary>
    /// <remarks>
    /// The two shapes cannot be confused — an array is not an object — so the file needs no version
    /// field. Splitting by GRAYNESS is what <c>Palette.InMode</c> does for a CookBook's own palette,
    /// which also records no mode, so a migrated swatch lands where saving it today would put it.
    /// </remarks>
    [Fact]
    public void A_palette_written_before_the_modes_existed_splits_by_grayness()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, PaletteService.FileName),
                """["hex:d6249f", "hex:3d3d3d", "hex:3d7f8f", "hex:c8c8c8"]""");

            var svc = new PaletteService(StateStore.At(dir));

            Assert.Equal(new[] { Pink, Teal }, svc.SwatchesIn(PaletteMode.Color));
            Assert.Equal(new[] { Gray, Gray2 }, svc.SwatchesIn(PaletteMode.Grayscale));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{"grayscale": null, "color": null}""")]
    public void A_corrupt_palette_file_loads_as_empty(string contents)
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, PaletteService.FileName), contents);

            var svc = new PaletteService(StateStore.At(dir));
            Assert.Empty(svc.SwatchesIn(PaletteMode.Grayscale));
            Assert.Empty(svc.SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void One_unreadable_swatch_costs_only_itself()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, PaletteService.FileName),
                """{"color": ["hex:d6249f", "no prefix", "hex:3d7f8f"]}""");

            Assert.Equal(new[] { Pink, Teal },
                new PaletteService(StateStore.At(dir)).SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_failed_save_is_swallowed_and_the_session_keeps_its_swatches()
    {
        var dir = TempDir();
        try
        {
            // Pinned at a path that is a FILE: creating the folder and writing into it both fail.
            var blocked = Path.Combine(dir, "not-a-folder");
            File.WriteAllText(blocked, "x");
            var svc = new PaletteService(StateStore.At(blocked));

            svc.Add(Pink, PaletteMode.Color);   // must not throw

            Assert.Equal(new[] { Pink }, svc.SwatchesIn(PaletteMode.Color));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Swatches_saved_before_there_was_anywhere_to_write_move_in_when_a_folder_is_chosen()
    {
        var beside = TempDir();
        var working = TempDir();
        var chosen = TempDir();
        try
        {
            // Nowhere writable: a file sits on the .nfty name at both candidate roots.
            File.WriteAllText(Path.Combine(beside, StateStore.FolderName), "in the way");
            File.WriteAllText(Path.Combine(working, StateStore.FolderName), "in the way");
            var store = new StateStore(kitchen: null, beside: beside, working: working);
            var svc = new PaletteService(store);
            svc.Add(Pink, PaletteMode.Color);
            Assert.True(store.Resolution.IsInMemory);

            Assert.True(store.Choose(chosen).Accepted);

            // On disk now, so the next launch finds them again by rule 2 or 3 — no pointer file.
            var moved = new PaletteService(StateStore.At(Path.Combine(chosen, StateStore.FolderName)));
            Assert.Equal(new[] { Pink }, moved.SwatchesIn(PaletteMode.Color));
        }
        finally
        {
            foreach (var d in new[] { beside, working, chosen })
                try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        }
    }
}
