using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Make()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(nav, dialogs, new StubTheme(), new StatusService());
        return shell;
    }

    private sealed class StubTheme : Nfty.App.Services.IThemeService
    { public bool IsDark { get; private set; } public void Toggle() => IsDark = !IsDark; }

    [Fact]
    public void Zoom_in_and_out_stays_within_50_to_300()
    {
        var shell = Make();
        for (int i = 0; i < 50; i++) shell.ZoomInCommand.Execute(null);
        Assert.True(shell.Zoom <= 300);
        for (int i = 0; i < 50; i++) shell.ZoomOutCommand.Execute(null);
        Assert.True(shell.Zoom >= 50);
        shell.ZoomResetCommand.Execute(null);
        Assert.Equal(100, shell.Zoom);
    }

    // Open_kitchen_reports_not_yet_wired was here. It pinned a command that no view ever bound, so
    // it only ever proved that dead code still ran. Kitchens are unbuilt, and the honest contract
    // for an unbuilt feature is the one LandingViewModel already keeps: the control is genuinely
    // DISABLED, not enabled-and-apologising. That is asserted in LandingViewModelTests.

    [Fact]
    public void Close_dialog_clears_the_active_dialog()
    {
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new StubTheme(), new StatusService());
        _ = dialogs.ShowAsync<object>(new HelpViewModel(dialogs));
        Assert.NotNull(dialogs.Active);

        shell.CloseDialogCommand.Execute(null);

        Assert.Null(dialogs.Active);
    }

    /// <summary>
    /// A status message belongs to the page that said it, and does not survive a navigation.
    /// </summary>
    /// <remarks>
    /// The bar is a last-message board, so the Explorer's "Editing locked - unlock to make changes."
    /// followed the user onto the Set browser, a screen with no lock and nothing to unlock. Seen by
    /// cooking a Set in the running app and reading the bar underneath it.
    /// </remarks>
    [Fact]
    public void A_pages_status_message_does_not_follow_the_user_to_the_next_page()
    {
        var nav = new FakeNav();
        var status = new StatusService();
        var shell = new ShellViewModel(nav, new FakeDialogs(), new StubTheme(), status);

        nav.To(new LandingStub());
        status.Say("Editing locked - unlock to make changes.");
        Assert.Equal("Editing locked - unlock to make changes.", shell.StatusMessage);

        nav.To(new LandingStub());                     // any other page
        Assert.Equal("", shell.StatusMessage);
    }

    /// <summary>...but a page that HAS an opening line still gets to say it on arrival.</summary>
    /// <remarks>
    /// The Explorer announces its lock state from its constructor, which runs before the navigation
    /// that shows it -- so clearing on page change would have swallowed exactly the message the
    /// titlebar chip has to agree with.
    /// </remarks>
    [AvaloniaFact]
    public void The_explorer_still_announces_its_lock_state_when_it_becomes_the_current_page()
    {
        var nav = new FakeNav();
        var status = new StatusService();
        var shell = new ShellViewModel(nav, new FakeDialogs(), new StubTheme(), status);
        var (path, session, _, _) = IngredientEditorSaveTests.OnDisk();
        try
        {
            var dialogs = new FakeDialogs();
            var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
                ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
                ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
                ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status);

            nav.To(explorer);

            Assert.Contains("lock", shell.StatusMessage, System.StringComparison.OrdinalIgnoreCase);
            explorer.Dispose();
        }
        finally { session.Dispose(); System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path)!, true); }
    }

    private sealed class LandingStub : ViewModelBase { }
}
