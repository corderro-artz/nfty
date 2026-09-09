using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The cook dialog's footer buttons are ENABLED when the state they belong to arrives.
/// </summary>
/// <remarks>
/// <para>Open Set shipped disabled. Its <c>CanExecute</c> read <c>IsDone</c> and was correct;
/// <c>IsDone</c> simply never told that command to re-read it, and a button's <c>IsEnabled</c>
/// follows <c>CanExecuteChanged</c> and nothing else. So the control was laid out, styled, rendered
/// in the capture set, and dead to the pointer.</para>
///
/// <para>Every test of it called <c>OpenSetCommand.CanExecute(null)</c>, which evaluates the
/// predicate directly and cannot see the notification at all — the same shape as "a test that calls
/// the command is not a test that the control invokes it". This one renders the real view and asks
/// the BUTTON.</para>
/// </remarks>
public class CookDialogGestureTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }
    private sealed class NoRevealer : IFolderRevealer { public void Reveal(string p) { } }

    [AvaloniaFact]
    public void Every_button_the_done_state_shows_is_one_the_pointer_can_press()
    {
        using var book = ExplorerViewModelTests.TwoRecipeBook();
        var vm = new CookDialogViewModel(book, new NoPicker(), new NoRevealer(), new FakeDialogs());
        var view = new Views.CookDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // Rendered FIRST and flipped after, which is the order the running app does it in: the
            // dialog is on screen while the cook runs. Setting the state before the first layout
            // would let a binding pick the value up on its initial read and hide the missing
            // notification entirely.
            vm.IsDone = true;
            vm.ResultText = "Cooked 2 assets.";
            vm.OutputPath = @"C:\Temp\demo";
            Dispatcher.UIThread.RunJobs();

            // IsEffectivelyEnabled, NOT IsEnabled. A Button drives the former from its command's
            // CanExecuteChanged and leaves the latter exactly as the markup set it - so the first cut
            // of this test read `IsEnabled`, saw True on a button the pointer could not press, and
            // passed with the notification deleted. That is the vacuous-assertion trap again: it was
            // reading a value nothing under test writes.
            var dead = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.IsEffectivelyVisible && !b.IsEffectivelyEnabled)
                .Select(b => b.Content as string ?? b.Name ?? "<unnamed>")
                .ToArray();

            Assert.True(dead.Length == 0,
                "the done state shows buttons nothing can press: " + string.Join(", ", dead));
        }
        finally { window.Close(); }
    }
}
