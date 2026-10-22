using System;
using System.IO;
using System.Linq;
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

/// <summary>
/// Rise launched from the Ingredient editor: the clean-editor gate, the depth coming back into the
/// editor so ITS next save does not overwrite it, and the refresh reaching the Explorer through the editor.
/// </summary>
public class RiseFromEditorTests
{
    private sealed class Rig : IDisposable
    {
        public readonly FakeNav Nav = new();
        public readonly FakeDialogs Dialogs = new();
        public readonly CookBookSession Session;
        public readonly string Path;
        public readonly IngredientEditorViewModel Editor;

        public Rig()
        {
            (Session, Path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
            var book = Session.Current!;
            var recipe = book.Recipes[0];
            Editor = new IngredientEditorViewModel(recipe.Ingredients[0], recipe, book, new ImageBridge(), Nav,
                Session, Dialogs, new FilePickerService(), isEditing: () => true,
                rise: src => new RiseViewModel(src, new ImageBridge(), Nav, Dialogs, new FilePickerService(), new StatusService()));
        }

        public void Dispose()
        {
            Editor.Dispose();
            Session.Dispose();
            try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!, recursive: true); } catch (IOException) { }
        }
    }

    private static int OakFront(string path)
    {
        using var book = CookBookArchive.Read(path);
        var body = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        return body.Depths.TryGetValue("oak", out var m) ? m.Front(2, 1) : 0;
    }

    [AvaloniaFact]
    public void The_editor_rises_its_selected_variant()
    {
        using var rig = new Rig();
        rig.Editor.SelectVariantCommand.Execute(rig.Editor.Variants.Single(v => v.Id == "ghost"));

        Assert.True(rig.Editor.RiseCommand.CanExecute(null));
        rig.Editor.RiseCommand.Execute(null);

        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.IsType<VariantRiseSource>(rise.Source);
        Assert.Equal("ghost", rise.SelectedItem.Id);
        Assert.Equal("Rise this variant into a model.", rig.Editor.RiseTip);
    }

    [AvaloniaFact]
    public void Rise_is_refused_while_the_editor_has_unsaved_edits()
    {
        using var rig = new Rig();
        rig.Editor.ApplyToolStroke(new[] { (0, 0) });
        Assert.True(rig.Editor.IsDirty);

        Assert.False(rig.Editor.RiseCommand.CanExecute(null));
        Assert.Equal("Save the layer first — Rise reads the saved picture.", rig.Editor.RiseTip);
        Assert.Equal(rig.Editor.RiseTip, rig.Editor.SaveNoteText);   // visible line, not only the tooltip
        rig.Editor.RiseCommand.Execute(null);        // the generated command does not consult CanExecute

        Assert.Null(rig.Nav.Current);
    }

    [AvaloniaFact]
    public async Task Saving_the_editor_after_a_rise_save_keeps_the_rise_depth()
    {
        using var rig = new Rig();
        rig.Editor.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await rise.SaveCommand.ExecuteAsync(null);
        Assert.Equal(1, OakFront(rig.Path));

        rig.Editor.ApplyToolStroke(new[] { (0, 0) });
        await rig.Editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(1, OakFront(rig.Path));         // the editor's own save did not write the old maps back
    }

    [AvaloniaFact]
    public async Task A_rise_save_refreshes_the_explorer_through_the_editor()
    {
        using var rig = new Rig();
        LoadedCookBook? told = null;
        rig.Editor.Saved += b => told = b;
        rig.Editor.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);

        await rise.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(told);
        Assert.Same(rig.Session.Current, told);
        Assert.True(rig.Editor.RiseCommand.CanExecute(null));   // the editor is still clean
    }

    [AvaloniaFact]
    public async Task A_loose_rise_save_reaches_the_loose_editor()
    {
        var book = RiseFixtures.ChestBook();
        var ing = book.Recipes[0].Ingredients[0];
        var path = System.IO.Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-").FullName, "Body.igt");
        await IngredientArchive.WriteAsync(path, ing.Manifest, ing.VariantImages, ing.Depths);
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        using var session = new CookBookSession();
        using var editor = new IngredientEditorViewModel(ing, book.Recipes[0], book, new ImageBridge(), nav,
            session, dialogs, new FilePickerService(), looseSavePath: path,
            rise: src => new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService()));
        editor.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(nav.Current);
        rise.ApplyDepthStroke(new[] { (2, 1) }, DepthSide.Front, false);
        await rise.SaveCommand.ExecuteAsync(null);

        editor.ApplyToolStroke(new[] { (0, 0) });
        await editor.SaveCommand.ExecuteAsync(null);

        using var reread = IngredientArchive.Read(path);
        Assert.Equal(1, reread.Depths["oak"].Front(2, 1));
    }
}
