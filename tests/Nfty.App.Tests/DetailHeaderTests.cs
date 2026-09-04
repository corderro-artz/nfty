using System.IO;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The detail pane's header band (explorer.html .pane-h.detail-h).
///
/// The explorer capture selects an ingredient, so only that variant of the header ever reaches a
/// rendered frame — the CookBook and Recipe variants have no visual evidence at all. Same class of
/// gap as the editor's disabled toolstrip and the Custom-only ingredient fixtures, so these pin the
/// two the frames cannot show, plus the ingredient one so all three stay consistent.
///
/// The title is uppercased by the ViewModel because Avalonia has no text-transform and the mockup
/// applies `text-transform: uppercase` to this band — verified against the rendered mockup, where
/// the count opts back out with `text-transform: none` but the title does not.</summary>
public class DetailHeaderTests
{
    private static (ExplorerViewModel vm, CookBookSession session, string path) Explorer()
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav(); var dialogs = new FakeDialogs();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());
        return (vm, session, path);
    }

    [AvaloniaFact]
    public void Header_describes_each_selected_kind()
    {
        var (vm, session, path) = Explorer();
        try
        {
            // CookBook — selected on open. Mockup: name + "N recipes" + COOKBOOK.
            Assert.Equal(ExplorerNodeKind.CookBook, vm.DetailKind);
            Assert.Equal("COOKBOOK", vm.DetailTag);
            Assert.Contains("recipe", vm.DetailCount);   // "1 recipe" / "3 recipes" - Pluralize owns the s
            Assert.True(vm.IsDetailCookBook);
            Assert.False(vm.IsDetailRecipe || vm.IsDetailIngredient);

            // Recipe — mockup counts LAYERS here, not ingredients or variants. Getting the noun
            // wrong is invisible in a screenshot but wrong in the domain's own vocabulary.
            var recipe = vm.Root.Children[0];
            vm.SelectNodeCommand.Execute(recipe);
            Assert.Equal("RECIPE", vm.DetailTag);
            Assert.Contains("layer", vm.DetailCount);
            Assert.True(vm.IsDetailRecipe);

            // Ingredient — mockup titles this "recipe › ingredient", not the bare name.
            var ing = recipe.Children[0];
            vm.SelectNodeCommand.Execute(ing);
            Assert.Equal("INGREDIENT", vm.DetailTag);
            Assert.Contains("variant", vm.DetailCount);
            Assert.Contains("›", vm.DetailTitle);
            Assert.True(vm.IsDetailIngredient);

            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Header_title_is_uppercased_because_avalonia_has_no_text_transform()
    {
        var (vm, session, path) = Explorer();
        try
        {
            var recipe = vm.Root.Children[0];
            vm.SelectNodeCommand.Execute(recipe);

            Assert.Equal(vm.DetailTitle.ToUpperInvariant(), vm.DetailTitle);
            // Not vacuous: the fixture's own name must have lower-case in it, or this proves nothing.
            Assert.NotEqual(recipe.Name, recipe.Name.ToUpperInvariant());
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Header_counts_track_the_selection_rather_than_being_a_fixed_string()
    {
        var (vm, session, path) = Explorer();
        try
        {
            var book = vm.DetailCount;
            vm.SelectNodeCommand.Execute(vm.Root.Children[0]);
            var recipe = vm.DetailCount;
            vm.SelectNodeCommand.Execute(vm.Root.Children[0].Children[0]);
            var ing = vm.DetailCount;

            Assert.NotEqual(book, recipe);
            Assert.NotEqual(recipe, ing);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
