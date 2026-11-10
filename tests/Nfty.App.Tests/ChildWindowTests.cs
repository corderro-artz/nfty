using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Desktop;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The other-windows badge: in full screen it is the only way back to a viewer behind the shell, so
/// it lists the open windows, raises the one chosen, cycles on a chord, and never moves the titlebar.
/// </summary>
public class ChildWindowTests
{
    private static ChildWindowEntry Entry(string title, string? hint, System.Action? activate = null) => new()
    {
        Title = () => title, Hint = () => hint, Activate = activate ?? (() => { }),
    };

    private static ShellViewModel Shell(IChildWindows windows) =>
        new(new FakeNav(), new DialogService(), new ThemeService(), new StatusService(), childWindows: windows);

    [Fact]
    public void The_registry_adds_removes_and_cycles_in_order()
    {
        var reg = new ChildWindows();
        var order = new System.Collections.Generic.List<string>();
        var a = reg.Register(Entry("a", null, () => order.Add("a")));
        reg.Register(Entry("b", null, () => order.Add("b")));

        reg.ActivateNext(); reg.ActivateNext(); reg.ActivateNext();
        Assert.Equal(["a", "b", "a"], order);

        a.Dispose();
        a.Dispose();                                   // idempotent
        Assert.Single(reg.Windows);
        Assert.NotNull(reg.ActivateNext());
        Assert.Null(new ChildWindows().ActivateNext());
    }

    [Fact]
    public void The_shell_lists_windows_with_their_hint_and_a_row_raises_its_window()
    {
        var reg = new ChildWindows();
        var raised = 0;
        reg.Register(Entry("Chest", "live", () => raised++));
        var shell = Shell(reg);

        Assert.True(shell.HasChildWindows);
        Assert.Equal("1", shell.ChildWindowCountText);
        var row = Assert.Single(shell.ChildWindowItems);
        Assert.Equal("Chest", row.Title);
        Assert.Equal("live", row.Hint);

        row.ActivateCommand.Execute(null);
        Assert.Equal(1, raised);

        reg.Register(Entry("Set", "snapshot"));
        Assert.Equal("2", shell.ChildWindowCountText);
    }

    [Fact]
    public void The_chords_cycle_and_list_and_say_so_when_nothing_is_open()
    {
        var reg = new ChildWindows();
        var shell = Shell(reg);
        var menu = 0;
        shell.ChildWindowMenuRequested += () => menu++;

        shell.ShowChildWindowsCommand.Execute(null);
        shell.CycleChildWindowsCommand.Execute(null);
        Assert.Equal(0, menu);
        Assert.False(shell.HasChildWindows);

        var raised = 0;
        reg.Register(Entry("x", null, () => raised++));
        shell.CycleChildWindowsCommand.Execute(null);
        shell.ShowChildWindowsCommand.Execute(null);
        Assert.Equal(1, raised);
        Assert.Equal(1, menu);
    }

    [AvaloniaFact]
    public void The_badge_slot_is_always_laid_out_and_only_inked_when_a_window_is_open()
    {
        var reg = new ChildWindows();
        var shell = Shell(reg);
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var badge = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ChildWindowsButton");
            var lockFlag = view.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("lockflag"));
            var before = (badge.Bounds, badge.TranslatePoint(default, window), lockFlag.TranslatePoint(default, window));

            Assert.Equal(0, badge.Opacity);
            Assert.False(badge.IsHitTestVisible);
            Assert.True(badge.Bounds.Width > 0);

