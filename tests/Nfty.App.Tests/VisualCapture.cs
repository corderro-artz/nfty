using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

    private static Button Btn(string content, params string[] classes)
    {
        var btn = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var cls in classes) btn.Classes.Add(cls);
        return btn;
    }

    /// <summary>Mirrors MainWindow.axaml's wordmark TextBlock: "nft" in default foreground, "y" in
    /// AccentTextBrush.</summary>
    private static TextBlock Wordmark(ThemeVariant variant)
    {
        var tb = new TextBlock { Margin = new Thickness(0, 2) };
        tb.Classes.Add("wordmark");
        tb.Inlines = new InlineCollection
        {
            new Run("nft"),
            new Run("y") { Foreground = Res("AccentTextBrush", variant) },
        };
        return tb;
    }

    private static Border KindChip(string cls, string text) => new()
    {
        Classes = { cls },
        Margin = new Thickness(0, 0, 8, 0),
        Child = Label(text, "kind-txt"),
    };

    /// <summary>Mirrors MainWindow's titlebar + status bar chrome (brand tile, wordmark, borderless
    /// window controls, zoom/help controls) so it can be visually captured — MainWindow itself can't
    /// be instantiated headlessly (it's the desktop head's top-level Window). Keep this structurally
    /// in sync with src/Nfty.Desktop/MainWindow.axaml's titlebar/status-bar markup.</summary>
    private static IBrush Res(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var v) ? (IBrush)v! : Brushes.Magenta;

    private static Control ChromeStrip(ThemeVariant variant)
    {
        var radiusSm = Application.Current!.TryGetResource("RadiusSm", variant, out var r)
            ? (CornerRadius)r!
            : new CornerRadius(5);

        var brandTile = new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = radiusSm,
            Background = Res("AccentWashBrush", variant),
            Child = new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(2),
                Background = Res("AccentBrush", variant),
                RenderTransform = new RotateTransform(45),
            },
        };

        var titlebar = new Grid
        {
            Height = 46,
            Background = Res("PanelBrush", variant),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 9,
            Children = { brandTile, Wordmark(variant) },
        };
        Grid.SetColumn(brand, 0);
        var winControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Children = { Btn("—", "icon"), Btn("▢", "icon"), Btn("✕", "icon", "danger") },
        };
        Grid.SetColumn(winControls, 2);
        titlebar.Children.Add(brand);
        titlebar.Children.Add(winControls);

        var statusBar = new Grid
        {
            Height = 34,
            Background = Res("BgAltBrush", variant),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        var statusText = Label("Ready · 1,024 assets", "muted");
        statusText.Margin = new Thickness(16, 0);
        statusText.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(statusText, 0);
        var zoomGroup = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2,
            Children =
            {
                Btn("−", "icon"),
                new TextBlock { Text = "100%", Width = 46, TextAlignment = Avalonia.Media.TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Btn("+", "icon"),
                Btn("?", "icon"),
            },
        };
        Grid.SetColumn(zoomGroup, 1);
        statusBar.Children.Add(statusText);
        statusBar.Children.Add(zoomGroup);

        return new StackPanel
        {
            Spacing = 0,
            Children = { titlebar, statusBar },
        };
    }

    private static Control Gallery(ThemeVariant variant) => new Border
    {
        Background = Application.Current!.TryGetResource("BgBrush", variant, out var bg) ? (IBrush)bg! : Brushes.Magenta,
        Padding = new Thickness(20),
        Width = 640,
        Child = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                ChromeStrip(variant),
                Label("The quick brown fox — sans body text"),
                Label("nfty", "wordmark"),
                Label("CookBook › Recipe › Ingredient", "crumbs"),
                Label("dna-0x9f3a  ·  mono 0123456789", "mono"),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { Btn("Open CookBook", "tbtn"), Btn("Cook", "accent"), Btn("Ghost", "ghost"), Btn("✕", "icon"), Btn("⚄", "dice") },
                },
                new Border
                {
                    Classes = { "panel" },
                    Padding = new Thickness(14, 12),
                    Margin = new Thickness(0, 6, 0, 0),
                    Child = Label("panel surface — shadow + border + r-md"),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Children =
                    {
                        new Border { Classes = { "card" }, Width = 140, Child = Label("card surface") },
                        new Border { Classes = { "tile" }, Width = 120, Height = 44, Child = Label("tile surface") },
                    },
                },
                new Border
                {
                    Classes = { "idchip" },
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = Label("id: aura", "mono"),
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        KindChip("kind-dynamic", "dynamic"),
                        KindChip("kind-static", "static"),
                        KindChip("kind-custom", "custom"),
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 10,
                    Margin = new Thickness(0, 6, 0, 0),
                    Children =
                    {
                        new TextBox { Text = "aura", Watermark = "id", Width = 110 },
                        new Slider { Minimum = 0, Maximum = 1, Value = 0.6, Width = 110 },
                        new CheckBox { IsChecked = true, Content = "checked" },
                        new RadioButton { IsChecked = true, Content = "picked" },
                        new NumericUpDown { Value = 12, Width = 110 },
                    },
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
