using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Nfty.Model;
using Xunit;
using Point = Avalonia.Point;
using Vector = Avalonia.Vector;

namespace Nfty.App.Tests;

/// <summary>The preview view's resource, subscription and repaint-budget behavior (fix round 1b):
/// one bitmap and buffer, one VM subscription, coalesced repaints that are exact at gesture end.
/// Gestures themselves are in <see cref="ModelPreviewGestureTests"/>.</summary>
public class ModelPreviewViewTests
{
    private static (Window Window, ModelPreviewViewModel Vm, ModelPreviewView View) Render() =>
        ModelPreviewGestureTests.Render();

    // ---- I4: one bitmap, one buffer ------------------------------------------------------------

    [AvaloniaFact]
    public void Repainting_at_the_same_size_reuses_the_same_bitmap()
    {
        var (window, _, view) = Render();
        try
        {
            view.RepaintNow();
            var first = view.Surface.Source;
            view.RepaintNow();
            view.RepaintNow();

            Assert.NotNull(first);
            Assert.Same(first, view.Surface.Source);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_resize_replaces_the_bitmap_and_disposes_the_old_one()
    {
        var (window, _, view) = Render();
        try
        {
            view.RepaintNow();
            var old = (WriteableBitmap)view.Surface.Source!;

            window.Width = 300;
            window.Height = 300;
            Dispatcher.UIThread.RunJobs();
            view.RepaintNow();

            var now = (WriteableBitmap)view.Surface.Source!;
            Assert.NotSame(old, now);
            Assert.Equal(new PixelSize(300, 300), now.PixelSize);
            Assert.Throws<ObjectDisposedException>(() => old.Lock());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Detaching_releases_the_bitmap_and_the_view_model_subscription()
    {
        var (window, vm, view) = Render();
        try
        {
            view.RepaintNow();
            var bitmap = (WriteableBitmap)view.Surface.Source!;

            window.Content = null; // detaches the view
            Dispatcher.UIThread.RunJobs();

            Assert.Null(view.Surface.Source);
            Assert.Throws<ObjectDisposedException>(() => bitmap.Lock());
            view.RepaintNow(); // clears any request left over from before the detach
            vm.ZoomStep(1f);
            Assert.False(view.RepaintPending);
        }
        finally { window.Close(); }
    }

    // ---- I5: subscription hygiene --------------------------------------------------------------

    [AvaloniaFact]
    public void A_replaced_view_model_no_longer_drives_repaints()
    {
        var (window, oldVm, view) = Render();
        try
        {
            var newVm = new ModelPreviewViewModel(new Vector3(-1, -1, 0), new Vector3(1, 1, 0));
            view.DataContext = newVm;
            view.RepaintNow();

            oldVm.ZoomStep(1f);
            Assert.False(view.RepaintPending);

            newVm.ZoomStep(1f);
            Assert.True(view.RepaintPending);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_same_view_model_set_again_after_null_still_drives_repaints_through_one_subscription()
    {
        var (window, vm, view) = Render();
        try
        {
            view.DataContext = null;
            view.DataContext = vm;
            view.RepaintNow();

            vm.ZoomStep(1f);
            Assert.True(view.RepaintPending);
            view.RepaintNow();

            // Replacing it now must silence it completely: a leftover second subscription from the
            // A -> null -> A round trip would still mark the view dirty from the old instance.
            view.DataContext = new ModelPreviewViewModel(new Vector3(-1, -1, 0), new Vector3(1, 1, 0));
            view.RepaintNow();
            vm.ZoomStep(1f);
            Assert.False(view.RepaintPending);
        }
        finally { window.Close(); }
    }

    // ---- I6: coalesced repaint -----------------------------------------------------------------

    [AvaloniaFact]
    public void Several_camera_changes_in_one_turn_make_one_repaint()
    {
        var (window, vm, view) = Render();
        try
        {
            view.RepaintNow();
            int before = view.RepaintCount;

            vm.ZoomStep(1f);
            vm.ZoomStep(1f);
            vm.Orbit(new Vector2(10, 0));
            vm.Orbit(new Vector2(10, 0));

            Assert.True(view.RepaintPending);
            Assert.Equal(before, view.RepaintCount); // nothing drawn synchronously
            // RunJobs drains what is already posted, and the repaint was posted before this call
            // (MarkDirty posts at once), so one drain is enough here. It is not a general wait.
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.RepaintPending);
            Assert.Equal(before + 1, view.RepaintCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Releasing_a_drag_repaints_synchronously()
    {
        var (window, vm, view) = Render();
        try
        {
            // The headless input helpers pump the dispatcher after every event, so a coalesced post
            // would run between MouseMove and MouseUp and hide whether RELEASE itself repaints. Look
            // from inside the release event instead: a window handler runs right after the view's own,
            // before any pump.
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(240, 200));
            int before = -1;
            bool pendingAfterView = true;
            int countAfterView = -1;
            // Tunnel: runs just BEFORE the view's handler and dirties the view, so there is certainly
            // a request waiting when the release arrives, however the helper pumped earlier.
            window.AddHandler(InputElement.PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) =>
            {
                vm.ZoomStep(1f);
                before = view.RepaintCount;
            }, RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(InputElement.PointerReleasedEvent, (object? _, PointerReleasedEventArgs e) =>
            {
                pendingAfterView = view.RepaintPending;
                countAfterView = view.RepaintCount;
            }, RoutingStrategies.Bubble, handledEventsToo: true);

            window.MouseUp(new Point(240, 200), MouseButton.Left);

            Assert.False(pendingAfterView);
            Assert.Equal(before + 1, countAfterView);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Losing_capture_mid_drag_repaints_synchronously()
    {
        var (window, vm, view) = Render();
        try
        {
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseMove(new Point(240, 200));
            vm.ZoomStep(1f); // pending, un-pumped
            int before = view.RepaintCount;

            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            view.RaiseEvent(new PointerCaptureLostEventArgs(view, pointer));

            Assert.Equal(before + 1, view.RepaintCount);
            Assert.False(view.RepaintPending);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_key_action_repaints_exactly_and_at_once()
    {
        var (window, _, view) = Render();
        try
        {
            view.Focus();
            view.RepaintNow();
            int before = -1;
            bool pendingInsideKey = true;
            int countInsideKey = -1;
            // The tunnel handler dirties the view just before the view's own handler; the bubble one
            // runs right after it and before the helper pumps the dispatcher, so a coalesced post
            // cannot be mistaken for the synchronous repaint.
            window.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) => before = view.RepaintCount,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            window.AddHandler(InputElement.KeyDownEvent, (object? _, KeyEventArgs e) =>
            {
                pendingInsideKey = view.RepaintPending;
                countInsideKey = view.RepaintCount;
            }, RoutingStrategies.Bubble, handledEventsToo: true);

            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

            Assert.False(pendingInsideKey);
            Assert.Equal(before + 1, countInsideKey);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Setting_a_larger_mesh_makes_home_frame_the_new_bounds()
    {
        var (window, vm, view) = Render();
        try
        {
            view.Focus();
            float[] positions = [-40, -40, -3, 40, -40, -3, 40, 40, 3, -40, 40, 3];
            float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
            float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
            var big = new Mesh(positions, normals, uvs, [0u, 1, 2, 0, 2, 3], 2, 2);
            view.Mesh = big;

            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);

            var vp = vm.CameraState.ViewProjection((int)view.Bounds.Width, (int)view.Bounds.Height);
            foreach (var x in new[] { big.Min.X, big.Max.X })
            foreach (var y in new[] { big.Min.Y, big.Max.Y })
            foreach (var z in new[] { big.Min.Z, big.Max.Z })
            {
                var clip = Vector4.Transform(new Vector4(x, y, z, 1f), vp);
                Assert.True(clip.W > 0f);
                Assert.InRange(clip.X / clip.W, -1f, 1f);
                Assert.InRange(clip.Y / clip.W, -1f, 1f);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Home_in_a_tall_window_frames_a_wide_model_using_the_viewport_aspect()
    {
        var (window, vm, view) = Render();
        try
        {
            float[] positions = [-40, -2, 0, 40, -2, 0, 40, 2, 0, -40, 2, 0];
            float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
            float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
            var wide = new Mesh(positions, normals, uvs, [0u, 1, 2, 0, 2, 3], 2, 2);
            view.Mesh = wide;
            window.Width = 200;
            window.Height = 600;
            Dispatcher.UIThread.RunJobs();
            view.Focus();

            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);

            var vp = vm.CameraState.ViewProjection((int)view.Bounds.Width, (int)view.Bounds.Height);
            Assert.True(view.Bounds.Width < view.Bounds.Height);
            foreach (var x in new[] { wide.Min.X, wide.Max.X })
            foreach (var y in new[] { wide.Min.Y, wide.Max.Y })
            {
                var clip = Vector4.Transform(new Vector4(x, y, 0f, 1f), vp);
                Assert.InRange(clip.X / clip.W, -1f, 1f);
            }
        }
        finally { window.Close(); }
    }

    // ---- M4, M7 --------------------------------------------------------------------------------

    [AvaloniaFact]
    public void The_wheel_and_a_press_are_marked_handled_so_a_scrolling_ancestor_stays_put()
    {
        var (window, _, _) = Render();
        try
        {
            bool wheelHandled = false, pressHandled = false;
            // handledEventsToo: this listens on the way up, after the view, and must still hear it.
            window.AddHandler(InputElement.PointerWheelChangedEvent,
                (object? _, PointerWheelEventArgs e) => wheelHandled = e.Handled,
                RoutingStrategies.Bubble, handledEventsToo: true);
            window.AddHandler(InputElement.PointerPressedEvent,
                (object? _, PointerPressedEventArgs e) => pressHandled = e.Handled,
                RoutingStrategies.Bubble, handledEventsToo: true);

            window.MouseWheel(new Point(200, 200), new Vector(0, 1));
            window.MouseDown(new Point(200, 200), MouseButton.Left);
            window.MouseUp(new Point(200, 200), MouseButton.Left);

            Assert.True(wheelHandled);
            Assert.True(pressHandled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_zero_size_control_produces_no_bitmap()
    {
        var vm = new ModelPreviewViewModel(new Vector3(-1, -1, 0), new Vector3(1, 1, 0));
        var view = new ModelPreviewView
        {
            DataContext = vm, Mesh = ModelPreviewGestureTests.TinyQuad(),
            Texture = ModelPreviewGestureTests.TinyTexture(), Width = 0, Height = 0,
        };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            view.RepaintNow();
            Assert.Null(view.Surface.Source);
        }
        finally { window.Close(); }
    }

    // ---- M12: gestures the original suite left out ---------------------------------------------

    [AvaloniaFact]
    public void A_middle_drag_pans_the_camera_target()
    {
        var (window, vm, _) = Render();
        try
        {
            var startTarget = vm.CameraState.Target;
            window.MouseDown(new Point(200, 200), MouseButton.Middle);
            window.MouseMove(new Point(240, 170));
            window.MouseUp(new Point(240, 170), MouseButton.Middle);
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(startTarget, vm.CameraState.Target);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Plus_and_minus_keys_zoom_in_and_out()
    {
        var (window, vm, view) = Render();
        try
        {
            view.Focus();
            float start = vm.CameraState.Distance;

            window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.None);
            float zoomedIn = vm.CameraState.Distance;
            window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.None);

            Assert.True(zoomedIn < start);
            Assert.Equal(start, vm.CameraState.Distance, 3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_home_key_resets_the_camera()
    {
        var (window, vm, view) = Render();
        try
        {
            view.Focus();
            var original = vm.CameraState;
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Assert.NotEqual(original.Yaw, vm.CameraState.Yaw);

            window.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);

            Assert.Equal(original.Yaw, vm.CameraState.Yaw, 4);
            Assert.Equal(original.Distance, vm.CameraState.Distance, 3);
        }
        finally { window.Close(); }
    }

    // ---- M6 ------------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Re_parenting_the_view_does_not_take_focus_again()
    {
        var (window, _, view) = Render();
        try
        {
            var other = new TextBox();
            var host = new StackPanel();
            window.Content = host;
            host.Children.Add(other);
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(other.IsFocused);

            host.Children.Add(view); // a second attach
            Dispatcher.UIThread.RunJobs();

            Assert.True(other.IsFocused);
            Assert.False(view.IsFocused);
        }
        finally { window.Close(); }
    }
}
