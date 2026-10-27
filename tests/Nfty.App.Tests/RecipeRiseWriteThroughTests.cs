using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The recipe source writing through: picking a layer by its pixel, painting that layer's own variant, the
/// union following, and one save for every variant that changed.</summary>
public class RecipeRiseWriteThroughTests
{
    /// <summary>A session that runs a callback when the save swaps the book in: the moment the archive is
    /// provably written and the save provably still in flight.</summary>
    private sealed class HookedSession(CookBookSession inner) : ICookBookSession
    {
        public Action? OnReplace;
        public int Replaces;
        public LoadedCookBook? Current => inner.Current;
        public string? SourcePath => inner.SourcePath;
        public event Action? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
        public void Open(LoadedCookBook book, string? sourcePath = null) => inner.Open(book, sourcePath);
        public void Replace(LoadedCookBook book) { inner.Replace(book); Replaces++; OnReplace?.Invoke(); }
        public void Close() => inner.Close();
        public void Dispose() => inner.Dispose();
    }

    private sealed class Rig : IDisposable
    {
        public required HookedSession Session { get; init; }
        public required CookBookSession Inner { get; init; }
        public required string Path { get; init; }
        public required RecipeRiseSource Src { get; init; }
        public bool Editing = true;
        public int Saved;
        public RiseSubject Subject => Src.Open(Src.Items[0]);
        public IDepthEditor Editor => Src.EditorFor(Subject);

        public void Dispose()
        {
            Src.Dispose();
            Inner.Dispose();
        }
    }

    /// <summary>Oak (1 front, 9 back) everywhere under a gold strap (2 front, 0 back).</summary>
    private static Rig Open(bool ghost = false, bool deep = true, bool session = true, bool pine = false)
    {
        var book = RiseFixtures.ChestBook(
            oakDepth: deep ? RiseFixtures.Flat(1, 9) : null, goldDepth: deep ? RiseFixtures.Flat(2, 0) : null,
            withGhost: ghost, withPine: pine);
        var seed = RiseFixtures.OakSeed(book, book.Recipes[0]);
        var (inner, path) = RiseFixtures.OnDisk(book);
        var hooked = new HookedSession(inner);
        var recipe = inner.Current!.Recipes[0];
        var flag = new bool[] { true };
        Rig? rig = null;
        var src = session
            ? new RecipeRiseSource(inner.Current!, recipe, seed, new ImageBridge(), hooked, () => rig!.Editing)
            : new RecipeRiseSource(inner.Current!, recipe, seed, new ImageBridge());
        rig = new Rig { Session = hooked, Inner = inner, Path = path, Src = src };
        src.Saved += _ => rig.Saved++;
        return rig;
    }

    private static DepthStep Step(DepthSide side, int delta, params (int x, int y)[] px) => new(px, side, delta);

    private static LoadedIngredient Ing(LoadedCookBook b, string id) =>
        b.Recipes[0].Ingredients.Single(i => i.Manifest.Id == id);

    [AvaloniaFact]
    public void Left_picks_the_front_owner_and_right_picks_the_back_owner()
    {
        using var rig = Open();
        Assert.True(rig.Src.TryPick(3, 3, DepthSide.Front));
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.True(rig.Src.TryPick(3, 3, DepthSide.Back));
        Assert.Equal("Body / Oak", rig.Src.Target!.Name);
        Assert.Contains(rig.Src.Layers, r => r is { IngredientId: "body", IsTarget: true });
        Assert.Contains(rig.Src.Layers, r => r is { IngredientId: "lock", IsTarget: false });
    }

