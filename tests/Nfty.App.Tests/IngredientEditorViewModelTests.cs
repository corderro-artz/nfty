using Nfty.App.ViewModels;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

public class IngredientEditorViewModelTests
{
    private static IngredientEditorViewModel Make(out FakeNotYetWired n, out FakeNav nav)
    { n = new FakeNotYetWired(); nav = new FakeNav(); return new IngredientEditorViewModel(nav, n); }

    [Fact]
    public void Select_tool_sets_the_active_tool()
    {
        var vm = Make(out _, out _);
        vm.SelectToolCommand.Execute(EditorTool.Fill);
        Assert.Equal(EditorTool.Fill, vm.ActiveTool);
    }

    [Fact]
    public void Mode_toggle_changes_the_layer_kind()
    {
        var vm = Make(out _, out _);
        vm.Mode = LayerKind.Static;
        Assert.Equal(LayerKind.Static, vm.Mode);
    }

    [Fact]
    public void Paint_and_save_report_not_yet_wired()
    {
        var vm = Make(out var n, out _);
        vm.ApplyStrokeCommand.Execute(null); Assert.Equal("Paint", n.Last);
        vm.SaveCommand.Execute(null); Assert.Equal("Save ingredient", n.Last);
    }

    [Fact]
    public void Select_variant_sets_the_selected_variant()
    {
        var vm = Make(out _, out _);
        var second = vm.Variants[1];
        vm.SelectVariantCommand.Execute(second);
        Assert.Same(second, vm.SelectedVariant);
    }

    [Fact]
    public void Undo_and_redo_report_not_yet_wired()
    {
        var vm = Make(out var n, out _);
        vm.UndoCommand.Execute(null); Assert.Equal("Undo", n.Last);
        vm.RedoCommand.Execute(null); Assert.Equal("Redo", n.Last);
    }

    [Fact]
    public void Enlarge_and_fill_pane_preview_report_not_yet_wired()
    {
        var vm = Make(out var n, out _);
        vm.EnlargePreviewCommand.Execute(null); Assert.Equal("Enlarge preview", n.Last);
        vm.FillPanePreviewCommand.Execute(null); Assert.Equal("Fill pane", n.Last);
    }
}
