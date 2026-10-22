using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Nfty.App.Models;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Rise from the Explorer, end to end: the pane buttons, the page they open, and the two things the
/// Explorer owes it afterwards — the lock pushed in as it flips, and a save fed back through the one
/// path an editor save takes. Built the way the app builds it: a real session on a real .cbk and the
/// real page factory, only the navigation and dialogs faked.
/// </summary>
public class RiseEntryExplorerTests
{
    private sealed class Rig : IDisposable
    {
        public readonly FakeNav Nav = new();
        public readonly FakeDialogs Dialogs = new();
        public readonly StatusService Status = new();
        public readonly CookBookSession Session;
        public readonly string Path;
        public readonly ExplorerViewModel Explorer;

        public Rig(bool unlocked = true)
        {
            (Session, Path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
            Explorer = new ExplorerViewModel(Session.Current!, Nav, Dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(Nav, Session, Dialogs),
                ExplorerViewModelTests.CookFactory(Dialogs), Session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(Nav, Session, Dialogs), Status,
                riseFactory: src => new RiseViewModel(src, new ImageBridge(), Nav, Dialogs,
                    new FilePickerService(), Status));
            if (unlocked) Explorer.ToggleLockCommand.Execute(null);
        }

        public ExplorerNode Recipe => Explorer.Root.Children[0];

        public IngredientDetailViewModel OpenBody()
        {
            Explorer.SelectNodeCommand.Execute(Recipe.Children.Single(c => c.Id == "body"));
            return Assert.IsType<IngredientDetailViewModel>(Explorer.CurrentDetail);
        }

        public RecipeDetailViewModel OpenRecipe()
        {
            Explorer.SelectNodeCommand.Execute(Recipe);
            return Assert.IsType<RecipeDetailViewModel>(Explorer.CurrentDetail);
        }

        public void Dispose()
        {
            (Nav.Current as IDisposable)?.Dispose();
            Explorer.Dispose();
            Session.Dispose();
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); } catch (IOException) { }
        }
    }

    private static byte[] Pixels(Bitmap bmp)
    {
        var buffer = new byte[bmp.PixelSize.Width * bmp.PixelSize.Height * 4];
        unsafe
        {
            fixed (byte* p = buffer)
                bmp.CopyPixels(new Avalonia.PixelRect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height),
                    (nint)p, buffer.Length, bmp.PixelSize.Width * 4);
        }
        return buffer;
    }

    private static int OakFront(string path)
    {
        using var book = CookBookArchive.Read(path);
        var body = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        return body.Depths.TryGetValue("oak", out var m) ? m.Front(2, 1) : 0;
    }

    [AvaloniaFact]
    public void The_ingredient_pane_rises_the_selected_variant()
    {
        using var rig = new Rig();
        var pane = rig.OpenBody();
        pane.SelectVariantCommand.Execute("ghost");

        Assert.True(pane.RiseCommand.CanExecute(null));
        pane.RiseCommand.Execute(null);

        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.IsType<VariantRiseSource>(rise.Source);
        Assert.Equal("ghost", rise.SelectedItem.Id);
    }

    [AvaloniaFact]
    public void The_recipe_pane_rises_the_roll_it_shows()
    {
        using var rig = new Rig();
        var pane = rig.OpenRecipe();

        pane.RiseCommand.Execute(null);

        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        var source = Assert.IsType<RecipeRiseSource>(rise.Source);
        Assert.Equal(pane.RollSeed, source.Seed);
        Assert.Equal(Pixels(pane.Hero), Pixels(new ImageBridge().ToBitmap(rise.Subject.Art)));
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Rerolling_then_rising_rises_the_new_roll()
    {
        using var rig = new Rig();
        var pane = rig.OpenRecipe();
        int before = pane.RollSeed;
        await pane.RerollCommand.ExecuteAsync(null);
        Assert.NotEqual(before, pane.RollSeed);

        pane.RiseCommand.Execute(null);

        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.Equal(pane.RollSeed, ((RecipeRiseSource)rise.Source).Seed);
        Assert.Equal(Pixels(pane.Hero), Pixels(new ImageBridge().ToBitmap(rise.Subject.Art)));
    }

    [AvaloniaFact]
    public void Flipping_the_lock_reaches_an_open_rise_page()
    {
        using var rig = new Rig();
        var pane = rig.OpenBody();
        pane.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        Assert.True(rise.CanSave);
        string open = rise.FooterText;
        // What the view hears: it binds FooterText and SaveCommand, and re-reads them only when told.
        var heard = new System.Collections.Generic.List<string?>();
        rise.PropertyChanged += (_, e) => heard.Add(e.PropertyName);
        int saveChanges = 0;
        rise.SaveCommand.CanExecuteChanged += (_, _) => saveChanges++;

        rig.Explorer.ToggleLockCommand.Execute(null);      // lock; no depth edit in between

        Assert.Contains(nameof(RiseViewModel.FooterText), heard);
        Assert.True(saveChanges > 0);
        Assert.False(rise.CanSave);
        Assert.False(rise.SaveCommand.CanExecute(null));
        Assert.NotEqual(open, rise.FooterText);
        rig.Explorer.ToggleLockCommand.Execute(null);      // unlock
        Assert.True(rise.CanSave);
        Assert.Equal(open, rise.FooterText);
    }

    [AvaloniaFact]
    public void Flipping_the_lock_reaches_an_open_editor()
    {
        using var rig = new Rig();
        var pane = rig.OpenBody();
        pane.EditIngredientCommand.Execute(null);
        var editor = Assert.IsType<IngredientEditorViewModel>(rig.Nav.Current);
        Assert.False(editor.IsReadOnly);
        var changed = new System.Collections.Generic.List<string?>();
        editor.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        string? note = editor.SaveNoteText;

        rig.Explorer.ToggleLockCommand.Execute(null);      // lock

        Assert.True(editor.IsReadOnly);
        Assert.Contains(nameof(IngredientEditorViewModel.IsReadOnly), changed);
        Assert.Contains(nameof(IngredientEditorViewModel.SaveNoteText), changed);
        Assert.NotEqual(note, editor.SaveNoteText);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task A_rise_save_rebuilds_the_tree_and_rerecords_the_recent()
    {
        using var rig = new Rig();
        var dir = Directory.CreateTempSubdirectory("nfty-recents-").FullName;
        try
        {
            var recents = new RecentsService(dir);
            // The composition root's wrapper, around the same kind of page the Explorer opens.
            var sp = new ServiceCollection()
                .AddSingleton<ICookBookSession>(rig.Session)
                .AddSingleton<IRecentsService>(recents)
                .BuildServiceProvider();
            var explorer = new ExplorerViewModel(rig.Session.Current!, rig.Nav, rig.Dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(rig.Nav, rig.Session, rig.Dialogs),
                ExplorerViewModelTests.CookFactory(rig.Dialogs), rig.Session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(rig.Nav, rig.Session, rig.Dialogs), rig.Status,
                riseFactory: src => ServiceRegistration.RememberOnRiseSave(sp,
                    new RiseViewModel(src, new ImageBridge(), rig.Nav, rig.Dialogs, new FilePickerService(), rig.Status)));
            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children[0].Children.Single(c => c.Id == "body"));
            var pane = Assert.IsType<IngredientDetailViewModel>(explorer.CurrentDetail);
            var oldBook = rig.Session.Current;
            pane.RiseCommand.Execute(null);
            var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
            rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);

            await rise.SaveCommand.ExecuteAsync(null);

            Assert.Equal(1, OakFront(rig.Path));
            Assert.NotSame(oldBook, rig.Session.Current);
            // The tree was rebuilt from the saved book (nodes hold the NEW graph) ...
            var node = explorer.Root.Children[0].Children.Single(c => c.Id == "body");
            var (liveRecipe, _) = ((LoadedRecipe, LoadedIngredient))node.Domain!;
            Assert.Same(rig.Session.Current!.Recipes[0], liveRecipe);
            // ... the pane on show is a fresh one over it ...
            Assert.IsType<IngredientDetailViewModel>(explorer.CurrentDetail);
            // ... and the Recent was re-recorded against what is now on disk.
            var recent = Assert.Single(recents.Items);
            Assert.Equal(System.IO.Path.GetFullPath(rig.Path), recent.Path);
            Assert.Equal(LandingViewModel.RecentMeta(rig.Session.Current!), recent.Meta);
            explorer.Dispose();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Rising_after_a_save_reads_the_live_book_not_the_graph_the_pane_was_built_over()
    {
        using var rig = new Rig();
        var pane = rig.OpenBody();
        pane.RiseCommand.Execute(null);
        var first = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        first.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await first.SaveCommand.ExecuteAsync(null);
        first.Dispose();

        // The SAME pane object is pressed again after the book was swapped out under it, if the
        // Explorer kept it; whichever pane is on show now, the page it opens must carry the saved depth.
        var after = Assert.IsType<IngredientDetailViewModel>(rig.Explorer.CurrentDetail);
        after.RiseCommand.Execute(null);
        var second = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.Equal(1, second.Surface.Get(2, 1).Front);
        pane.RiseCommand.Execute(null);                    // the pane built BEFORE the save
        var third = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.Equal(1, third.Surface.Get(2, 1).Front);
    }

    [AvaloniaFact]
    public void A_rise_opened_on_a_locked_book_cannot_save()
    {
        using var rig = new Rig(unlocked: false);
        var pane = rig.OpenBody();
        pane.RiseCommand.Execute(null);                    // opening is allowed: it is also how you look
        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);

        Assert.False(rise.CanSave);
        Assert.False(rise.SaveCommand.CanExecute(null));
        rise.SaveCommand.Execute(null);                    // the generated command does not consult CanExecute
        Assert.Equal(0, OakFront(rig.Path));
    }

    [AvaloniaFact]
    public void A_closed_rise_page_is_no_longer_pushed_the_lock()
    {
        using var rig = new Rig();
        var pane = rig.OpenBody();
        pane.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        int changes = 0;
        rise.Source.Changed += () => changes++;
        rise.Dispose();

        rig.Explorer.ToggleLockCommand.Execute(null);

        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void The_rise_and_reroll_buttons_share_one_row()
    {
        using var rig = new Rig();
        var pane = rig.OpenRecipe();
        var view = new Views.RecipeDetailView { DataContext = pane };
        var window = new Avalonia.Controls.Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            var chips = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view)
                .OfType<Avalonia.Controls.Button>().Where(b => b.Classes.Contains("sampleroll")).ToList();
            Assert.Equal(2, chips.Count);
            var ys = chips.Select(c => c.TranslatePoint(new Point(0, 0), view)!.Value.Y).ToList();
            Assert.Equal(ys[0], ys[1], precision: 1);
            // Fixed widths, so nothing in the pair moves when the die changes face or a button dims.
            Assert.Equal(28, chips[0].Bounds.Width, precision: 1);
            Assert.Equal(58, chips[1].Bounds.Width, precision: 1);
            Assert.True(chips[1].IsEnabled);
        }
        finally { window.Close(); }
    }
}
