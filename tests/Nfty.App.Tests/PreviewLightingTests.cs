using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.App.Viewer;
using Nfty.App.Services;
using Nfty.App.Views;
using Nfty.Core.Imaging;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public class PreviewLightingTests
{
    [AvaloniaFact]
    public void The_shared_footer_light_control_has_the_button()
    {
        var view = new PreviewLightingView();
        Assert.NotNull(view.FindControl<Button>("LightButton"));
    }

    private static (Window Window, ModelPreviewViewModel Vm, ModelPreviewView View, PreviewLightingView Footer) RenderWithFooter()
    {
        var vm = new ModelPreviewViewModel(new System.Numerics.Vector3(-1, -1, 0), new System.Numerics.Vector3(1, 1, 0));
        var view = new ModelPreviewView { DataContext = vm, Mesh = ModelPreviewGestureTests.TinyQuad(), Texture = ModelPreviewGestureTests.TinyTexture() };
        var footer = new PreviewLightingView { DataContext = vm, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        grid.Children.Add(view);
        Grid.SetRow(footer, 1);
        grid.Children.Add(footer);
        var window = new Window { Content = grid, Width = 400, Height = 440 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view, footer);
    }

    [Fact]
    public void Invalid_or_partial_color_keeps_the_last_valid_light()
    {
        var vm = new ModelPreviewViewModel(-System.Numerics.Vector3.One, System.Numerics.Vector3.One);
        vm.LightColor = "#12AB34";
        var last = vm.Lighting;
        foreach (string text in new[] { "", "#", "#12", "#12AB3Z", "12AB34", "#12345678" })
        {
            vm.LightColor = text;
            Assert.False(vm.IsLightColorValid);
            Assert.Equal(last, vm.Lighting);
            Assert.Equal("#12AB34", vm.LightColorSwatch);
        }
        vm.LightColor = "#abcdef";
        Assert.True(vm.IsLightColorValid);
        Assert.Equal(new Rgba32(171, 205, 239), vm.Lighting.Color);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(110, 100)]
    public void Preview_intensity_is_clamped(int value, int expected)
    {
        var vm = new ModelPreviewViewModel(-System.Numerics.Vector3.One, System.Numerics.Vector3.One);
        vm.LightValue = value;
        Assert.Equal(expected, vm.Lighting.Value);
    }

    [AvaloniaFact]
    public void Reset_light_preserves_camera_edges_turntable_and_viewport_geometry()
    {
        var (window, vm, view, footer) = RenderWithFooter();
        try
        {
            view.Clock = () => 0;
            vm.Orbit(new System.Numerics.Vector2(20, 10));
            vm.Wireframe = true;
            vm.Turntable = TurntableSpeed.Slow;
            vm.LightValue = 20;
            vm.LightColor = "#AA3311";
            var camera = vm.CameraState;
            var bounds = view.Bounds;
            var surface = view.Surface.Bounds;
            var button = footer.FindControl<Button>("LightButton")!;
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            var content = Assert.IsType<StackPanel>(flyout.Content);
            // Exercise the actual shipped reset control. Its flyout is hosted by the real view.
            button.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            var reset = content.FindControl<Button>("ResetLightButton")!;
            Assert.Same(vm, content.DataContext);
            Assert.Same(vm.ResetLightingCommand, reset.Command);
            reset.Focus();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal(PreviewLighting.Default, vm.Lighting);
            Assert.Same(camera, vm.CameraState);
            Assert.True(vm.Wireframe);
            Assert.Equal(TurntableSpeed.Slow, vm.Turntable);
            Assert.Equal(bounds, view.Bounds);
            Assert.Equal(surface, view.Surface.Bounds);
            flyout.Hide();
        }
        finally { window.Close(); view.Texture?.Dispose(); }
    }

    [AvaloniaFact]
    public void Changing_light_repaints_the_real_bitmap_and_leaves_the_source_texture_untouched()
    {
        var (window, vm, view) = ModelPreviewGestureTests.Render();
        try
        {
            byte[] before = view.EncodeStill()!;
            var source = view.Texture![0, 0];
            var camera = vm.CameraState;
            vm.LightValue = 0;
            Assert.True(view.RepaintPending);
            byte[] dark = view.EncodeStill()!;
            Assert.False(before.SequenceEqual(dark));
            vm.LightValue = 100;
            vm.LightColor = "#FF0000";
            Assert.True(view.RepaintPending);
            Assert.False(before.SequenceEqual(view.EncodeStill()!));
            vm.ResetLightingCommand.Execute(null);
            Assert.Equal(before, view.EncodeStill());
            Assert.Same(camera, vm.CameraState);
            Assert.Equal(source, view.Texture[0, 0]);
        }
        finally { window.Close(); view.Texture?.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lighting_changes_only_model_pixels_in_the_rendered_frame(bool dark)
    {
        var (window, vm, view) = ModelPreviewGestureTests.Render();
        window.RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
        try
        {
            Dispatcher.UIThread.RunJobs();
            view.RepaintNow();
            using var mask = ImageFormats.Load(view.EncodeStill()!);
            using var before = Capture(window);
            vm.LightColor = "#4080FF";
            vm.LightValue = 60;
            view.RepaintNow();
            Dispatcher.UIThread.RunJobs();
            using var after = Capture(window);
            Assert.Equal(before.Size, after.Size);
            int changed = 0, unchangedOutside = 0;
            for (int y = 0; y < before.Height; y++)
            for (int x = 0; x < before.Width; x++)
            {
                if (mask[x, y].A == 0)
                {
                    Assert.Equal(before[x, y], after[x, y]);
                    unchangedOutside++;
                }
                else if (before[x, y] != after[x, y]) changed++;
            }
            Assert.True(changed > 0, "A different light must visibly change the model.");
            Assert.True(unchangedOutside > 0, "The screenshot must include the surrounding layout.");
            var dir = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR");
            if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is not null && dir is not null)
            {
                Directory.CreateDirectory(dir);
                before.SaveAsPng(Path.Combine(dir, $"preview-geometry-before-{(dark ? "dark" : "light")}.png"));
                after.SaveAsPng(Path.Combine(dir, $"preview-geometry-after-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally { window.Close(); view.Texture?.Dispose(); }
    }

    private static SixLabors.ImageSharp.Image<Rgba32> Capture(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        bitmap.Save(stream, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        return ImageFormats.Load(stream.ToArray());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rise_light_lives_in_the_readout_footer_and_changes_no_layout_at_minimum_size(bool dark)
    {
        using var page = RisePages.Show(RisePages.VariantVm(), RisePages.MinArea.Width, RisePages.MinArea.Height,
            dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        AssertFooterGeometry(page.Window, page.View, page.Vm.Preview, "readout");
    }

    [AvaloniaTheory]
    [InlineData(false, 778.3333333333334, 392.3333333333333, 1)]
    [InlineData(true, 778.3333333333334, 392.3333333333333, 1)]
    [InlineData(false, 533.3333333333334, 400, 1)]
    [InlineData(true, 533.3333333333334, 400, 1)]
    [InlineData(false, 778.3333333333334, 392.3333333333333, 1.2)]
    [InlineData(true, 778.3333333333334, 392.3333333333333, 1.2)]
    [InlineData(false, 778.3333333333334, 392.3333333333333, 2)]
    [InlineData(true, 778.3333333333334, 392.3333333333333, 2)]
    public void Viewer_light_fits_the_footer_and_changes_no_layout(bool dark, double width, double height, double scale)
    {
        using var art = ViewerViewTests.Art();
        var model = ViewerViewTests.Model();
        model.Texture[0, 0] = new Rgba32(200, 120, 80, 255);
        using var texture = model.Texture;
        using var subject = new SnapshotSubject("Light", [new("SIZE", "16 x 16")], art, model);
        using var vm = ViewerViewTests.Vm(subject);
        var view = new ViewerView { DataContext = vm };
        Control content = scale == 1 ? view : new LayoutTransformControl
            { Child = view, LayoutTransform = new Avalonia.Media.ScaleTransform(scale, scale) };
        var window = new Window { Content = content, Width = width * scale, Height = height * scale,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            AssertFooterGeometry(window, view, vm.Preview, "toolstrip");
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ScrollBar>(), b => b.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    private static void AssertFooterGeometry(Window window, Control host, ModelPreviewViewModel vm, string footerClass)
    {
        var preview = host.GetVisualDescendants().OfType<ModelPreviewView>().Single();
        var light = host.GetVisualDescendants().OfType<PreviewLightingView>().Single();
        Assert.DoesNotContain(preview.GetVisualDescendants(), v => v is PreviewLightingView);
        var footer = light.GetVisualAncestors().OfType<Border>().First(b => b.Classes.Contains(footerClass));
        var at = light.TranslatePoint(default, footer)!.Value;
        Assert.True(light.IsEffectivelyVisible);
        Assert.True(at.X >= 0 && at.Y >= 0 && at.X + light.Bounds.Width <= footer.Bounds.Width + 1
            && at.Y + light.Bounds.Height <= footer.Bounds.Height + 1, "The light control must fit entirely inside its real footer.");
        var geometry = (preview.Bounds, preview.Surface.Bounds, footer.Bounds, light.Bounds);
        var button = light.FindControl<Button>("LightButton")!;
        var ink = new Avalonia.Media.FormattedText("Light", System.Globalization.CultureInfo.InvariantCulture,
            Avalonia.Media.FlowDirection.LeftToRight, new Avalonia.Media.Typeface(button.FontFamily),
            button.FontSize, Avalonia.Media.Brushes.Black);
        Assert.True(button.Bounds.Width + .5 >= ink.Width, "The full Light label must fit its actual hit target.");
        Assert.True(button.Bounds.Height >= 16, "The light trigger must retain a usable line-height hit target.");
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        flyout.ShowAt(button);
        Dispatcher.UIThread.RunJobs();
        var controls = Assert.IsType<StackPanel>(flyout.Content);
        var hex = controls.FindControl<TextBox>("LightColorBox")!;
        var camera = vm.CameraState;
        hex.Focus();
        window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Same(camera, vm.CameraState);
        hex.Text = "#A0C8FF";
        controls.FindControl<Slider>("LightValueSlider")!.Value = 42;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(42, vm.Lighting.Value);
        Assert.Equal(new Rgba32(160, 200, 255), vm.Lighting.Color);
        vm.ResetLightingCommand.Execute(null);
        Assert.Equal(PreviewLighting.Default, vm.Lighting);
        flyout.Hide();
        Dispatcher.UIThread.RunJobs();
        using var before = Capture(window);
        // A scaled clipped surface's first redraw can settle an edge pixel by one channel level,
        // even without changing any state (proved by the no-op capture). Compare equivalent
        // repainted frames; retain exact equality outside the pane, with no tolerance or fringe mask.
        preview.RepaintNow();
        Dispatcher.UIThread.RunJobs();
        using var unchanged = Capture(window);
        vm.LightValue = 75;
        vm.LightColor = "#4080FF";
        preview.RepaintNow();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(geometry, (preview.Bounds, preview.Surface.Bounds, footer.Bounds, light.Bounds));
        using var after = Capture(window);
        var origin = preview.TranslatePoint(default, window)!.Value;
        var transform = preview.TransformToVisual(window)!.Value;
        var modelRect = new Rect(origin, new Avalonia.Size(preview.Bounds.Width * Math.Abs(transform.M11),
            preview.Bounds.Height * Math.Abs(transform.M22)));
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") is { } diagnosticDir && Math.Abs(transform.M11 - 1) > .001)
        {
            Directory.CreateDirectory(diagnosticDir);
            var name = FormattableString.Invariant($"dpi-{window.Width:0.##}-{window.ActualThemeVariant.Key}");
            before.SaveAsPng(Path.Combine(diagnosticDir, name + "-before.png"));
            unchanged.SaveAsPng(Path.Combine(diagnosticDir, name + "-noop.png"));
            after.SaveAsPng(Path.Combine(diagnosticDir, name + "-light.png"));
            File.WriteAllText(Path.Combine(diagnosticDir, name + ".json"), System.Text.Json.JsonSerializer.Serialize(new
                { ModelRect = modelRect.ToString(), Transform = transform.ToString(), window.RenderScaling,
                  FrameWidth = before.Width, FrameHeight = before.Height,
                  ParentRect = ((Control)preview.Parent!).Bounds.ToString(),
                  Noop = PixelDifferences(before, unchanged, modelRect), Light = PixelDifferences(before, after, modelRect),
                  LightAgainstSettled = PixelDifferences(unchanged, after, modelRect) }));
        }
        int changedInside = 0;
        for (int y = 0; y < before.Height; y++)
        for (int x = 0; x < before.Width; x++)
            if (!modelRect.Contains(new Avalonia.Point(x + .5, y + .5)))
                Assert.True(unchanged[x, y] == after[x, y], $"Outside-model pixel changed at ({x},{y}): {unchanged[x, y]} → {after[x, y]}; model {modelRect}.");
            else if (unchanged[x, y] != after[x, y]) changedInside++;
        Assert.True(changedInside > 0, "A visible solid must change inside the model pane when the light changes.");
        vm.ResetLightingCommand.Execute(null);
        preview.RepaintNow();
        Dispatcher.UIThread.RunJobs();
        using var reset = Capture(window);
        for (int y = 0; y < reset.Height; y++)
        for (int x = 0; x < reset.Width; x++) Assert.Equal(unchanged[x, y], reset[x, y]);
    }

    private static object PixelDifferences(SixLabors.ImageSharp.Image<Rgba32> a, SixLabors.ImageSharp.Image<Rgba32> b, Rect model)
    {
        int total = 0, outside = 0;
        double furthest = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            if (a[x, y] == b[x, y]) continue;
            total++;
            var center = new Avalonia.Point(x + .5, y + .5);
            if (model.Contains(center)) continue;
            outside++;
            furthest = Math.Max(furthest, Math.Max(Math.Max(model.Left - center.X, center.X - model.Right),
                Math.Max(model.Top - center.Y, center.Y - model.Bottom)));
        }
        return new { Changed = total, OutsideCenterMask = outside, FurthestOutsideDevicePixels = furthest };
    }

    [AvaloniaFact]
    public void Capture_preview_lighting()
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE") is null) return;
        var dir = Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        foreach (var theme in new[] { Avalonia.Styling.ThemeVariant.Light, Avalonia.Styling.ThemeVariant.Dark })
        {
            var (window, vm, view, footer) = RenderWithFooter();
            window.RequestedThemeVariant = theme;
            try
            {
                vm.LightValue = 75;
                vm.LightColor = "#A0C8FF";
                view.RepaintNow();
                Dispatcher.UIThread.RunJobs();
                window.CaptureRenderedFrame()!.Save(
                    Path.Combine(dir, $"preview-light-{theme.Key.ToString()!.ToLowerInvariant()}.png"),
                    Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                var button = footer.FindControl<Button>("LightButton")!;
                var flyout = Assert.IsType<Flyout>(button.Flyout);
                var controls = Assert.IsType<StackPanel>(flyout.Content);
                // Capture the shipped flyout content itself: headless popups are separate surfaces.
                // No replica of the light controls is constructed for the capture.
                flyout.Content = null;
                var panel = new Border
                {
                    Background = (Avalonia.Media.IBrush)Application.Current!.FindResource(theme, "PanelBrush")!,
                    Padding = new Avalonia.Thickness(12), Child = controls, DataContext = vm,
                };
                var card = new Window { Content = panel, Width = 264, Height = 178, RequestedThemeVariant = theme };
                card.Show();
                Dispatcher.UIThread.RunJobs();
                try
                {
                    card.CaptureRenderedFrame()!.Save(
                        Path.Combine(dir, $"preview-light-controls-{theme.Key.ToString()!.ToLowerInvariant()}.png"),
                        Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
                finally { card.Close(); }
            }
            finally { window.Close(); view.Texture?.Dispose(); }
        }
    }
}
