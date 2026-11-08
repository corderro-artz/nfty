using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Rise;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Art = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

namespace Nfty.App.Tests;

public class AppRiseViewerAuditTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Live_save_keeps_invocation_pixels_and_scale_across_picker_refresh_or_close(bool close)
    {
        var directory = Directory.CreateTempSubdirectory();
        using var source = new LiveSource();
        using var subject = new LiveSubject(source);
        var picker = new HeldPicker();
        var dialogs = new FakeDialogs();
        using var vm = new ViewerViewModel(subject, picker, new NoopClipboardService(), dialogs);
        try
        {
            using var original = subject.Art!.Clone();
            using var expected = Nfty.Core.Imaging.Upscale.By(original, 2);
            using var expectedBytes = new MemoryStream();
            await expected.SaveAsPngAsync(expectedBytes);
            var saving = vm.SaveImageCommand.ExecuteAsync(2);
            Assert.False(saving.IsCompleted);
            if (close) { vm.Dispose(); subject.Dispose(); }
            else { source.Change(model: true); Dispatcher.UIThread.RunJobs(); }
            vm.ImageScale = 8;
            string path = Path.Combine(directory.FullName, "saved.png");
            picker.Result.SetResult(path);
            await saving;
            Assert.Null(dialogs.Active);
            Assert.True(File.Exists(path));
            Assert.Equal(expectedBytes.ToArray(), File.ReadAllBytes(path));
        }
        finally { directory.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Live_model_transitions_update_rendered_controls_fall_back_and_keep_camera(bool dark)
    {
        using var source = new LiveSource();
        using var subject = new LiveSubject(source);
        using var vm = new ViewerViewModel(subject, new HeldPicker(), new NoopClipboardService(), new FakeDialogs());
        var view = new ViewerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 720, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var export = view.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.ExportModelCommand));
            var both = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string s && s == "Both");
            Assert.False(export.IsEffectivelyEnabled);
            Assert.Equal(0.45, both.Opacity);
            var exportBounds = export.Bounds;
            var bothBounds = both.Bounds;
            Capture(window, $"viewer-no-model-{dark}");
            var notified = new HashSet<string?>();
            vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);
            source.Change(model: true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(export.IsEffectivelyEnabled);
            Assert.Equal(1, both.Opacity);
            Assert.Equal(exportBounds, export.Bounds);
            Assert.Equal(bothBounds, both.Bounds);
            Capture(window, $"viewer-model-arrival-{dark}");
            foreach (string property in new[] { nameof(vm.ThreeDAvailableOpacity), nameof(vm.LayoutNote), nameof(vm.HasModelFiles), nameof(vm.ExportFormatOpacity), nameof(vm.CanExportModel) })
                Assert.Contains(property, notified);
            vm.Layout = ViewerLayout.ThreeD;
            Dispatcher.UIThread.RunJobs();
            var preview = vm.Preview;
            preview.Orbit(new System.Numerics.Vector2(20, 10));
            var camera = preview.CameraState;
            source.Change(model: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(preview, vm.Preview);
            Assert.Equal(camera, preview.CameraState);
            Assert.Equal(ViewerLayout.ThreeD, vm.Layout);
            source.Change(model: false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(export.IsEffectivelyEnabled);
            Assert.Equal(0.45, both.Opacity);
            Assert.Equal(ViewerLayout.TwoD, vm.Layout);
            Assert.True(vm.TwoDHit);
            Assert.Equal(1, vm.TwoDOpacity);
            Assert.Equal(camera, preview.CameraState);
            Capture(window, $"viewer-model-loss-{dark}");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Inspector_wheel_keeps_reachable_art_coordinate_from_fit_or_old_pan_limit(double scale)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "art.png");
            using (var art = new Art(64, 64)) art.SaveAsPng(path);
            var item = new SetItem(1, path, "dna", "r", [], []);
            using var vm = new SetInspectViewModel([new SetItemRow(1, path, item)], 0,
                new HeldPicker(), new FakeDialogs(), new StatusService());
            var view = new SetInspectView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 700 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var viewport = view.FindControl<Border>("Viewport")!;
                var canvas = view.FindControl<Avalonia.Controls.Image>("Canvas")!;
                vm.Scale = scale;
                vm.PanY = (canvas.Bounds.Height * scale - viewport.Bounds.Height) / 2;
                var pointer = new Avalonia.Point(viewport.Bounds.Width / 2, viewport.Bounds.Height * 0.4);
                var offset = pointer.Y - viewport.Bounds.Height / 2;
                double coordinateBefore = (offset - vm.PanY) / vm.Scale;
                double wantedPan = offset - coordinateBefore * scale * 1.2;
                Assert.InRange(wantedPan, -(canvas.Bounds.Height * scale * 1.2 - viewport.Bounds.Height) / 2,
                    (canvas.Bounds.Height * scale * 1.2 - viewport.Bounds.Height) / 2);
                window.MouseWheel(viewport.TranslatePoint(pointer, window)!.Value, new Vector(0, 1));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(scale * 1.2, vm.Scale, 8);
                Assert.Equal(coordinateBefore, (offset - vm.PanY) / vm.Scale, 8);
            }
            finally { window.Close(); }
        }
        finally { directory.Delete(true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rise_voxel_field_accepts_and_displays_canonical_minimum_precision_and_maximum(bool dark)
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            string path = Path.Combine(directory.FullName, "art.png");
            using (var art = new Art(2, 2, new Rgba32(10, 20, 30, 255))) art.SaveAsPng(path);
            using var source = PictureRiseSource.Open(path, new ImageBridge());
            using var vm = new RiseViewModel(source, new ImageBridge(), new FakeNav(), new FakeDialogs(), new HeldPicker(), new StatusService());
            var view = new RiseView { DataContext = vm };
            var window = new Window { Content = view, Width = 1180, Height = 720, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
            window.Show();
            vm.ShowExportTabCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            try
            {
                var numeric = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Width == 94);
                Assert.Equal((decimal)ModelVoxelSize.Min, numeric.Minimum);
                Assert.Equal((decimal)ModelVoxelSize.Max, numeric.Maximum);
                var text = numeric.GetVisualDescendants().OfType<TextBox>().Single();
                var row = Assert.IsType<Grid>(numeric.Parent);
                var rowBounds = row.Bounds;
                foreach (double value in new[] { ModelVoxelSize.Min, 0.123, ModelVoxelSize.Max })
                {
                    vm.VoxelSize = 1;
                    text.Focus();
                    text.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
                    text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = text });
                    view.Focus();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal((decimal)value, numeric.Value);
                    Assert.Equal(value, vm.VoxelSize, 8);
                    Assert.Equal((float)value, vm.ExportOptions.VoxelSize);
                    Assert.Equal(value.ToString("0.###", CultureInfo.InvariantCulture), text.Text);
                    var dimensions = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(),
                        t => t.Text == vm.ModelDimensionsText);
                    Assert.InRange(dimensions.TextLayout.WidthIncludingTrailingWhitespace, 0,
                        row.Bounds.Width - numeric.Bounds.Width - row.ColumnSpacing + 0.5);
                    Assert.All(dimensions.TextLayout.TextLines, line =>
                    {
                        Assert.False(line.HasCollapsed);
                        Assert.False(line.HasOverflowed);
                    });
                    var lastLine = dimensions.TextLayout.TextLines[^1];
                    Assert.True(lastLine.FirstTextSourceIndex + lastLine.Length >= vm.ModelDimensionsText.Length);
                    Assert.InRange(dimensions.TextLayout.Height, 0, row.Bounds.Height);
                    Assert.Equal(rowBounds, row.Bounds);
                    Capture(window, $"rise-voxel-{value.ToString(CultureInfo.InvariantCulture)}-{dark}");
                }
            }
            finally { window.Close(); }
        }
        finally { directory.Delete(true); }
    }

    private static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("NFTY_RISE_AUDIT_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private sealed class HeldPicker : IFilePickerService
    {
        public TaskCompletionSource<string?> Result { get; } = new();
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Result.Task;
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    private sealed class LiveSource : ILiveViewerSource, IDisposable
    {
        public Art ViewerArt { get; } = new(2, 2, new Rgba32(10, 20, 30, 255));
        private readonly ViewerModel _model = ViewerViewTests.Model();
        private bool _hasModel;
        public string ViewerTitle => "live";
        public ViewerModel? ViewerModel => _hasModel ? _model : null;
        public IReadOnlyList<ViewerFact> ViewerFacts => [];
        public bool AllowExport => true;
        public event Action? ViewerChanged;
        public event Action? Detached { add { } remove { } }
        public void Change(bool model)
        {
            _hasModel = model;
            ViewerArt[0, 0] = new Rgba32(99, 100, 101, 255);
            ViewerChanged?.Invoke();
        }
        public void Dispose() { ViewerArt.Dispose(); _model.Texture.Dispose(); }
    }
}
