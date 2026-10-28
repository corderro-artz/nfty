using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Color = Avalonia.Media.Color;
using Point = Avalonia.Point;
using Size = Avalonia.Size;

namespace Nfty.App.Tests;

/// <summary>
/// The pop-out viewer's view: the layout switch that reserves space, the fit at the smallest window,
/// the backdrop, and the overlay host. The window half (ownership, remembered size) is
/// <see cref="ViewerWindowTests"/>.
/// </summary>
public class ViewerViewTests
{
    private sealed class Picker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    internal sealed class TrackedSubject(string title, Image<Rgba32>? art, ViewerModel? model)
        : ViewerSubject
    {
        public bool Disposed { get; private set; }
        public override string Title => title;
        public override IReadOnlyList<ViewerFact> Facts { get; } =
            [new("Number", "#0042"), new("Size", "16 x 16"), new("DNA", "9f3ac1d2e4b57a10")];
        public override Image<Rgba32>? Art => art;
        public override ViewerModel? Model => model;
        public override void Dispose() => Disposed = true;
    }

    internal static Image<Rgba32> Art(int side = 16)
    {
        var img = new Image<Rgba32>(side, side);
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
            img[x, y] = (x + y) % 3 == 0 ? new Rgba32(0, 0, 0, 0) : new Rgba32((byte)(x * 15), (byte)(y * 15), 160, 255);
        return img;
    }

    internal static ViewerModel Model() => new(
        new Mesh([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1, 0, 0, 1, 0, 0, 1], [0, 0, 1, 0, 0, 1], [0, 1, 2], 1, 1),
        new Image<Rgba32>(1, 1), null);

    internal static ViewerViewModel Vm(ViewerSubject s, IDialogService? dialogs = null) =>
        new(s, new Picker(), new NoopClipboardService(), dialogs ?? new DialogService());

