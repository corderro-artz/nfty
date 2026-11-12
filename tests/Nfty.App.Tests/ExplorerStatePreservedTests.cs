using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.Imaging;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// What the Explorer must NOT lose when the graph under it is replaced.
/// </summary>
/// <remarks>
/// A save rebuilds the whole tree from a new graph. Selection was already carried across by id;
/// expansion was not, so saving a layer dropped the author back on a fully collapsed root, three
/// levels from where they were working. Status messages also clear when opening another book;
/// the lock badges carry the persistent mode.
/// </remarks>
public class ExplorerStatePreservedTests
{
    private static ExplorerViewModel Explorer(LoadedCookBook book, IStatusService status)
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var session = new CookBookSession();
        return new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
    }

    private static IEnumerable<ExplorerNode> Descend(ExplorerNode n)
    {
        yield return n;
        foreach (var c in n.Children)
            foreach (var d in Descend(c)) yield return d;
    }

    private static ExplorerNode Node(ExplorerViewModel vm, string id) =>
        Descend(vm.Root!).First(n => n.Id == id);

    [AvaloniaFact]
    public void A_book_opens_with_its_root_already_expanded()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        using var vm = Explorer(book, new StatusService());

        Assert.True(vm.Root!.IsExpanded, "a collapsed root says nothing about what is in the book");
    }

    [AvaloniaFact]
    public void Open_branches_survive_the_graph_being_replaced()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        using var vm = Explorer(book, new StatusService());

        var openId = book.Recipes[0].Manifest.Id;
        var shutId = book.Recipes[1].Manifest.Id;
        Node(vm, openId).IsExpanded = true;
        Node(vm, shutId).IsExpanded = false;

        vm.OnEditorSaved(book);   // what a save does: hand the page an entirely new tree

        Assert.True(vm.Root!.IsExpanded);
        Assert.True(Node(vm, openId).IsExpanded, "the open branch closed itself when the tree rebuilt");
        Assert.False(Node(vm, shutId).IsExpanded, "a closed branch must not open itself either");
    }

    [AvaloniaFact]
    public void The_status_line_is_clear_on_open()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var status = new StatusService();
        using var vm = Explorer(book, status);

        Assert.False(vm.IsEditing);                        // a book opens locked
        Assert.Equal("", status.Last);
    }

    [AvaloniaFact]
    public void Opening_a_second_book_does_not_inherit_the_first_ones_lock_line()
    {
        var status = new StatusService();

        using (var first = ExplorerViewModelTests.TwoRecipeBook())
        using (var vm = Explorer(first, status))
        {
            vm.ToggleLockCommand.Execute(null);
            Assert.True(vm.IsEditing);
            Assert.Equal("", status.Last);
            status.Say("A previous action's message.");
        }

        using var second = ExplorerViewModelTests.TwoRecipeBook();
        using var vm2 = Explorer(second, status);

        Assert.False(vm2.IsEditing);
        Assert.Equal("", status.Last);
    }
}
