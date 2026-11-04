using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Point = Avalonia.Point;
using Size = Avalonia.Size;
using Color = Avalonia.Media.Color;

namespace Nfty.App.Tests;

/// <summary>
/// The viewer's info rail: tabs per group, the list snapped to whole rows with a more line instead
/// of a scrollbar, and the extended panel over the panes. Every size here is derived from
/// <see cref="ViewerWindowState"/> and <see cref="ShellViewModel.BaseScale"/>, never stated.
/// </summary>
public class ViewerInfoRailTests
{
    // The smallest viewer window's content: the window less the shared frame (24 gutter + 2 border,
    // 36 titlebar), at the shell's scale - the same derivation ViewerViewTests uses.
    private static readonly Size Smallest = new(
        (ViewerWindowState.MinWidth - 26) / ShellViewModel.BaseScale,
        ((ViewerWindowState.MinHeight - 26) / ShellViewModel.BaseScale) - 36);

    // A maximized 4K window's content, at the same scale.
    private static readonly Size Huge = new((3840 - 26) / ShellViewModel.BaseScale, ((2160 - 26) / ShellViewModel.BaseScale) - 36);

    private sealed class Subject(IReadOnlyList<ViewerFact> facts, Image<Rgba32> art) : ViewerSubject
    {
        public override string Title => "Vapor Pets #42";
        public override IReadOnlyList<ViewerFact> Facts => facts;
        public override Image<Rgba32>? Art => art;
        public override ViewerModel? Model => null;
        public override void Dispose() { }
    }

    // What a cooked Set asset carries: ASSET, RECIPE, SIZE, DNA, its traits, how its model was found.
    internal static List<ViewerFact> AssetFacts(int traits = 6) =>
    [
        new("ASSET", "#0042"), new("RECIPE", "chest"), new("SIZE", "32 x 32"),
        new("DNA", "9f3ac1d2e4b57a10c3e1a8d0f4c26b19e7d5a03c8f61b2e94d07a5c3f81e6b2d"),
        .. Enumerable.Range(1, traits).Select(i =>
            new ViewerFact("TRAIT " + i.ToString(CultureInfo.InvariantCulture), "Value (12.5%)", Group: ViewerFactGroup.Traits)),
        new("MODEL", "recorded in the Set", Group: ViewerFactGroup.File),
    ];

    private static (Window Window, Views.ViewerView View, ViewerViewModel Vm, Image<Rgba32> Art) Host(
        IReadOnlyList<ViewerFact> facts, Size size)
    {
        var art = ViewerViewTests.Art();
        // InWindow: the viewer as its own window shows it, the name and badge in the titlebar and no
        // header row in the body - which is what the smallest window's content size describes.
        var vm = new ViewerViewModel(new Subject(facts, art), new NoPicker(), new NoopClipboardService(),
            new DialogService()) { InWindow = true };
        var view = new Views.ViewerView { DataContext = vm };
        var window = new Window { Content = view, Width = size.Width, Height = size.Height };
        window.Show();
        Settle();
        return (window, view, vm, art);
    }

