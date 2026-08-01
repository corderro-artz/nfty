using Avalonia.Headless.XUnit;
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
        var nav = new FakeNav(); var dialogs = new FakeDialogs(); var notify = new FakeNotYetWired();
        var vm = new LandingViewModel(nav, dialogs, notify,
            new FilePickerService(), new RecentsService(System.IO.Directory.CreateTempSubdirectory().FullName), new CookBookSession(),
            book => new ExplorerViewModel(book, nav, dialogs, notify, new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs)),
            set => new SetBrowserViewModel(set),
                (_, _, _) => null!);
        foreach (var c in new[] { "NewCookBookCommand","NewKitchenCommand","NewRecipeCommand","NewIngredientCommand",
                                  "OpenCookBookCommand","ImportCommand","OpenSetCommand","OpenRecentCommand","ShowHelpCommand" })
            Assert.True(HasCommand(vm, c), $"Landing missing {c}");
    }

    [Fact]
    public void Explorer_exposes_every_mapped_command()
    {
        // §6.2's "expand" row is intentionally not a VM command: tree expand/collapse is handled by
        // Avalonia's native TreeView expander, so a ToggleExpand command would be vestigial. This
        // test covers the row via that documented decision rather than a command assertion.
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        using var vm = new ExplorerViewModel(ExplorerViewModelTests.TwoRecipeBook(), nav, dialogs, new FakeNotYetWired(), new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
            ExplorerViewModelTests.CookFactory(dialogs), new CookBookSession(),
            new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), dialogs));
        foreach (var c in new[] { "ToggleLockCommand","AddCommand","DeleteSelectedCommand",
                                  "ImportCommand","SelectNodeCommand","OpenIngredientCommand" })
            Assert.True(HasCommand(vm, c), $"Explorer missing {c}");
    }

    [AvaloniaFact]
    public void Editor_exposes_every_mapped_command()
    {
        var (ing, recipe, book) = IngredientEditorViewModelTests.Real();
        using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(), new FakeNotYetWired(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        // ApplyStroke is no longer a mapped command: painting commits via vm.ApplyToolStroke(points),
        // a plain method called by the view's pointer handlers (Task 3) with the gesture's pixel path.
        foreach (var c in new[] { "SelectToolCommand","UndoCommand","RedoCommand","AddVariantCommand",
                                  "DuplicateVariantCommand","DeleteVariantCommand",
                                  "RerollPreviewCommand","EnlargePreviewCommand","FillPanePreviewCommand",
                                  "SaveCommand","BackCommand","SelectVariantCommand","ImportImageCommand" })
            Assert.True(HasCommand(vm, c), $"Editor missing {c}");
    }
}
