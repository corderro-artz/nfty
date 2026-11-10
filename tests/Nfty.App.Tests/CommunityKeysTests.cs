using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Publish.Community;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Community page's way out and its keyboard, driven the way a reader drives them: real key presses on the
/// window (routed through the focused control, as the app routes them) and real button clicks - never a call to
/// the command the gesture is supposed to reach, which is how a chord ends up printed on the help sheet and bound
/// to nothing.
/// </summary>
public class CommunityKeysTests
{
    private sealed record Rig(Window Window, Views.CommunityView View, CommunityViewModel Vm, FakeNav Nav,
        FakeCommunityCache Cache, FakeDialogs Dialogs) : IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Vm.Dispose();
        }
    }

    private static Rig Open(int entries = 12, bool online = true, HttpClient? http = null)
    {
        var list = Enumerable.Range(0, entries).Select(i => CommunityFixtures.Entry($"e{i:00}", $"Collection {i:00}",
            tags: i % 2 == 0 ? ["props"] : ["creatures"], updated: CommunityFixtures.T0.AddDays(entries - i))).ToList();
        var notice = online ? null : ShareRules.Offline(new DateTime(2026, 11, 9, 13, 0, 0));
        var cache = new FakeCommunityCache
        {
            Next = CommunityFixtures.Listing(list, notice),
            PreviewPath = CommunityFixtures.Sheet(Directory.CreateTempSubdirectory("nfty-keys-sheet-").FullName),
            Tags = CommunityLayoutTests.GalleryTags,
        };
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = new CommunityViewModel(cache, new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName),
            new FakeGitHubAccount(), nav, dialogs, new StatusService(), _ => Task.CompletedTask, http: http);
        UiThreadWait.For(vm.PendingRefresh);
        var area = CommunityLayoutTests.PageArea;
        var view = new Views.CommunityView { DataContext = vm };
        var window = new Window { Content = view, Width = area.Width, Height = area.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        foreach (var t in vm.Tiles) UiThreadWait.For(t.PendingDecode);
        Dispatcher.UIThread.RunJobs();
        return new Rig(window, view, vm, nav, cache, dialogs);
    }

    private static void Press(Window w, PhysicalKey key, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPressQwerty(key, mods);
        w.KeyReleaseQwerty(key, mods);
        Dispatcher.UIThread.RunJobs();
    }

    private static Visual? Focused(Window w) => w.FocusManager?.GetFocusedElement() as Visual;

    /// <summary>A real left click at the button's middle, through the window's input, so the button's own
    /// Command runs exactly as a reader's click runs it.</summary>
    private static void Click(Button b)
    {
        var window = (Window)TopLevel.GetTopLevel(b)!;
        var at = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), window)!.Value;
        window.MouseMove(at);
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void The_page_takes_the_keyboard_on_arrival()
    {
        using var rig = Open();
        Assert.Same(rig.View, Focused(rig.Window));
    }

    [AvaloniaFact]
    public void The_back_button_and_alt_left_leave_the_page()
    {
        using var rig = Open();
        Click(rig.View.FindControl<Button>("BackButton")!);
        Assert.Equal(1, rig.Nav.BackCount);

        Press(rig.Window, PhysicalKey.ArrowLeft, RawInputModifiers.Alt);
        Assert.Equal(2, rig.Nav.BackCount);
    }

    [AvaloniaFact]
    public void Ctrl_K_and_slash_put_the_keyboard_in_the_search_box_and_a_letter_there_is_text()
    {
        using var rig = Open();
        var search = rig.View.FindControl<TextBox>("SearchBox")!;

        Press(rig.Window, PhysicalKey.K, RawInputModifiers.Control);
        Assert.Same(search, Focused(rig.Window)?.FindAncestorOfType<TextBox>(includeSelf: true));

        // R and T typed into the box are letters, not Refresh and the tag picker
        int fetched = rig.Cache.Refreshes;
        rig.Window.KeyTextInput("rt");
        Press(rig.Window, PhysicalKey.R);
        Press(rig.Window, PhysicalKey.T);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(fetched, rig.Cache.Refreshes);
        Assert.False(((Flyout)rig.View.FindControl<Button>("TagPickerButton")!.Flyout!).IsOpen);
        Assert.Equal("rt", rig.Vm.Query);

        // Escape clears the box first, then hands the keyboard back to the page; a third leaves
        Press(rig.Window, PhysicalKey.Escape);
        Assert.Equal("", rig.Vm.Query);
        Assert.Equal(0, rig.Nav.BackCount);
        Press(rig.Window, PhysicalKey.Escape);
        Assert.Same(rig.View, Focused(rig.Window));
        Assert.Equal(0, rig.Nav.BackCount);
        Press(rig.Window, PhysicalKey.Escape);
        Assert.Equal(1, rig.Nav.BackCount);

        // the bare slash, from the page
        Press(rig.Window, PhysicalKey.Slash);
        Assert.Same(search, Focused(rig.Window)?.FindAncestorOfType<TextBox>(includeSelf: true));
    }

    [AvaloniaFact]
    public void Escape_on_the_page_clears_a_search_before_it_leaves()
    {
        using var rig = Open();
        rig.Vm.Query = "#props";
        rig.View.Focus();

        Press(rig.Window, PhysicalKey.Escape);
        Assert.Equal("", rig.Vm.Query);
        Assert.Equal(0, rig.Nav.BackCount);

        Press(rig.Window, PhysicalKey.Escape);
        Assert.Equal(1, rig.Nav.BackCount);
    }

    [AvaloniaFact]
    public void The_arrows_move_the_selection_and_page_keys_turn_the_page()
    {
        using var rig = Open(entries: 20);
        var vm = rig.Vm;
        Assert.True(vm.PageSize > 1 && vm.Columns > 1, $"page {vm.PageSize}, columns {vm.Columns}");
        Assert.Null(vm.Selected);

        Press(rig.Window, PhysicalKey.ArrowRight);           // nothing selected: the first tile on the page
        Assert.Same(vm.Tiles[0], vm.Selected);
        Press(rig.Window, PhysicalKey.ArrowRight);
        Assert.Same(vm.Tiles[1], vm.Selected);
        Press(rig.Window, PhysicalKey.ArrowDown);            // a whole row, as the grid is measured
        Assert.Same(vm.Tiles[1 + vm.Columns], vm.Selected);
        Press(rig.Window, PhysicalKey.ArrowUp);
        Press(rig.Window, PhysicalKey.ArrowLeft);
        Assert.Same(vm.Tiles[0], vm.Selected);
        Press(rig.Window, PhysicalKey.ArrowLeft);            // clamped: never wraps round
        Assert.Same(vm.Tiles[0], vm.Selected);

        Press(rig.Window, PhysicalKey.PageDown);
        Assert.Equal(1, vm.PageIndex);
        Press(rig.Window, PhysicalKey.PageUp);
        Assert.Equal(0, vm.PageIndex);
        Press(rig.Window, PhysicalKey.End);
        Assert.Equal(vm.PageCount - 1, vm.PageIndex);
        Press(rig.Window, PhysicalKey.Home);
        Assert.Equal(0, vm.PageIndex);

        // the last tile of a page, stepped past, turns the page and selects the next one
        vm.SelectCommand.Execute(vm.Tiles[^1]);
        Press(rig.Window, PhysicalKey.ArrowRight);
        Assert.Equal(1, vm.PageIndex);
        Assert.Same(vm.Tiles[0], vm.Selected);
    }

    [AvaloniaFact]
    public void Enter_on_a_focused_tile_selects_it_and_downloads_it()
    {
        var http = new ScriptedHttp(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage();
        });
        using var rig = Open(http: http.Client());
        var tile = rig.View.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ctile")).ElementAt(2);
        tile.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(rig.Window, PhysicalKey.Enter);
        Assert.Same(rig.Vm.Tiles[2], rig.Vm.Selected);
        Assert.True(rig.Vm.IsDownloading);
        rig.Vm.StopDownloadCommand.Execute(null);
    }

    [AvaloniaFact]
    public void Enter_offline_selects_but_does_not_download()
    {
        using var rig = Open(online: false);
        var tile = rig.View.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ctile")).ElementAt(1);
        tile.Focus();
        Dispatcher.UIThread.RunJobs();

        Press(rig.Window, PhysicalKey.Enter);
        Assert.Same(rig.Vm.Tiles[1], rig.Vm.Selected);
        Assert.False(rig.Vm.IsDownloading);
    }

    [AvaloniaFact]
    public void T_opens_the_tag_picker_and_R_fetches_the_list_again()
    {
        using var rig = Open();
        var flyout = (Flyout)rig.View.FindControl<Button>("TagPickerButton")!.Flyout!;

        Press(rig.Window, PhysicalKey.T);
        Assert.True(flyout.IsOpen);
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();

        rig.View.Focus();
        int fetched = rig.Cache.Refreshes;
        Press(rig.Window, PhysicalKey.R);
        UiThreadWait.For(rig.Vm.PendingRefresh);
        Assert.Equal(fetched + 1, rig.Cache.Refreshes);
    }

    [AvaloniaFact]
    public void The_pickers_filter_narrows_and_Enter_adds_its_first_match()
    {
        using var rig = Open();
        Press(rig.Window, PhysicalKey.T);
        var flyout = (Flyout)rig.View.FindControl<Button>("TagPickerButton")!.Flyout!;
        var filter = ((Control)flyout.Content!).GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "TagFilterBox");
        Dispatcher.UIThread.RunJobs();
        Assert.Same(filter, Focused(TopLevel.GetTopLevel(filter) as Window ?? rig.Window)?.FindAncestorOfType<TextBox>(includeSelf: true));

        rig.Window.KeyTextInput("scen");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["scenery"], rig.Vm.PickerTags.Select(t => t.Label));
        Press(rig.Window, PhysicalKey.Enter);
        Assert.Equal("#scenery", rig.Vm.Query);

        // Escape clears the filter, then closes the picker - and never reaches the page's Back
        Press(rig.Window, PhysicalKey.Escape);
        Assert.Equal("", rig.Vm.TagFilter);
        Assert.True(flyout.IsOpen);
        Press(rig.Window, PhysicalKey.Escape);
        Assert.False(flyout.IsOpen);
        Assert.Equal(0, rig.Nav.BackCount);
        Assert.Equal("#scenery", rig.Vm.Query);
    }

    [AvaloniaFact]
    public void No_key_acts_while_a_dialog_is_open()
    {
        using var rig = Open();
        _ = rig.Dialogs.ShowAsync<object>(new HelpViewModel(rig.Dialogs));
        int fetched = rig.Cache.Refreshes;

        Press(rig.Window, PhysicalKey.Escape);
        Press(rig.Window, PhysicalKey.R);
        Press(rig.Window, PhysicalKey.ArrowRight);
        Assert.Equal(0, rig.Nav.BackCount);
        Assert.Equal(fetched, rig.Cache.Refreshes);
        Assert.Null(rig.Vm.Selected);
    }

    [AvaloniaFact]
    public void The_brand_mark_leads_home_from_the_community_page()
    {
        var shell = ShellCommunity(out var nav, out var page);
        Assert.True(shell.HasOpenDocument);

        // click the titlebar's own brand button, the shipped chrome
        var chrome = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = chrome, Width = 1416, Height = 864 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var brand = chrome.GetVisualDescendants().OfType<Views.BrandMarkView>().First().FindAncestorOfType<Button>()!;
            Assert.True(brand.IsEffectivelyEnabled, "the brand is dim on the Community page");
            Click(brand);
            Assert.Same(nav.Pages[^1], nav.Current);          // back on the root (Landing)
            Assert.DoesNotContain(page, nav.Pages);
        }
        finally { window.Close(); }
    }

    private static ShellViewModel ShellCommunity(out NavigationService nav, out CommunityViewModel page)
    {
        nav = new NavigationService();
        var dialogs = new FakeDialogs();
        nav.To(new HelpViewModel(dialogs));          // any root stands in for Landing
        var cache = new FakeCommunityCache { Next = CommunityFixtures.Listing([CommunityFixtures.Entry("a")]) };
        page = new CommunityViewModel(cache, new CommunitySeen(Directory.CreateTempSubdirectory("nfty-seen-").FullName),
            new FakeGitHubAccount(), nav, dialogs, new StatusService(), _ => Task.CompletedTask);
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService());
        nav.To(page);
        return shell;
    }
}
