using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class FooterAttentionTests
{
    private static Image<Rgba32> Frame(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
    }

    private static void Save(Image<Rgba32> frame, string name)
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        frame.SaveAsPng(Path.Combine(dir, name + ".png"));
    }

    private static async Task PumpUntil(Window window, Func<bool> ready)
    {
        // Sample on the same dispatcher as the animation. A thread-pool delay can resume after
        // the entire brief cue has finished on a loaded CI worker.
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        int ticks = 0;
        timer.Tick += (_, _) =>
        {
            using var frame = window.CaptureRenderedFrame();
            if (ready()) done.TrySetResult();
            else if (++ticks >= 200) done.TrySetException(new TimeoutException("Footer cue did not reach the requested frame."));
        };
        timer.Start();
        try { await done.Task; }
        finally { timer.Stop(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warnings_replay_a_footer_only_cue_then_restore_the_baseline(bool dark)
    {
        var status = new StatusService();
        var shell = new ShellViewModel(new FakeNav(), new DialogService(), new ThemeService(), status);
        var view = new ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1180, Height = 720,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            const string warning = "Editing is locked. Unlock to make changes.";
            status.Say(warning);
            Dispatcher.UIThread.RunJobs();
            var footer = view.FindControl<Grid>("StatusBar")!;
            var wash = view.FindControl<Border>("StatusAttentionWash")!;
            var bounds = new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size);
            using var baseline = Frame(window);
            Save(baseline, $"footer-normal-{dark}");
            Assert.Equal(0, shell.StatusAttention);
            status.Warn(warning);
            Assert.Equal(1, shell.StatusAttention);
            Assert.False(view.PendingStatusAttention.IsCompleted, "Cue completed before its first frame.");
            await PumpUntil(window, () => wash.Opacity > 0.05);
            using var alert = Frame(window);
            Save(alert, $"footer-alert-{dark}");
            Assert.Equal(bounds, new Rect(footer.TranslatePoint(default, window)!.Value, footer.Bounds.Size));
            int changed = 0, outside = 0;
            // The frame scales its footer; use its transformed bottom-right, not raw Bounds.Size.
            var end = footer.TranslatePoint(new Avalonia.Point(footer.Bounds.Width, footer.Bounds.Height), window)!.Value;
            for (int y = 0; y < baseline.Height; y++)
            for (int x = 0; x < baseline.Width; x++)
                if (baseline[x, y] != alert[x, y])
                {
                    changed++;
                    // A fractional frame scale antialiases the clipped edge into its neighboring pixel.
                    if (x < Math.Floor(bounds.X) - 1 || x >= Math.Ceiling(end.X) + 1
                        || y < Math.Floor(bounds.Y) - 1 || y >= Math.Ceiling(end.Y) + 1) outside++;
                }
            Assert.True(changed > 0);
            Assert.Equal(0, outside);
            Assert.False(wash.IsHitTestVisible);
            var first = view.PendingStatusAttention;
            status.Warn(warning);
            Assert.NotSame(first, view.PendingStatusAttention);
            Assert.Equal(2, shell.StatusAttention);
            await PumpUntil(window, () => view.PendingStatusAttention.IsCompleted);
            await view.PendingStatusAttention;
            Assert.Equal(0, wash.Opacity);
            Assert.Equal(warning, shell.StatusMessage);
            using var settled = Frame(window);
            Save(settled, $"footer-settled-{dark}");
            int delta = 0;
            for (int y = 0; y < baseline.Height; y++)
            for (int x = 0; x < baseline.Width; x++)
            {
                var a = baseline[x, y]; var b = settled[x, y];
                delta = Math.Max(delta, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
            }
            // Skia may round the frame's antialiased corners one channel level differently after compositing.
            Assert.InRange(delta, 0, 1);
            status.Say("Saved.");
            Assert.Equal(2, shell.StatusAttention);
            Assert.Equal(0, wash.Opacity);
        }
        finally { window.Close(); }
        var detached = view.PendingStatusAttention;
        status.Warn("Another warning.");
        Assert.Same(detached, view.PendingStatusAttention);
    }
}
