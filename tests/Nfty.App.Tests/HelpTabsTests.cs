using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The quick-reference sheet is three underline tabs: BASICS, RISE &amp; 3D, VIEWER &amp; RUNTIME.
/// </summary>
/// <remarks>
/// The three pages share one grid cell and the inactive two are inked out, never collapsed, so the
/// sheet is as tall as the tallest page whichever tab shows and a tab switch moves nothing. The
/// sheet is a fixed card that may not scroll at the smallest window (<c>ModalFitTests</c> owns that
/// number; the area is re-derived here from the same <c>ShellViewModel</c> constants).
/// </remarks>
public class HelpTabsTests
{
    private static (Window window, Views.HelpView view, HelpViewModel vm) Render(bool smallest = false)
    {
        var vm = new HelpViewModel(new FakeDialogs());
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

    private static Grid Page(Views.HelpView v, HelpTab t) =>
        v.FindControl<Grid>(t switch { HelpTab.Basics => "SheetBody", HelpTab.Rise => "RisePage", _ => "ViewerPage" })!;

    private static Button Tab(Views.HelpView v, string label) =>
        v.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("railtab")).Single(b => (b.Content as string) == label);

    private static string TextOf(Visual root) => string.Join("\n",
        root.GetVisualDescendants().OfType<TextBlock>().Select(t =>
            (t.Text ?? "") + string.Concat(t.Inlines?.OfType<Run>().Select(r => r.Text) ?? [])));

    [AvaloniaFact]
    public void Opens_on_basics_and_each_tab_shows_only_its_page()
    {
        var (w, view, vm) = Render();
        try
        {
            Assert.True(vm.IsBasicsTab);
            foreach (var t in new[] { HelpTab.Basics, HelpTab.Rise, HelpTab.Viewer })
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                foreach (var p in new[] { HelpTab.Basics, HelpTab.Rise, HelpTab.Viewer })
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
    public void Switching_tabs_moves_neither_the_tabs_nor_the_sheet()
    {
        var (w, view, vm) = Render();
        try
        {
            var labels = new[] { "BASICS", "RISE & 3D", "VIEWER & RUNTIME" };
            var tabs = labels.Select(l => Tab(view, l).Bounds).ToArray();
            var sheet = view.FindControl<Border>("Sheet")!.Bounds;
            foreach (var t in new[] { HelpTab.Rise, HelpTab.Viewer })
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(tabs, labels.Select(l => Tab(view, l).Bounds).ToArray());
                Assert.Equal(sheet, view.FindControl<Border>("Sheet")!.Bounds);
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Only_the_active_tab_is_underlined_in_accent()
    {
        var (w, view, vm) = Render();
        try
        {
            Application.Current!.TryGetResource("AccentBrush", w.ActualThemeVariant, out var a);
            Assert.Equal((IBrush)a!, Tab(view, "BASICS").BorderBrush);
            Assert.Equal(Brushes.Transparent, Tab(view, "RISE & 3D").BorderBrush);
            Assert.Equal(new Thickness(0, 0, 0, 2), Tab(view, "RISE & 3D").BorderThickness);   // present when off too

            vm.ShowTabCommand.Execute(HelpTab.Rise);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brushes.Transparent, Tab(view, "BASICS").BorderBrush);
            Assert.Equal((IBrush)a!, Tab(view, "RISE & 3D").BorderBrush);
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
            var cell = view.FindControl<Panel>("Pages")!;
            var wants = new[] { HelpTab.Basics, HelpTab.Rise, HelpTab.Viewer }
                .Select(t => { var g = Page(view, t); g.Measure(new Size(980, double.PositiveInfinity)); return g.DesiredSize.Height; })
                .ToArray();
            Assert.True(wants.All(h => h > 100), "a page that measures nothing proves nothing: " + string.Join(", ", wants));
            Assert.Equal(wants.Max(), cell.Bounds.Height, 0.5);
            foreach (var t in new[] { HelpTab.Basics, HelpTab.Rise, HelpTab.Viewer })
                Assert.Equal(cell.Bounds.Height, Page(view, t).Bounds.Height, 0.5);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_sheet_fits_the_smallest_modal_area_on_every_tab_without_a_scrollbar()
    {
        var (w, view, vm) = Render(smallest: true);
        try
        {
            foreach (var t in new[] { HelpTab.Basics, HelpTab.Rise, HelpTab.Viewer })
            {
                vm.ShowTabCommand.Execute(t);
                Dispatcher.UIThread.RunJobs();
                var sheet = view.FindControl<Border>("Sheet")!;
                Assert.True(sheet.Bounds.Height <= view.Bounds.Height + 0.5,
                    $"{t}: sheet {sheet.Bounds.Height:0} tall in {view.Bounds.Height:0}");
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
                    s => s.IsEffectivelyVisible);
            }
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
            foreach (var t in new[] { HelpTab.Basics, HelpTab.Rise, HelpTab.Viewer })
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
    public void The_new_pages_say_what_they_promise_and_never_spell_a_modifier_by_hand()
    {
        var (w, view, _) = Render();
        try
        {
            var rise = TextOf(Page(view, HelpTab.Rise));
            Assert.Contains("voxel", rise);
            Assert.Contains("Make voxel-ready", rise);
            Assert.Contains("2D only", rise);
            Assert.Contains("3D VIEW", rise);

            var viewer = TextOf(Page(view, HelpTab.Viewer));
            Assert.Contains("snapshot", viewer);
            Assert.Contains("view-only", viewer, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains(".mnu", viewer);
            Assert.Contains("Ticket", viewer);
            Assert.Contains("Nfty.Ticket", viewer);
            Assert.Contains(Services.KeyHints.ViewerClick, viewer);

            // A modifier is spelled by KeyHints (or it is wrong on a Mac). Everything the markup writes
            // by hand on the two new pages must be free of them; the KeyHints strings are the only source.
            string[] banned = ["Ctrl", "Shift", "Alt", "⌘", "⌥", "⇧"];
            var hints = new[] { Services.KeyHints.ViewerClick, Services.KeyHints.RiseConstrain, Services.KeyHints.LowerDepth, Services.KeyHints.CloseDocument };
            foreach (var page in new[] { rise, viewer })
            {
                var stripped = hints.Aggregate(page, (s, h) => s.Replace(h, ""));
                foreach (var b in banned)
                    Assert.DoesNotContain(b, stripped, System.StringComparison.Ordinal);
            }
        }
        finally { w.Close(); }
    }
}
