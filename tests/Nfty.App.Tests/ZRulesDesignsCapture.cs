using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Throwaway capture of the three rules-panel designs.</summary>
public class ZRulesDesignsCapture
{
    private static string? Dir => System.Environment.GetEnvironmentVariable("NFTY_VARIANT_DIR");

    private static ZRuleBag Bag(int n)
    {
        var all = new List<ZRule>
        {
            new(true,  new ZSide("BG", "day"),      new[] { new ZSide("AURA", "none") }),
            new(false, new ZSide("BG", "night"),    new[] { new ZSide("AURA", "glow") }),
            new(true,  new ZSide("HAT", "crown"),   new[] { new ZSide("EARS", "folded"), new ZSide("EARS", "bat") }),
            new(true,  new ZSide("EYES", "closed"), new[] { new ZSide("AURA", "glow") }),
            new(false, new ZSide("BODY", "ghost"),  new[] { new ZSide("AURA", "none") }),
            new(true,  new ZSide("HAT", "halo"),    new[] { new ZSide("BODY", "ghost") }),
        };
        return new ZRuleBag(all.GetRange(0, n));
    }

    /// <summary>Throwaway.</summary>
    [AvaloniaFact]
    public void Render()
    {
        if (Dir is null) return;
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            var k = theme.Key.ToString()!.ToLowerInvariant();
            foreach (var (n, tag) in new[] { (2, "few"), (6, "many") })
            {
                var view = new ZRulesDesigns { DataContext = Bag(n) };
                var w = new Window { RequestedThemeVariant = theme, Content = view, Width = 990, Height = 700 };
                w.Show();
                Dispatcher.UIThread.RunJobs();
                w.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, $"designs-{tag}-{k}.png"),
                    PngBitmapEncoderOptions.Default);
                w.Close();
            }
        }
    }
}
