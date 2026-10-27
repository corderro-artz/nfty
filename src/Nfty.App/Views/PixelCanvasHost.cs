using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>
/// The zoomable pixel canvas both the Ingredient editor and the Rise page draw on: the mapping
/// between canvas pixels and control coordinates, the transform that draws the art there, the pan
/// clamp, the anchored zoom and the lattice backdrop. Each view keeps its own pointer and key wiring
/// and asks this class where things are.
/// </summary>
/// <remarks>
/// <para><b>THE ZOOM EXISTS IN EXACTLY ONE FUNCTION: <see cref="TryGeometry"/>.</b> The pointer
/// mapping, the marquee, the depth labels and the lattice all map through it, so magnification put
/// there moves the pixel a click lands on, the pixel the marquee is drawn around and the square the
/// lattice paints together, by construction - in both views. <see cref="Geometry()"/> is the same
/// answer as a rectangle. The arithmetic itself is <see cref="CanvasMapping"/>'s.</para>
/// <para><b>The pointer talks to the untransformed <c>surface</c>, never the image</b>: Avalonia
/// hit-tests and reports positions through a render transform, so a transform on the image would
/// shrink the clickable region to the middle of the tile. The transform therefore sits on the image
/// (and on any <see cref="Followers"/>), and the lattice is anchored by translating from the surface
/// for the same reason.</para>
/// </remarks>
internal sealed class PixelCanvasHost
{
    private readonly Image _image;
    private readonly Panel _surface;
    private readonly Panel _backdrop;
    private readonly Func<IZoomPan?> _vm;
    private readonly Func<string, IBrush?> _brush;

    // The geometry the backdrop was last built from. A brush is rebuilt only when one of these
    // actually moves: RebuildBackdrop runs off Bounds changes, which fire on every layout pass, and
    // allocating a DrawingBrush per pass is the churn the perf work exists to prevent.
    private (double Scale, double OffX, double OffY, int Step, bool Grid) _backdropFrom = (-1, -1, -1, -1, false);

    /// <summary>Creates the host over a canvas.</summary>
    /// <param name="image">The art image; carries the zoom as a render transform.</param>
    /// <param name="surface">The untransformed panel the pointer talks to.</param>
    /// <param name="backdrop">The panel whose background is the lattice.</param>
    /// <param name="vm">The current zoom state, or null while there is none.</param>
    /// <param name="brush">Resolves a theme brush by key, or null before the control has a theme.</param>
    public PixelCanvasHost(Image image, Panel surface, Panel backdrop, Func<IZoomPan?> vm,
        Func<string, IBrush?> brush)
    {
        _image = image;
        _surface = surface;
        _backdrop = backdrop;
        _vm = vm;
        _brush = brush;
    }

    /// <summary>Other images that move with the art under one transform (the Rise page's dimmed
    /// layers around the target).</summary>
    public IReadOnlyList<Image> Followers { get; init; } = Array.Empty<Image>();

    /// <summary>
    /// The mapping between canvas pixels and control coordinates: the letterbox a
    /// <c>Stretch="Uniform"</c> image leaves inside its box, times the zoom, plus the pan.
    /// </summary>
    /// <param name="scale">Control pixels per canvas pixel.</param>
    /// <param name="offX">Where the art's left edge sits, in the surface's coordinates.</param>
    /// <param name="offY">Where its top edge sits.</param>
    /// <returns>False while the image has no size to map against.</returns>
    public bool TryGeometry(out double scale, out double offX, out double offY)
    {
        scale = offX = offY = 0;
        if (_image.Source is not Bitmap bmp) return false;
        var vm = _vm();
        if (!CanvasMapping.TryFrame(bmp.PixelSize.Width, bmp.PixelSize.Height,
                _surface.Bounds.Width, _surface.Bounds.Height,
                vm?.Zoom ?? 1, vm?.PanX ?? 0, vm?.PanY ?? 0, out var frame)) return false;
        (scale, offX, offY) = (frame.Scale, frame.OffX, frame.OffY);
        return true;
    }

    /// <summary>The drawn art as a rectangle in the surface's coordinates, or empty while there is
    /// no frame.</summary>
    public Rect Geometry()
    {
        if (_image.Source is not Bitmap bmp || !TryGeometry(out var s, out var x, out var y)) return default;
        return new Rect(x, y, bmp.PixelSize.Width * s, bmp.PixelSize.Height * s);
    }

    /// <summary>The frame as the value the pointer mapping takes, or null while there is none.</summary>
    public CanvasFrame? Frame() =>
        TryGeometry(out var s, out var x, out var y) ? new CanvasFrame(s, x, y) : null;

