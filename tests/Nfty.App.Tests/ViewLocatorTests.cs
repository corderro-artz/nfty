using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Nfty.App;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ViewLocatorTests
{
    private sealed class SampleViewModel : ViewModelBase { }

    [AvaloniaFact]
    public void Build_returns_a_placeholder_for_a_viewmodel_with_no_view()
    {
        var locator = new ViewLocator();
        var control = locator.Build(new SampleViewModel());
        Assert.IsType<TextBlock>(control);
    }

    [AvaloniaFact]
    public void Match_is_true_for_viewmodels_and_false_otherwise()
    {
        var locator = new ViewLocator();
        Assert.True(locator.Match(new SampleViewModel()));
        Assert.False(locator.Match("not a vm"));
    }
}
