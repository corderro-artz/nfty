using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nfty.App;
using Xunit;

namespace Nfty.App.Tests;

public class BootTests
{
    [Fact]
    public void App_assembly_marker_is_reachable() => Assert.NotNull(typeof(AssemblyMarker));

    [AvaloniaFact]
    public void A_headless_window_can_be_shown()
    {
        var window = new Window { Content = new TextBlock { Text = "nfty" } };
        window.Show();
        Assert.True(window.IsVisible);
    }
}
