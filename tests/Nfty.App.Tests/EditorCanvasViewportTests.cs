using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using PixelRect = Nfty.Core.Editing.PixelRect;
using Rectangle = Avalonia.Controls.Shapes.Rectangle;

namespace Nfty.App.Tests;

/// <summary>The image editor's magnified art and marquee use the entire canvas pane.</summary>
public class EditorCanvasViewportTests
{
    [AvaloniaFact]
    public void Art_marquee_and_pointer_gestures_work_beyond_the_original_canvas_outline()
    {
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var (book, recipe, ing) = VisualCapture.DynamicIngredient();
            using var ownedBook = book;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++) ing.VariantImages["soft"][x, y] = new Rgba32(80, 80, 80, 255);
            using var session = new CookBookSession();
            using var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
                session, new FakeDialogs(), new FilePickerService());
            var view = new IngredientEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = RisePages.MinArea.Width,
                Height = RisePages.MinArea.Height, RequestedThemeVariant = theme };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                using var fit = window.CaptureRenderedFrame();
                var surface = view.FindControl<Panel>("CanvasSurface")!;
                var overlay = view.FindControl<Canvas>("CanvasOverlay")!;
                var fittedOrigin = surface.TranslatePoint(default, window)!.Value;
                double side = surface.Bounds.Width;
                Capture(window, "fit", theme);
                vm.BrushSize = 1;
                vm.BrushValue = 255;
                var emptyPane = fittedOrigin + new Vector(-20, side / 2);
                window.MouseDown(emptyPane, MouseButton.Left);
                window.MouseUp(emptyPane, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(80, vm.ValueAt(i % 8, i / 8)));
                vm.Zoom = 1.25;
                Dispatcher.UIThread.RunJobs();
                Capture(window, "outline", theme);

                vm.Zoom = 2;
                Dispatcher.UIThread.RunJobs();
                // Independently map a center in column1, which has moved outside the original LEFT edge.
                double cell = side / 4;
                var pixel = fittedOrigin + new Vector(-side / 2 + 1.5 * cell, -side / 2 + 3.5 * cell);
                Assert.True(pixel.X < fittedOrigin.X);
                using (var frame = Pixels(window))
                    Assert.True(frame[(int)pixel.X, (int)pixel.Y] == new Rgba32(80, 80, 80, 255),
                        $"Zoomed art at {pixel} is clipped at the original {fittedOrigin} fitted box.");
                Capture(window, "zoom", theme);

                vm.ActiveTool = EditorTool.Select;
                window.MouseDown(pixel, MouseButton.Left);
                window.MouseMove(pixel + new Vector(cell, 0));
                window.MouseUp(pixel + new Vector(cell, 0), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new PixelRect(1, 3, 2, 1), vm.Selection);
                var marquee = Assert.IsType<Rectangle>(Assert.Single(overlay.Children));
                Assert.Equal(2 * cell, marquee.Width, 0.01);
                Assert.Equal(-cell, Canvas.GetLeft(marquee), 0.01);
                using var withMarquee = Pixels(window);
                overlay.Opacity = 0;
                Dispatcher.UIThread.RunJobs();
                using var withoutMarquee = Pixels(window);
                int ink = 0;
                // A visible section of the marquee's top edge, outside the old fitted box.
                for (int y = (int)(pixel.Y - cell / 2) - 2; y <= pixel.Y - cell / 2 + 2; y++)
                    for (int x = (int)(pixel.X - cell / 2) + 4; x < fittedOrigin.X - 4; x++)
                        if (withMarquee[x, y] != withoutMarquee[x, y]) ink++;
                Assert.True(ink > 5, "The selection exists but its outline is clipped outside the fitted canvas.");
                overlay.Opacity = 1;
                Capture(window, "marquee", theme);

                vm.ActiveTool = EditorTool.Brush;
                vm.BrushSize = 1;
                vm.BrushValue = 255;
                window.MouseWheel(pixel, new Vector(0, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(2.5, vm.Zoom);
                double panX = vm.PanX, panY = vm.PanY;
                var drag = new Vector(20, 10);
                window.MouseDown(pixel, MouseButton.Middle);
                window.MouseMove(pixel + drag);
                window.MouseUp(pixel + drag, MouseButton.Middle);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(panX + 20, vm.PanX, 0.01);
                Assert.Equal(panY + 10, vm.PanY, 0.01);
                Assert.All(Enumerable.Range(0, 64), i => Assert.Equal(80, vm.ValueAt(i % 8, i / 8)));

                window.MouseDown(pixel + drag, MouseButton.Left);
                window.MouseUp(pixel + drag, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.All(Enumerable.Range(0, 64), i =>
                    Assert.Equal(i == 3 * 8 + 1 ? 255 : 80, vm.ValueAt(i % 8, i / 8)));

                var outline = view.FindControl<Border>("CanvasOutline")!;
                Assert.Equal(side * 2.5 + 2, outline.Bounds.Width, 0.01);
                vm.FillPanePreviewCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(side + 2, outline.Bounds.Width, 0.01);
                vm.BrushValue = 15;
                window.MouseDown(pixel + drag, MouseButton.Left);
                window.MouseUp(pixel + drag, MouseButton.Left);
                window.MouseWheel(pixel + drag, new Vector(0, 1));
                Assert.Equal(2.5, vm.Zoom);
                Capture(window, "preview", theme);
                vm.FillPanePreviewCommand.Execute(null);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(side * 2.5 + 2, outline.Bounds.Width, 0.01);
                Assert.Equal(255, vm.ValueAt(1, 3));
            }
            finally { window.Close(); }
        }
    }

    private static SixLabors.ImageSharp.Image<Rgba32> Pixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()!;
        using var bytes = new MemoryStream();
        frame.Save(bytes, PngBitmapEncoderOptions.Default);
        return SixLabors.ImageSharp.Image.Load<Rgba32>(bytes.ToArray());
    }

    private static void Capture(Window window, string state, ThemeVariant theme)
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null) return;
        var dir = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        using var frame = window.CaptureRenderedFrame()!;
        frame.Save(Path.Combine(dir, $"editor-canvas-{state}-{theme.Key}.png"), PngBitmapEncoderOptions.Default);
    }
}
