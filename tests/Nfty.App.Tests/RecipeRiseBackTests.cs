using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Back from a Recipe Rise page, through the real page stack with the page's view on screen.</summary>
public class RecipeRiseBackTests
{
    private sealed class Root : ViewModelBase { }

    [AvaloniaFact]
    public async Task Back_from_the_recipe_rise_page_leaves_it_cleanly_with_its_view_showing()
    {
        var (session, _) = RisePages.DemoSession();
        var book = session.Current!;
        var nav = new NavigationService();
        nav.To(new Root());
        var dialogs = new FakeDialogs();
        var vm = new RiseViewModel(
            new RecipeRiseSource(book, book.Recipes.Single(r => r.Manifest.Id == "chest"), 1, new ImageBridge(),
                session, () => true),
            new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService());
        nav.To(vm);
        var view = new RiseView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var unhandled = new System.Collections.Generic.List<Exception>();
            Dispatcher.UIThread.UnhandledException += (_, e) => { unhandled.Add(e.Exception); e.Handled = true; };

            await vm.BackCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();

            Assert.IsType<Root>(nav.Current);
            Assert.True(unhandled.Count == 0, string.Join(Environment.NewLine, unhandled.Select(e => e.ToString())));
        }
        finally { window.Close(); session.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Back_from_the_recipe_rise_page_opened_from_the_explorer_with_every_view_live()
    {
        var (session, _) = RisePages.DemoSession();
        var nav = new NavigationService();
        var dialogs = new FakeDialogs();
        var status = new StatusService();
        nav.To(new Root());
        var explorer = new ExplorerViewModel(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status,
            riseFactory: src => new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), status));
        nav.To(explorer);
        var host = new ContentControl();
        var locator = new Nfty.App.ViewLocator();
        // The shell's own wiring: the page's view INHERITS its DataContext from the host, so when the
        // page changes the old view sees its context move while the page it bound has been disposed.
        void Show()
        {
            host.DataContext = nav.Current;
            host.Content = nav.Current is { } c ? locator.Build(c) : null;
        }
        nav.Changed += Show;
        Show();
        var window = new Window { Content = host, Width = 1366, Height = 768 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var unhandled = new System.Collections.Generic.List<Exception>();
            Dispatcher.UIThread.UnhandledException += (_, e) => { unhandled.Add(e.Exception); e.Handled = true; };

            explorer.ToggleLockCommand.Execute(null);
            explorer.SelectNodeCommand.Execute(explorer.Root.Children.First(n => n.Id == "chest"));
            Dispatcher.UIThread.RunJobs();
            var pane = Assert.IsType<RecipeDetailViewModel>(explorer.CurrentDetail);
            pane.RiseCommand.Execute(null);
            var rise = Assert.IsType<RiseViewModel>(nav.Current);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();

            await rise.BackCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(explorer, nav.Current);
            Assert.True(unhandled.Count == 0, string.Join(Environment.NewLine, unhandled.Select(e => e.ToString())));
        }
        finally { window.Close(); explorer.Dispose(); session.Dispose(); }
    }
}
