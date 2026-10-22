using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Rise from the Set browser: one button for the asset the rail shows, the Set lent to the page and
/// handed back untouched, and the seal gating exports but not looking.
/// </summary>
public class RiseFromSetBrowserTests
{
    private sealed class Rig : IDisposable
    {
        public readonly FakeNav Nav = new();
        public readonly SetBrowserViewModel Browser;
        public readonly LoadedSet Set;

        public Rig(LoadedSet set)
        {
            Set = set;
            Browser = new SetBrowserViewModel(set, new FilePickerService(), new FakeDialogs(), new StatusService(),
                new NoopFolderRevealer(),
                rise: src => new RiseViewModel(src, new ImageBridge(), Nav, new FakeDialogs(),
                    new FilePickerService(), new StatusService()),
                nav: Nav, bridge: new ImageBridge());
        }

        // Both off-thread searches (the browser's and the page's) post their result to the UI thread
        // when they finish. Torn down before that, the post lands on disposed objects in whatever
        // test pumps next, from a thread that does not own them - so settle first, as the Set
        // browser's own tests do, then tear down.
        public void Dispose()
        {
            var searches = new System.Collections.Generic.List<System.Threading.Tasks.Task> { Browser.BookSearch };
            if (Nav.Current is RiseViewModel { Source: SetRiseSource src }) searches.Add(src.BookSearch);
            var all = System.Threading.Tasks.Task.WhenAll(searches);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!all.IsCompleted && sw.ElapsedMilliseconds < 5000)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(1);
            }
            Dispatcher.UIThread.RunJobs();
            (Nav.Current as IDisposable)?.Dispose();
            Browser.Dispose();
        }
    }

    // RisePages hands the Set back as the owner of the page it builds; the page is dropped and the
    // Set reopened under a browser. The source disposes nothing of the Set's.
    private static Rig Ordinary()
    {
        var (vm, owner) = RisePages.SetVm();
        vm.Dispose();
        return new Rig((LoadedSet)owner);
    }

    [AvaloniaFact]
    public void Rise_opens_on_the_shown_asset()
    {
        using var rig = Ordinary();
        rig.Browser.SelectCommand.Execute(rig.Browser.Items[2]);
        rig.Browser.Hover(rig.Browser.Items[5]);        // the rail follows the pointer: Rise follows the rail

        Assert.True(rig.Browser.RiseCommand.CanExecute(null));
        rig.Browser.RiseCommand.Execute(null);

        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.IsType<SetRiseSource>(rise.Source);
        Assert.Equal($"{rig.Browser.Items[5].Number:D4}", rise.Source.InitialItem.Id);
        Assert.Equal(rig.Browser.Items.Count, rise.Source.Items.Count);
    }

    [AvaloniaFact]
    public void Rise_needs_the_app_to_offer_it()
    {
        var (vm, owner) = RisePages.SetVm();
        vm.Dispose();
        var bare = new SetBrowserViewModel((LoadedSet)owner);
        Assert.False(bare.RiseCommand.CanExecute(null));
        bare.Dispose();
    }

    [AvaloniaFact]
    public void A_sealed_set_can_be_risen_but_not_exported()
    {
        var (vm, owner) = RisePages.SealedSetVm();
        vm.Dispose();
        using var rig = new Rig((LoadedSet)owner);

        Assert.True(rig.Browser.IsSealed);
        Assert.False(rig.Browser.ExportCommand.CanExecute(null));
        Assert.True(rig.Browser.RiseCommand.CanExecute(null));
        rig.Browser.RiseCommand.Execute(null);

        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);
        Assert.NotNull(rise.Source.ExportRefusal);
    }

    // A packed .set unpacks into a temp directory that disposing the Set deletes - the only kind of
    // Set whose disposal is observable, so the only kind that can prove Back does not do it.
    private static Rig Packed()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-packed-").FullName;
        using (var cooked = Nfty.Core.Generation.Generator.Generate(RiseFixtures.ChestBook(),
                   new Nfty.Core.Generation.GenerateOptions(8, "launch", EnforceUniqueDna: false)))
            SetWriter.Write(cooked, dir, pack: true);
        return new Rig(SetReader.Read(Directory.GetFiles(dir, "*.set").Single()));
    }

    [AvaloniaFact]
    public void Coming_back_keeps_the_browser_and_its_selection()
    {
        using var rig = Packed();
        rig.Browser.SelectCommand.Execute(rig.Browser.Items[3]);
        var selected = rig.Browser.SelectedItem;
        rig.Browser.RiseCommand.Execute(null);
        var rise = Assert.IsType<RiseViewModel>(rig.Nav.Current);

        rise.Dispose();                 // what Back does to the page it pops

        Assert.True(Directory.Exists(rig.Set.SourceDirectory));
        Assert.Same(selected, rig.Browser.SelectedItem);
        Assert.True(rig.Browser.Items[3].DecodeNow().PixelSize.Width > 0);
        Assert.True(rig.Browser.RiseCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void The_rise_button_sits_with_the_controls()
    {
        using var rig = Ordinary();
        var view = new Views.SetBrowserView { DataContext = rig.Browser };
        var window = new Window { Width = 1280, Height = 720, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
        var rise = buttons.Single(b => b.Command == rig.Browser.RiseCommand);
        var export = buttons.Single(b => b.Command == rig.Browser.ExportCommand);
        var rarity = buttons.First(b => b.Command == rig.Browser.ShowRarityOddsCommand);

        Assert.Same(rise.GetVisualParent(), export.GetVisualParent());
        double riseX = rise.TranslatePoint(default, view)!.Value.X;
        double exportX = export.TranslatePoint(default, view)!.Value.X;
        double rarityX = rarity.TranslatePoint(default, view)!.Value.X;
        Assert.True(rarityX < riseX && riseX < exportX, $"{rarityX} < {riseX} < {exportX}");
        Assert.True(riseX > view.Bounds.Width / 2);                       // the control half
        Assert.True(exportX + export.Bounds.Width <= view.Bounds.Width);  // Export not pushed off
        window.Close();
    }

    [AvaloniaFact]
    public void The_header_controls_stay_on_one_row_at_the_smallest_page()
    {
        using var rig = Ordinary();
        var view = new Views.SetBrowserView { DataContext = rig.Browser };
        double w = (ShellViewModel.MinWindowWidth - 24) / ShellViewModel.BaseScale;
        var window = new Window { Width = w, Height = 526, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var buttons = view.GetVisualDescendants().OfType<Button>().ToList();
        var rise = buttons.Single(b => b.Command == rig.Browser.RiseCommand);
        var export = buttons.Single(b => b.Command == rig.Browser.ExportCommand);
        double ry = rise.TranslatePoint(default, view)!.Value.Y;
        double ey = export.TranslatePoint(default, view)!.Value.Y;
        Assert.Equal(ry, ey);
        Assert.Equal(rise.Bounds.Height, export.Bounds.Height);
        // INK, not Bounds: the label must fit inside the button it is drawn in.
        var label = rise.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Rise");
        Assert.True(label.Bounds.Width <= rise.Bounds.Width);
        Assert.True(export.TranslatePoint(new Point(export.Bounds.Width, 0), view)!.Value.X <= view.Bounds.Width);
        window.Close();
    }

    private static SetBrowserViewModel Offered(LoadedSet set, FakeNav? nav, IImageBridge? bridge, bool rise = true) =>
        new(set, rise: rise ? (_ => throw new NotSupportedException()) : null, nav: nav, bridge: bridge);

    [AvaloniaFact]
    public void Rise_needs_each_of_the_factory_the_navigation_and_the_bridge()
    {
        var (vm, owner) = RisePages.SetVm();
        vm.Dispose();
        var set = (LoadedSet)owner;
        var all = Offered(set, new FakeNav(), new ImageBridge());
        Assert.True(all.RiseCommand.CanExecute(null));
        Assert.False(Offered(set, null, new ImageBridge()).RiseCommand.CanExecute(null));
        Assert.False(Offered(set, new FakeNav(), null).RiseCommand.CanExecute(null));
        Assert.False(Offered(set, new FakeNav(), new ImageBridge(), rise: false).RiseCommand.CanExecute(null));
        all.Dispose();
    }

    [AvaloniaFact]
    public void Hovering_and_selecting_tell_the_button_to_look_again()
    {
        using var rig = Ordinary();
        int raised = 0;
        rig.Browser.RiseCommand.CanExecuteChanged += (_, _) => raised++;

        rig.Browser.Hover(rig.Browser.Items[1]);
        Assert.True(raised > 0);
        raised = 0;
        rig.Browser.Hover(null);
        Assert.True(raised > 0);
        raised = 0;
        rig.Browser.SelectCommand.Execute(rig.Browser.Items[2]);
        Assert.True(raised > 0);
    }
}
