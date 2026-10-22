using System;
using Avalonia;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Make voxel-ready on the Rise page: the preview, the write, the undo, and the gates that
/// each hold on their own.</summary>
public class RiseVoxelReadyTests
{
    /// <summary>Answers the preview card with a scripted choice and keeps what it showed.</summary>
    internal sealed class ScriptedDialogs : IDialogService
    {
        public bool Answer;
        public Action? WhileCardIsUp;
        public List<string> Rows = new();
        public List<string> Titles = new();
        public bool ThumbsMade;
        public int Cards;
        public string? ErrorText;
        public ViewModelBase? Active => null;
        public event Action? Changed { add { } remove { } }

        public Task<TResult?> ShowAsync<TResult>(ViewModelBase dialog)
        {
            if (dialog is VoxelReadyDialogViewModel card)
            {
                Cards++;
                Rows = card.Rows.Select(r => r.Text).ToList();
                ThumbsMade = card.Rows.Count > 0 && card.Rows[0].Before is not null && card.Rows[0].After is not null;
                WhileCardIsUp?.Invoke();
                return Task.FromResult((TResult?)(object)Answer);
            }
            if (dialog is ErrorDialogViewModel error) ErrorText = error.Message;
            return Task.FromResult<TResult?>(default);
        }

        public void Close(object? result) { }
    }

    /// <summary>The chest, with the ghost body carrying one 128-alpha pixel (3,3) and one 127-alpha pixel (4,3).</summary>
    internal static (CookBookSession Session, string Path) Disk(bool secondGhost = false)
    {
        var book = RiseFixtures.ChestBook(withSecondGhost: secondGhost);
        var ghost = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body").VariantImages["ghost"];
        ghost[4, 3] = new Rgba32(200, 40, 40, 127);
        return RiseFixtures.OnDisk(book);
    }

    /// <summary>A preview card over <paramref name="count"/> partial variants with long names and big
    /// counts: the riskiest ink, for the contrast sweep, the capture and the fit test.</summary>
    internal static VoxelReadyDialogViewModel PreviewCard(IDialogService dialogs, int count)
    {
        var changes = Enumerable.Range(1, count).Select(n => new VoxelReadyChange("chest", "body",
            "Background Scenery With A Very Long Layer Name", $"v{n}", $"Sunset Over The Western Harbour {n}",
            12_345 + n, 3_210 + n)).ToList();
        var bridge = new ImageBridge();
        return new VoxelReadyDialogViewModel(dialogs, changes, _ =>
        {
            using var before = RiseFixtures.Picture(RiseFixtures.Ghost);
            using var after = VoxelReady.Snap(before);
            return (bridge.ToBitmap(before), bridge.ToBitmap(after));
        });
    }

    private static RiseViewModel Vm(RiseSource src, IDialogService dialogs, StatusService? status = null) =>
        new(src, new ImageBridge(), new FakeNav(), dialogs, new FilePickerService(), status ?? new StatusService());

    private static Rgba32 Ghost(string path, int x, int y)
    {
        using var book = CookBookArchive.Read(path);
        return book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body").VariantImages["ghost"][x, y];
    }

    // ---------------------------------------------------------------- preview

    [AvaloniaFact]
    public async Task The_button_lists_every_partial_variant_in_the_book_before_applying()
    {
        var (s, path) = Disk(secondGhost: true);
        var dialogs = new ScriptedDialogs { Answer = false };
        using var vm = Vm(RiseFixtures.Variant(s, "ghost"), dialogs);
        var plan = VoxelReady.Plan(s.Current!);

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        Assert.Equal(2, plan.Count);
        Assert.Equal(new[]
        {
            "Body / Ghost · 1 px solid · 1 px clear",
            "Body / Ghost Two · 1 px solid · 0 px clear",
        }, dialogs.Rows);
        Assert.Equal(plan.Select(p => $"Body / {p.VariantName}"), dialogs.Rows.Select(r => r.Split(" · ")[0]));
        Assert.True(dialogs.ThumbsMade, "the rows make their before and after pictures");
        Assert.Equal(128, Ghost(path, 3, 3).A);     // listing wrote nothing
    }

    // ---------------------------------------------------------------- apply

