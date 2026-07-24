using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>Renders styled primitives under the themed test app and saves a PNG, so visual parity
/// with the mockups can be checked from a real rendered frame (not imagined from XAML). No-ops in a
/// normal test run; set env var NFTY_CAPTURE=1 (and optionally NFTY_CAPTURE_DIR) to activate.</summary>
public class VisualCapture
{
    private static string? Dir =>
        Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null
            ? null
            : (Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath());

    private static TextBlock Label(string text, string? cls = null)
    {
        var tb = new TextBlock { Text = text, Margin = new Thickness(0, 2) };
        if (cls is not null) tb.Classes.Add(cls);
        return tb;
    }

    private static Button Btn(string content, string cls) =>
        new() { Content = content, Classes = { cls }, Margin = new Thickness(0, 0, 8, 0) };

    private static Control Gallery(ThemeVariant variant) => new Border
    {
        Background = Application.Current!.TryGetResource("BgBrush", variant, out var bg) ? (IBrush)bg! : Brushes.Magenta,
        Padding = new Thickness(20),
        Width = 520,
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Label("The quick brown fox — sans body text"),
                Label("nfty", "wordmark"),
                Label("CookBook › Recipe › Ingredient", "crumbs"),
                Label("dna-0x9f3a  ·  mono 0123456789", "mono"),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { Btn("Open CookBook", "tbtn"), Btn("Cook", "accent"), Btn("Ghost", "ghost"), Btn("✕", "icon"), Btn("⚄", "dice") },
                },
            },
        },
    };

    [AvaloniaFact]
    public void Capture_style_gallery()
    {
        if (Dir is null) return;   // inert unless explicitly capturing

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            // A focusable sink at the top takes initial focus so no button shows a focus adorner
            // in the capture (buttons auto-focus on window show otherwise, muddying the comparison).
            var sink = new Button { Width = 0, Height = 0, Opacity = 0 };
            var content = new StackPanel { Children = { sink, Gallery(variant) } };
            var window = new Window
            {
                RequestedThemeVariant = variant,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            sink.Focus();
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var path = Path.Combine(Dir!, $"gallery-{variant.Key.ToString()!.ToLowerInvariant()}.png");
            frame!.Save(path);
        }
    }
}
