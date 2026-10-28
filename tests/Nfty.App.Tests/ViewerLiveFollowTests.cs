using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A viewer that follows an open page: it sees what the page sees, once per frame, never keeps the
/// page's images, and survives the page closing.
/// </summary>
public class ViewerLiveFollowTests
{
    private static byte[] Bytes(Image<Rgba32> img)
    {
        var buffer = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(buffer);
        return buffer;
    }

    private static void Frame() => Dispatcher.UIThread.RunJobs();

    private static void Stroke(RiseViewModel vm, int x) =>
        vm.ApplyDepthStroke(new[] { (x, 3) }, DepthSide.Front, lower: false);

    /// <summary>A source that counts who is listening, so a subject that lets go can be told from one that
    /// merely stopped being asked.</summary>
    private sealed class FakeSource : ILiveViewerSource
    {
        public Image<Rgba32> Art = new(2, 2, new Rgba32(1, 2, 3, 255));
        public string ViewerTitle => "fake";
        public Image<Rgba32> ViewerArt => Art;
        public ViewerModel? ViewerModel => null;
        public bool AllowExport => true;
        public event Action? ViewerChanged;
        public event Action? Detached;
        public int Listeners => (ViewerChanged?.GetInvocationList().Length ?? 0) + (Detached?.GetInvocationList().Length ?? 0);
        public void Change() => ViewerChanged?.Invoke();
        public void Close() => Detached?.Invoke();
    }

    // ------------------------------------------------------------------ Rise

