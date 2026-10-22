using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The journey that lost data: editor, Rise, make voxel-ready, back, edit, Save. The editor holds its own
/// copy of the layer's pixels (its draft), so unless it reloads when Rise writes new ones, its next Save
/// writes the pre-snap pixels back over the snap.
/// </summary>
public class RiseVoxelEditorJourneyTests
{
    private static IngredientEditorViewModel EditorOver(CookBookSession session, RiseVoxelReadyTests.ScriptedDialogs dialogs,
        FakeNav nav, string? looseSavePath = null, LoadedIngredient? loose = null, LoadedCookBook? wrapper = null)
    {
        var book = wrapper ?? session.Current!;
        var recipe = book.Recipes[0];
        var ing = loose ?? recipe.Ingredients.Single(i => i.Manifest.Id == "body");
        return new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), nav, session, dialogs,
            new FilePickerService(), looseSavePath: looseSavePath, isEditing: () => true,
            rise: src => new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService()));
    }

    private static Rgba32 Pixel(LoadedIngredient ing, string variant, int x, int y) => ing.VariantImages[variant][x, y];

    private static async Task<RiseViewModel> SnapFromEditor(IngredientEditorViewModel editor, FakeNav nav)
    {
        editor.SelectVariantCommand.Execute(editor.Variants.Single(v => v.Id == "ghost"));
        editor.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(nav.Current);
        await rise.MakeVoxelReadyCommand.ExecuteAsync(null);
        Assert.True(rise.ShowVoxelReadyUndo);
        return rise;
    }

    [AvaloniaFact]
    public async Task An_editor_save_after_a_voxel_ready_keeps_the_snapped_pixels_in_the_cookbook()
    {
        var (session, path) = RiseVoxelReadyTests.Disk();
        var nav = new FakeNav();
        var dialogs = new RiseVoxelReadyTests.ScriptedDialogs { Answer = true };
        using var editor = EditorOver(session, dialogs, nav);
        await SnapFromEditor(editor, nav);
        Assert.False(editor.IsDirty);

        editor.ApplyToolStroke(new[] { (0, 0) });            // one pixel of the selected (ghost) variant
        Assert.True(editor.IsDirty);
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.False(editor.IsDirty);

        using var reread = CookBookArchive.Read(path);
        Assert.False(Opacity.HasPartialAlpha(reread), "the book is two-D only again: the old pixels were written back");
        var body = reread.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        Assert.Equal(255, Pixel(body, "ghost", 3, 3).A);     // 128 stayed snapped solid
        Assert.Equal(0, Pixel(body, "ghost", 4, 3).A);       // 127 stayed snapped clear
        Assert.Equal(255, Pixel(body, "oak", 2, 1).A);       // the other variants are untouched
    }

    [AvaloniaFact]
    public async Task A_loose_editor_save_after_a_voxel_ready_keeps_the_snapped_pixels_in_the_igt()
    {
        var book = RiseFixtures.ChestBook();
        var ing = book.Recipes[0].Ingredients.Single(i => i.Manifest.Id == "body");
        ing.VariantImages["ghost"][4, 3] = new Rgba32(200, 40, 40, 127);
        var path = Path.Combine(Directory.CreateTempSubdirectory("nfty-rise-").FullName, "Body.igt");
        await IngredientArchive.WriteAsync(path, ing.Manifest, ing.VariantImages, ing.Depths);
        var nav = new FakeNav();
        var dialogs = new RiseVoxelReadyTests.ScriptedDialogs { Answer = true };
        using var session = new CookBookSession();
        var wrapper = Nfty.App.Services.LooseWorkspace.WrapIngredient(ing);
        using var editor = EditorOver(session, dialogs, nav, path, ing, wrapper);
        await SnapFromEditor(editor, nav);

        editor.ApplyToolStroke(new[] { (0, 0) });
        await editor.SaveCommand.ExecuteAsync(null);

        using var reread = IngredientArchive.Read(path);
        Assert.False(Opacity.HasPartialAlpha(reread));
        Assert.Equal(255, Pixel(reread, "ghost", 3, 3).A);
        Assert.Equal(0, Pixel(reread, "ghost", 4, 3).A);
        Assert.Equal(255, Pixel(reread, "oak", 2, 1).A);
    }

    [AvaloniaFact]
    public async Task An_editor_that_was_somehow_dirty_refuses_to_save_over_the_snap_and_says_why()
    {
        var (session, path) = RiseVoxelReadyTests.Disk();
        var nav = new FakeNav();
        var dialogs = new RiseVoxelReadyTests.ScriptedDialogs { Answer = true };
        using var editor = EditorOver(session, dialogs, nav);
        editor.SelectVariantCommand.Execute(editor.Variants.Single(v => v.Id == "ghost"));
        editor.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(nav.Current);
        editor.ApplyToolStroke(new[] { (0, 0) });            // not reachable through the UI; the guard is for it
        await rise.MakeVoxelReadyCommand.ExecuteAsync(null);
        var snapped = File.ReadAllBytes(path);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.NotNull(dialogs.ErrorText);
        Assert.Contains("Rise changed this layer", dialogs.ErrorText);
        Assert.Equal(snapped, File.ReadAllBytes(path));
    }
}
