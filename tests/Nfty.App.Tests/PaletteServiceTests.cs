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
/// may ever block or crash the editor — the discipline RecentsService already applied.</summary>
public class PaletteServiceTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory().FullName;

    [Fact]
    public void A_first_run_has_no_swatches()
    {
        var dir = TempDir();
        try { Assert.Empty(new PaletteService(StateStore.At(dir)).Swatches); }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Swatches_round_trip_through_the_store()
    {
        var dir = TempDir();
        try
        {
            var first = new PaletteService(StateStore.At(dir));
            first.Add(new RgbColor(214, 36, 159));
            first.Add(new RgbColor(61, 127, 143));

            var second = new PaletteService(StateStore.At(dir));

            Assert.Equal(first.Swatches, second.Swatches);
            Assert.Equal(new[] { new RgbColor(214, 36, 159), new RgbColor(61, 127, 143) }, second.Swatches);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_file_holds_prefixed_specs_so_it_stays_readable_by_hand()
    {
        var dir = TempDir();
        try
        {
            new PaletteService(StateStore.At(dir)).Add(new RgbColor(214, 36, 159));

            Assert.Contains("hex:d6249f", File.ReadAllText(Path.Combine(dir, PaletteService.FileName)));
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
            svc.Add(new RgbColor(1, 2, 3));
            svc.Add(new RgbColor(4, 5, 6));
            svc.Add(new RgbColor(1, 2, 3));

            Assert.Equal(new[] { new RgbColor(1, 2, 3), new RgbColor(4, 5, 6) }, svc.Swatches);
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
            svc.Add(new RgbColor(1, 2, 3));
            svc.Add(new RgbColor(4, 5, 6));

            svc.Remove(new RgbColor(1, 2, 3));
            svc.Remove(new RgbColor(9, 9, 9));

            Assert.Equal(new[] { new RgbColor(4, 5, 6) }, svc.Swatches);
            Assert.Equal(new[] { new RgbColor(4, 5, 6) }, new PaletteService(StateStore.At(dir)).Swatches);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[null]")]
    [InlineData("[1, 2, 3]")]
    public void A_corrupt_palette_file_loads_as_empty(string contents)
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, PaletteService.FileName), contents);

            Assert.Empty(new PaletteService(StateStore.At(dir)).Swatches);
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
                """["hex:d6249f", "no prefix", "hex:3d7f8f"]""");

            Assert.Equal(new[] { new RgbColor(214, 36, 159), new RgbColor(61, 127, 143) },
                new PaletteService(StateStore.At(dir)).Swatches);
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

            svc.Add(new RgbColor(1, 2, 3));   // must not throw

            Assert.Equal(new[] { new RgbColor(1, 2, 3) }, svc.Swatches);
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
            svc.Add(new RgbColor(214, 36, 159));
            Assert.True(store.Resolution.IsInMemory);

            Assert.True(store.Choose(chosen).Accepted);

            // On disk now, so the next launch finds them again by rule 2 or 3 — no pointer file.
            var moved = new PaletteService(StateStore.At(Path.Combine(chosen, StateStore.FolderName)));
            Assert.Equal(new[] { new RgbColor(214, 36, 159) }, moved.Swatches);
        }
        finally
        {
            foreach (var d in new[] { beside, working, chosen })
                try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        }
    }
}
