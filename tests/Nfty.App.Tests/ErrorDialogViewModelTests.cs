using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class ErrorDialogViewModelTests
{
    [Fact]
    public void Close_clears_the_active_dialog()
    {
        var dialogs = new FakeDialogs();
        var vm = new ErrorDialogViewModel(dialogs, "Could not open", "bad archive");
        dialogs.ShowAsync<object>(vm);
        Assert.Equal("bad archive", vm.Message);
        vm.CloseCommand.Execute(null);
        Assert.Null(dialogs.Active);
    }
}
