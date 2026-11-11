using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The preview does not re-rasterize per resize step, and does not spin while nobody can
/// see it.</summary>
public class ModelPreviewResizeTests
{
    [AvaloniaFact]
    public void A_live_resize_holds_the_last_frame_and_settles_on_one_exact_frame()
    {
        var (window, _, view) = ModelPreviewGestureTests.Render();
        try
        {
            view.RepaintNow();
            int before = view.RepaintCount;
            var held = (WriteableBitmap)view.Surface.Source!;

            for (int i = 1; i <= 20; i++)
            {
                window.Width = 400 - i * 5;
                window.Height = 400 - i * 3;
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(before, view.RepaintCount);          // nothing re-rasterized mid-resize
            Assert.Same(held, view.Surface.Source);            // the last frame still stands
            Assert.True(view.RepaintPending);

            view.RepaintNow();                                 // the settle, flushed
            Assert.True(view.RepaintCount - before <= 2);
            Assert.False(view.RepaintPending);
            var settled = view.EncodeStill();

            var (w2, _, fresh) = ModelPreviewGestureTests.Render();
            try
            {
                w2.Width = window.Width;
                w2.Height = window.Height;
                Dispatcher.UIThread.RunJobs();
                fresh.RepaintNow();
                Assert.Equal(fresh.EncodeStill(), settled);
            }
            finally { w2.Close(); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task The_settled_frame_arrives_by_itself_after_the_size_is_still()
    {
        var (window, _, view) = ModelPreviewGestureTests.Render();
        try
        {
            view.RepaintNow();
            window.Width = 300;
            window.Height = 250;
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.RepaintPending);

            for (int i = 0; i < 100 && view.RepaintPending; i++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(view.RepaintPending);
            Assert.Equal(new Avalonia.PixelSize(300, 250), ((WriteableBitmap)view.Surface.Source!).PixelSize);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_turntable_pauses_while_the_window_is_minimized_and_resumes_on_restore()
    {
        var (window, vm, view) = ModelPreviewGestureTests.Render();
        try
        {
            vm.Turntable = TurntableSpeed.Slow;
            Assert.True(view.TurntableRunning);

            window.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.TurntableRunning);
            Assert.Equal(TurntableSpeed.Slow, vm.Turntable); // the setting is kept

            window.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.TurntableRunning);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_turntable_does_not_run_while_the_preview_is_hidden()
    {
        var (window, vm, view) = ModelPreviewGestureTests.Render();
        try
        {
            vm.Turntable = TurntableSpeed.Slow;
            view.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.TurntableRunning);
            view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.TurntableRunning);
        }
        finally { window.Close(); }
    }
}
