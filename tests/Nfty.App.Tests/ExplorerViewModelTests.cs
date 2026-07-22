using Nfty.App.Models;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ExplorerViewModelTests
{
    private static ExplorerViewModel Make(out FakeNotYetWired n)
    { n = new FakeNotYetWired(); return new ExplorerViewModel(new FakeNav(), new FakeDialogs(), n); }

    [Fact]
    public void Opens_read_only_and_lock_toggles_editing()
    {
        var vm = Make(out _);
        Assert.False(vm.IsEditing);
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void Delete_is_disabled_until_editing()
    {
        var vm = Make(out _);
        Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
        vm.ToggleLockCommand.Execute(null);
        Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void Add_label_tracks_the_selected_node_kind()
    {
        var vm = Make(out _);
        vm.SelectNodeCommand.Execute(new ExplorerNode("r", "Cat", ExplorerNodeKind.Recipe, []));
        Assert.Equal("Add ingredient", vm.AddLabel);
        vm.SelectNodeCommand.Execute(new ExplorerNode("i", "Aura", ExplorerNodeKind.Ingredient, []));
        Assert.Equal("Add variant", vm.AddLabel);
    }

    [Fact]
    public void Search_and_import_report_not_yet_wired()
    {
        var vm = Make(out var n);
        vm.SearchCommand.Execute(null); Assert.Equal("Search (⌘K)", n.Last);
        vm.ImportCommand.Execute(null); Assert.Equal("Import", n.Last);
    }
}