            reg.Register(Entry("Chest", "live"));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, badge.Opacity);
            Assert.True(badge.IsHitTestVisible);
            Assert.Equal(before, (badge.Bounds, badge.TranslatePoint(default, window), lockFlag.TranslatePoint(default, window)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Clicking_a_row_raises_the_window_and_folds_the_list()
    {
        var reg = new ChildWindows();
        var raised = 0;
        reg.Register(Entry("Chest", "live", () => raised++));
        var shell = Shell(reg);
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var badge = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "ChildWindowsButton");
            shell.ShowChildWindowsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            var menu = (Flyout)badge.ContextFlyout!;      // the list is the badge's right-click menu
            Assert.True(menu.IsOpen);

            var flyoutContent = menu.Content as Control;
            var row = flyoutContent!.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("winrow"));
            row.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, raised);
            Assert.False(menu.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_chords_are_bound_on_the_main_window()
    {
        var reg = new ChildWindows();
        var raised = 0;
        reg.Register(Entry("x", null, () => raised++));
        var window = new MainWindow { DataContext = Shell(reg) };
        window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.Backquote, RawInputModifiers.Control);
            Assert.Equal(1, raised);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_viewer_window_registers_with_the_shell_and_leaves_when_it_closes()
    {
        var reg = new ChildWindows();
        var host = new DesktopViewerHost(new FilePickerService(), new NoopClipboardService(),
            new ViewerWindowStateService(StateStore.InMemory()), new ImageBridge(), () => null, reg);
        ViewerWindow? window = null;
        host.Opened += w => window = w;
        host.Open(new ViewerViewTests.TrackedSubject("Chest", null, null));
        try
        {
            var entry = Assert.Single(reg.Windows);
            Assert.Equal("Chest", entry.Title());
            Assert.Equal("snapshot", entry.Hint!());
        }
        finally { window!.Close(); }
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(reg.Windows);
    }

    [Fact]
    public void The_chords_are_in_the_quick_reference()
    {
        Assert.Contains(HelpCatalog.Entries, e => e.Cap == KeyHints.ChildWindows);
        Assert.Contains(HelpCatalog.Entries, e => e.Cap == KeyHints.ChildWindowList);
    }

    private sealed class Win
    {
        public bool Shown; public int Raised, Hidden;
        public ChildWindowEntry Entry(string title) => new()
        {
            Title = () => title, Activate = () => { Raised++; Shown = true; },
            IsShown = () => Shown, Hide = () => { Hidden++; Shown = false; },
        };
    }

    [Fact]
    public void A_left_click_raises_the_last_window_and_a_second_puts_it_away()
    {
        var reg = new ChildWindows();
        var a = new Win(); var b = new Win();
        var ea = a.Entry("a"); var eb = b.Entry("b");
        reg.Register(ea); reg.Register(eb);
        Assert.Same(eb, reg.Last);                   // nothing used yet: the newest

        reg.Touch(ea);                               // the reader focused "a"
        Assert.Same(ea, reg.ToggleLast());
        Assert.Equal((1, 0), (a.Raised, a.Hidden));  // not shown: raised
        reg.ToggleLast();
        Assert.Equal((1, 1), (a.Raised, a.Hidden));  // shown: put away
        reg.ToggleLast();
        Assert.Equal((2, 1), (a.Raised, a.Hidden));  // and back
        Assert.Equal(0, b.Raised + b.Hidden);        // the other window never touched
    }

    [Fact]
    public void Picking_from_the_list_makes_that_window_the_last_one()
    {
        var reg = new ChildWindows();
        var a = new Win(); var b = new Win();
        reg.Register(a.Entry("a")); reg.Register(b.Entry("b"));
        var shell = Shell(reg);
        shell.RefreshChildWindows();
        shell.ChildWindowItems[0].ActivateCommand.Execute(null);   // "a" from the list
        Assert.Equal("a", reg.Last!.Title());
        a.Shown = true;
        shell.ToggleLastChildWindowCommand.Execute(null);           // left click: put "a" away
        Assert.Equal(1, a.Hidden);
    }

    [Fact]
    public void A_closed_window_is_never_the_last_one()
    {
        var reg = new ChildWindows();
        var a = new Win(); var b = new Win();
        var ea = a.Entry("a");
        var token = reg.Register(ea); reg.Register(b.Entry("b"));
        reg.Touch(ea);
        token.Dispose();
        Assert.Equal("b", reg.Last!.Title());
    }
}
