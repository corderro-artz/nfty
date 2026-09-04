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
    public void NoopFolderRevealer_Reveal_does_not_throw()
    {
        var revealer = new NoopFolderRevealer();
        revealer.Reveal("x");  // Must not throw
    }
}
