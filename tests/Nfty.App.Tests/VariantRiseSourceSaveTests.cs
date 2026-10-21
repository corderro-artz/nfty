using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The failure and ordering edges of saving depth: the paths where a user's book is at risk.</summary>
public class VariantRiseSourceSaveTests
{
    /// <summary>A session that runs a callback when the save swaps the book in — the moment the archive is
    /// provably written and the save provably still in flight.</summary>
    private sealed class HookedSession : ICookBookSession
    {
        private readonly CookBookSession _inner;
        public Action? OnReplace;
        public HookedSession(CookBookSession inner) => _inner = inner;
        public LoadedCookBook? Current => _inner.Current;
        public string? SourcePath => _inner.SourcePath;
        public event Action? Changed { add => _inner.Changed += value; remove => _inner.Changed -= value; }
        public void Open(LoadedCookBook book, string? sourcePath = null) => _inner.Open(book, sourcePath);
        public void Replace(LoadedCookBook book) { _inner.Replace(book); OnReplace?.Invoke(); }
        public void Close() => _inner.Close();
        public void Dispose() => _inner.Dispose();
    }

    private static LoadedIngredient Body(LoadedCookBook b) =>
        b.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");

    private static VariantRiseSource SourceOn(ICookBookSession s, string variant = "oak")
    {
        var recipe = s.Current!.Recipes[0];
        return new VariantRiseSource(recipe, recipe.Ingredients.Single(i => i.Manifest.Id == "body"), variant,
            new ImageBridge(), s, () => true);
    }

    private static void PaintOak(VariantRiseSource src) =>
        src.Open(src.Items[0]).History.Do(new DepthStep(new[] { (2, 1) }, DepthSide.Front, +1), src.Open(src.Items[0]).Depth);

