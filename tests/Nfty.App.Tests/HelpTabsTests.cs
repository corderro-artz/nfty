using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The quick-reference sheet is four tabs in a segmented tray: BASICS, EDITOR, RISE &amp; 3D,
/// VIEWER &amp; RUNTIME.
/// </summary>
/// <remarks>
/// The pages share one grid cell and the inactive ones are inked out, never collapsed, so the sheet is
/// as tall as the tallest page whichever tab shows and a tab switch moves nothing. The sheet is a
/// fixed card that may not scroll at the smallest window (<c>ModalFitTests</c> owns that number; the
/// area is re-derived here from the same <c>ShellViewModel</c> constants).
/// </remarks>
public class HelpTabsTests
{
    private static readonly HelpTab[] All = [HelpTab.Basics, HelpTab.Editor, HelpTab.Rise, HelpTab.Viewer, HelpTab.Community];
    private static readonly string[] Labels = ["BASICS", "EDITOR", "RISE & 3D", "VIEWER & RUNTIME", "COMMUNITY"];

    internal static (Window window, Views.HelpView view, HelpViewModel vm) Render(bool smallest = false, string query = "")
    {
        var vm = new HelpViewModel(new FakeDialogs()) { Query = query };
        var view = new Views.HelpView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = smallest ? ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale : 1180,
            Height = smallest ? (ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale : 720,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    internal static Grid Page(Views.HelpView v, HelpTab t) =>
        v.GetVisualDescendants().OfType<Grid>()
            .Single(g => g.Classes.Contains("helppage") && g.DataContext is HelpPage p && p.Tab == t);

    internal static Button Tab(Views.HelpView v, string label) =>
        v.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("seg-b"))
            .Single(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == label));

    private static IBrush? Fill(Button b) =>
        b.GetVisualDescendants().OfType<ContentPresenter>().First(c => c.Name == "PART_ContentPresenter").Background;