    /// <summary>
    /// Draws the art at the zoom and pan <see cref="TryGeometry"/> reports. Origin CENTER, which is
    /// what makes this one transform: scaling a centered letterbox about its own center leaves it
    /// centered, so the pan is a plain translate on top. Overlays are deliberately NOT transformed -
    /// they draw in control coordinates through the geometry, and scaling them would multiply a
    /// dashed stroke's width by the zoom.
    /// </summary>
    public void ApplyCanvasTransform()
    {
        var vm = _vm();
        double zoom = vm?.Zoom ?? 1;
        double px = vm?.PanX ?? 0, py = vm?.PanY ?? 0;

        Apply(_image);
        foreach (var follower in Followers) Apply(follower);

        void Apply(Image image)
        {
            if (image.RenderTransform is TransformGroup { Children: [ScaleTransform s, TranslateTransform t] })
            {
                s.ScaleX = s.ScaleY = zoom;
                t.X = px; t.Y = py;
                return;
            }

            image.RenderTransformOrigin = RelativePoint.Center;
            image.RenderTransform = new TransformGroup
            {
                Children = { new ScaleTransform(zoom, zoom), new TranslateTransform(px, py) },
            };
        }
    }

    /// <summary>
    /// Keeps the art overlapping the tile it is drawn in. One rule covers both directions: the pan
    /// is measured from centered, so the art stays in contact with the tile exactly while the pan is
    /// no further than half the difference between the tile and the art.
    /// </summary>
    public void ClampPan()
    {
        if (_vm() is not { } vm || _image.Source is not Bitmap bmp) return;
        var (x, y) = CanvasMapping.ClampPan(bmp.PixelSize.Width, bmp.PixelSize.Height,
            _surface.Bounds.Width, _surface.Bounds.Height, vm.Zoom, vm.PanX, vm.PanY);

        // Assigned unconditionally: the generated setter drops an equal value, so this settles in
        // one hop rather than re-entering through the notification it would otherwise raise.
        vm.PanX = x;
        vm.PanY = y;
    }

    /// <summary>
    /// Zooms by a factor, keeping whatever is under the pointer under it. The canvas pixel under the
    /// pointer is the inverse of <see cref="TryGeometry"/>, and the new pan is whatever puts the same
    /// pixel back under the same point at the new scale.
    /// </summary>
    /// <param name="pointer">The point to hold still, in the surface's coordinates.</param>
    /// <param name="factor">How much bigger to draw it.</param>
    public void ZoomAt(Point pointer, double factor)
    {
        if (_vm() is not { } vm || _image.Source is not Bitmap bmp) return;
        if (!TryGeometry(out double scale, out double offX, out double offY)) return;

        double before = vm.Zoom;
        vm.Zoom *= factor;
        double after = vm.Zoom;
        if (after == before) return;                  // already at a limit

        var (panX, panY) = CanvasMapping.PanKeeping(bmp.PixelSize.Width, bmp.PixelSize.Height,
            _surface.Bounds.Width, _surface.Bounds.Height,
            new CanvasFrame(scale, offX, offY), before, after, pointer.X, pointer.Y);
        vm.PanX = panX;
        vm.PanY = panY;
        ClampPan();
    }

    /// <summary>
    /// Paints the backdrop as a lattice of the ART'S OWN PIXELS, or as a flat ground.
    /// </summary>
    /// <remarks>
    /// <para>Both the square size and the PHASE come from the laid-out art: a square is
    /// <c>GridSize</c> canvas pixels times the scale, and the tile starts at the art's corner rather
    /// than the panel's. It maps through <see cref="TryGeometry"/>, so the lattice and the pixel a
    /// click lands on cannot disagree.</para>
    /// <para>Under two device pixels a square is not a square (it averages to a flat tone or aliases
    /// into a moire), so the flat ground is drawn instead. Rebuilt only when something it depends on
    /// MOVES.</para>
    /// </remarks>
    public void RebuildBackdrop()
    {
        var vm = _vm();
        int step = vm?.GridSize ?? 1;
        bool grid = vm?.ShowPixelGrid ?? false;

        if (!grid || !TryGeometry(out var scale, out var offX, out var offY)) { Flatten(step); return; }

        double cell = step * scale;
        if (cell < 2) { Flatten(step); return; }

        // The art's corner in the BACKDROP's coordinates. Translated from the SURFACE rather than
        // from the image: the image carries the zoom as a render transform, and TranslatePoint
        // applies one - going through it would multiply the zoom in twice.
        if (_surface.TranslatePoint(new Point(offX, offY), _backdrop) is not { } art) return;

        var next = (scale, art.X, art.Y, step, true);
        if (_backdropFrom == next && _backdrop.Background is DrawingBrush) return;

        var light = _brush("BgAltBrush");
        var dark = _brush("BgAlt2Brush");
        // Before the control is attached there is no theme to read a brush out of, and caching that
        // failure would leave the backdrop unpainted for the life of the view.
        if (light is null || dark is null) return;
        _backdropFrom = next;

        // THE PHASE IS THE FIX, not the size (the arithmetic is PixelLattice's).
        _backdrop.Background = PixelLattice.Build(cell, art.X, art.Y, light, dark);
    }

    /// <summary>Forgets the cached lattice, so a host handed to a new owner rebuilds from scratch.</summary>
    public void Detach() => _backdropFrom = (-1, -1, -1, -1, false);

    private void Flatten(int step)
    {
        var flat = _brush("BgAltBrush");
        if (flat is null) return;                       // no theme yet; try again after attach
        if (ReferenceEquals(_backdrop.Background, flat)) return;
        _backdropFrom = (0, 0, 0, step, false);
        _backdrop.Background = flat;
    }
}
