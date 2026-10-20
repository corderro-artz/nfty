using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Nfty.App.ViewModels;
using Nfty.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
// Both aliases are load-bearing, not decoration: SixLabors.ImageSharp declares its own non-generic
// `Image` (the abstract base of `Image<TPixel>`) and its own `Point`, so once both namespaces are in
// scope, the BARE names `Image` and `Point` are genuinely ambiguous (CS0104) — arity alone does not
// save `Image`, since ImageSharp's non-generic `Image` collides with Avalonia's non-generic
// `Avalonia.Controls.Image` at the exact arity this file uses for the control field. The alias only
// binds the arity-0 name; `Image<Rgba32>` (arity 1, used below for Texture) still resolves uniquely
// to ImageSharp's generic type, since Avalonia has no generic Image.
using Image = Avalonia.Controls.Image;
using Point = Avalonia.Point;
// System.Numerics also declares a non-generic static `Vector` class, colliding with Avalonia's
// `Vector` struct (used below for the WriteableBitmap's DPI) — same shape of ambiguity as Image/Point
// above, fixed the same way.
using Vector = Avalonia.Vector;

namespace Nfty.App.Views;

/// <summary>The 3D preview surface: a <see cref="WriteableBitmap"/> painted by <see cref="Raster"/>,
/// driven by pointer and keyboard gestures resolved through <see cref="ModelPreviewViewModel"/>. This
/// control knows nothing about where its <see cref="Mesh"/>/<see cref="Texture"/> come from — no page
/// hosts it yet.</summary>
public sealed partial class ModelPreviewView : UserControl
{
    /// <summary>Backs <see cref="Mesh"/>.</summary>
    public static readonly StyledProperty<Mesh?> MeshProperty =
        AvaloniaProperty.Register<ModelPreviewView, Mesh?>(nameof(Mesh));

    /// <summary>Backs <see cref="Texture"/>.</summary>
    public static readonly StyledProperty<Image<Rgba32>?> TextureProperty =
        AvaloniaProperty.Register<ModelPreviewView, Image<Rgba32>?>(nameof(Texture));

    /// <summary>The mesh to rasterize. Null draws nothing.</summary>
    public Mesh? Mesh
    {
        get => GetValue(MeshProperty);
        set => SetValue(MeshProperty, value);
    }

    /// <summary>The texture <see cref="Mesh"/>'s UVs address. Null draws nothing.</summary>
    public Image<Rgba32>? Texture
    {
        get => GetValue(TextureProperty);
        set => SetValue(TextureProperty, value);
    }

    private readonly Image _surface;
    private Point? _dragOrigin;
    private bool _dragIsPan;

