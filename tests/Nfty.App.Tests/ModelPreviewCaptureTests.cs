using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Frames of the 3D preview for a human to LOOK at (NFTY_CAPTURE=1, like
/// <see cref="VisualCapture"/>). Inert in a normal run.</summary>
public class ModelPreviewCaptureTests
{
    [AvaloniaFact]
    public void Capture_the_preview_fully_zoomed_in()
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null) return;
        var dir = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            // 16x16 disc in four colored quadrants, so single pixels are countable in the frame.
            using var art = new Image<Rgba32>(16, 16);
            for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                double d = ((x - 7.5) * (x - 7.5)) + ((y - 7.5) * (y - 7.5));
                if (d > 49) continue;
                art[x, y] = (x < 8, y < 8) switch
                {
                    (true, true) => new Rgba32(220, 70, 60, 255),
                    (false, true) => new Rgba32(70, 170, 90, 255),
                    (true, false) => new Rgba32(70, 110, 220, 255),
                    _ => new Rgba32(230, 200, 70, 255),
                };
            }
            var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
            var vm = new ModelPreviewViewModel(mesh.Min, mesh.Max);
            var view = new ModelPreviewView { Width = 200, Height = 200, DataContext = vm, Mesh = mesh, Texture = art };
            var window = new Window { RequestedThemeVariant = variant, Content = view, Width = 200, Height = 200 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                // 40 steps reaches the floor (a 16px sprite then fills the frame with about one
                // pixel); 22 steps is the working-zoom view where individual pixels read.
                int done = 0;
                foreach (var (steps, name) in new[] { (22, "preview-zoomed-mid"), (40, "preview-zoomed-in") })
                {
                    for (; done < steps; done++) vm.ZoomStep(1f);
                    view.RepaintNow();
                    window.CaptureRenderedFrame()!.Save(
                        Path.Combine(dir, $"{name}-{variant.Key.ToString()!.ToLowerInvariant()}.png"),
                        PngBitmapEncoderOptions.Default);
                }
            }
            finally { window.Close(); }
        }
    }
}
