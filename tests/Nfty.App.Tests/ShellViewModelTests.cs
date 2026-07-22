using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ShellViewModelTests
{
    private static ShellViewModel Make(out FakeNotYetWired notify)
    {
        notify = new FakeNotYetWired();
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(nav, dialogs, notify, new StubTheme());
        return shell;
    }

    private sealed class StubTheme : Nfty.App.Services.IThemeService
    { public bool IsDark { get; private set; } public void Toggle() => IsDark = !IsDark; }

    [Fact]
    public void Zoom_in_and_out_stays_within_50_to_300()
    {
        var shell = Make(out _);
        for (int i = 0; i < 50; i++) shell.ZoomInCommand.Execute(null);
        Assert.True(shell.Zoom <= 300);
        for (int i = 0; i < 50; i++) shell.ZoomOutCommand.Execute(null);
        Assert.True(shell.Zoom >= 50);
        shell.ZoomResetCommand.Execute(null);
        Assert.Equal(100, shell.Zoom);
    }

    [Fact]
    public void Open_kitchen_reports_not_yet_wired()
    {
        var shell = Make(out var notify);
        shell.OpenKitchenCommand.Execute(null);
        Assert.Equal("New Kitchen", notify.Last);
    }
}