    [AvaloniaFact]
    public void On_a_paper_stack_both_buttons_pick_the_topmost_solid_layer()
    {
        using var rig = Open(deep: false);
        Assert.True(rig.Src.TryPick(3, 3, DepthSide.Front));
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.True(rig.Src.TryPick(2, 2, DepthSide.Back));       // body only: nothing above it
        Assert.Equal("Body / Oak", rig.Src.Target!.Name);
        Assert.True(rig.Src.TryPick(3, 3, DepthSide.Back));       // both layers are 0/0: the tie goes to the upper one
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.False(rig.Src.TryPick(0, 0, DepthSide.Front));     // clear canvas
        Assert.False(rig.Src.TryPick(99, 99, DepthSide.Front));   // outside it
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);        // a refused pick keeps the target
    }

    [AvaloniaFact]
    public void Picking_changes_no_depth()
    {
        using var rig = Open();
        Assert.True(rig.Src.TryPick(3, 3, DepthSide.Front));
        Assert.True(rig.Src.TryPick(2, 2, DepthSide.Front));
        Assert.Equal(0, rig.Src.DirtyCount);
        Assert.False(rig.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void An_edit_changes_only_the_targets_own_map_and_the_union_follows()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        Assert.True(rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3))));

        Assert.Equal(new DepthPixel(3, 0), rig.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(new DepthPixel(3, 9), rig.Subject.Depth.Get(3, 3));      // union: gold's front, oak's back
        Assert.False(rig.Subject.IsDirty);                                      // a derived union is never unsaved work
        Assert.Equal(1, rig.Src.DirtyCount);
        rig.Src.TryPick(3, 3, DepthSide.Back);                                  // the oak: untouched
        Assert.Equal(new DepthPixel(1, 9), rig.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(1, rig.Src.DirtyCount);
    }

    [AvaloniaFact]
    public void A_stroke_over_dimmed_pixels_changes_only_the_targets_solid_pixels()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        // (1,2) is body only, (3,3) is gold: only the gold pixel can move on the gold layer.
        Assert.True(rig.Editor.Do(Step(DepthSide.Front, +1, (1, 2), (3, 3))));

        Assert.Equal(new DepthPixel(3, 0), rig.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(new DepthPixel(0, 0), rig.Src.Target!.Surface.Get(1, 2));
        Assert.Equal(new DepthPixel(1, 9), rig.Subject.Depth.Get(1, 2));
        Assert.False(rig.Src.IsTargetSolid(1, 2));
        Assert.True(rig.Src.IsTargetSolid(3, 3));
    }

    [AvaloniaFact]
    public void Undo_crosses_layers_and_retargets_to_the_layer_it_touched()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));       // gold
        rig.Src.TryPick(2, 2, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (2, 2)));       // oak
        Assert.Equal("Body / Oak", rig.Src.Target!.Name);
        rig.Src.TryPick(3, 3, DepthSide.Front);                 // look away: back on gold

        rig.Editor.Undo();
        Assert.Equal("Body / Oak", rig.Src.Target!.Name);
        Assert.Equal(new DepthPixel(1, 9), rig.Src.Target!.Surface.Get(2, 2));
        Assert.Equal(new DepthPixel(1, 9), rig.Subject.Depth.Get(2, 2));

        rig.Editor.Undo();
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.Equal(new DepthPixel(2, 0), rig.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(0, rig.Src.DirtyCount);

        rig.Editor.Redo();
        Assert.Equal(new DepthPixel(3, 0), rig.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(new DepthPixel(3, 9), rig.Subject.Depth.Get(3, 3));
    }

    [AvaloniaFact]
    public void A_reroll_keeps_unsaved_depth_on_its_variant()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        int seed = rig.Src.Seed;

        rig.Src.Reroll();

        Assert.Equal(seed + 1, rig.Src.Seed);
        Assert.Equal(1, rig.Src.DirtyCount);
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);                       // still its variant: still the target
        Assert.Equal(new DepthPixel(3, 9), rig.Subject.Depth.Get(3, 3));         // its depth is back in the new union
        Assert.True(rig.Src.Layers.Single(r => r.IngredientId == "lock").IsTarget);
    }

    [AvaloniaFact]
    public void A_reroll_that_drops_the_targets_variant_keeps_its_depth_and_clears_the_target()
    {
        using var rig = Open(ghost: true);
        rig.Src.TryPick(2, 2, DepthSide.Front);
        Assert.Equal("Body / Oak", rig.Src.Target!.Name);
        rig.Editor.Do(Step(DepthSide.Front, +1, (2, 2)));

        for (int i = 0; i < 50 && rig.Src.Layers[0].VariantName != "Ghost"; i++) rig.Src.Reroll();
        Assert.Equal("Ghost", rig.Src.Layers[0].VariantName);
        Assert.Null(rig.Src.Target);
        Assert.Equal(1, rig.Src.DirtyCount);

        for (int i = 0; i < 50 && rig.Src.Layers[0].VariantName != "Oak"; i++) rig.Src.Reroll();
        Assert.Equal("Oak", rig.Src.Layers[0].VariantName);
        Assert.Equal(1, rig.Src.DirtyCount);
        Assert.Equal(2, rig.Subject.Depth.Get(2, 2).Front);                       // the oak's painting came back with it
    }

    [AvaloniaFact]
    public void Undo_of_a_layer_the_roll_lacks_restores_it_and_says_so()
    {
        using var rig = Open(ghost: true);
        rig.Src.TryPick(2, 2, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (2, 2)));
        for (int i = 0; i < 50 && rig.Src.Layers[0].VariantName != "Ghost"; i++) rig.Src.Reroll();
        Assert.Null(rig.Src.Target);

        rig.Editor.Undo();

        Assert.Equal(0, rig.Src.DirtyCount);                                      // the oak's depth is back to what the book holds
        Assert.Null(rig.Src.Target);                                              // and the page did not jump to a layer it is not showing
        Assert.Contains("Body / Oak", rig.Src.LastUndoNote);
        rig.Editor.Redo();
        Assert.Equal(1, rig.Src.DirtyCount);
    }

    [AvaloniaFact]
    public async Task Save_writes_every_dirty_variant_in_one_persist_and_raises_saved_once()
    {
        using var rig = Open(ghost: true);
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        rig.Src.TryPick(2, 2, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (2, 2)));
        Assert.Equal(2, rig.Src.DirtyCount);

        await rig.Src.SaveAsync();

        Assert.Equal(1, rig.Session.Replaces);
        Assert.Equal(1, rig.Saved);
        Assert.Equal(0, rig.Src.DirtyCount);
        Assert.Equal(3, Ing(rig.Session.Current!, "lock").Depths["gold"].Front(3, 3));
        Assert.Equal(2, Ing(rig.Session.Current!, "body").Depths["oak"].Front(2, 2));
        Assert.False(Ing(rig.Session.Current!, "body").Depths.ContainsKey("ghost"));
        using var reread = CookBookArchive.Read(rig.Path);
        Assert.Equal(3, Ing(reread, "lock").Depths["gold"].Front(3, 3));
        Assert.Equal(2, Ing(reread, "body").Depths["oak"].Front(2, 2));
    }

    [AvaloniaFact]
    public async Task A_second_save_while_one_runs_joins_it_and_reroll_and_retarget_are_refused()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        Task? second = null;
        bool? retarget = null, pick = null;
        int seedBefore = rig.Src.Seed, seedDuring = -1;
        rig.Session.OnReplace = () =>
        {
            second = rig.Src.SaveAsync();
            retarget = rig.Src.SetTarget("body");
            pick = rig.Src.TryPick(2, 2, DepthSide.Front);
            rig.Src.Reroll();
            seedDuring = rig.Src.Seed;
        };

        var first = rig.Src.SaveAsync();
        await first;

        Assert.Same(first, second);
        Assert.False(retarget);
        Assert.False(pick);
        Assert.Equal(seedBefore, seedDuring);
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.Equal(1, rig.Session.Replaces);
        // and once it is over, both work again
        Assert.True(rig.Src.SetTarget("body"));
        rig.Src.Reroll();
        Assert.Equal(seedBefore + 1, rig.Src.Seed);
    }

    [AvaloniaFact]
    public async Task A_locked_source_refuses_save_and_writes_nothing()
    {
        using var rig = Open();
        rig.Editing = false;
        rig.Src.TryPick(3, 3, DepthSide.Front);
        Assert.True(rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3))));          // painting is scratch, and works
        var before = rig.Session.Current;
        var bytes = File.ReadAllBytes(rig.Path);

        Assert.True(rig.Src.IsLocked);
        Assert.False(rig.Src.CanSave);
        await rig.Src.SaveAsync();

        Assert.Same(before, rig.Session.Current);
        Assert.Equal(bytes, File.ReadAllBytes(rig.Path));
        Assert.Equal(1, rig.Src.DirtyCount);
        Assert.Equal(0, rig.Saved);
    }

    [AvaloniaFact]
    public async Task A_save_refuses_when_a_painted_variants_picture_changed()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        // Another route rewrote the gold variant's picture after Rise opened it.
        var book = rig.Session.Current!;
        var gold = Ing(book, "lock");
        var changed = new LoadedIngredient
        {
            Manifest = gold.Manifest,
            VariantImages = new System.Collections.Generic.Dictionary<string, Image<Rgba32>>
            {
                ["gold"] = RiseFixtures.Picture(RiseFixtures.Oak),
            },
            Depths = gold.Depths,
        };
        rig.Session.Replace(CookBookEdits.UpsertIngredient(book, "chest", changed));
        var swapped = rig.Session.Current;
        var bytes = File.ReadAllBytes(rig.Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Src.SaveAsync());

        Assert.Contains("Gold", ex.Message, StringComparison.Ordinal);
        Assert.Same(swapped, rig.Session.Current);
        Assert.Equal(bytes, File.ReadAllBytes(rig.Path));
        Assert.Equal(0, rig.Saved);
        Assert.Equal(1, rig.Src.DirtyCount);          // still unsaved, and says so
    }

    [AvaloniaFact]
    public async Task After_a_save_the_source_reads_the_live_book()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        await rig.Src.SaveAsync();
        var afterFirst = rig.Session.Current;

        rig.Src.TryPick(2, 2, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (2, 2)));
        await rig.Src.SaveAsync();

        Assert.NotSame(afterFirst, rig.Session.Current);
        Assert.Equal(2, rig.Saved);
        using var reread = CookBookArchive.Read(rig.Path);
        Assert.Equal(3, Ing(reread, "lock").Depths["gold"].Front(3, 3));          // the first save's depth survived the second
        Assert.Equal(2, Ing(reread, "body").Depths["oak"].Front(2, 2));
    }

    [AvaloniaFact]
    public void A_recombine_keeps_the_previous_texture_alive_until_the_next()
    {
        using var rig = Open();
        var first = rig.Subject.Texture;
        rig.Src.TryPick(3, 3, DepthSide.Front);

        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        Assert.NotSame(first, rig.Subject.Texture);
        _ = first[0, 0];                                                          // the page may still be reading it

        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        Assert.Throws<ObjectDisposedException>(() => first[0, 0]);
    }

    [AvaloniaFact]
    public void Without_a_session_the_source_stays_read_only()
    {
        using var rig = Open(session: false);
        Assert.False(rig.Src.IsEditable);
        Assert.Equal("combined depth · read-only", rig.Src.ReadOnlyPill);
        Assert.False(rig.Src.TryPick(3, 3, DepthSide.Front));
        Assert.False(rig.Src.SetTarget("lock"));
        Assert.Null(rig.Src.Target);
        Assert.False(rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3))));
        Assert.Equal(0, rig.Src.DirtyCount);
    }

    [AvaloniaFact]
    public void Stepping_walks_the_layers_present_and_the_reach_and_dim_art_follow_the_target()
    {
        using var rig = Open();
        Assert.Null(rig.Src.TargetReach);
        Assert.Null(rig.Src.BelowArt);

        Assert.True(rig.Src.StepTarget(+1));
        Assert.Equal("Body / Oak", rig.Src.Target!.Name);                         // from nothing, up starts at the bottom
        Assert.Equal(0, rig.Src.BelowArt![3, 3].A);                               // nothing under the bottom layer
        Assert.True(rig.Src.AboveArt![3, 3].A > 0);                               // the gold strap paints over it
        Assert.Equal("in every asset", rig.Src.TargetReach!.Text);                // one recipe, one variant

        Assert.True(rig.Src.StepTarget(+1));
        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.False(rig.Src.StepTarget(+1));                                     // the top is the end
        Assert.True(rig.Src.BelowArt![3, 3].A > 0);
        Assert.True(rig.Src.SetTarget(null));
        Assert.Null(rig.Src.Target);
        Assert.Null(rig.Src.AboveArt);
    }

    [AvaloniaFact]
    public void An_undo_that_retargets_never_raises_with_the_previous_undos_note()
    {
        using var rig = Open(pine: true);
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));                          // gold (on every roll)
        rig.Src.TryPick(2, 2, DepthSide.Back);
        rig.Editor.Do(Step(DepthSide.Back, +1, (2, 2)));                           // oak
        for (int i = 0; i < 60 && rig.Src.Layers[0].VariantName != "Pine"; i++) rig.Src.Reroll();
        Assert.Equal("Pine", rig.Src.Layers[0].VariantName);
        Assert.Null(rig.Src.Target);
        var seen = new System.Collections.Generic.List<string?>();
        rig.Src.Changed += () => seen.Add(rig.Src.LastUndoNote);

        rig.Editor.Undo();                                                          // the oak: not in this roll
        Assert.NotNull(rig.Src.LastUndoNote);
        seen.Clear();

        rig.Editor.Undo();                                                          // the gold: in this roll, a different target

        Assert.Equal("Lock / Gold", rig.Src.Target!.Name);
        Assert.Null(rig.Src.LastUndoNote);
        Assert.Equal(new string?[] { null }, seen);                                 // one raise, and it never showed the old sentence
    }

    [AvaloniaFact]
    public async Task A_save_refuses_when_a_painted_variants_depth_changed_in_the_book()
    {
        using var rig = Open();
        rig.Src.TryPick(3, 3, DepthSide.Front);
        rig.Editor.Do(Step(DepthSide.Front, +1, (3, 3)));
        // Another route stamped a different depth on the same variant after this edit began from the old one.
        var book = rig.Session.Current!;
        var gold = Ing(book, "lock");
        var restamped = new LoadedIngredient
        {
            Manifest = gold.Manifest,
            VariantImages = gold.VariantImages,
            Depths = new System.Collections.Generic.Dictionary<string, DepthMap> { ["gold"] = RiseFixtures.Flat(7, 7) },
        };
        rig.Session.Replace(CookBookEdits.UpsertIngredient(book, "chest", restamped));
        var bytes = File.ReadAllBytes(rig.Path);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Src.SaveAsync());

        Assert.Contains("Gold", ex.Message, StringComparison.Ordinal);
        Assert.Contains("depth", ex.Message, StringComparison.Ordinal);
        Assert.Equal(bytes, File.ReadAllBytes(rig.Path));
        Assert.Equal(0, rig.Saved);
        Assert.Equal(1, rig.Src.DirtyCount);
    }

    private static void SamePixelsOrFail(Image<Rgba32> expected, Image<Rgba32>? actual, string what)
    {
        Assert.NotNull(actual);
        Assert.Equal((expected.Width, expected.Height), (actual!.Width, actual.Height));
        for (int y = 0; y < expected.Height; y++)
            for (int x = 0; x < expected.Width; x++)
                Assert.True(expected[x, y] == actual[x, y], $"{what}: pixel ({x},{y}) is {actual[x, y]}, a PreviewLayer render makes {expected[x, y]}");
    }

    [AvaloniaFact]
    public void The_dim_art_is_the_picture_a_preview_layer_render_makes_of_the_same_layers()
    {
        // A DYNAMIC body under a Custom strap: the colorized layer is where a second rule would show.
        var book = RiseFixtures.DynamicBodyUnderStrap();
        var (inner, _) = RiseFixtures.OnDisk(book);
        using var owner = inner;
        var live = inner.Current!;
        var recipe = live.Recipes[0];
        const int seed = 5;
        using var src = new RecipeRiseSource(live, recipe, seed, new ImageBridge(), inner, () => true);

        using var asset = Generator.GenerateStreaming(live,
            new GenerateOptions(1, seed.ToString(System.Globalization.CultureInfo.InvariantCulture), "chest",
                EnforceUniqueDna: false)).First();
        var roll = asset.ColorRolls.Single(r => r.LayerId == "body");
        var body = new PreviewLayer(recipe.Ingredients.Single(i => i.Manifest.Id == "body"), "oak", null,
            new RolledColor(roll.H!.Value, roll.S!.Value));
        var strap = new PreviewLayer(recipe.Ingredients.Single(i => i.Manifest.Id == "lock"), "gold", null);
        var canvas = live.Manifest.Canvas;

        src.SetTarget("lock");
        using (var below = StackPreview.Render(canvas, new[] { body }))
        using (var target = StackPreview.Render(canvas, new[] { strap }))
        using (var above = StackPreview.Render(canvas, Array.Empty<PreviewLayer>()))
        {
            SamePixelsOrFail(below, src.BelowArt, "below the strap");
            SamePixelsOrFail(target, src.TargetArt, "the strap");
            SamePixelsOrFail(above, src.AboveArt, "above the strap");
        }

        src.SetTarget("body");
        using (var below = StackPreview.Render(canvas, Array.Empty<PreviewLayer>()))
        using (var target = StackPreview.Render(canvas, new[] { body }))
        using (var above = StackPreview.Render(canvas, new[] { strap }))
        {
            SamePixelsOrFail(below, src.BelowArt, "below the body");
            SamePixelsOrFail(target, src.TargetArt, "the body");
            SamePixelsOrFail(above, src.AboveArt, "above the body");
        }
    }
}
