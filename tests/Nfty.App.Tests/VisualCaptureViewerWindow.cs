using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.App.Viewer;
using Nfty.Desktop;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

public partial class VisualCapture
{
    private sealed class WindowPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string title, params string[] extensions) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string title, string defaultExtension) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// The pop-out viewer as the WINDOW the desktop head opens: the shared frame (gutter, rounded
    /// border, titlebar with brand, name, badge and layout switch, caption buttons) around the viewer.
    /// Both themes, at the default side-by-side size, at the smallest window, and with an empty info
    /// rail (a subject with nothing to say), plus a gallery. The corners outside the frame are
    /// transparent in the PNG, as they are on screen.
    /// </summary>
    [AvaloniaFact]
    public void Capture_viewer_window()
    {
        if (Dir is null) return;
        var cases = new (string Tag, double W, double H, bool Model, bool Facts, bool Gallery, bool Extended, bool Sealed)[]
        {
            ("window-both", 1280, 720, true, true, false, false, false),
            ("window-min", ViewerWindowState.MinWidth, ViewerWindowState.MinHeight, true, true, false, false, false),
            ("window-nofacts", 1024, 576, false, false, false, false, false),
            ("window-gallery", 1280, 720, false, true, true, false, false),
            ("window-extended", 1280, 720, true, true, false, true, false),
            ("window-extended-min", ViewerWindowState.MinWidth, ViewerWindowState.MinHeight, true, true, false, true, false),
            ("window-sealed", 1280, 720, true, true, false, false, true),
            ("window-2d", 1024, 576, false, true, false, false, false),
        };
        foreach (var variant in Themes)
        foreach (var (tag, w, h, withModel, withFacts, gallery, extended, sealedSubject) in cases)
        {
            using var art = new Image<Rgba32>(32, 32);
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                double d = ((x - 15.5) * (x - 15.5)) + ((y - 15.5) * (y - 15.5));
                if (d > 196) continue;
                art[x, y] = (x < 16, y < 16) switch
                {
                    (true, true) => new Rgba32(220, 70, 60, 255),
                    (false, true) => new Rgba32(70, 170, 90, 255),
                    (true, false) => new Rgba32(70, 110, 220, 255),
                    _ => new Rgba32(230, 200, 70, 255),
                };
            }
            var mesh = withModel ? Nfty.Model.Riser.TryBuild(art, null, new Nfty.Model.RiseOptions()) : null;
            var model = mesh is null ? null : new ViewerModel(mesh, Nfty.Model.FaceAtlas.Build(art), null);
            var facts = withFacts
                ? new List<ViewerFact> { new("ASSET", "#0042"), new("RECIPE", "chest"), new("SIZE", "32 x 32"),
                    new("DNA", "9f3ac1d2e4b57a10c3e1a8d0f4c26b19e7d5a03c8f61b2e94d07a5c3f81e6b2d"),
                    new("BODY", "Planked (24%)", Group: ViewerFactGroup.Traits),
                    new("BODY COLOR", "hsv:28,61,72", Group: ViewerFactGroup.Traits),
                    new("BANDS", "Straps (18%)", Group: ViewerFactGroup.Traits),
                    new("LOCK", "Brass (12.5%)", Group: ViewerFactGroup.Traits),
                    new("FEET", "Claws (33%)", Group: ViewerFactGroup.Traits),
                    new("GLOW", "Sparks (50%)", Group: ViewerFactGroup.Traits),
                    new("MODEL", withModel ? "recorded in the Set" : "none", Group: ViewerFactGroup.File) }
                : new List<ViewerFact>();
            ViewerSubject subject = gallery
                ? new GallerySubject(ArchiveGalleryTests.MixedGallery())
                : new SnapshotSubject("Vapor Pets #42", facts, art, model) { AllowExport = !sealedSubject };

            var host = new DesktopViewerHost(new WindowPicker(), new NoopClipboardService(),
                new ViewerWindowStateService(StateStore.InMemory()), new ImageBridge(), () => null);
            ViewerWindow? window = null;
            host.Opened += opened => { window = opened; opened.RequestedThemeVariant = variant; opened.Width = w; opened.Height = h; };
            host.Open(subject);
            try
            {
                Dispatcher.UIThread.RunJobs();
                window!.Width = w; window.Height = h;
                window.ViewModel.IsInfoExtended = extended;
                var solid = window.GetVisualDescendantsOfType<Views.ModelPreviewView>();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (withModel && sw.ElapsedMilliseconds < 5000
                       && !(solid is { } s && s.RepaintCount > 0 && !s.RepaintPending))
                {
                    Dispatcher.UIThread.RunJobs();
                    System.Threading.Thread.Sleep(1);
                    solid = window.GetVisualDescendantsOfType<Views.ModelPreviewView>();
                }
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(withFacts || gallery, window.ViewModel.HasFacts);
                window.CaptureRenderedFrame()!.Save(Path.Combine(Dir!, $"viewer-{tag}-{KeyOf(variant)}.png"),
                    PngBitmapEncoderOptions.Default);
            }
            finally { window?.Close(); }
        }
    }
}

internal static class VisualExt
{
    public static T? GetVisualDescendantsOfType<T>(this Avalonia.Visual v) where T : class =>
        System.Linq.Enumerable.FirstOrDefault(
            System.Linq.Enumerable.OfType<T>(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v)));
}
