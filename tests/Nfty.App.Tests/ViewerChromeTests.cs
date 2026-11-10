using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Viewer;
using Nfty.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The pop-out viewer's own window: it draws the SAME frame as the shell (one shared control), its
/// info rail says something real for every kind of subject, and a sealed subject cannot be taken out.
/// </summary>
public class ViewerChromeTests
{
    private sealed class Picker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class RecordingClipboard : IClipboardService
    {
        public string? Text;
        public Task SetTextAsync(string text) { Text = text; return Task.CompletedTask; }
    }

    private static ViewerWindow Open(ViewerSubject subject, IClipboardService? clipboard = null)
    {
        var host = new DesktopViewerHost(new Picker(), clipboard ?? new RecordingClipboard(),
            new ViewerWindowStateService(StateStore.InMemory()), new ImageBridge(), () => null);
        ViewerWindow? opened = null;
        host.Opened += w => opened = w;
        host.Open(subject);
        Dispatcher.UIThread.RunJobs();
        return opened!;
    }

    private static WindowFrame FrameOf(ViewerWindow w) => w.GetVisualDescendants().OfType<WindowFrame>().Single();

    // ---------------------------------------------------------------- the shared frame

    [AvaloniaFact]
    public void The_viewer_window_draws_the_shells_own_frame_and_titlebar()
    {
        using var art = ViewerViewTests.Art();
        var w = Open(new ViewerViewTests.TrackedSubject("Chest #42", art, null));
        try
        {
            var frame = FrameOf(w);
            Assert.Equal(ShellViewModel.BaseScale, frame.Scale);
            Assert.Equal(WindowDecorations.None, w.WindowDecorations);

            // The brand and the subject's name are in the titlebar, the caption buttons beside them.
            Assert.Single(frame.GetVisualDescendants().OfType<Views.BrandMarkView>());
            Assert.Contains(frame.TitlebarArea.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Chest #42");
            var caption = frame.TitlebarArea.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("icon") && b.IsEffectivelyVisible).ToList();
            Assert.Equal(3, caption.Count);
            Assert.Single(caption, b => b.Classes.Contains("danger"));

            // The viewer's body header would repeat the titlebar, so it is gone in a window.
            var view = w.GetVisualDescendants().OfType<Views.ViewerView>().Single();
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Views.ViewerLiveBadge>(),
                b => b.IsEffectivelyVisible && !frame.TitlebarArea.IsVisualAncestorOf(b));

            // Same height as the shell's: it is the one control.
            var shell = new Views.ShellChromeView
            {
                DataContext = new ShellViewModel(new FakeNav(), new FakeDialogs(), new ThemeService(), new StatusService()),
            };
            var host = new Window { Content = shell, Width = 1416, Height = 864 };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            try { Assert.Equal(shell.Frame.TitlebarArea.Bounds.Height, frame.TitlebarArea.Bounds.Height); }
            finally { host.Close(); }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_close_button_closes_the_viewer()
    {
        using var art = ViewerViewTests.Art();
        var w = Open(new ViewerViewTests.TrackedSubject("x", art, null));
        var frame = FrameOf(w);
        Assert.NotNull(frame.CloseCommand);
        Assert.NotNull(frame.MinimizeCommand);
        Assert.NotNull(frame.ToggleMaximizeCommand);
        frame.CloseCommand!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
    }

    [AvaloniaFact]
    public void A_maximized_viewer_squares_its_frame_off_like_the_main_window()
    {
        using var art = ViewerViewTests.Art();
        var w = Open(new ViewerViewTests.TrackedSubject("x", art, null));
        try
        {
            var border = FrameOf(w).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("frame"));
            Assert.Equal(new Thickness(12), border.Margin);

            w.WindowState = WindowState.Maximized;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new Thickness(0), border.Margin);
            Assert.Equal(new CornerRadius(0), border.CornerRadius);
            var clip = FrameOf(w).GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("frame-clip"));
            Assert.Equal(new CornerRadius(0), clip.CornerRadius);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_viewer_resizes_from_its_edges_and_the_shell_does_not()
    {
        using var art = ViewerViewTests.Art();
        var w = Open(new ViewerViewTests.TrackedSubject("x", art, null));
        try
        {
            var frame = FrameOf(w);
            Assert.True(frame.EdgeResize);
            var edges = frame.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "Edges");
            Assert.True(edges.IsEffectivelyVisible);
            Assert.Equal(8, edges.Children.Count);
            Assert.All(edges.Children.OfType<Border>(), b => Assert.NotNull(b.Background));   // hit-testable

            var requests = new List<Avalonia.Controls.WindowEdge>();
            frame.ResizeRequested += (edge, _) => requests.Add(edge);
            Assert.False(new Views.ShellChromeView().Frame.EdgeResize);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void The_smallest_viewer_window_fits_its_content_with_no_scrollbar()
    {
        using var art = ViewerViewTests.Art();
        using var model = new ViewerViewTests.TrackedSubject("Chest #42", art, ViewerViewTests.Model());
        var w = Open(model);
        try
        {
            w.Width = ViewerWindowState.MinWidth;
            w.Height = ViewerWindowState.MinHeight;
            Dispatcher.UIThread.RunJobs();
            var view = w.GetVisualDescendants().OfType<Views.ViewerView>().Single();
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>(),
                sb => sb.IsEffectivelyVisible);
            // The ScrollBar check above can read green on an auto-hiding bar; the extent cannot lie.
            var rail = view.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Content is ItemsControl);
            Assert.True(rail.Extent.Height <= rail.Viewport.Height + 0.5,
                $"the info rail overflows: {rail.Extent.Height} of {rail.Viewport.Height}");
            foreach (var b in view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var c = b.TranslatePoint(new Avalonia.Point(b.Bounds.Width / 2, b.Bounds.Height / 2), view)!.Value;
                Assert.True(c.X > 0 && c.X < view.Bounds.Width && c.Y > 0 && c.Y < view.Bounds.Height,
                    $"button '{b.Content}' centre {c} is outside {view.Bounds.Size}");
            }
        }
        finally { w.Close(); }
    }

    // ---------------------------------------------------------------- the info rail

    private static void AssertRowsPopulated(IEnumerable<ViewerFact> facts, string kind)
    {
        var list = facts.ToList();
        Assert.True(list.Count > 0, $"{kind}: the info rail is empty");
        Assert.All(list, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Label), $"{kind}: a row has no label");
            Assert.False(string.IsNullOrWhiteSpace(f.Value), $"{kind}: row {f.Label} has no value");
        });
    }

    private static string Dir() => Directory.CreateTempSubdirectory("nfty-chrome-").FullName;

    [AvaloniaFact]
    public async Task Every_subject_kind_fills_the_rail_with_real_rows()
    {
        var dialogs = new ArchiveGalleryTests.Scripted();

        // A picture.
        string root = Dir();
        using (var img = new Image<Rgba32>(6, 5, new Rgba32(1, 2, 3, 255))) img.SaveAsPng(Path.Combine(root, "a.png"));
        using (var png = await ViewerOpener.OpenAsync(Path.Combine(root, "a.png"), dialogs))
            AssertRowsPopulated(png!.Facts, "picture");

        // An asset's metadata file (both shapes).
        Directory.CreateDirectory(Path.Combine(root, "nfty"));
        Directory.CreateDirectory(Path.Combine(root, "metadata"));
        Directory.CreateDirectory(Path.Combine(root, "images"));
        File.WriteAllText(Path.Combine(root, "nfty", "0001.json"), JsonSerializer.Serialize(new
        {
            setNumber = 1, recipe = "chest", dna = "abc123", seed = "s",
            rarity = new[] { new { trait_type = "Lock", value = "Brass", rarityPct = 12.5 } },
            layers = Array.Empty<object>(),
        }));
        File.WriteAllText(Path.Combine(root, "metadata", "0001.json"), JsonSerializer.Serialize(new
        {
            name = "Chest #1", description = "d", image = "ipfs://x/0001.png",
            attributes = new[] { new { trait_type = "Lock", value = "Brass" } },
        }));
        foreach (var folder in new[] { "nfty", "metadata" })
        {
            using var meta = await ViewerOpener.OpenAsync(Path.Combine(root, folder, "0001.json"), dialogs);
            AssertRowsPopulated(meta!.Facts, "metadata/" + folder);
        }

        // An archive gallery.
        using var gallery = new GallerySubject(ArchiveGalleryTests.MixedGallery());
        AssertRowsPopulated(gallery.Facts, "archive");

        // A live Rise page: it used to be an empty rail.
        var vm = RisePages.VariantVm();
        using var live = new LiveSubject(vm);
        AssertRowsPopulated(live.Facts, "rise page");
        Assert.Contains(live.Facts, f => f.Label == "SIZE");
        vm.Dispose();
    }

    [AvaloniaFact]
    public void The_rail_draws_a_label_and_a_value_per_fact_and_follows_a_live_change()
    {
        var source = new FakeLive();
        using var live = new LiveSubject(source);
        var vm = ViewerViewTests.Vm(live);
        var view = new Views.ViewerView { DataContext = vm };
        var win = new Window { Content = view, Width = 900, Height = 600 };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            string[] Values() => view.GetVisualDescendants().OfType<SelectableTextBlock>()
                .Where(t => t.Classes.Contains("factval") && t.IsVisible).Select(t => t.Text!).ToArray();
            Assert.Equal(new[] { "one" }, Values());

            source.Facts = new ViewerFact[] { new("FAKE", "two"), new("MORE", "three") };
            source.Change();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (Values().Length != 2 && sw.ElapsedMilliseconds < 3000) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
            Assert.Equal(new[] { "two", "three" }, Values());
        }
        finally { win.Close(); vm.Dispose(); source.Art.Dispose(); }
    }

    private sealed class FakeLive : ILiveViewerSource
    {
        public Image<Rgba32> Art = new(2, 2, new Rgba32(1, 2, 3, 255));
        public IReadOnlyList<ViewerFact> Facts = new ViewerFact[] { new("FAKE", "one") };
        public string ViewerTitle => "fake";
        public Image<Rgba32> ViewerArt => Art;
        public IReadOnlyList<ViewerFact> ViewerFacts => Facts;
        public ViewerModel? ViewerModel => null;
        public bool AllowExport => true;
        public event Action? ViewerChanged;
        public event Action? Detached;
        public void Change() => ViewerChanged?.Invoke();
        public void Close() => Detached?.Invoke();
    }

    [AvaloniaFact]
    public void A_fact_line_becomes_a_label_and_a_value_and_none_is_dropped()
    {
        Assert.Equal(new ViewerFact("SEED", "viewer"), ViewerFact.FromLine("seed viewer"));
        Assert.Equal(new ViewerFact("CANVAS", "64 × 64"), ViewerFact.FromLine("canvas 64 × 64"));
        Assert.Equal(new ViewerFact("PICTURE", "32 × 32"), ViewerFact.FromLine("picture · 32 × 32"));
        Assert.Equal(new ViewerFact("NOTE", "1 → paints first"), ViewerFact.FromLine("1 → paints first"));
    }

    // ---------------------------------------------------------------- the actions

    [AvaloniaFact]
    public async Task Copy_info_puts_the_rows_on_the_clipboard()
    {
        var clip = new RecordingClipboard();
        using var art = ViewerViewTests.Art();
        using var subject = new ViewerViewTests.TrackedSubject("x", art, null);
        var vm = new ViewerViewModel(subject, new Picker(), clip, new DialogService());
        Assert.True(vm.CopyFactsCommand.CanExecute(null));
        await vm.CopyFactsCommand.ExecuteAsync(null);
        Assert.NotNull(clip.Text);
        // Ready to paste: the name, a rule, then aligned "Label:  value" lines.
        var lines = clip.Text!.Split(Environment.NewLine);
        Assert.Equal("x", lines[0]);
        Assert.Equal("-", lines[1]);
        Assert.Equal(subject.Facts.Count + 2, lines.Length);
        Assert.All(subject.Facts, f => Assert.Contains(lines, l =>
            l.StartsWith(f.Label, StringComparison.OrdinalIgnoreCase) && l.EndsWith(f.Value, StringComparison.Ordinal)));
        // Values start in one column.
        Assert.Single(subject.Facts.Zip(lines.Skip(2), (f, l) => l.Length - f.Value.Length).Distinct());
        vm.Dispose();
    }

    [AvaloniaFact]
    public async Task A_sealed_subject_refuses_save_copy_export_and_copy_info_and_says_why()
    {
        var clip = new RecordingClipboard();
        using var art = ViewerViewTests.Art();
        using var subject = new ViewerViewTests.TrackedSubject("Sealed", art, ViewerViewTests.Model()) { AllowExport = false };
        var vm = new ViewerViewModel(subject, new Picker(), clip, new DialogService()) { InWindow = true };
        Assert.False(vm.CopyFactsCommand.CanExecute(null));
        Assert.False(vm.CopyCommand.CanExecute(null));
        Assert.False(vm.SaveImageCommand.CanExecute(1));
        Assert.False(vm.SaveStillCommand.CanExecute(null));
        Assert.False(vm.ExportModelCommand.CanExecute(null));
        await vm.CopyFactsCommand.ExecuteAsync(null);     // Execute does not consult CanExecute: the body must
        Assert.Null(clip.Text);
        Assert.False(string.IsNullOrWhiteSpace(vm.ExportNote));

        var view = new Views.ViewerView { DataContext = vm };
        var win = new Window { Content = view, Width = 900, Height = 600 };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var copyInfo = view.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CopyInfoButton");
            Assert.False(copyInfo.IsEffectivelyEnabled);
        }
        finally { win.Close(); vm.Dispose(); }
    }
}