    /// <summary>Loads the view and wires the gestures that drive <see cref="ModelPreviewViewModel"/>.</summary>
    public ModelPreviewView()
    {
        InitializeComponent();
        _surface = this.FindControl<Image>("PART_Surface")!;

        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerCaptureLost;
        PointerWheelChanged += OnPointerWheelChanged;
        // Subscribed through the NON-generic AddHandler overload, with a handler typed to the base
        // RoutedEventArgs rather than the idiomatic `DoubleTapped += (s, TappedEventArgs e) => ...`.
        // Avalonia's generic AddHandler<TEventArgs> wraps every subscription in an adapter that hard-
        // casts the raised args to TEventArgs (TappedEventArgs here) before invoking the handler —
        // fine for a real double-tap, which Avalonia's own gesture recognizer always raises as a
        // genuine TappedEventArgs, but it means a test cannot raise this event with a plain
        // RoutedEventArgs the way TileClickGestureTests does for Button.ClickEvent. This handler
        // needs nothing TappedEventArgs-specific (no pointer, no position), so widening its declared
        // parameter to RoutedEventArgs and forcing the non-generic overload (via the explicit Delegate
        // cast below) keeps both paths — a real double-tap and a synthetic RoutedEventArgs — working
        // through the exact same subscription, verified against the installed Avalonia 12.1.1 package
        // rather than assumed.
        AddHandler(InputElement.DoubleTappedEvent, (Delegate)new EventHandler<RoutedEventArgs>(OnDoubleTapped));
        KeyDown += OnKeyDown;

        this.GetObservable(MeshProperty).Subscribe(new Sub<Mesh?>(_ => Repaint()));
        this.GetObservable(TextureProperty).Subscribe(new Sub<Image<Rgba32>?>(_ => Repaint()));
        this.GetObservable(DataContextProperty).Subscribe(new Sub<object?>(dc =>
        {
            if (dc is ModelPreviewViewModel vm)
                vm.PropertyChanged += (_, _) => Repaint();
            Repaint();
        }));
        // Mesh/Texture/DataContext are typically all set before this control is ever attached to a
        // visual tree (see the object initializer in ModelPreviewGestureTests.Render), when Bounds is
        // still (0,0,0,0) — so the first Repaint() any of the subscriptions above triggers paints at
        // the 1x1 fallback Math.Max(Bounds.*, 1) falls back to, and nothing repaints it again once
        // layout gives the control its real size unless Bounds itself is watched too. A live 3D
        // viewport must also repaint on every ordinary resize, which this buys for free.
        this.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => Repaint()));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Takes initial focus once the view is on screen, so the keyboard path (arrow keys,
    /// +/-, Home) reaches this control rather than silently landing on the window behind it.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed
            && !point.Properties.IsMiddleButtonPressed)
            return;

        _dragOrigin = point.Position;
        _dragIsPan = point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed;
        e.Pointer.Capture(this);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragOrigin is not { } origin || DataContext is not ModelPreviewViewModel vm) return;
        var current = e.GetCurrentPoint(this).Position;
        var delta = new Vector2((float)(current.X - origin.X), (float)(current.Y - origin.Y));
        _dragOrigin = current;

        if (_dragIsPan)
            vm.Pan(delta, (int)Math.Max(Bounds.Height, 1));
        else
            vm.Orbit(delta);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragOrigin = null;
        e.Pointer.Capture(null);
    }

    /// <summary>Treated as abort, the rule every other gesture in this app keeps: a drag that wanders
    /// off the control (or loses capture to something else) stops reporting rather than leaving a
    /// half-applied camera move.</summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _dragOrigin = null;

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        var point = e.GetCurrentPoint(this).Position;
        vm.Zoom((float)e.Delta.Y, new Vector2((float)point.X, (float)point.Y),
            (int)Math.Max(Bounds.Width, 1), (int)Math.Max(Bounds.Height, 1));
    }

    private void OnDoubleTapped(object? sender, RoutedEventArgs e) =>
        (DataContext as ModelPreviewViewModel)?.Reset();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        switch (e.Key)
        {
            case Key.Left: vm.OrbitStep(new Vector2(-1, 0)); e.Handled = true; break;
            case Key.Right: vm.OrbitStep(new Vector2(1, 0)); e.Handled = true; break;
            case Key.Up: vm.OrbitStep(new Vector2(0, -1)); e.Handled = true; break;
            case Key.Down: vm.OrbitStep(new Vector2(0, 1)); e.Handled = true; break;
            case Key.OemPlus or Key.Add: vm.ZoomStep(1f); e.Handled = true; break;
            case Key.OemMinus or Key.Subtract: vm.ZoomStep(-1f); e.Handled = true; break;
            case Key.Home: vm.Reset(); e.Handled = true; break;
        }
    }

    private void Repaint()
    {
        if (Mesh is not { } mesh || Texture is not { } texture || DataContext is not ModelPreviewViewModel vm)
            return;

        int width = (int)Math.Max(Bounds.Width, 1);
        int height = (int)Math.Max(Bounds.Height, 1);
        if (width <= 0 || height <= 0) return;

        var target = new RenderTarget(width, height);
        target.Clear(new Rgba32(0, 0, 0, 0)); // the WriteableBitmap composites over the control's own themed Background
        Raster.Render(mesh, texture, vm.CameraState, target);

        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
            PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var fb = bitmap.Lock())
        {
            unsafe
            {
                var span = new Span<byte>((void*)fb.Address, width * height * 4);
                System.Runtime.InteropServices.MemoryMarshal.AsBytes<Rgba32>(target.Color).CopyTo(span);
            }
        }
        _surface.Source = bitmap;
    }

    /// <summary>Minimal <see cref="IObserver{T}"/> for an Avalonia property subscription — the
    /// framework hands out <c>IObservable</c> and the BCL has no lambda overload for it.</summary>
    private sealed class Sub<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