    [AvaloniaFact]
    public async Task Applying_writes_the_snapped_pixels_and_the_book_stops_being_two_d_only()
    {
        var (s, path) = Disk();
        var dialogs = new ScriptedDialogs { Answer = true };
        var status = new StatusService();
        using var vm = Vm(RiseFixtures.Variant(s, "ghost"), dialogs, status);
        LoadedCookBook? told = null;
        vm.Saved += b => told = b;
        Assert.True(vm.BookIsTwoDOnly);

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        using var reread = CookBookArchive.Read(path);
        Assert.False(Opacity.HasPartialAlpha(reread));
        Assert.Equal(255, Ghost(path, 3, 3).A);     // 128 -> solid
        Assert.Equal(0, Ghost(path, 4, 3).A);       // 127 -> clear
        Assert.False(vm.BookIsTwoDOnly);
        Assert.NotNull(told);                       // the Explorer and recents refresh from this
        Assert.Same(s.Current, told);
        Assert.Equal("Voxel-ready: 1 variant snapped", vm.VoxelReadyNote);
        Assert.True(vm.ShowVoxelReadyUndo);
        Assert.False(vm.ShowVoxelReadyButton);
        Assert.Contains("Voxel-ready: 1 variant snapped", status.Last);
    }

    [AvaloniaFact]
    public async Task Cancel_writes_nothing()
    {
        var (s, path) = Disk();
        var before = File.ReadAllBytes(path);
        var dialogs = new ScriptedDialogs { Answer = false };
        using var vm = Vm(RiseFixtures.Variant(s, "ghost"), dialogs);

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Cards);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(vm.ShowVoxelReadyUndo);
    }

    // ---------------------------------------------------------------- undo

    [AvaloniaFact]
    public async Task Undo_restores_the_archive_pixels()
    {
        var (s, path) = Disk();
        var dialogs = new ScriptedDialogs { Answer = true };
        using var vm = Vm(RiseFixtures.Variant(s, "ghost"), dialogs);
        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);
        Assert.Equal(255, Ghost(path, 3, 3).A);

        await vm.UndoVoxelReadyCommand.ExecuteAsync(null);

        Assert.Equal(128, Ghost(path, 3, 3).A);
        Assert.Equal(127, Ghost(path, 4, 3).A);
        Assert.True(vm.BookIsTwoDOnly);
        Assert.True(vm.ShowVoxelReadyButton);
        Assert.False(vm.ShowVoxelReadyUndo);
        Assert.False(vm.SelectedItem.IsRisableOrUnknown);   // back to a picture that cannot rise
    }

    [AvaloniaFact]
    public async Task Undo_refuses_in_the_error_card_when_the_snapped_pixels_were_edited_since()
    {
        var (s, path) = Disk();
        var dialogs = new ScriptedDialogs { Answer = true };
        using var vm = Vm(RiseFixtures.Variant(s, "ghost"), dialogs);
        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);
        // Another route rewrites the variant after the snap.
        var edited = s.Current!;
        var body = edited.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        var changed = body.VariantImages["ghost"].Clone();
        changed[0, 0] = new Rgba32(1, 2, 3, 255);
        var images = new Dictionary<string, Image<Rgba32>>(body.VariantImages) { ["ghost"] = changed };
        var next = CookBookEdits.UpsertIngredient(edited, "chest",
            new LoadedIngredient { Manifest = body.Manifest, VariantImages = images, Depths = body.Depths });
        await CookBookPersistence.PersistAsync(s, next);
        var bytes = File.ReadAllBytes(path);

        await vm.UndoVoxelReadyCommand.ExecuteAsync(null);

        Assert.NotNull(dialogs.ErrorText);
        Assert.Contains("Ghost", dialogs.ErrorText);
        Assert.Equal(bytes, File.ReadAllBytes(path));       // nothing restored
        Assert.True(vm.ShowVoxelReadyUndo);                 // and the line stays
    }

    // ---------------------------------------------------------------- gates

    [AvaloniaFact]
    public async Task A_locked_book_refuses_even_when_the_command_is_forced()
    {
        var (s, path) = Disk();
        var before = File.ReadAllBytes(path);
        var dialogs = new ScriptedDialogs { Answer = true };
        using var src = RiseFixtures.Variant(s, "ghost", editing: false);
        using var vm = Vm(src, dialogs);

        Assert.False(vm.CanMakeVoxelReady);
        Assert.Equal("The CookBook is read-only — unlock it in the Explorer.", vm.VoxelReadyTip);
        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);           // the command's own re-check
        Assert.Equal(0, dialogs.Cards);
        Assert.Equal(before, File.ReadAllBytes(path));

        // The source holds the line on its own, whatever the page does: a disabled button is a label.
        Assert.Null(await src.MakeVoxelReadyAsync());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [AvaloniaFact]
    public async Task A_lock_closed_while_the_card_is_up_stops_the_write()
    {
        var (s, path) = Disk();
        var before = File.ReadAllBytes(path);
        bool editing = true;
        var dialogs = new ScriptedDialogs { Answer = true, WhileCardIsUp = () => editing = false };
        using var vm = Vm(RiseFixtures.Variant(s, "ghost", () => editing), dialogs);

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Cards);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(vm.ShowVoxelReadyUndo);
    }

    [AvaloniaFact]
    public async Task Unsaved_depth_disables_it_and_says_why()
    {
        var (s, path) = Disk();
        var before = File.ReadAllBytes(path);
        var dialogs = new ScriptedDialogs { Answer = true };
        using var vm = Vm(RiseFixtures.Variant(s, "oak"), dialogs);
        Assert.True(vm.CanMakeVoxelReady);

        vm.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);

        Assert.True(vm.HasUnsavedDepth);
        Assert.False(vm.CanMakeVoxelReady);
        Assert.Equal("Save or discard depth first.", vm.VoxelReadyTip);
        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);           // forced
        Assert.Equal(0, dialogs.Cards);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ---------------------------------------------------------------- the page follows

    [AvaloniaFact]
    public async Task The_snapped_variant_becomes_risable_on_the_canvas()
    {
        var (s, _) = Disk();
        var dialogs = new ScriptedDialogs { Answer = true };
        using var vm = Vm(RiseFixtures.Variant(s, "ghost"), dialogs);
        Assert.False(vm.SelectedItem.IsRisableOrUnknown);
        Assert.Null(vm.Mesh);
        var oldSubject = vm.Subject;

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        Assert.True(vm.SelectedItem.IsRisableOrUnknown);
        Assert.NotNull(vm.Mesh);
        Assert.NotSame(oldSubject, vm.Subject);
        Assert.True(oldSubject.IsDisposed);          // let go only after the page was told
        Assert.Equal(0, vm.Subject.Depth.PartialCount);
    }

    [AvaloniaFact]
    public async Task A_loose_layer_writes_the_igt_and_tells_its_editor()
    {
        var book = RiseFixtures.ChestBook();
        var ing = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        ing.VariantImages["ghost"][4, 3] = new Rgba32(200, 40, 40, 127);
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-").FullName, "Body.igt");
        await IngredientArchive.WriteAsync(path, ing.Manifest, ing.VariantImages, ing.Depths);
        using var session = new CookBookSession();
        var dialogs = new ScriptedDialogs { Answer = true };
        using var src = new VariantRiseSource(book.Recipes[0], ing, "ghost", new ImageBridge(), session,
            looseSavePath: path);
        LoadedIngredient? told = null;
        src.LooseSaved += i => told = i;
        using var vm = Vm(src, dialogs);

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        using var reread = IngredientArchive.Read(path);
        Assert.False(Opacity.HasPartialAlpha(reread));
        Assert.Equal(255, reread.VariantImages["ghost"][3, 3].A);
        Assert.Equal(0, reread.VariantImages["ghost"][4, 3].A);
        Assert.NotNull(told);
        Assert.True(vm.SelectedItem.IsRisableOrUnknown);

        await vm.UndoVoxelReadyCommand.ExecuteAsync(null);
        using var undone = IngredientArchive.Read(path);
        Assert.Equal(128, undone.VariantImages["ghost"][3, 3].A);
    }

    // ---------------------------------------------------------------- pictures

    private static string GhostPng()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-pic-").FullName, "ghost.png");
        using var img = RiseFixtures.Picture(RiseFixtures.Ghost);
        img[4, 3] = new Rgba32(200, 40, 40, 127);
        img.Save(path);
        return path;
    }

    [AvaloniaFact]
    public async Task A_picture_snaps_in_the_session_only()
    {
        var path = GhostPng();
        var before = File.ReadAllBytes(path);
        var dialogs = new ScriptedDialogs { Answer = true };
        using var src = PictureRiseSource.Open(path, new ImageBridge());
        using var vm = Vm(src, dialogs);
        Assert.False(vm.SelectedItem.IsRisableOrUnknown);
        Assert.True(vm.CanMakeVoxelReady);

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);

        Assert.Equal(before, File.ReadAllBytes(path));        // the file is untouched
        Assert.True(vm.SelectedItem.IsRisableOrUnknown);
        Assert.Equal(255, vm.Subject.Art[3, 3].A);
        Assert.Equal(0, vm.Subject.Art[4, 3].A);
        Assert.Equal("Voxel-ready: 1 cell snapped", vm.VoxelReadyNote);

        await vm.UndoVoxelReadyCommand.ExecuteAsync(null);
        Assert.Equal(128, vm.Subject.Art[3, 3].A);
        Assert.False(vm.SelectedItem.IsRisableOrUnknown);
    }

    // ---------------------------------------------------------------- recipe and Set only list

    [AvaloniaFact]
    public void A_recipe_lists_the_problem_and_says_the_fix_lives_on_a_layer()
    {
        var (s, _) = Disk();
        var src = new RecipeRiseSource(s.Current!, s.Current!.Recipes[0], 1, new ImageBridge());
        using var vm = Vm(src, new ScriptedDialogs());

        Assert.False(vm.CanMakeVoxelReady);
        if (vm.HasPartialAlphaRows)
        {
            Assert.True(vm.ShowVoxelReadyHint);
            Assert.Equal("Open a layer of this book in Rise to make it voxel-ready.", vm.VoxelReadyHint);
        }
        Assert.False(vm.ShowVoxelReadyButton);
    }

    // ---------------------------------------------------------------- the preview card at the smallest page

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(60)]
    public void The_preview_card_fits_the_page_and_only_its_list_scrolls(int count)
    {
        using var card = PreviewCard(new FakeDialogs(), count);
        var view = new Views.VoxelReadyDialogView { DataContext = card };
        var window = new Window { Content = view, Width = RisePages.MinArea.Width, Height = RisePages.MinArea.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var box = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            var foot = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal-foot"));
            var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();

            Assert.True(box.Bounds.Height <= view.Bounds.Height + 0.5,
                $"the card is {box.Bounds.Height:0} tall in {view.Bounds.Height:0} of page");
            var footBottom = foot.TranslatePoint(new Avalonia.Point(0, foot.Bounds.Height), box)!.Value.Y;
            Assert.True(footBottom <= box.Bounds.Height + 0.5 && foot.Bounds.Height > 20,
                "the footer, with Apply and Cancel, stays inside the card");
            if (count == 2) Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5, "a short list does not scroll");
            else Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "a long list scrolls inside the card");
            Assert.True(card.Rows[0].Before is not null);
        }
        finally { window.Close(); }
    }

    // ---------------------------------------------------------------- geometry

    private static byte[] Region(Avalonia.Media.Imaging.WriteableBitmap frame, int x0, int y0, int x1, int y1)
    {
        using var fb = frame.Lock();
        var bytes = new byte[(x1 - x0) * (y1 - y0) * 4];
        int row = fb.RowBytes;
        var all = new byte[row * fb.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, all, 0, all.Length);
        int o = 0;
        for (int y = y0; y < y1; y++)
        {
            Buffer.BlockCopy(all, y * row + x0 * 4, bytes, o, (x1 - x0) * 4);
            o += (x1 - x0) * 4;
        }
        return bytes;
    }

    private static Button VoxelButton(RisePage page) =>
        page.View.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Make voxel-ready…");

    [AvaloniaFact]
    public async Task Its_arrival_moves_nothing_above_it()
    {
        var (s, _) = Disk(secondGhost: true);
        var vm = Vm(RiseFixtures.Variant(s, "ghost"), new ScriptedDialogs { Answer = true });
        using var page = RisePages.Show(vm, RisePages.MinArea.Width, RisePages.MinArea.Height);
        var slot = page.View.FindControl<Panel>("VoxelSlot")!;
        Assert.False(slot.IsVisible);                // before the snap there is nothing at the foot of the rail

        await vm.MakeVoxelReadyCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(slot.IsEffectivelyVisible);
        var top = (int)Math.Floor(slot.TranslatePoint(default, page.Window)!.Value.Y);
        int x0 = (int)(page.Window.ClientSize.Width - 300), x1 = (int)page.Window.ClientSize.Width;
        // The foot of the rail is the last thing in its stack: nothing sits below it to be shoved either.
        Assert.Same(slot, page.View.FindControl<StackPanel>("DepthStack")!.Children.Last());

        using var with = page.Window.CaptureRenderedFrame()!;
        var above = Region(with, x0, 0, x1, top);
        int height = (int)slot.Bounds.Height;

        slot.IsVisible = false;                      // the page without it
        Dispatcher.UIThread.RunJobs();
        using var without = page.Window.CaptureRenderedFrame()!;

        Assert.Equal(above, Region(without, x0, 0, x1, top));
        Assert.NotEqual(Region(with, x0, top, x1, top + height), Region(without, x0, top, x1, top + height));
    }

    [AvaloniaFact]
    public void The_button_costs_the_rail_no_height_and_the_busiest_rail_still_fits()
    {
        // Painted oak (three thickness rows) beside two listed pictures is the tallest the DEPTH rail
        // gets at the smallest window; the button must not make it scroll, with room to spare - fitting
        // exactly is one style tweak from not.
        using var page = RisePages.Show(RisePages.VariantVm(oakDepth: RisePages.PaintedOak(), withPine: true),
            RisePages.MinArea.Width, RisePages.MinArea.Height);
        var scroll = page.View.FindControl<ScrollViewer>("RailScroll")!;
        var button = VoxelButton(page);
        Assert.True(page.Vm.ShowVoxelReadyButton && ((Control)button.Parent!).Opacity > 0.99 && ((Control)button.Parent!).IsHitTestVisible);

        double room = ((Control)scroll.Parent!).Bounds.Height - scroll.Bounds.Y;
        double slack = room - scroll.Extent.Height;
        Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 0.5 && slack >= 6,
            $"the rail needs {scroll.Extent.Height:0} of {room:0}, slack {slack:0}");

        // Read-only: dim, and the section's own closing line gives the reason, so the rail is no taller.
        using var locked = RisePages.Show(
            RisePages.VariantVm(isEditing: () => false, oakDepth: RisePages.PaintedOak(), withPine: true),
            RisePages.MinArea.Width, RisePages.MinArea.Height);
        var lockedScroll = locked.View.FindControl<ScrollViewer>("RailScroll")!;
        Assert.False(locked.Vm.CanMakeVoxelReady);
        Assert.Equal("The CookBook is read-only — unlock it in the Explorer.", locked.Vm.TwoDOnlyFootnote);
        Assert.Equal(scroll.Extent.Height, lockedScroll.Extent.Height, 0.5);
        Assert.True(lockedScroll.Extent.Height <= lockedScroll.Viewport.Height + 0.5 && slack >= 6);

        // It sits inside the 2D ONLY header's box, beside the label and the count, not on a row of its own.
        var header = button.GetVisualAncestors().OfType<Grid>().First(g => g.Classes.Contains("ctlh"));
        Assert.True(header.Bounds.Height < 30, $"the header is {header.Bounds.Height:0} tall");
        Assert.True(button.TranslatePoint(new Avalonia.Point(button.Bounds.Width, 0), header)!.Value.X <= header.Bounds.Width + 0.5);
    }

    [AvaloniaFact]
    public void The_button_is_inked_only_where_the_source_can_snap_its_own_art()
    {
        using var variant = RisePages.Show(RisePages.VariantVm("ghost"), RisePages.MinArea.Width, RisePages.MinArea.Height);
        Assert.True(((Control)VoxelButton(variant).Parent!).Opacity > 0.99);
        Assert.True(variant.Vm.CanMakeVoxelReady);

        using var recipe = RisePages.Show(RisePages.RecipeVm(), RisePages.MinArea.Width, RisePages.MinArea.Height);
        Assert.False(recipe.Vm.ShowVoxelReadyButton);
        // The header is not laid out for a recipe at all unless it lists something; when it does the
        // button keeps its box and no ink, and cannot be pressed.
        foreach (var b in recipe.View.GetVisualDescendants().OfType<Button>().Where(b => b.Content as string == "Make voxel-ready…"))
        {
            Assert.True(((Control)b.Parent!).Opacity < 0.01);
            Assert.False(((Control)b.Parent!).IsHitTestVisible);
        }
    }
}
