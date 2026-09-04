using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>
/// The inspector's gestures: wheel-zoom at the cursor, drag-to-pan, and the clamp that keeps the
/// image inside its own viewport.
/// </summary>
/// <remarks>
/// <para>This lives in code-behind because it is arithmetic over laid-out bounds, which a ViewModel
/// cannot see. The ViewModel owns the <em>scale</em> and the <em>offset</em>; this owns what those
/// numbers are allowed to be, because only the view knows how big the viewport turned out.</para>
///
/// <para>"Fit" is 1.0, not a pixel size. The <c>Image</c> is laid out by Avalonia at whatever size
/// the viewport gives it (<c>Stretch</c> defaults to <c>Uniform</c>), so scale 1 already means "the
/// whole asset, as large as it goes". Everything above multiplies that. Doing it the other way —
/// tracking an absolute pixel scale — would need re-deriving on every resize, and would make "Fit"
/// a number that changes meaning.</para>
/// </remarks>
public partial class SetInspectView : UserControl
{
    private Border? _viewport;
    private Image? _canvas;
    private ScaleTransform? _zoom;
    private TranslateTransform? _pan;

    private SetInspectViewModel? _watching;
    private bool _dragging;
    private Point _dragFrom;
    private double _panFromX, _panFromY;

    /// <summary>Creates the view.</summary>
    public SetInspectView()
    {
        InitializeComponent();
        _viewport = this.FindControl<Border>("Viewport");
        _canvas = this.FindControl<Image>("Canvas");
        if (_viewport is null || _canvas is null) return;

        // Scale then translate, about the image's own center — which is where Avalonia puts
        // RenderTransformOrigin by default, and what the Clamp arithmetic below assumes.
        _zoom = new ScaleTransform(1, 1);
        _pan = new TranslateTransform(0, 0);
        _canvas.RenderTransform = new TransformGroup { Children = { _zoom, _pan } };

        _viewport.PointerWheelChanged += OnWheel;
        _viewport.PointerPressed += OnPressed;
        _viewport.PointerMoved += OnMoved;
        _viewport.PointerReleased += OnReleased;
        _viewport.DoubleTapped += OnDoubleTapped;

        // The transforms follow the ViewModel, and the clamp runs on every change -- including the
        // ones the SLIDER makes, which never pass through a gesture. Without this subscription the
        // zoom control moved a number nothing read.
        DataContextChanged += (_, _) =>
        {
            if (_watching is not null) _watching.PropertyChanged -= OnVmChanged;
            _watching = Vm;
            if (_watching is not null) _watching.PropertyChanged += OnVmChanged;
            Sync();
        };
        _viewport.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) Sync();   // a resize can leave the pan out of bounds
        };
        _canvas.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) Sync();   // and so can a differently-shaped asset
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetInspectViewModel.Scale)
            or nameof(SetInspectViewModel.PanX)
            or nameof(SetInspectViewModel.PanY)
            or nameof(SetInspectViewModel.Image)) Sync();
    }

    private SetInspectViewModel? Vm => DataContext as SetInspectViewModel;

    /// <summary>Pushes the ViewModel's scale and offset onto the transforms, clamped.</summary>
    private void Sync()
    {
        if (Vm is not { } vm || _zoom is null || _pan is null) return;

        var s = Math.Clamp(vm.Scale, SetInspectViewModel.MinScale, SetInspectViewModel.MaxScale);
        _zoom.ScaleX = _zoom.ScaleY = s;

        var (x, y) = Clamp(vm.PanX, vm.PanY, s);
        vm.PanX = x;
        vm.PanY = y;
        _pan.X = x;
        _pan.Y = y;
    }

    /// <summary>
    /// The bound on panning: the scaled image's edge may reach the viewport's edge and no further.
    /// </summary>
    /// <remarks>At or below Fit the image is smaller than the viewport in both axes, the slack is
    /// zero, and the offset collapses to centered — which is why a drag at Fit does nothing rather
    /// than being separately disabled.</remarks>
    private (double x, double y) Clamp(double x, double y, double scale)
    {
        if (_viewport is null || _canvas is null) return (0, 0);
        var vp = _viewport.Bounds.Size;
        var img = _canvas.Bounds.Size;
        var slackX = Math.Max(0, (img.Width * scale - vp.Width) / 2);
        var slackY = Math.Max(0, (img.Height * scale - vp.Height) / 2);
        return (Math.Clamp(x, -slackX, slackX), Math.Clamp(y, -slackY, slackY));
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Vm is not { } vm || _viewport is null) return;
        e.Handled = true;

        var before = vm.Scale;
        var after = Math.Clamp(before * (e.Delta.Y > 0 ? 1.2 : 1 / 1.2),
            SetInspectViewModel.MinScale, SetInspectViewModel.MaxScale);
        if (Math.Abs(after - before) < 1e-6) return;

        // Zoom at the cursor, not at the center: the point under the pointer stays under it. Without
        // this, zooming into a corner walks the thing you were looking at off the screen.
        var p = e.GetPosition(_viewport);
        var c = new Point(_viewport.Bounds.Width / 2, _viewport.Bounds.Height / 2);
        var k = after / before;
        vm.PanX = (vm.PanX - (p.X - c.X)) * k + (p.X - c.X);
        vm.PanY = (vm.PanY - (p.Y - c.Y)) * k + (p.Y - c.Y);
        vm.Scale = after;
        Sync();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { CanPan: true } vm || _viewport is null) return;
        _dragging = true;
        _dragFrom = e.GetPosition(_viewport);
        _panFromX = vm.PanX;
        _panFromY = vm.PanY;
        e.Pointer.Capture(_viewport);
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || Vm is not { } vm || _viewport is null) return;
        var p = e.GetPosition(_viewport);
        vm.PanX = _panFromX + (p.X - _dragFrom.X);
        vm.PanY = _panFromY + (p.Y - _dragFrom.Y);
        Sync();
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Double-click toggles Fit and a close-up, the way every image viewer does.</summary>
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is not { } vm) return;
        vm.Scale = vm.Scale > 1.0001 ? 1.0 : 4.0;
        Sync();
    }
}