    // A key is drawn by KeyChord (a cap per key, some of them drawings), so the chord string as the
    // catalog spells it is part of what a page says.
    internal static string TextOf(Visual root) => string.Join("\n",
        root.GetVisualDescendants().OfType<TextBlock>().Select(t =>
            (t.Text ?? "") + string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? []))
        .Concat(root.GetVisualDescendants().OfType<Nfty.App.Controls.KeyChord>().Select(c => c.Chord ?? "")));

    [AvaloniaFact]
    public void Opens_on_basics_and_each_tab_shows_only_its_page()
    {
        var (w, view, vm) = Render();
        try
        {
            Assert.True(vm.IsBasicsTab);
            foreach (var t in All)
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                foreach (var p in All)
                {
                    var g = Page(view, p);
                    Assert.Equal(p == t ? 1.0 : 0.0, g.Opacity);
                    Assert.Equal(p == t, g.IsHitTestVisible);
                }
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Every_tab_is_drawn_whole_and_clear_of_the_search_box()
    {
        // The fifth tab first shipped arranged UNDER the search box: the tray's star column was narrower
        // than the tray, so the last segment was clipped to its icon while every Bounds looked fine.
        var (w, view, _) = Render(smallest: true);
        try
        {
            var search = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("search"));
            var searchLeft = search.TranslatePoint(default, view)!.Value.X;
            var tray = view.FindControl<Border>("TabTray")!;
            var trayRight = tray.TranslatePoint(new Point(tray.Bounds.Width, 0), view)!.Value.X;
            Assert.True(trayRight <= searchLeft, $"the tab tray ends at {trayRight:0}, the search box begins at {searchLeft:0}");
            foreach (var l in Labels)
            {
                var b = Tab(view, l);
                var right = b.TranslatePoint(new Point(b.Bounds.Width, 0), view)!.Value.X;
                Assert.True(right <= trayRight + 0.5, $"{l} ends at {right:0.0}, the tray at {trayRight:0.0}");
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Switching_tabs_moves_neither_the_segments_nor_the_sheet()
    {
        var (w, view, vm) = Render();
        try
        {
            var segs = Labels.Select(l => Tab(view, l).Bounds).ToArray();
            var sheet = view.FindControl<Border>("Sheet")!.Bounds;
            var tray = view.FindControl<Border>("TabTray")!.Bounds;
            foreach (var t in All)
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                // Selection is bold + a fill, and neither may change a segment's box: identical bounds
                // whichever one is chosen (a width that followed the label's weight would shove its neighbours).
                Assert.Equal(segs, Labels.Select(l => Tab(view, l).Bounds).ToArray());
                Assert.Equal(tray, view.FindControl<Border>("TabTray")!.Bounds);
                Assert.Equal(sheet, view.FindControl<Border>("Sheet")!.Bounds);
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_chosen_segment_is_filled_and_the_others_are_not()
    {
        var (w, view, vm) = Render();
        try
        {
            Application.Current!.TryGetResource("AccentBrush", w.ActualThemeVariant, out var accent);
            foreach (var t in All)
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                for (var i = 0; i < All.Length; i++)
                {
                    var b = Tab(view, Labels[i]);
                    if (All[i] == t)
                    {
                        Assert.Equal((IBrush)accent!, Fill(b));
                        Assert.Equal(FontWeight.SemiBold, b.FontWeight);
                    }
                    else
                    {
                        Assert.Equal(Brushes.Transparent, Fill(b));
                        Assert.NotEqual(FontWeight.SemiBold, b.FontWeight);
                        Assert.NotEqual(Tab(view, Labels[Array.IndexOf(All, t)]).Foreground, b.Foreground);
                    }
                }
            }
        }
        finally { w.Close(); }
    }

    /// <summary>Every page asks for no more than the shared cell, and the cell is exactly the tallest page.</summary>
    [AvaloniaFact]
    public void Every_page_fits_the_one_reserved_height()
    {
        var (w, view, _) = Render();
        try
        {
            var cell = view.FindControl<ItemsControl>("PagesHost")!;
            var wants = All
                .Select(t => { var g = Page(view, t); g.Measure(new Size(1040, double.PositiveInfinity)); return g.DesiredSize.Height; })
                .ToArray();
            Assert.True(wants.All(h => h > 100), "a page that measures nothing proves nothing: " + string.Join(", ", wants));
            Assert.Equal(wants.Max(), cell.Bounds.Height, 0.5);
            foreach (var t in All)
                Assert.Equal(cell.Bounds.Height, Page(view, t).Bounds.Height, 0.5);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_sheet_fits_the_smallest_modal_area_on_every_tab_and_in_a_search_without_a_scrollbar()
    {
        var (w, view, vm) = Render(smallest: true);
        try
        {
            void Check(string what)
            {
                var sheet = view.FindControl<Border>("Sheet")!;
                Assert.True(sheet.Bounds.Height <= view.Bounds.Height + 0.5,
                    $"{what}: sheet {sheet.Bounds.Height:0} tall in {view.Bounds.Height:0}");
                Assert.True(sheet.Bounds.Width <= view.Bounds.Width + 0.5,
                    $"{what}: sheet {sheet.Bounds.Width:0} wide in {view.Bounds.Width:0}");
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
                    s => s.IsEffectivelyVisible);
            }
            foreach (var t in All)
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                Check(t.ToString());
            }
            vm.Query = "zoom";     // a handful of hits: fills the cell with no bar
            Dispatcher.UIThread.RunJobs();
            Check("search");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_dna_footer_is_on_every_tab()
    {
        var (w, view, vm) = Render();
        try
        {
            var footer = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("sh-f"));
            foreach (var t in All)
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                Assert.True(footer.IsEffectivelyVisible);
                Assert.Contains("Unique DNA", TextOf(footer));
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_pages_say_what_they_promise_and_never_spell_a_modifier_by_hand()
    {
        var (w, view, _) = Render();
        try
        {
            var rise = TextOf(Page(view, HelpTab.Rise));
            Assert.Contains("voxel", rise);
            Assert.Contains("Make voxel-ready", rise);
            Assert.Contains("2D only", rise);
            Assert.Contains("3D VIEW", rise);

            var editor = TextOf(Page(view, HelpTab.Editor));
            Assert.Contains("eraser", editor);
            Assert.Contains("SET INSPECTOR", editor);
            Assert.Contains("[  ]", editor);

            var viewer = TextOf(Page(view, HelpTab.Viewer));
            Assert.Contains("snapshot", viewer);
            Assert.Contains("view-only", viewer, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".mnu", viewer);
            Assert.Contains("Ticket", viewer);
            Assert.Contains("Nfty.Ticket", viewer);
            Assert.Contains(Services.KeyHints.ViewerClick, viewer);

            // A modifier is spelled by KeyHints (or it is wrong on a Mac). Everything the catalog writes
            // by hand must be free of them once the KeyHints strings are taken out.
            string[] banned = ["Ctrl", "Shift", "Alt", "⌘", "⌥", "⇧"];
            var hints = HelpCatalogTests.AllHints();
            foreach (var page in new[] { rise, editor, viewer })
            {
                var stripped = hints.OrderByDescending(h => h.Length).Aggregate(page, (s, h) => s.Replace(h, ""));
                foreach (var b in banned)
                    Assert.DoesNotContain(b, stripped, System.StringComparison.Ordinal);
            }
        }
        finally { w.Close(); }
    }
}
