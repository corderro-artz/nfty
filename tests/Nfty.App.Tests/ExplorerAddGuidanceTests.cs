using System.IO;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Adding is GATED (edit-lock, and a .cbk on disk to write to), not unbuilt. Those gated states used
/// to be reported through <see cref="INotYetWired"/>, which the shell renders as "Not wired yet: …" —
/// so a working feature told the user it did not exist. These pin the split: gated paths must speak
/// through <see cref="IStatusService"/> and must NOT touch the not-wired channel.
/// </summary>
public class ExplorerAddGuidanceTests
{
    private static (ExplorerViewModel vm, FakeNotYetWired notify, StatusService status, CookBookSession session, string path, FakeNav nav)
        Explorer()
    {
        (var path, var session, _, _) = IngredientEditorSaveTests.OnDisk();
        var nav = new FakeNav(); var dialogs = new FakeDialogs();
        var notify = new FakeNotYetWired(); var status = new StatusService();
        var vm = new ExplorerViewModel(session.Current!, nav, dialogs, notify, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session,
            new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);
        return (vm, notify, status, session, path, nav);
    }

    [AvaloniaFact]
    public async Task Add_while_locked_explains_the_lock_and_never_claims_to_be_unwired()
    {
        var (vm, notify, status, session, path, _) = Explorer();
        try
        {
            vm.SelectNodeCommand.Execute(vm.Root);      // cookbook root, edit-lock still ON
            await vm.AddCommand.ExecuteAsync(null);

            Assert.Null(notify.Last);                   // must NOT go through the not-wired channel
            Assert.NotNull(status.Last);
            Assert.Contains("lock", status.Last!, System.StringComparison.OrdinalIgnoreCase);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Add_on_an_ingredient_opens_the_editor_that_owns_variants()
    {
        var (vm, notify, status, session, path, nav) = Explorer();
        try
        {
            vm.ToggleLockCommand.Execute(null);                       // unlock
            var ing = vm.Root.Children[0].Children[0];
            vm.SelectNodeCommand.Execute(ing);
            await vm.AddCommand.ExecuteAsync(null);

            // The assertion that gives this test its name. Without it the test passes with the
            // ingredient branch of Add() DELETED, because the default fallback also speaks through
            // status and also leaves the not-wired channel alone - so "notify is null AND status
            // said something" is satisfied by doing nothing useful. Verified: with that branch
            // removed, the two assertions below stayed green and only this one goes red.
            Assert.IsType<IngredientEditorViewModel>(nav.Current);

            Assert.Null(notify.Last);                                 // not a "not wired" case
            Assert.NotNull(status.Last);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [AvaloniaFact]
    public void Clicking_a_layer_selects_that_ingredient_instead_of_reporting_a_stub()
    {
        var (vm, notify, status, session, path, _) = Explorer();
        try
        {
            var ingId = vm.Root.Children[0].Children[0].Id;
            vm.OpenIngredientCommand.Execute(ingId);
            Assert.Equal(ingId, vm.SelectedNode!.Id);   // really navigates
            Assert.Null(notify.Last);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>Opening a cookbook used to leave NOTHING selected, so the Add button read a bare
    /// "Add", had no target, and fell through to the not-wired stub - the dead end reported from a
    /// real run. The cookbook is now selected on open, so Add is immediately meaningful.</summary>
    [AvaloniaFact]
    public void A_freshly_opened_explorer_selects_the_cookbook_so_add_has_a_target()
    {
        var (vm, notify, status, session, path, _) = Explorer();
        try
        {
            Assert.NotNull(vm.SelectedNode);
            Assert.Equal(vm.Root.Id, vm.SelectedNode!.Id);
            Assert.Equal("Add recipe", vm.AddLabel);   // not a bare "Add"
            Assert.Null(notify.Last);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    /// <summary>The edit lock is a toggle with real state: the tooltip states the current mode and the
    /// status line confirms the change (it previously had no visible state at all).</summary>
    [AvaloniaFact]
    public void Toggling_the_lock_announces_and_describes_the_new_state()
    {
        var (vm, notify, status, session, path, _) = Explorer();
        try
        {
            // "unlocked" CONTAINS "locked", so asserting the locked tip merely contains "locked"
            // passes in both states and cannot tell them apart. Assert the absence of the other word.
            Assert.DoesNotContain("unlocked", vm.LockTip, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("locked", vm.LockTip, System.StringComparison.OrdinalIgnoreCase);

            vm.ToggleLockCommand.Execute(null);
            Assert.True(vm.IsEditing);
            Assert.Contains("unlocked", vm.LockTip, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unlocked", status.Last!, System.StringComparison.OrdinalIgnoreCase);
            vm.Dispose();
        }
        finally { session.Dispose(); Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
