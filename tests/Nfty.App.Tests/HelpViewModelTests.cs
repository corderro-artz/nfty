using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class HelpViewModelTests
{
    [Fact]
    public void Close_clears_the_active_dialog()
    {
        var dialogs = new FakeDialogs();
        var help = new HelpViewModel(dialogs);
        dialogs.ShowAsync<object>(help);
        help.CloseCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
