using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Measures the shared control's actual dispatcher-driven turntable, including bitmap
/// copies. Reported observations only; a contended machine must never fail a timing assertion.</summary>
public class PreviewCadencePerfTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(64, 256)]
    [InlineData(512, 512)]
    public void Actual_turntable_cadence_and_CPU_frame_cost_are_reported(int sourceSize, int viewportSize)
    {
        using var art = new Image<Rgba32>(sourceSize, sourceSize);
        var bytes = new byte[sourceSize * sourceSize * 2];
        float c = (sourceSize - 1) / 2f, r = (sourceSize / 2f) - 1;
        for (int y = 0; y < sourceSize; y++)
        for (int x = 0; x < sourceSize; x++)
        {
            float distance = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            if (distance > r) continue;
            art[x, y] = new Rgba32(200, 120, 80);
            int i = ((y * sourceSize) + x) * 2;
            bytes[i] = (byte)Math.Clamp(8 - (int)(distance * 8 / r), 0, 8);
            bytes[i + 1] = 1;
        }
        var mesh = Riser.TryBuild(art, DepthMap.FromBytes(sourceSize, sourceSize, bytes), new RiseOptions())!;
        using var texture = FaceAtlas.Build(art);
        var vm = new ModelPreviewViewModel(mesh.Min, mesh.Max);
        var view = new ModelPreviewView { DataContext = vm, Mesh = mesh, Texture = texture };
        var window = new Window { Content = view, Width = viewportSize, Height = viewportSize };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 5; i++) view.RepaintNow();
        int before = view.RepaintCount;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        Perf.Enable();
        var watch = Stopwatch.StartNew();
        try
        {
            vm.Turntable = TurntableSpeed.Medium;
            Dispatcher.UIThread.MainLoop(stop.Token);
            watch.Stop();
            int frames = view.RepaintCount - before;
            Assert.True(frames > 0, "The real dispatcher must draw turntable frames.");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"source={sourceSize}, viewport={viewportSize}, frames={frames}, elapsed={watch.Elapsed.TotalSeconds:F3}s, actual={frames / watch.Elapsed.TotalSeconds:F2}fps"));
            output.WriteLine(Perf.Report());
        }
        finally
        {
            vm.Turntable = TurntableSpeed.Off;
            Perf.Disable();
            window.Close();
        }
    }
}
