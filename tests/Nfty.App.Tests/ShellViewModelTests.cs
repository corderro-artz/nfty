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
}