    [AvaloniaFact]
    public void A_rise_stroke_updates_a_live_viewer_byte_for_byte()
    {
        var vm = RisePages.VariantVm();
        using var live = new LiveSubject(vm);
        var firstMesh = live.Model!.Mesh;
        int changes = 0;
        live.Changed += () => changes++;

        Stroke(vm, 3);
        Assert.Equal(0, changes);           // nothing is cloned inside the stroke
        Frame();

        Assert.Equal(1, changes);
        Assert.NotSame(firstMesh, live.Model!.Mesh);
        Assert.Same(vm.Mesh, live.Model.Mesh);
        Assert.Equal(Bytes(vm.Subject.Art), Bytes(live.Art!));
        Assert.Equal(Bytes(vm.Subject.Texture), Bytes(live.Model.Texture));
        // A copy, not a reference: freeing the page's image cannot reach the viewer's.
        Assert.NotSame(vm.Subject.Art, live.Art);
        Assert.NotSame(vm.Subject.Texture, live.Model.Texture);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Ten_strokes_in_one_frame_raise_one_change()
    {
        var vm = RisePages.VariantVm();
        using var live = new LiveSubject(vm);
        int changes = 0;
        live.Changed += () => changes++;

        for (int i = 0; i < 10; i++) Stroke(vm, i % 8);
        Frame();

        Assert.Equal(1, changes);
        // And the next frame's stroke is a new change: coalescing must not stick.
        Stroke(vm, 1);
        Frame();
        Assert.Equal(2, changes);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Closing_the_rise_page_turns_its_viewer_into_a_snapshot_and_keeps_the_art()
    {
        var vm = RisePages.VariantVm();
        using var live = new LiveSubject(vm);
        Assert.True(live.IsLive);
        Stroke(vm, 2);
        var expected = Bytes(vm.Subject.Art);
        var expectedMesh = vm.Mesh;
        int changes = 0;
        live.Changed += () => changes++;

        vm.Dispose();                        // BEFORE the frame: the pending change is taken at close

        Assert.False(live.IsLive);
        Assert.Equal(expected, Bytes(live.Art!));
        Assert.Same(expectedMesh, live.Model!.Mesh);
        Assert.True(changes >= 1);           // so the badge can say "snapshot"
        Frame();
        Assert.Equal(expected, Bytes(live.Art!));
    }

    [AvaloniaFact]
    public void Disposing_the_source_image_after_a_change_leaves_the_viewer_drawable()
    {
        var vm = RisePages.VariantVm();
        var live = new LiveSubject(vm);
        var viewerVm = new ViewerViewModel(live, new FilePickerService(), new NoopClipboardService(), new FakeDialogs());
        var view = new Views.ViewerView { DataContext = viewerVm };
        var window = new Window { Content = view, Width = 1067, Height = 633 };
        window.Show();
        Frame();
        try
        {
            Stroke(vm, 4);
            Frame();                          // the viewer re-reads the new clone
            Assert.NotNull(viewerVm.ArtBitmap);

            vm.Dispose();                     // the page frees its art, texture and mesh
            Frame();

            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.NotNull(viewerVm.ArtBitmap);
            Assert.Equal("snapshot", viewerVm.LiveBadge);
        }
        finally { window.Close(); viewerVm.Dispose(); live.Dispose(); }
    }

    // ------------------------------------------------------------------ editor

    [AvaloniaFact]
    public void An_editor_stroke_updates_a_live_viewer_with_the_rendered_layer()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        using var live = new LiveSubject(vm);
        var before = Bytes(live.Art!);
        int changes = 0;
        live.Changed += () => changes++;

        vm.ApplyToolStroke(new[] { (1, 1), (2, 1), (3, 1) }, EditorTool.Brush);
        Frame();

        Assert.True(changes >= 1);
        var after = Bytes(live.Art!);
        Assert.NotEqual(before, after);
        // The layer as generation would render it: colorized, not the gray value-map being painted.
        // (a gray pixel has equal R, G and B; a colorized dynamic layer has none of them equal.)
        bool anyColored = false;
        for (int i = 0; i < after.Length; i += 4)
            if (after[i + 3] == 255 && !(after[i] == after[i + 1] && after[i + 1] == after[i + 2])) anyColored = true;
        Assert.True(anyColored, "the viewer should show the colorized layer");
        // And exactly the corner tile: the same pixels the Preview bitmap holds.
        var bmp = (Avalonia.Media.Imaging.Bitmap)vm.Preview!;
        int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
        Assert.Equal((w, h), (live.Art!.Width, live.Art.Height));
        var shown = new byte[w * h * 4];
        unsafe
        {
            fixed (byte* p = shown)
                bmp.CopyPixels(new Avalonia.PixelRect(0, 0, w, h), (nint)p, shown.Length, w * 4);
        }
        int compared = 0;
        for (int i = 0; i < after.Length; i += 4)
        {
            if (after[i + 3] != 255) continue;
            compared++;
            // The bitmap's channel order is the platform's (RGBA headless, BGRA on Windows): either way
            // it is the same three values.
            var mine = (after[i], after[i + 1], after[i + 2]);
            Assert.True(mine == (shown[i], shown[i + 1], shown[i + 2]) || mine == (shown[i + 2], shown[i + 1], shown[i]),
                $"pixel {i / 4}: viewer {mine} vs tile ({shown[i]},{shown[i + 1]},{shown[i + 2]})");
        }
        Assert.True(compared > 0);
        vm.Dispose();
    }

    [AvaloniaFact]
    public void Closing_the_editor_turns_its_viewer_into_a_snapshot()
    {
        var (book, recipe, ing) = VisualCapture.DynamicIngredient();
        var vm = new IngredientEditorViewModel(ing, recipe, book, new ImageBridge(), new FakeNav(),
            new CookBookSession(), new FakeDialogs(), new FilePickerService());
        using var live = new LiveSubject(vm);
        var kept = Bytes(live.Art!);

        vm.Dispose();

        Assert.False(live.IsLive);
        Assert.Equal(kept, Bytes(live.Art!));
    }

    // ------------------------------------------------------------------ ownership

    [AvaloniaFact]
    public void A_live_subject_lets_go_of_its_source_when_it_detaches_or_is_disposed()
    {
        var detached = new FakeSource();
        var live1 = new LiveSubject(detached);
        Assert.Equal(2, detached.Listeners);
        detached.Close();
        Assert.Equal(0, detached.Listeners);          // the source is no longer kept alive by the viewer
        Assert.False(live1.IsLive);
        live1.Dispose();

        var disposed = new FakeSource();
        var live2 = new LiveSubject(disposed);
        live2.Dispose();
        Assert.Equal(0, disposed.Listeners);
    }

    [AvaloniaFact]
    public void A_change_after_the_viewer_is_gone_neither_throws_nor_clones()
    {
        var source = new FakeSource();
        var live = new LiveSubject(source);
        source.Change();                               // posts a refresh
        live.Dispose();                                // the viewer closes before the frame
        source.Art.Dispose();                          // and the page frees its image
        Frame();                                       // the refresh runs against a disposed subject: nothing to do
        Assert.Equal(0, source.Listeners);
    }
}
