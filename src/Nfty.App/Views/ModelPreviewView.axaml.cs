using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private ModelPreviewViewModel? _subscribed;
    private RenderTarget? _target;
    private WriteableBitmap? _bitmap;
    private bool _focusedOnce;

    /// <summary>True while a repaint has been requested (a camera, mesh, texture or bounds change)
    /// but not yet drawn. The handle step 7's 30-per-second budget hangs on, and the one tests use:
    /// <c>Dispatcher.RunJobs</c> is a drain, not a wait, so a test flushes with
    /// <see cref="RepaintNow"/> or asserts this flag directly (the <c>PendingReorder</c> precedent).</summary>
    internal bool RepaintPending { get; private set; }

    /// <summary>How many times a frame has actually been rasterized. Coalescing is only observable
    /// as "N triggers, one repaint", so tests count here.</summary>
    internal int RepaintCount { get; private set; }

    /// <summary>The image control the rasterized bitmap is shown in.</summary>
    internal Image Surface => _surface;

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

        this.GetObservable(MeshProperty).Subscribe(new Sub<Mesh?>(mesh =>
        {
            // The mesh is the one source of truth for the subject's extent: the VM learns it here,
            // keeping the camera where it is (only Reset/Home/double-click reframe).
            if (mesh is not null) (DataContext as ModelPreviewViewModel)?.SetSubject(mesh.Min, mesh.Max);
            MarkDirty();
        }));
        this.GetObservable(TextureProperty).Subscribe(new Sub<Image<Rgba32>?>(_ => MarkDirty()));
        this.GetObservable(DataContextProperty).Subscribe(new Sub<object?>(dc =>
        {
            Subscribe(dc as ModelPreviewViewModel);
            MarkDirty();
        }));
        // Mesh/Texture/DataContext are typically all set before this control is ever attached to a
        // visual tree (see the object initializer in ModelPreviewGestureTests.Render), when Bounds is
        // still (0,0,0,0), and nothing repaints once layout gives the control its real size unless
        // Bounds itself is watched too. A live 3D viewport must also repaint on every ordinary resize,
        // which this buys for free.
        this.GetObservable(BoundsProperty).Subscribe(new Sub<Rect>(_ => MarkDirty()));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>Holds exactly one subscription: the old view model is released before the new one is
    /// taken, so A to null to A cannot leave two handlers rasterizing every camera move twice.</summary>
    private void Subscribe(ModelPreviewViewModel? vm)
    {
        if (ReferenceEquals(_subscribed, vm)) return;
        if (_subscribed is not null) _subscribed.PropertyChanged -= OnViewModelChanged;
        _subscribed = vm;
        if (vm is not null) vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => MarkDirty();

    /// <summary>Takes initial focus the FIRST time the view reaches the screen, so the keyboard path
    /// (arrow keys, +/-, Home) lands on this control rather than silently on the window behind it. Only
    /// the first: a host that re-parents the control (a tile/canvas swap) must not have focus stolen
    /// from whatever the user was working in.</summary>
    /// <param name="e">Forwarded to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(DataContext as ModelPreviewViewModel);
        if (!_focusedOnce)
        {
            _focusedOnce = true;
            Focus();
        }
        MarkDirty();
    }

    /// <summary>Releases the view model subscription, the render buffers and the bitmap.</summary>
    /// <param name="e">Forwarded to the base implementation.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
        _surface.Source = null; // never dispose a bitmap that is still the Source: measure reads it
        _bitmap?.Dispose();
        _bitmap = null;
        _target = null;
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
        e.Handled = true;
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
        bool wasDragging = _dragOrigin is not null;
        _dragOrigin = null;
        e.Pointer.Capture(null);
        if (wasDragging) RepaintNow(); // gesture end: exact, never left to the coalesced post
    }

    /// <summary>Treated as abort, the rule every other gesture in this app keeps: a drag that wanders
    /// off the control (or loses capture to something else) stops reporting rather than leaving a
    /// half-applied camera move. The frame is brought up to date, so no stale one stands.</summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        bool wasDragging = _dragOrigin is not null;
        _dragOrigin = null;
        if (wasDragging) RepaintNow();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        var point = e.GetCurrentPoint(this).Position;
        vm.Zoom((float)e.Delta.Y, new Vector2((float)point.X, (float)point.Y),
            (int)Math.Max(Bounds.Width, 1), (int)Math.Max(Bounds.Height, 1));
        e.Handled = true; // an ancestor ScrollViewer must not scroll as well
    }

    private void OnDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        vm.Reset(Aspect());
        RepaintNow();
    }

    private float Aspect() => Bounds.Width >= 1 && Bounds.Height >= 1 ? (float)(Bounds.Width / Bounds.Height) : 1f;

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ModelPreviewViewModel vm) return;
        switch (e.Key)
        {
            case Key.Left: vm.OrbitStep(new Vector2(-1, 0)); break;
            case Key.Right: vm.OrbitStep(new Vector2(1, 0)); break;
            case Key.Up: vm.OrbitStep(new Vector2(0, -1)); break;
            case Key.Down: vm.OrbitStep(new Vector2(0, 1)); break;
            case Key.OemPlus or Key.Add: vm.ZoomStep(1f); break;
            case Key.OemMinus or Key.Subtract: vm.ZoomStep(-1f); break;
            case Key.Home: vm.Reset(Aspect()); break;
            default: return;
        }
        e.Handled = true;
        RepaintNow(); // a key action is a whole gesture: exact on the spot
    }

    /// <summary>Requests a repaint and posts at most ONE pending job for it; further triggers while
    /// one is pending post nothing. Camera moves during a drag therefore cost one rasterization per
    /// dispatcher turn at Render priority, not one per property change. The job is the 30-per-second
    /// budget's hook for step 7.</summary>
    private void MarkDirty()
    {
        if (RepaintPending) return;
        RepaintPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (RepaintPending) RepaintNow(); // already drawn by a gesture end: nothing left to do
        }, DispatcherPriority.Render);
    }

    /// <summary>Draws the current frame synchronously and clears the pending request. Gesture ends
    /// call this so the final frame is exact; tests call it to flush deterministically.</summary>
    internal void RepaintNow()
    {
        RepaintPending = false;
        // Detached: the buffers were released on detach; drawing now would allocate a bitmap nothing
        // owns. Attaching marks dirty again.
        if (VisualRoot is null) return;
        if (Mesh is not { } mesh || Texture is not { } texture || DataContext is not ModelPreviewViewModel vm)
            return;

        int width = (int)Bounds.Width;
        int height = (int)Bounds.Height;
        if (width < 1 || height < 1) return;

        if (_target is null || _target.Width != width || _target.Height != height)
            _target = new RenderTarget(width, height);
        _target.Clear(new Rgba32(0, 0, 0, 0)); // the WriteableBitmap composites over the control's own themed Background
        Raster.Render(mesh, texture, vm.CameraState, _target);

        var size = new PixelSize(width, height);
        bool reused = _bitmap is not null && _bitmap.PixelSize == size;
        var bitmap = reused
            ? _bitmap!
            : new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);

        using (var fb = bitmap.Lock())
        {
            var source = System.Runtime.InteropServices.MemoryMarshal.AsBytes<Rgba32>(_target.Color);
            int rowBytes = width * 4;
            unsafe
            {
                for (int y = 0; y < height; y++)
                {
                    // RowBytes is the framebuffer's stride, which the API does not promise equals width*4.
                    var row = new Span<byte>((byte*)fb.Address + (long)y * fb.RowBytes, rowBytes);
                    source.Slice(y * rowBytes, rowBytes).CopyTo(row);
                }
            }
        }

        if (!reused)
        {
            var old = _bitmap;
            _bitmap = bitmap;
            _surface.Source = bitmap;
            old?.Dispose(); // only after it stopped being the Source
        }
        else
        {
            // Same Source instance, new pixels: assigning it again is a no-op to the property system,
            // so the image must be told its content changed.
            _surface.InvalidateVisual();
        }
        RepaintCount++;
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
