using System.IO;
using Nfty.App.Models;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

public class LandingViewModelTests
{
    private static LandingViewModel Make(out FakeNotYetWired notify, out FakeDialogs dialogs)
        => Make(new RecentsService(Directory.CreateTempSubdirectory().FullName), out notify, out dialogs);

    private static LandingViewModel Make(IRecentsService recents, out FakeNotYetWired notify, out FakeDialogs dialogs)
    {
        notify = new FakeNotYetWired();
        dialogs = new FakeDialogs();
        var nav = new FakeNav(); var d = dialogs; var no = notify;
        return new LandingViewModel(nav, dialogs, notify,
            new FilePickerService(), recents, new CookBookSession(),
            book => new ExplorerViewModel(book, nav, d, no, new ImageBridge(), ExplorerViewModelTests.EditorFactory(nav),
                ExplorerViewModelTests.CookFactory(d), new CookBookSession(),
                new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, new CookBookSession(), d), new StatusService()),
            set => new SetBrowserViewModel(set),
                (_, _, _) => null!);
    }

    [Fact]
    public void New_cookbook_opens_the_wizard_dialog()
    {
        var vm = Make(out _, out var dialogs);
        vm.NewCookBookCommand.Execute(null);
        Assert.IsType<NewCookBookViewModel>(dialogs.Active);
    }

    [Fact]
    public void A_fresh_landing_has_no_recents()
    {
        var vm = Make(out _, out _);
        Assert.Empty(vm.Recents);
    }

    [Fact]
    public void Recents_reflect_the_underlying_service()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var recents = new RecentsService(dir);
            recents.Add(new RecentItem("VaporPets", "3 recipes · 1000x1000", Path.Combine(dir, "vaporpets.cbk"), false));
            var vm = Make(recents, out _, out _);
            Assert.NotEmpty(vm.Recents);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void New_kitchen_is_disabled_reserved()
    {
        var vm = Make(out _, out _);
        Assert.False(vm.NewKitchenCommand.CanExecute(null));
    }
}
