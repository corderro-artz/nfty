using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class NavigationServiceTests
{
    private sealed class DisposablePage : ViewModelBase, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Back_disposes_the_popped_page()
    {
        var nav = new NavigationService();
        var home = new DisposablePage();
        var page = new DisposablePage();
        nav.To(home);
        nav.To(page);

        nav.Back();

        Assert.True(page.Disposed);     // popped page freed
        Assert.False(home.Disposed);    // page still current is untouched
        Assert.Same(home, nav.Current);
    }

    [Fact]
    public void Dispose_disposes_every_remaining_page()
    {
        var nav = new NavigationService();
        var a = new DisposablePage();
        nav.To(a);
        nav.Dispose();
        Assert.True(a.Disposed);
    }
}
