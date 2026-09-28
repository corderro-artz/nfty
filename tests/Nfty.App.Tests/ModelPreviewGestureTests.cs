using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;
using Image = Avalonia.Controls.Image;
// SixLabors.ImageSharp declares its own non-generic Point and its own (non-generic, static) Vector,
// which collide with Avalonia's once both namespaces are in scope — same ambiguity as ModelPreviewView
// itself hits, fixed the same way.
using Point = Avalonia.Point;
using Vector = Avalonia.Vector;

namespace Nfty.App.Tests;

/// <summary>Drives the real control with real pointer and key events — the pattern
/// <c>CanvasZoomTests</c> establishes: a test that calls <c>ModelPreviewViewModel.Orbit</c> directly
/// proves nothing about whether the drag reaches it.</summary>
public class ModelPreviewGestureTests
{
    private static Mesh TinyQuad()
    {
        float[] positions = [-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 2, textureHeight: 2);
    }

    private static Image<Rgba32> TinyTexture()
    {
        var img = new Image<Rgba32>(2, 2);
        img.Mutate(ctx => ctx.BackgroundColor(new Rgba32(200, 100, 50, 255)));
        return img;
    }

    private static (Window Window, ModelPreviewViewModel Vm, ModelPreviewView View) Render()
    {
        var vm = new ModelPreviewViewModel(new Vector3(-1, -1, -1), new Vector3(1, 1, 1));
        var view = new ModelPreviewView { DataContext = vm, Mesh = TinyQuad(), Texture = TinyTexture() };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, view);
    }

    [AvaloniaFact]
    public void A_left_drag_orbits_the_camera()
    {
        var (window, vm, _) = Render();
        try
        {
            float startYaw = vm.CameraState.Yaw;
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(260, 200));
            window.MouseUp(new Point(260, 200), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startYaw, vm.CameraState.Yaw);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_right_drag_pans_the_camera_target()
    {
        var (window, vm, _) = Render();
        try
        {
            var startTarget = vm.CameraState.Target;
            window.MouseDown(new Point(200, 200), MouseButton.Right);
            window.MouseMove(new Point(230, 180));
            window.MouseUp(new Point(230, 180), MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startTarget, vm.CameraState.Target);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_wheel_zooms_anchored_on_the_pointer()
    {
        var (window, vm, _) = Render();
        try
        {
            float startDistance = vm.CameraState.Distance;
            window.MouseWheel(new Point(260, 150), new Vector(0, 1));
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.CameraState.Distance < startDistance);
        }
        finally { window.Close(); }
    }

    /// <summary>Raises the routed event directly rather than simulating two real clicks within a
    /// timing threshold — the same shortcut <c>TileClickGestureTests</c> takes for
    /// <c>Button.ClickEvent</c>, and for the same reason: the code-behind handler only needs a
    /// <see cref="RoutedEventArgs"/> carrying the right <see cref="RoutedEvent"/>, not real input
    /// timing.</summary>
    /// <remarks>
    /// Raised against <see cref="InputElement.DoubleTappedEvent"/>, not <c>Gestures.DoubleTappedEvent</c>
    /// — verified against the installed Avalonia 12.1.1 package: <c>Gestures</c> carries only a plain
    /// static <c>EventHandler&lt;TappedEventArgs&gt;</c> backing field named <c>DoubleTapped</c>
    /// (internal gesture-recognizer plumbing), not a <see cref="RoutedEvent"/>; the actual
    /// <c>RoutedEvent&lt;TappedEventArgs&gt;</c> the CLR <c>DoubleTapped</c> event wraps lives on
    /// <see cref="InputElement"/>. Separately, the view subscribes through the non-generic
    /// <c>AddHandler</c> overload with a <see cref="RoutedEventArgs"/>-typed handler rather than the
    /// idiomatic <c>DoubleTapped += (s, TappedEventArgs e) =&gt; ...</c>: the generic overload wraps
    /// every subscription in an adapter that hard-casts the raised args to <c>TappedEventArgs</c>
    /// before invoking, which throws <see cref="InvalidCastException"/> for a plain
    /// <see cref="RoutedEventArgs"/> like the one this test raises (confirmed empirically against the
    /// installed package, decompiling <c>Interactive.AddHandler</c>/<c>EventRoute.RaiseEventImpl</c>
    /// to see the cast) — a real double-tap still reaches the same handler, since Avalonia's own
    /// gesture recognizer always raises a genuine <c>TappedEventArgs</c>, which is itself a
    /// <see cref="RoutedEventArgs"/>.
    /// </remarks>
    [AvaloniaFact]
    public void Double_click_resets_the_camera()
    {
        var (window, vm, view) = Render();
        try
        {
            var original = vm.CameraState;
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(300, 260));
            window.MouseUp(new Point(300, 260), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual(original.Yaw, vm.CameraState.Yaw);

            view.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent) { Source = view });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(original.Target, vm.CameraState.Target);
            Assert.Equal(original.Distance, vm.CameraState.Distance, 4);
            Assert.Equal(original.Yaw, vm.CameraState.Yaw, 4);
            Assert.Equal(original.Pitch, vm.CameraState.Pitch, 4);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Arrow_keys_orbit_when_the_view_has_focus()
    {
        var (window, vm, view) = Render();
        try
        {
            view.Focus();
            Dispatcher.UIThread.RunJobs();
            float startYaw = vm.CameraState.Yaw;

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startYaw, vm.CameraState.Yaw);
        }
        finally { window.Close(); }
    }

    /// <summary>The rule every other gesture in this app keeps: <c>PointerCaptureLost</c> is treated
    /// as abort, so a drag that wanders off the control stops reporting rather than leaving a
    /// half-applied camera move.</summary>
    /// <remarks>
    /// <see cref="PointerCaptureLostEventArgs"/>'s constructor was verified against the installed
    /// Avalonia 12.1.1 package (decompiled): <c>PointerCaptureLostEventArgs(object? source, IPointer
    /// pointer)</c>, which internally sets its own <see cref="RoutedEventArgs.RoutedEvent"/> to
    /// <see cref="InputElement.PointerCaptureLostEvent"/> — so, unlike the double-click test above,
    /// this constructs a genuine, fully-typed instance of the event's own args type and needs no
    /// non-generic-overload workaround; the view's ordinary <c>PointerCaptureLost += ...</c>
    /// subscription receives it directly.
    /// </remarks>
    [AvaloniaFact]
    public void Losing_pointer_capture_mid_drag_aborts_it_without_a_partial_move()
    {
        var (window, vm, view) = Render();
        try
        {
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(220, 200));
            Dispatcher.UIThread.RunJobs();
            float yawAfterFirstMove = vm.CameraState.Yaw;

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            view.RaiseEvent(new PointerCaptureLostEventArgs(view, pointer));
            Dispatcher.UIThread.RunJobs();

            window.MouseMove(new Point(280, 200));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(yawAfterFirstMove, vm.CameraState.Yaw);
        }
        finally { window.Close(); }
    }

    /// <summary>Otherwise every key binding above is raised on the Window instead and silently does
    /// nothing — the exact defect CLAUDE.md records the editor page hitting once already.</summary>
    [AvaloniaFact]
    public void The_view_takes_focus_on_attach()
    {
        var (window, _, view) = Render();
        try
        {
            Assert.True(view.IsFocused);
        }
        finally { window.Close(); }
    }

    /// <summary>Exact-pixel per house style: render the same Mesh/Camera through <c>Raster</c>
    /// directly into a <c>RenderTarget</c>, and assert the control's <c>WriteableBitmap</c>'s pixel
    /// bytes match — proving the view's paint path is the same pipeline <c>Raster</c> owns, not a
    /// second one.</summary>
    [AvaloniaFact]
    public void The_rendered_bitmap_reflects_the_current_mesh_and_camera()
    {
        var (window, vm, view) = Render();
        try
        {
            var surface = view.GetVisualDescendants().OfType<Image>().Single();
            var bitmap = (Avalonia.Media.Imaging.WriteableBitmap)surface.Source!;
            int width = (int)view.Bounds.Width, height = (int)view.Bounds.Height;

            var expected = new RenderTarget(width, height);
            expected.Clear(new Rgba32(0, 0, 0, 0));
            Raster.Render(view.Mesh!, view.Texture!, vm.CameraState.ViewProjection(width, height), expected);

            using var fb = bitmap.Lock();
            unsafe
            {
                var actual = new ReadOnlySpan<byte>((void*)fb.Address, expected.Color.Length * 4);
                var expectedBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes<Rgba32>(expected.Color);
                Assert.True(actual.SequenceEqual(expectedBytes));
            }
        }
        finally { window.Close(); }
    }
}
