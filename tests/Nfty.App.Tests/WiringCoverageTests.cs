using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class WiringCoverageTests
{
    private static bool HasCommand(object vm, string name) =>
        vm.GetType().GetProperty(name)?.GetValue(vm) is System.Windows.Input.ICommand;

    [Fact]
    public void Landing_exposes_every_mapped_command()
    {
        var vm = new LandingViewModel(new FakeNav(), new FakeDialogs(), new FakeNotYetWired(),
            new FilePickerService(), new RecentsService());
        foreach (var c in new[] { "NewCookBookCommand","NewKitchenCommand","NewRecipeCommand","NewIngredientCommand",
                                  "OpenCookBookCommand","ImportCommand","OpenSetCommand","OpenRecentCommand","ShowHelpCommand" })
            Assert.True(HasCommand(vm, c), $"Landing missing {c}");
    }

    [Fact]
    public void Explorer_exposes_every_mapped_command()
    {
        var vm = new ExplorerViewModel(new FakeNav(), new FakeDialogs(), new FakeNotYetWired());
        foreach (var c in new[] { "ToggleLockCommand","SearchCommand","AddCommand","DeleteSelectedCommand",
                                  "ImportCommand","SelectNodeCommand","OpenIngredientCommand" })
            Assert.True(HasCommand(vm, c), $"Explorer missing {c}");
    }

    [Fact]
    public void Editor_exposes_every_mapped_command()
    {
        var vm = new IngredientEditorViewModel(new FakeNav(), new FakeNotYetWired());
        foreach (var c in new[] { "SelectToolCommand","UndoCommand","RedoCommand","AddVariantCommand",
                                  "DuplicateVariantCommand","DeleteVariantCommand","ApplyStrokeCommand",
                                  "RerollPreviewCommand","EnlargePreviewCommand","FillPanePreviewCommand",
                                  "SaveCommand","BackCommand" })
            Assert.True(HasCommand(vm, c), $"Editor missing {c}");
    }
}