    private static void Settle()
    {
        for (int i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();
    }

    private static ScrollViewer Rows(Views.ViewerView view) => view.FindControl<ScrollViewer>("FactRows")!;

    // The rows a reader can see: realized row containers of the rail list.
    private static List<Control> RowContainers(Views.ViewerView view) =>
        view.FindControl<ItemsControl>("FactList")!.GetRealizedContainers().Where(c => c.IsVisible).ToList();

    // ---------------------------------------------------------------- tabs

    [AvaloniaFact]
    public void Only_groups_with_facts_get_a_tab_each_with_its_count()
    {
        using var art = ViewerViewTests.Art();
        var vm = ViewerViewTests.Vm(new Subject(AssetFacts(), art));
        Assert.Equal(new[] { ViewerFactGroup.Item, ViewerFactGroup.Traits, ViewerFactGroup.File },
            vm.Tabs.Select(t => t.Group));
        Assert.Equal(new[] { 4, 6, 1 }, vm.Tabs.Select(t => t.Count));
        Assert.True(vm.ShowTabs);
        Assert.Equal(ViewerFactGroup.Item, vm.SelectedGroup);
        Assert.All(vm.TabFacts, f => Assert.Equal(ViewerFactGroup.Item, f.Group));

        vm.SelectGroupCommand.Execute(ViewerFactGroup.Traits);
        Assert.Equal(6, vm.TabFacts.Count);
        Assert.Single(vm.Tabs, t => t.IsSelected && t.Group == ViewerFactGroup.Traits);
        // Copy info still copies everything, whatever tab is showing.
        Assert.Equal(AssetFacts().Count, vm.Facts.Count);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void One_group_draws_no_tab_strip_and_gives_its_row_to_the_facts()
    {
        var one = new List<ViewerFact> { new("FILE", "a.png"), new("SIZE", "6 x 5") };
        var (w1, v1, vm1, a1) = Host(one, Smallest);
        var (w2, v2, vm2, a2) = Host(AssetFacts(), Smallest);
        try
        {
            Assert.False(vm1.ShowTabs);
            var strip1 = v1.FindControl<Border>("FactTabs")!;
            var strip2 = v2.FindControl<Border>("FactTabs")!;
            Assert.False(strip1.IsVisible);
            Assert.True(strip2.IsVisible);
            // The strip's row collapses: the single group's facts start higher than a tabbed rail's.
            var list1 = v1.FindControl<ItemsControl>("FactList")!;
            var list2 = v2.FindControl<ItemsControl>("FactList")!;
            var top1 = list1.TranslatePoint(default, v1)!.Value.Y;
            var top2 = list2.TranslatePoint(default, v2)!.Value.Y;
            Assert.True(top1 < top2 - 10, $"single-group facts at {top1}, tabbed at {top2}");
        }
        finally { w1.Close(); w2.Close(); vm1.Dispose(); vm2.Dispose(); a1.Dispose(); a2.Dispose(); }
    }

    [AvaloniaFact]
    public void The_tab_choice_survives_a_live_change_and_falls_back_only_while_its_group_is_empty()
    {
        var source = new FakeLive { Facts = AssetFacts() };
        using var live = new LiveSubject(source);
        var vm = ViewerViewTests.Vm(live);
        vm.SelectGroupCommand.Execute(ViewerFactGroup.Traits);
        source.Facts = AssetFacts(traits: 3);
        source.Change();
        Settle();
        Assert.Equal(ViewerFactGroup.Traits, vm.SelectedGroup);
        Assert.Equal(3, vm.TabFacts.Count);

        source.Facts = AssetFacts(traits: 0);
        source.Change();
        Settle();
        Assert.Equal(ViewerFactGroup.Item, vm.SelectedGroup);      // nothing to show under Traits

        source.Facts = AssetFacts(traits: 2);
        source.Change();
        Settle();
        Assert.Equal(ViewerFactGroup.Traits, vm.SelectedGroup);    // the reader's choice comes back
        vm.Dispose();
        source.Art.Dispose();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_tab_and_its_count_fit_the_rail_at_the_smallest_window(bool bundle)
    {
        var facts = AssetFacts();
        if (bundle) facts.Add(new ViewerFact("LINK", "https://chests.example/", Group: ViewerFactGroup.Links));
        var (window, view, vm, art) = Host(facts, Smallest);
        try
        {
            var strip = view.FindControl<Border>("FactTabs")!;
            var tabs = strip.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("facttab")).ToList();
            Assert.Equal(vm.Tabs.Count, tabs.Count);
            foreach (var tab in tabs)
            {
                var right = tab.TranslatePoint(new Point(tab.Bounds.Width, 0), strip)!.Value.X;
                Assert.True(right <= strip.Bounds.Width + 0.5,
                    $"tab '{((tab.Content as StackPanel)?.Children[0] as TextBlock)?.Text}' ends at {right:0.#} of {strip.Bounds.Width:0.#}");
                // The INK of its label fits the box it is given: a clipped TextBlock reports the width it asked for.
                var label = tab.GetVisualDescendants().OfType<TextBlock>().First();
                var ink = new FormattedText(label.Text!, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(label.FontFamily), label.FontSize, Brushes.Black) { };
                Assert.True(ink.Width <= label.Bounds.Width + 0.5 + (label.LetterSpacing * label.Text!.Length),
                    $"tab label '{label.Text}' ink {ink.Width:0.#} in {label.Bounds.Width:0.#}");
            }
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    // ---------------------------------------------------------------- the snap

    [AvaloniaFact]
    public void A_set_assets_common_facts_need_no_scrolling_at_the_smallest_window()
    {
        var (window, view, vm, art) = Host(AssetFacts(), Smallest);
        try
        {
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
                sb => sb.IsEffectivelyVisible);
            Assert.Equal(0, vm.RowsBelow);
            Assert.Equal(0, vm.RowsAbove);
            Assert.Equal(0, view.FindControl<Button>("MoreButton")!.Opacity);
            var rows = Rows(view);
            Assert.True(rows.Extent.Height <= rows.Viewport.Height + 0.5,
                $"the Item tab overflows: {rows.Extent.Height} in {rows.Viewport.Height}");
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    // Every row is either wholly inside the host's viewport or wholly outside it.
    private static void AssertNoHalfRow(Views.ViewerView view, string what)
    {
        var host = Rows(view);
        double top = host.Offset.Y, bottom = host.Offset.Y + host.Viewport.Height;
        foreach (var row in RowContainers(view))
        {
            bool inside = row.Bounds.Top >= top - 0.5 && row.Bounds.Bottom <= bottom + 0.5;
            bool outside = row.Bounds.Bottom <= top + 0.5 || row.Bounds.Top >= bottom - 0.5;
            Assert.True(inside || outside,
                $"{what}: a row {row.Bounds.Top:0.#}..{row.Bounds.Bottom:0.#} is cut by the fold {top:0.#}..{bottom:0.#}");
        }
    }

    [AvaloniaFact]
    public void A_long_tab_shows_whole_rows_says_how_many_more_and_scrolls_by_whole_rows()
    {
        var (window, view, vm, art) = Host(AssetFacts(traits: 14), Smallest);
        try
        {
            vm.SelectGroupCommand.Execute(ViewerFactGroup.Traits);
            Settle();
            Assert.True(vm.RowsBelow > 0, "the fixture must overflow, or this proves nothing about overflow");
            AssertNoHalfRow(view, "first page");
            var more = view.FindControl<Button>("MoreButton")!;
            Assert.Equal(1, more.Opacity);
            Assert.Equal($"{vm.RowsBelow} more", vm.MoreText);
            int below = vm.RowsBelow;
            // No bulky bar: the list scrolls by the wheel and the more line.
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
                sb => sb.IsEffectivelyVisible);

            // One wheel notch = one row.
            var host = Rows(view);
            var at = host.TranslatePoint(new Point(host.Bounds.Width / 2, host.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(at, new Vector(0, -1));
            Settle();
            Assert.Equal(1, vm.RowsAbove);
            Assert.Equal(below - 1, vm.RowsBelow);     // every row one height, so the page keeps its length
            AssertNoHalfRow(view, "after one notch");

            // The more line pages to the end, then offers the way back.
            for (int i = 0; i < 20 && vm.RowsBelow > 0; i++) { vm.ShowMoreCommand.Execute(null); Settle(); AssertNoHalfRow(view, "paging"); }
            Assert.Equal(0, vm.RowsBelow);
            Assert.Equal("back to top", vm.MoreText);
            vm.ShowMoreCommand.Execute(null);
            Settle();
            Assert.Equal(0, vm.RowsAbove);
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Rows_are_whole_and_labels_unclipped_at_every_size(int which)
    {
        var size = which switch { 0 => Smallest, 1 => new Size(1280 / ShellViewModel.BaseScale, 640 / ShellViewModel.BaseScale), _ => Huge };
        var facts = AssetFacts(traits: 14);
        facts.Add(new ViewerFact("ABOUT", "A demo collection that ships with nfty: layered chests to open, edit and cook. Nothing here is precious."));
        var (window, view, vm, art) = Host(facts, size);
        try
        {
            foreach (var group in vm.Tabs.Select(t => t.Group).ToList())
            {
                vm.SelectGroupCommand.Execute(group);
                Settle();
                AssertNoHalfRow(view, $"{group} at {size}");
                foreach (var lbl in view.FindControl<ItemsControl>("FactList")!.GetVisualDescendants().OfType<TextBlock>()
                             .Where(t => t.Classes.Contains("factlbl") && t.IsEffectivelyVisible))
                {
                    var ink = new FormattedText(lbl.Text!, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface(lbl.FontFamily), lbl.FontSize, Brushes.Black);
                    Assert.True(ink.Width <= lbl.Bounds.Width + (lbl.LetterSpacing * lbl.Text!.Length) + 0.5,
                        $"label '{lbl.Text}' ink {ink.Width:0.#} clipped in {lbl.Bounds.Width:0.#}");
                }
            }
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    [AvaloniaFact]
    public void Growing_the_window_pulls_hidden_rows_back_in_rather_than_leaving_a_gap()
    {
        var (window, view, vm, art) = Host(AssetFacts(traits: 14), Smallest);
        try
        {
            vm.SelectGroupCommand.Execute(ViewerFactGroup.Traits);
            Settle();
            for (int i = 0; i < 20 && vm.RowsBelow > 0; i++) { vm.ShowMoreCommand.Execute(null); Settle(); }
            Assert.True(vm.RowsAbove > 0);
            window.Height = Huge.Height;
            Settle();
            Assert.Equal(0, vm.RowsAbove);
            Assert.Equal(0, vm.RowsBelow);
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    // ---------------------------------------------------------------- extend

    [AvaloniaFact]
    public void Extending_covers_the_panes_without_moving_them_and_collapses_from_the_same_spot()
    {
        var (window, view, vm, art) = Host(AssetFacts(), Smallest);
        try
        {
            var panes = view.FindControl<Grid>("PaneGrid")!;
            var rail = view.FindControl<Border>("Rail")!;
            var paneBefore = panes.Bounds;
            var railBefore = rail.Bounds;
            var extend = view.FindControl<Button>("ExtendButton")!;
            var extendAt = extend.TranslatePoint(new Point(extend.Bounds.Width / 2, extend.Bounds.Height / 2), view)!.Value;

            Assert.False(view.FindControl<Border>("InfoPanel")!.IsVisible);
            extend.Command!.Execute(null);
            Settle();
            Assert.True(vm.IsInfoExtended);
            var panel = view.FindControl<Border>("InfoPanel")!;
            Assert.True(panel.IsVisible);
            Assert.Equal(paneBefore, panes.Bounds);    // an overlay: it covers, never pushes
            Assert.Equal(railBefore, rail.Bounds);

            // The panel's collapse button sits exactly on the button that opened it, and is the one hit there.
            var collapse = view.FindControl<Button>("CollapseButton")!;
            var collapseAt = collapse.TranslatePoint(new Point(collapse.Bounds.Width / 2, collapse.Bounds.Height / 2), view)!.Value;
            Assert.True(Math.Abs(collapseAt.X - extendAt.X) < 0.5 && Math.Abs(collapseAt.Y - extendAt.Y) < 0.5,
                $"collapse at {collapseAt}, extend at {extendAt}");
            // InputHitTest, not GetVisualAt: it is what a real click resolves through, and it honors
            // IsHitTestVisible (GetVisualAt does not, so it found the covered extend button).
            var hit = view.InputHitTest(collapseAt) as Visual;
            Assert.True(hit is not null && (ReferenceEquals(hit, collapse) || collapse.IsVisualAncestorOf(hit)),
                $"hit {hit?.GetType().Name} '{(hit as Control)?.Name}' classes [{string.Join(",", (hit as StyledElement)?.Classes ?? [])}] "
                + $"under {string.Join(" < ", (hit as Visual)?.GetVisualAncestors().Take(4).Select(a => a.GetType().Name + ((a as Control)?.Name is { Length: > 0 } n ? "#" + n : "")) ?? [])}");

            // Every group at once, every value whole.
            var full = panel.GetVisualDescendants().OfType<SelectableTextBlock>().Where(t => t.Classes.Contains("factfull")).ToList();
            Assert.Equal(vm.Facts.Count, full.Count);
            var dna = full.Single(t => t.Text!.StartsWith("9f3ac1", StringComparison.Ordinal));
            Assert.Equal(AssetFacts()[3].Value, dna.Text);
            Assert.Equal(TextTrimming.None, dna.TextTrimming);
            Assert.Equal(TextWrapping.Wrap, dna.TextWrapping);

            // Esc folds it.
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Settle();
            Assert.False(vm.IsInfoExtended);
            Assert.False(panel.IsVisible);
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    [AvaloniaFact]
    public void The_extended_panel_balances_whole_groups_into_two_columns()
    {
        using var art = ViewerViewTests.Art();
        var vm = ViewerViewTests.Vm(new Subject(AssetFacts(), art));
        Assert.Equal(new[] { ViewerFactGroup.Item }, vm.SectionsLeft.Select(s => s.Group));
        Assert.Equal(new[] { ViewerFactGroup.Traits, ViewerFactGroup.File }, vm.SectionsRight.Select(s => s.Group));
        Assert.Equal(vm.Facts.Count, vm.SectionsLeft.Concat(vm.SectionsRight).Sum(s => s.Facts.Count));
        vm.Dispose();
    }

    // ---------------------------------------------------------------- keys

    [AvaloniaFact]
    public void Every_viewer_key_the_quick_reference_names_is_bound_and_a_text_field_keeps_its_keys()
    {
        var (window, view, vm, art) = Host(AssetFacts(), Smallest);
        try
        {
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
            Settle();
            Assert.True(vm.IsInfoExtended);
            window.KeyPressQwerty(PhysicalKey.I, RawInputModifiers.None);
            Settle();
            Assert.False(vm.IsInfoExtended);

            vm.TwoD.Zoom = 4;
            window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.None);
            Settle();
            Assert.False(vm.TwoD.IsZoomed);

            // A "1" typed into the custom color is text, not a layout.
            var box = view.FindControl<TextBox>("CustomColorBox")!;
            box.Focus();
            box.SelectAll();
            var before = vm.Layout;
            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
            window.KeyTextInput("3");
            Settle();
            Assert.Equal(before, vm.Layout);
            // And typing in it makes it the ground.
            Assert.Equal(ViewerBackdrop.Custom, vm.TwoD.Backdrop);

            // The help sheet's viewer keys are each one the handler takes.
            var keys = HelpCatalog.Entries.OfType<HelpKey>().Where(k => k.Section == "KEYS · VIEWER").Select(k => k.Key).ToList();
            Assert.Equal(new[] { KeyHints.ViewerLayouts, KeyHints.ViewerFit, KeyHints.ViewerSpin, KeyHints.ViewerInfo }, keys);
        }
        finally { window.Close(); vm.Dispose(); art.Dispose(); }
    }

    [AvaloniaFact]
    public void The_layout_keys_switch_layout_and_s_steps_the_turntable()
    {
        using var art = ViewerViewTests.Art();
        var vm = ViewerViewTests.Vm(new ViewerViewTests.TrackedSubject("x", art, ViewerViewTests.Model()));
        var view = new Views.ViewerView { DataContext = vm };
        var window = new Window { Content = view, Width = Smallest.Width, Height = Smallest.Height };
        window.Show();
        Settle();
        try
        {
            view.Focus();
            window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.None);
            Assert.Equal(ViewerLayout.TwoD, vm.Layout);
            window.KeyPressQwerty(PhysicalKey.Digit3, RawInputModifiers.None);
            Assert.Equal(ViewerLayout.ThreeD, vm.Layout);
            window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.None);
            Assert.Equal(ViewerLayout.Both, vm.Layout);
            Assert.Equal("Off", vm.TurntableText);
            window.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.None);
            Assert.Equal(TurntableSpeed.Slow, vm.Preview.Turntable);
            Assert.True(vm.IsSpinning);
            for (int i = 0; i < 3; i++) vm.CycleTurntableCommand.Execute(null);
            Assert.Equal(TurntableSpeed.Off, vm.Preview.Turntable);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    private sealed class NoPicker : IFilePickerService
    {
        public System.Threading.Tasks.Task<string?> OpenFileAsync(string title, params string[] extensions) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> SaveFileAsync(string title, string defaultExtension) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);
        public System.Threading.Tasks.Task<string?> PickFolderAsync(string title) =>
            System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private sealed class FakeLive : ILiveViewerSource
    {
        public Image<Rgba32> Art = new(2, 2, new Rgba32(1, 2, 3, 255));
        public IReadOnlyList<ViewerFact> Facts = [];
        public string ViewerTitle => "fake";
        public Image<Rgba32> ViewerArt => Art;
        public IReadOnlyList<ViewerFact> ViewerFacts => Facts;
        public ViewerModel? ViewerModel => null;
        public bool AllowExport => true;
        public event Action? ViewerChanged;
        public event Action? Detached { add { } remove { } }
        public void Change() => ViewerChanged?.Invoke();
    }
}
