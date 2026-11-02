using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The sheet's search, driven through the real controls and real key events: a test that
/// calls <c>JumpCommand</c> proves nothing about whether the row, the box or the keys invoke it.</summary>
public class HelpSearchTests
{
    private static void Pump() => Dispatcher.UIThread.RunJobs();

    private static void Type(Window w, string text)
    {
        w.KeyTextInput(text);
        Pump();
    }

    [AvaloniaFact]
    public void Typing_filters_the_whole_catalog_and_replaces_the_pages_in_the_same_cell()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            var cell = view.FindControl<Panel>("Pages")!.Bounds;
            view.Search.Focus();
            Type(w, "zoom");

            Assert.Equal("zoom", vm.Query);
            Assert.True(vm.HasResults);
            var results = view.FindControl<ItemsControl>("ResultsList")!;
            Assert.Equal(vm.Results.Count, results.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("hrow")));
            Assert.Equal(1.0, view.FindControl<Grid>("ResultsLayer")!.Opacity);
            Assert.Equal(0.0, view.FindControl<ItemsControl>("PagesHost")!.Opacity);
            Assert.Equal(cell, view.FindControl<Panel>("Pages")!.Bounds);       // nothing moved
            Assert.Contains("matches", vm.ResultCountText);
            // each row names where it lives
            Assert.Contains("EDITOR · ", HelpTabsTests.TextOf(results));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void A_query_with_no_hits_says_so_by_name_and_carries_the_clear_button()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            view.Search.Focus();
            Type(w, "qzxv");

            Assert.True(vm.NoMatch);
            var empty = view.FindControl<StackPanel>("EmptyState")!;
            Assert.True(empty.IsEffectivelyVisible);
            Assert.Contains("Nothing matches “qzxv”", HelpTabsTests.TextOf(empty));
            Assert.False(view.FindControl<ScrollViewer>("ResultsScroll")!.IsEffectivelyVisible);

            var clear = view.FindControl<Button>("EmptyClear")!;
            clear.Command!.Execute(clear.CommandParameter);       // the button's own command
            Pump();
            Assert.Equal("", vm.Query);
            Assert.False(vm.NoMatch);
            Assert.Equal(1.0, HelpTabsTests.Page(view, HelpTab.Basics).Opacity);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Clicking_a_result_jumps_to_its_tab_and_clears_the_search()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            vm.Query = "eraser";
            Pump();
            var row = view.FindControl<ItemsControl>("ResultsList")!.GetVisualDescendants().OfType<Button>()
                .First(b => b.Classes.Contains("hrow"));
            Assert.Equal(HelpTab.Editor, ((HelpEntry)row.CommandParameter!).Tab);
            row.Command!.Execute(row.CommandParameter);   // what the click invokes
            Pump();

            Assert.Equal("", vm.Query);
            Assert.Equal(HelpTab.Editor, vm.Tab);
            Assert.Equal(1.0, HelpTabsTests.Page(view, HelpTab.Editor).Opacity);
            Assert.Equal(0.0, view.FindControl<Grid>("ResultsLayer")!.Opacity);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Enter_in_the_search_box_opens_the_first_result()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            view.Search.Focus();
            Type(w, "kitchen");
            Assert.Equal(HelpTab.Basics, vm.Results[0].Tab);
            vm.Tab = HelpTab.Viewer;       // not already there
            Pump();
            vm.Query = "inspector";
            Pump();
            var first = vm.Results[0];
            view.Search.Focus();
            w.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Pump();
            Assert.Equal("", vm.Query);
            Assert.Equal(first.Tab, vm.Tab);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Escape_clears_a_query_first_and_closes_the_sheet_only_when_it_is_empty()
    {
        var dialogs = new FakeDialogs();
        var vm = new HelpViewModel(dialogs);
        var view = new Views.HelpView { DataContext = vm };
        var w = new Window { Content = view, Width = 1180, Height = 720 };
        w.Show();
        Pump();
        try
        {
            dialogs.ShowAsync<object>(vm);
            Assert.NotNull(dialogs.Active);
            view.Search.Focus();
            Type(w, "zoom");

            w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
            Assert.Equal("", vm.Query);
            Assert.NotNull(dialogs.Active);              // first Esc only cleared it

            w.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump();
            Assert.Null(dialogs.Active);                 // second one closes
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Ctrl_F_and_slash_focus_the_search_box_but_slash_is_a_character_inside_it()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            view.Focus();
            Pump();
            Assert.False(view.Search.IsFocused);

            w.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Control);
            Pump();
            Assert.True(view.Search.IsFocused);

            view.Focus();
            Pump();
            Assert.False(view.Search.IsFocused);
            w.KeyPressQwerty(PhysicalKey.Slash, RawInputModifiers.None);
            Pump();
            Assert.True(view.Search.IsFocused);
            Assert.Equal("", view.Search.Text ?? "");      // the key focused it; it was not typed
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_search_pill_and_its_hint_do_not_move_when_a_query_is_typed()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            var pill = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("search"));
            var before = pill.Bounds;
            vm.Query = "zoom";
            Pump();
            Assert.Equal(before, pill.Bounds);
            Assert.True(view.FindControl<Button>("ClearQuery")!.IsEffectivelyVisible);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void A_tab_click_while_searching_leaves_the_search_and_shows_that_tab()
    {
        var (w, view, vm) = HelpTabsTests.Render();
        try
        {
            vm.Query = "zoom";
            Pump();
            var tab = HelpTabsTests.Tab(view, "RISE & 3D");
            tab.Command!.Execute(tab.CommandParameter);
            Pump();
            Assert.Equal("", vm.Query);
            Assert.True(vm.IsRiseTab);
        }
        finally { w.Close(); }
    }
}
