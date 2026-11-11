using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The loading diamond breathes only while it is shown: a hidden one must not keep an animation clock
/// running for every tile that has already loaded.</summary>
public class PlaceholderAnimationTests
{
    private static double Tick(Border b, int ms = 120)
    {
        Thread.Sleep(ms);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return b.Opacity;
    }

    private static (Window, Border) Host(bool visible, bool miss = false)
    {
        var b = new Border();
        b.Classes.Add("tload");
        if (miss) b.Classes.Add("miss");
        b.IsVisible = visible;
        var window = new Window { Content = new Panel { Children = { b } }, Width = 100, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, b);
    }

    [AvaloniaFact]
    public void A_shown_loading_diamond_breathes()
    {
        var (w, b) = Host(visible: true);
        try { Assert.True(Enumerable.Range(0, 6).Select(_ => Tick(b)).Distinct().Count() > 2); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void A_diamond_that_was_hidden_starts_its_breath_again_from_the_top_when_shown()
    {
        var (w, b) = Host(visible: true);
        try
        {
            for (int i = 0; i < 5; i++) Tick(b);        // well into the cycle
            Assert.True(b.Opacity > 0.12, $"not animating before the hide: {b.Opacity}");
            b.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < 5; i++) Tick(b);        // time passes while hidden
            b.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            // A fresh animation begins at 0.10; one that kept its clock while hidden has moved on.
            Assert.True(Tick(b, 20) < 0.12, $"resumed mid-cycle at {b.Opacity}: the hidden diamond kept its animation");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void A_missing_diamond_is_static()
    {
        var (w, b) = Host(visible: true, miss: true);
        try { Assert.Single(Enumerable.Range(0, 4).Select(_ => Tick(b)).Distinct()); }
        finally { w.Close(); }
    }
}