    private static (Window Window, Views.ViewerView View) Host(ViewerViewModel vm, double w, double h,
        ThemeVariant? theme = null)
    {
        var view = new Views.ViewerView { DataContext = vm };
        var window = new Window { Content = view, Width = w, Height = h };
        if (theme is not null) window.RequestedThemeVariant = theme;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static byte[] Crop(WriteableBitmap frame, PixelRect rect)
    {
        int stride = rect.Width * 4;
        var buffer = Marshal.AllocHGlobal(stride * rect.Height);
        try
        {
            frame.CopyPixels(rect, buffer, stride * rect.Height, stride);
            var bytes = new byte[stride * rect.Height];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [AvaloniaFact]
    public void Switching_layout_moves_nothing_in_the_info_rail()
    {
        using var art = Art();
        using var subject = new TrackedSubject("Chest #42", art, Model());
        var vm = Vm(subject);
        var (window, view) = Host(vm, 1067, 526);
        try
        {
            var rail = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "INFO")
                .GetVisualAncestors().OfType<Border>().First();
            var topLeft = rail.TranslatePoint(default, window)!.Value;
            using var probe = window.CaptureRenderedFrame()!;
            // Inside the rail's hairline, and never past the frame.
            int x = (int)Math.Ceiling(topLeft.X) + 2;
            var rect = new PixelRect(x, (int)topLeft.Y, Math.Min((int)rail.Bounds.Width - 4, probe.PixelSize.Width - x),
                Math.Min((int)rail.Bounds.Height, probe.PixelSize.Height - (int)topLeft.Y));

            var frames = new List<byte[]>();
            var widths = new List<double>();
            var lefts = new List<double>();
            foreach (var layout in new[] { ViewerLayout.TwoD, ViewerLayout.Both, ViewerLayout.ThreeD })
            {
                vm.Layout = layout;
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame()!;
                frames.Add(Crop(frame, rect));
                widths.Add(rail.Bounds.Width);
                lefts.Add(rail.TranslatePoint(default, window)!.Value.X);
            }

            // Position as well as pixels: the two star columns beside it split 827px into 413.5 each,
            // and layout rounding once pushed the rail a pixel right in Both only.
            Assert.All(widths, w => Assert.Equal(240, w));
            Assert.Single(lefts.Distinct());
            Assert.Equal(frames[0], frames[1]);
            Assert.Equal(frames[0], frames[2]);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void The_3d_pane_paints_the_solid_it_is_given_and_follows_a_change_of_subject_art()
    {
        using var art = Art();
        using var subject = new TrackedSubject("x", art, Model());
        var vm = Vm(subject);
        var (window, view) = Host(vm, 1067, 526);
        try
        {
            var solid = view.FindControl<Views.ModelPreviewView>("ModelView")!;
            Assert.Same(vm.Mesh, solid.Mesh);
            Assert.Same(vm.Texture, solid.Texture);
            Assert.Same(vm.Preview, solid.DataContext);
            for (int i = 0; i < 500 && solid.RepaintCount == 0; i++)
            {
                Dispatcher.UIThread.RunJobs();
                System.Threading.Thread.Sleep(2);
            }
            Assert.True(solid.RepaintCount > 0, $"never painted: pending={solid.RepaintPending} bounds={solid.Bounds}");
            Assert.NotNull(solid.Surface.Source);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void The_hidden_pane_is_inked_out_and_cannot_be_hit()
    {
        using var art = Art();
        using var subject = new TrackedSubject("x", art, Model());
        var vm = Vm(subject);
        var (window, view) = Host(vm, 1067, 526);
        try
        {
            var twoD = view.FindControl<Panel>("CanvasBackdrop")!.GetVisualAncestors().OfType<Border>().First();
            var threeD = view.FindControl<Views.ModelPreviewView>("ModelView")!;
            vm.Layout = ViewerLayout.ThreeD;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, twoD.Opacity);
            Assert.False(twoD.IsHitTestVisible);
            Assert.Equal(1, threeD.GetVisualAncestors().OfType<Border>().First().Opacity);
            vm.Layout = ViewerLayout.TwoD;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, twoD.Opacity);
            Assert.Equal(0, threeD.GetVisualAncestors().OfType<Border>().First().Opacity);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(1067.0, 526.0)]
    // The smallest window a viewer may be (640 x 480), seen through the shell's scale.
    [InlineData(640 / ShellViewModel.BaseScale, 480 / ShellViewModel.BaseScale)]
    public void The_viewer_fits_the_smallest_window(double w, double h)
    {
        using var art = Art();
        using var subject = new TrackedSubject("Chest #42", art, Model());
        var vm = Vm(subject);
        var (window, view) = Host(vm, w, h);
        try
        {
            view.Measure(new Size(w, h));
            Assert.True(view.DesiredSize.Width <= w + 0.5 && view.DesiredSize.Height <= h + 0.5,
                $"desires {view.DesiredSize} of {w}x{h}");

            // Every button is inside the view AND reachable: a button arranged past an edge still
            // reports bounds, and is simply not drawn.
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            Assert.NotEmpty(buttons);
            foreach (var b in buttons)
            {
                var centre = b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), view)!.Value;
                Assert.True(centre.X > 0 && centre.X < w && centre.Y > 0 && centre.Y < h,
                    $"button '{b.Content}' centre {centre} is outside {w}x{h}");
                var hit = view.GetVisualAt(centre, v => v.IsEffectivelyVisible);
                Assert.True(hit is not null && (ReferenceEquals(hit, b) || b.IsVisualAncestorOf(hit)),
                    $"button '{b.Content}' is covered by {hit}");
            }
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void The_checker_backdrop_is_a_lattice_and_a_custom_one_is_the_readers_color()
    {
        using var art = Art();
        using var subject = new TrackedSubject("x", art, null);
        var vm = Vm(subject);
        var (window, view) = Host(vm, 1067, 526);
        try
        {
            var backdrop = view.FindControl<Panel>("CanvasBackdrop")!;
            Assert.IsType<DrawingBrush>(backdrop.Background);

            vm.TwoD.Backdrop = ViewerBackdrop.Custom;
            vm.TwoD.BackdropColor = "#336699";
            Dispatcher.UIThread.RunJobs();
            var solid = Assert.IsAssignableFrom<ISolidColorBrush>(backdrop.Background);
            Assert.Equal(Color.Parse("#336699"), solid.Color);

            vm.TwoD.Backdrop = ViewerBackdrop.Checker;
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<DrawingBrush>(backdrop.Background);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void A_sealed_subject_shows_why_export_is_off()
    {
        using var art = Art();
        using var subject = new TrackedSubject("Sealed", art, Model()) { AllowExport = false };
        var vm = Vm(subject);
        var (window, view) = Host(vm, 1067, 526);
        try
        {
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                t => t.Text == vm.ExportNote && t.Text!.Length > 0);
            var save = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "2x"));
            Assert.False(save.IsEffectivelyEnabled);
            var copy = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Copy"));
            Assert.False(copy.IsEffectivelyEnabled);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public void A_second_overlay_replaces_and_disposes_the_first()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService());
        var host = new OverlayViewerHost(shell, new Picker(), new NoopClipboardService(), dialogs, new ImageBridge());

        using var art = Art();
        var first = new TrackedSubject("one", art, null);
        var second = new TrackedSubject("two", art, null);
        host.Open(first);
        var firstVm = Assert.IsType<ViewerViewModel>(shell.Overlay);
        Assert.True(firstVm.ShowClose);
        Assert.False(first.Disposed);

        host.Open(second);
        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        var secondVm = Assert.IsType<ViewerViewModel>(shell.Overlay);
        Assert.NotSame(firstVm, secondVm);
        Assert.Same(second, secondVm.Subject);

        secondVm.CloseCommand.Execute(null);
        Assert.Null(shell.Overlay);
        Assert.True(second.Disposed);
    }

    [AvaloniaFact]
    public void The_overlay_slot_draws_the_viewer_above_the_dialog_layer()
    {
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(nav, dialogs, new ThemeService(), new StatusService());
        var host = new OverlayViewerHost(shell, new Picker(), new NoopClipboardService(), dialogs, new ImageBridge());
        var chrome = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = chrome, Width = 1200, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using var art = Art();
            host.Open(new TrackedSubject("overlay", art, null));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(chrome.GetVisualDescendants().OfType<Views.ViewerView>());
            host.Current!.CloseCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(chrome.GetVisualDescendants().OfType<Views.ViewerView>());
        }
        finally { window.Close(); }
    }
}
