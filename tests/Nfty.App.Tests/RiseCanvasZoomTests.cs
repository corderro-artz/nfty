using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Controls;
using Nfty.App.Rise;
using Nfty.App.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Zoom must grow the drawing into the pane, including its labels and editable area.</summary>
public class RiseCanvasZoomTests
{
    [AvaloniaFact]
    public void Zoomed_art_and_numbers_extend_beyond_the_fitted_canvas_and_can_be_painted()
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            using var page = RisePages.Show(RisePages.VariantVm(), RisePages.MinArea.Width,
                RisePages.MinArea.Height, theme);
            var surface = page.View.FindControl<Panel>("CanvasSurface")!;
            var labels = page.View.FindControl<DepthLabelLayer>("DepthLabels")!;
            using var fittedFrame = page.Window.CaptureRenderedFrame();
            var topLeft = surface.TranslatePoint(default, page.Window)!.Value;
            double fitted = surface.Bounds.Width;
            Capture(page, "fit", theme);
            page.Vm.Zoom = 1.25;
            Dispatcher.UIThread.RunJobs();
            Capture(page, "outline", theme);

            // An 8px image at 2x: column 6 lies to the RIGHT of the original fitted box.
            // Derive this independently of CanvasMapping and the labels' own frame.
            page.Vm.Zoom = 2;
            Dispatcher.UIThread.RunJobs();
            double cell = fitted / 4;
            var pixel = topLeft + new Vector(-fitted / 2 + 6.5 * cell, -fitted / 2 + 2.5 * cell);
            Assert.True(pixel.X > topLeft.X + fitted);
            Capture(page, "zoom", theme);
            using var withLabels = Pixels(page);
            labels.Opacity = 0;
            Dispatcher.UIThread.RunJobs();
            using var artOnly = Pixels(page);
            var sample = pixel + new Vector(-cell * 0.3, -cell * 0.3);
            Assert.True(artOnly[(int)sample.X, (int)sample.Y] == new Rgba32(178, 122, 70, 255),
                $"Expected art at {sample}; got {artOnly[(int)sample.X, (int)sample.Y]}; fitted origin {topLeft}, side {fitted}.");
            int changed = 0;
            for (int y = (int)(pixel.Y - cell * 0.3); y < pixel.Y + cell * 0.3; y++)
                for (int x = (int)(pixel.X - cell * 0.3); x < pixel.X + cell * 0.3; x++)
                    if (withLabels[x, y] != artOnly[x, y]) changed++;
            Assert.True(changed > 5, "No thickness number was rendered outside the fitted box.");
            labels.Opacity = 1;
            var outline = page.View.FindControl<Border>("CanvasOutline")!;
            Assert.Equal(fitted * 2 + 2, outline.Bounds.Width, 0.01);
            Assert.Equal(-fitted / 2 - 1, outline.Margin.Left, 0.01);

            // Zoom again with the wheel over artwork beyond the old box. The pointed-to pixel stays put.
            page.Window.MouseWheel(pixel, new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2.5, page.Vm.Zoom);
            page.Window.MouseMove(pixel);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((6, 2), page.Vm.HoverPixel);

            page.Window.MouseDown(pixel, MouseButton.Left);
            page.Window.MouseUp(pixel, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, page.Vm.Surface.Get(6, 2).Front);
            Assert.Equal(0, page.Vm.Surface.Get(5, 2).Front);
        }
    }

    [AvaloniaFact]
    public void A_large_image_can_zoom_far_enough_to_read_its_depth_numbers_at_the_smallest_window()
    {
        var dir = Directory.CreateTempSubdirectory("nfty-rise-zoom-");
        try
        {
            var path = Path.Combine(dir.FullName, "large.png");
            using (var art = new SixLabors.ImageSharp.Image<Rgba32>(512, 512, new Rgba32(178, 122, 70, 255)))
                art.Save(path);
            var vm = RisePages.Vm(PictureRiseSource.Open(path, new ImageBridge(),
                () => System.Threading.Tasks.Task.FromResult<string?>(null)));
            using var page = RisePages.Show(vm, RisePages.MinArea.Width, RisePages.MinArea.Height, ThemeVariant.Dark);
            vm.Zoom = double.MaxValue;
            Dispatcher.UIThread.RunJobs();
            var labels = page.View.FindControl<DepthLabelLayer>("DepthLabels")!;
            using var frame = page.Window.CaptureRenderedFrame();
            Capture(page, "large-depth-numbers", ThemeVariant.Dark);
            Assert.True(labels.Frame.Scale >= DepthLabelLayer.MinCellPx,
                $"Zoom stops at {vm.Zoom}x, only {labels.Frame.Scale:0.##}px per cell: depth numbers never appear.");
            Assert.True(labels.LastLabelCount > 0);
        }
        finally { dir.Delete(recursive: true); }
    }

    private static SixLabors.ImageSharp.Image<Rgba32> Pixels(RisePage page)
    {
        using var frame = page.Window.CaptureRenderedFrame()!;
        using var bytes = new MemoryStream();
        frame.Save(bytes, PngBitmapEncoderOptions.Default);
        return SixLabors.ImageSharp.Image.Load<Rgba32>(bytes.ToArray());
    }

    private static void Capture(RisePage page, string state, ThemeVariant theme)
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null) return;
        var dir = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        using var frame = page.Window.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(dir, $"rise-canvas-{state}-{theme.Key}.png"), PngBitmapEncoderOptions.Default);
    }
}
