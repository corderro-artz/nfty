using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Rise page over a recipe that writes through: the first press picks, the next strokes paint only
/// that layer, the 3D tile keeps the whole character, and leaving asks about unsaved depth.</summary>
public class RiseWriteThroughPageTests
{
    private sealed class LeaveDialogs : IDialogService
    {
        public LeaveChoice Answer = LeaveChoice.Stay;
        public string? Message;
        public int Cards;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }

        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is LeaveDialogViewModel card)
            {
                Cards++;
                Message = card.Message;
                return Task.FromResult((TResult?)(object)Answer);
            }
            return Task.FromResult<TResult?>(default);
        }

        public void Close(object? result) { }
    }

    private sealed class Page : IDisposable
    {
        public required RiseViewModel Vm { get; init; }
        public required RecipeRiseSource Src { get; init; }
        public required CookBookSession Session { get; init; }
        public required FakeNav Nav { get; init; }
        public required LeaveDialogs Dialogs { get; init; }
        public required StatusService Status { get; init; }
        public required string Path { get; init; }

        public void Dispose()
        {
            Vm.Dispose();
            Session.Dispose();
        }
    }

    private static Page Open(int? supply = null, bool pine = false)
    {
        var book = RiseFixtures.ChestBook(oakDepth: RiseFixtures.Flat(1, 9), goldDepth: RiseFixtures.Flat(2, 0),
            withPine: pine, withGhost: !pine);
        if (supply is not null)
            book = new LoadedCookBook { Manifest = book.Manifest with { TargetSupply = supply }, Recipes = book.Recipes };
        var seed = RiseFixtures.OakSeed(book, book.Recipes[0]);
        var (session, path) = RiseFixtures.OnDisk(book);
        var src = new RecipeRiseSource(session.Current!, session.Current!.Recipes[0], seed, new ImageBridge(), session, () => true);
        var nav = new FakeNav();
        var dialogs = new LeaveDialogs();
        var status = new StatusService();
        var vm = new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), status);
        return new Page { Vm = vm, Src = src, Session = session, Nav = nav, Dialogs = dialogs, Status = status, Path = path };
    }

    private static (int x, int y)[] At(int x, int y) => new[] { (x, y) };

    private static void Click(RiseViewModel vm, int x, int y, DepthSide side = DepthSide.Front)
    {
        vm.PreviewDepthStroke(At(x, y), side, lower: false);
        vm.ApplyDepthStroke(At(x, y), side, lower: false);
    }

    [AvaloniaFact]
    public void The_first_click_picks_and_the_next_stroke_paints_the_target()
    {
        using var p = Open();
        Assert.True(p.Vm.IsPicking);
        Assert.Equal("click a pixel to choose its layer", p.Vm.TargetHeader);

        Click(p.Vm, 3, 3);

        Assert.StartsWith("editing Lock / Gold", p.Vm.TargetHeader, StringComparison.Ordinal);
        Assert.False(p.Vm.IsPicking);
        Assert.Equal(0, p.Vm.DirtyCount);               // the picking click painted nothing
        Assert.Equal(new DepthPixel(2, 0), p.Src.Target!.Surface.Get(3, 3));

        Click(p.Vm, 3, 3);

        Assert.Equal(new DepthPixel(3, 0), p.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(1, p.Vm.DirtyCount);
    }

    [AvaloniaFact]
    public void A_press_on_a_dimmed_pixel_does_nothing()
    {
        using var p = Open();
        Click(p.Vm, 3, 3);                              // gold is the target
        Assert.False(p.Vm.IsHittable(2, 3));            // body only: dimmed
        Assert.True(p.Vm.IsHittable(3, 3));
        // A fat brush pressed one pixel off the strap WOULD reach the strap's pixels - the press is what decides.
        p.Vm.BrushSize = 3;

        Click(p.Vm, 2, 3);

        Assert.Equal(0, p.Vm.DirtyCount);
        Assert.Equal(new DepthPixel(2, 0), p.Src.Target!.Surface.Get(3, 3));
        Assert.False(p.Vm.UndoCommand.CanExecute(null));
        Assert.Equal("Lock / Gold", p.Src.Target!.Name);
        Assert.Equal(new DepthPixel(1, 9), p.Vm.Subject.Depth.Get(1, 2));
    }

    /// <summary>Σ over triangles of a·(b×c)/6: the volume of a closed, consistently wound mesh.</summary>
    private static double Volume(Nfty.Model.Mesh m)
    {
        double v = 0;
        for (int t = 0; t < m.Indices.Count; t += 3)
        {
            Vector3 P(int k) { int i = (int)m.Indices[t + k] * 3; return new Vector3(m.Positions[i], m.Positions[i + 1], m.Positions[i + 2]); }
            v += Vector3.Dot(P(0), Vector3.Cross(P(1), P(2))) / 6.0;
        }
        return v;
    }

    [AvaloniaFact]
    public void The_3D_tile_shows_the_whole_character_while_a_layer_is_targeted()
    {
        using var p = Open();
        Click(p.Vm, 3, 3);                              // pick gold
        Click(p.Vm, 3, 3);                              // gold front 2 -> 3 at (3,3)

        // Oak is 1 front / 9 back everywhere it is solid, gold 2 (3 at one pixel) / 0 on its 2x2: the model is the UNION.
        double expected = 0;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                bool oak = RiseFixtures.Oak[y][x] != '.', gold = RiseFixtures.Gold[y][x] != '.';
                if (!oak && !gold) continue;
                int front = Math.Max(oak ? 1 : 0, gold ? (x == 3 && y == 3 ? 3 : 2) : 0);
                int back = oak ? 9 : 0;
                expected += 1 + front + back;
            }

        Assert.NotNull(p.Vm.Mesh);
        Assert.Equal(expected, Volume(p.Vm.Mesh!), 2);
    }

    [AvaloniaFact]
    public void The_reach_header_names_the_target_and_its_reach()
    {
        using var p = Open(supply: 1200);

        Click(p.Vm, 2, 2, DepthSide.Back);              // right-click: the back owner of a body-only pixel

        Assert.Equal("editing Body / Oak · in ~900 of 1,200 assets", p.Vm.TargetHeader);
        Assert.NotEmpty(p.Vm.TargetHeaderTip);
        Assert.True(p.Vm.ShowTargetHeader);
    }

    [AvaloniaFact]
    public void Fill_all_with_a_target_fills_only_that_variant()
    {
        using var p = Open();
        Click(p.Vm, 3, 3);                              // gold
        p.Vm.FillFront = 4;
        p.Vm.FillBack = 1;

        p.Vm.FillAllCommand.Execute(null);

        Assert.Equal(new DepthPixel(4, 1), p.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(new DepthPixel(4, 1), p.Src.Target!.Surface.Get(4, 4));
        p.Src.SetTarget("body");
        Assert.Equal(new DepthPixel(1, 9), p.Src.Target!.Surface.Get(3, 3));      // the oak: untouched
        Assert.Equal(new DepthPixel(1, 9), p.Src.Target!.Surface.Get(2, 2));

        p.Vm.UndoCommand.Execute(null);                  // one step undoes the whole fill, on the layer it touched
        Assert.Equal("Lock / Gold", p.Src.Target!.Name);
        Assert.Equal(new DepthPixel(2, 0), p.Src.Target!.Surface.Get(3, 3));
        Assert.Equal(0, p.Vm.DirtyCount);
    }

    [AvaloniaFact]
    public void Fill_all_waits_for_a_layer()
    {
        using var p = Open();
        Assert.False(p.Vm.FillAllCommand.CanExecute(null));
        p.Vm.FillAllCommand.Execute(null);
        Assert.Equal(0, p.Vm.DirtyCount);
    }

    [AvaloniaFact]
    public void Escape_drops_the_marquee_then_the_target()
    {
        using var p = Open();
        Click(p.Vm, 3, 3);
        p.Vm.SelectAllCommand.Execute(null);
        Assert.True(p.Vm.HasSelection);

        p.Vm.EscapeCommand.Execute(null);
        Assert.False(p.Vm.HasSelection);
        Assert.NotNull(p.Src.Target);

        p.Vm.EscapeCommand.Execute(null);
        Assert.Null(p.Src.Target);
        Assert.True(p.Vm.IsPicking);
    }

    [AvaloniaFact]
    public void PageUp_and_PageDown_step_the_target_through_present_layers()
    {
        using var p = Open();
        p.Vm.StepTargetCommand.Execute(1);
        Assert.Equal("Body / Oak", p.Src.Target!.Name);           // none chosen: PageUp starts at the bottom
        p.Vm.StepTargetCommand.Execute(1);
        Assert.Equal("Lock / Gold", p.Src.Target!.Name);
        p.Vm.StepTargetCommand.Execute(1);
        Assert.Equal("Lock / Gold", p.Src.Target!.Name);          // the top stays the top
        p.Vm.StepTargetCommand.Execute(-1);
        Assert.Equal("Body / Oak", p.Src.Target!.Name);
        Assert.Contains(p.Vm.LayerRows, r => r is { IngredientId: "body", IsTarget: true });
    }

    [AvaloniaFact]
    public void A_row_click_retargets_and_an_absent_row_does_not()
    {
        using var p = Open();
        var gold = p.Vm.LayerRows.Single(r => r.IngredientId == "lock");

        p.Vm.TargetLayerCommand.Execute(gold);

        Assert.Equal("Lock / Gold", p.Src.Target!.Name);
        Assert.True(gold.IsTarget);
        p.Vm.TargetLayerCommand.Execute(null);                    // no row: no change
        Assert.Equal("Lock / Gold", p.Src.Target!.Name);
    }

    [AvaloniaFact]
    public void The_layer_rows_follow_an_edit_and_a_save()
    {
        using var p = Open();
        var gold = p.Vm.LayerRows.Single(r => r.IngredientId == "lock");
        string before = gold.DepthText;
        var changed = new List<string?>();
        gold.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        Click(p.Vm, 3, 3);

        Click(p.Vm, 3, 3);

        Assert.NotEqual(before, gold.DepthText);
        Assert.Contains(nameof(RiseLayerRow.Caption), changed);
        Assert.Contains(gold.DepthText, gold.Caption, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Save_writes_the_painted_variant_and_the_page_tells_whoever_listens()
    {
        using var p = Open();
        LoadedCookBook? told = null;
        p.Vm.Saved += b => told = b;
        Click(p.Vm, 3, 3);
        Click(p.Vm, 3, 3);
        Assert.True(p.Vm.HasUnsavedDepth);
        Assert.True(p.Vm.SaveCommand.CanExecute(null));

        await p.Vm.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(told);
        Assert.False(p.Vm.HasUnsavedDepth);
        using var reread = CookBookArchive.Read(p.Path);
        var gold = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "lock");
        Assert.Equal(3, gold.Depths["gold"].Front(3, 3));
        Assert.Equal(2, gold.Depths["gold"].Front(4, 4));
    }

    [AvaloniaFact]
    public async Task Leaving_with_unsaved_write_through_depth_asks_first()
    {
        using var p = Open();
        Click(p.Vm, 3, 3);
        Click(p.Vm, 3, 3);

        p.Dialogs.Answer = LeaveChoice.Stay;
        await p.Vm.BackCommand.ExecuteAsync(null);

        Assert.Equal(1, p.Dialogs.Cards);
        Assert.Contains("unsaved depth on 1 variant", p.Dialogs.Message, StringComparison.Ordinal);
        Assert.Equal(0, p.Nav.BackCount);

        p.Dialogs.Answer = LeaveChoice.Discard;
        await p.Vm.BackCommand.ExecuteAsync(null);

        Assert.Equal(1, p.Nav.BackCount);
    }

    [AvaloniaFact]
    public async Task Leaving_a_clean_page_asks_nothing()
    {
        using var p = Open();
        Click(p.Vm, 3, 3);                               // picking is not an edit

        await p.Vm.BackCommand.ExecuteAsync(null);

        Assert.Equal(0, p.Dialogs.Cards);
        Assert.Equal(1, p.Nav.BackCount);
    }

    [AvaloniaFact]
    public void Undo_of_a_step_off_the_roll_says_so()
    {
        using var p = Open(pine: true);
        Click(p.Vm, 2, 2, DepthSide.Back);               // oak: body only
        Click(p.Vm, 2, 2, DepthSide.Back);
        for (int i = 0; i < 60 && p.Src.Layers[0].VariantName != "Pine"; i++) p.Src.Reroll();
        Assert.Equal("Pine", p.Src.Layers[0].VariantName);

        p.Vm.UndoCommand.Execute(null);

        Assert.Contains("does not include", p.Status.Last, StringComparison.Ordinal);
        Assert.Contains("Body / Oak", p.Status.Last, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_set_source_never_picks_or_writes()
    {
        var (vm, owner) = RisePages.SetVm();
        using (owner)
        using (vm)
        {
            Assert.False(vm.IsWriteThrough);
            Assert.False(vm.IsPicking);
            Assert.False(vm.ShowTargetHeader);
            Assert.Equal("", vm.TargetHeader);
            Assert.True(vm.IsHittable(0, 0));            // not a recipe: nothing is dimmed

            vm.PreviewDepthStroke(At(2, 2), DepthSide.Front, lower: false);
            vm.ApplyDepthStroke(At(2, 2), DepthSide.Front, lower: false);

            Assert.False(vm.PickAt(2, 2, DepthSide.Front));
            Assert.Equal(0, vm.DirtyCount);
            Assert.False(vm.ShowSave);
            Assert.Throws<NotSupportedException>(() => { _ = vm.Source.SaveAsync(); });
        }
    }

    [AvaloniaFact]
    public void A_read_only_recipe_page_has_no_header_and_no_target()
    {
        using var vm = RisePages.RecipeVm();             // built without a session
        Assert.False(vm.IsWriteThrough);
        Assert.Equal("", vm.TargetHeader);
        Assert.False(vm.PickAt(3, 3, DepthSide.Front));
        Assert.DoesNotContain(vm.LayerRows, r => r.IsPickable);
    }

    [AvaloniaFact]
    public void The_dimmed_art_follows_the_target()
    {
        using var p = Open();
        Assert.Null(p.Vm.BelowCanvas);
        Assert.Null(p.Vm.AboveCanvas);

        p.Vm.TargetLayerCommand.Execute(p.Vm.LayerRows.Single(r => r.IngredientId == "lock"));
        Assert.NotNull(p.Vm.BelowCanvas);                // the oak under the gold
        var canvas = p.Vm.Canvas;
        Assert.Equal(8, canvas.PixelSize.Width);

        p.Vm.EscapeCommand.Execute(null);
        Assert.Null(p.Vm.BelowCanvas);                   // back to the composite
        Assert.Null(p.Vm.AboveCanvas);
    }

    [AvaloniaFact]
    public void Picking_does_not_reset_the_camera()
    {
        using var p = Open();
        p.Vm.Zoom = 2;
        int resets = 0;
        p.Vm.SubjectChanged += () => resets++;

        Click(p.Vm, 3, 3);

        Assert.Equal(0, resets);
        Assert.Equal(2, p.Vm.Zoom);
    }
}
