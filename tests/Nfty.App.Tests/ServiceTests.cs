using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ServiceTests
{
    private sealed class PageA : ViewModelBase { }
    private sealed class PageB : ViewModelBase { }

    [Fact]
    public void Navigation_sets_current_and_back_restores_previous()
    {
        var nav = new NavigationService();
        var a = new PageA();
        var b = new PageB();
        nav.To(a);
        nav.To(b);
        Assert.Same(b, nav.Current);
        nav.Back();
        Assert.Same(a, nav.Current);
    }

    [Fact]
    public void NotYetWired_records_and_raises_the_last_action()
    {
        var n = new NotYetWired();
        string? seen = null;
        n.Reported += a => seen = a;
        n.Report("Open CookBook");
        Assert.Equal("Open CookBook", n.Last);
        Assert.Equal("Open CookBook", seen);
    }
}