    [AvaloniaFact]
    public async Task A_second_save_during_a_save_gets_the_same_task_not_a_silent_success()
    {
        var (inner, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var hooked = new HookedSession(inner);
        using var src = SourceOn(hooked);
        PaintOak(src);
        Task? second = null;
        bool secondDoneAtCall = true;
        hooked.OnReplace = () => { second = src.SaveAsync(); secondDoneAtCall = second.IsCompleted; };

        var first = src.SaveAsync();
        await first;

        Assert.NotNull(second);
        Assert.False(secondDoneAtCall);          // it was handed the running save, not a finished no-op
        Assert.Same(first, second);
        Assert.False(src.IsDirty);
        using var reread = CookBookArchive.Read(path);
        Assert.Equal(1, Body(reread).Depths["oak"].Front(2, 1));
        inner.Dispose();
    }

    [AvaloniaFact]
    public async Task A_second_caller_sees_the_failure_of_the_save_it_joined()
    {
        var (inner, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var hooked = new HookedSession(inner);
        using var src = SourceOn(hooked);
        PaintOak(src);
        Task? second = null;
        hooked.OnReplace = () => { second = src.SaveAsync(); throw new IOException("disk gone"); };

        await Assert.ThrowsAsync<IOException>(() => src.SaveAsync());

        Assert.NotNull(second);
        await Assert.ThrowsAsync<IOException>(() => second!);
        // and the marker is cleared: a later save starts a new write instead of replaying the failure
        hooked.OnReplace = null;
        await src.SaveAsync();
        inner.Dispose();
    }

    [AvaloniaFact]
    public async Task Subjects_are_marked_saved_before_the_Saved_handler_runs_so_a_throwing_handler_cannot_leave_them_dirty()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = SourceOn(s);
        PaintOak(src);
        bool dirtyInsideHandler = true;
        src.Saved += _ => { dirtyInsideHandler = src.IsDirty; throw new InvalidOperationException("handler"); };

        await Assert.ThrowsAsync<InvalidOperationException>(() => src.SaveAsync());

        Assert.False(dirtyInsideHandler);
        Assert.False(src.IsDirty);
        using var reread = CookBookArchive.Read(path);
        Assert.Equal(1, Body(reread).Depths["oak"].Front(2, 1));
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task A_write_that_fails_midway_leaves_the_original_archive_and_the_session_untouched()
    {
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var before = File.ReadAllBytes(path);
        var bookBefore = s.Current;
        using var src = SourceOn(s);
        PaintOak(src);
        // Another layer's image is gone: the archive write opens its temp file, then cannot encode it.
        s.Current!.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "lock").VariantImages["gold"].Dispose();

        await Assert.ThrowsAnyAsync<Exception>(() => src.SaveAsync());

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.Same(bookBefore, s.Current);
        Assert.True(src.IsDirty);                // still unsaved, and says so
        s.Dispose();
    }

    [AvaloniaFact]
    public async Task Depth_under_transparent_pixels_is_zero_in_the_surface_and_on_disk()
    {
        // In memory, so the stored map really carries depth under transparent pixels (a map read from an
        // archive never does - the writer zeroes it - which would make this test vacuous).
        var all2 = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)2, 128).ToArray());
        using var book = RiseFixtures.ChestBook(oakDepth: all2);
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-").FullName, "Body.igt");
        using var session = new CookBookSession();
        using var src = new VariantRiseSource(book.Recipes[0], Body(book), "oak", new ImageBridge(), session, null, path);
        var oak = src.Open(src.Items[0]);
        var surface = oak.Depth.ToDepthMap()!;
        Assert.Equal(0, surface.Front(0, 0));    // '.' pixel: no body, no depth
        Assert.Equal(2, surface.Front(2, 1));    // 'A' pixel keeps its depth
        Assert.False(src.IsDirty);               // dropping it is not an edit
        PaintOak(src);

        await src.SaveAsync();

        using var reread = IngredientArchive.Read(path);
        var map = reread.Depths["oak"];
        Assert.Equal(0, map.Front(0, 0));
        Assert.Equal(0, map.Back(7, 7));
        Assert.Equal(3, map.Front(2, 1));
    }

    [AvaloniaFact]
    public async Task A_loose_ingredient_painted_back_to_paper_loses_its_stored_map()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-rise-").FullName;
        var path = Path.Combine(dir, "Body.igt");
        var oakDepth = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)1, 128).ToArray());
        using (var book = RiseFixtures.ChestBook(oakDepth))
            IngredientArchive.Write(path, Body(book).Manifest, Body(book).VariantImages, Body(book).Depths);
        var ing = IngredientArchive.Read(path);
        Assert.True(ing.Depths.ContainsKey("oak"));
        var wrap = LooseWorkspace.WrapIngredient(ing);
        using var session = new CookBookSession();
        using var src = new VariantRiseSource(wrap.Recipes[0], ing, "oak", new ImageBridge(), session, null, path);
        src.Open(src.Items[0]).History.Do(new DepthFillAll(0, 0), src.Open(src.Items[0]).Depth);

        await src.SaveAsync();

        using var reread = IngredientArchive.Read(path);
        Assert.False(reread.Depths.ContainsKey("oak"));
    }

    [AvaloniaFact]
    public async Task Saving_one_variant_does_not_overwrite_a_clean_variants_newer_stored_depth()
    {
        // Pins "dirty only": while the Rise page is open, something else (the Ingredient Editor) stores a
        // newer depth for ghost. Saving oak must keep it; a save that rewrote every subject would put
        // the page's stale paper back.
        var (s, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = SourceOn(s);
        var live = Body(s.Current!);
        var newer = DepthMap.FromBytes(8, 8, Enumerable.Repeat((byte)4, 128).ToArray());
        var edited = new LoadedIngredient
        {
            Manifest = live.Manifest,
            VariantImages = live.VariantImages,
            Depths = new System.Collections.Generic.Dictionary<string, DepthMap> { ["ghost"] = newer },
        };
        await CookBookPersistence.PersistAsync(s, CookBookEdits.UpsertIngredient(s.Current!, "chest", edited));
        PaintOak(src);

        await src.SaveAsync();

        using var reread = CookBookArchive.Read(path);
        var ghost = Body(reread).Depths["ghost"];
        Assert.Equal(4, ghost.Front(2, 1));
        Assert.Equal(1, Body(reread).Depths["oak"].Front(2, 1));
        s.Dispose();
    }

    [AvaloniaFact]
    public void The_base_source_answers_dirty_can_save_and_where_it_saves()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = SourceOn(s);
        Assert.False(src.IsDirty);
        Assert.True(src.CanSave);
        Assert.Equal("CookBook", src.SaveTargetName);
        PaintOak(src);
        Assert.True(src.IsDirty);
        s.Dispose();
    }

    [AvaloniaFact]
    public void A_locked_source_cannot_save()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = RiseFixtures.Variant(s, editing: false);
        Assert.False(src.CanSave);
        s.Dispose();
    }

    [AvaloniaFact]
    public void The_opening_line_names_the_lower_key_through_KeyHints()
    {
        var (s, _) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        using var src = SourceOn(s);
        Assert.Contains($"{KeyHints.LowerDepth} lowers", src.OpeningLine);
        s.Dispose();
    }
}
