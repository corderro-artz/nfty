namespace Nfty.App.Services;

/// <summary>Where art is drawn inside a surface: control pixels per art pixel, and the control
/// coordinates of the art's top-left corner.</summary>
/// <param name="Scale">Control pixels per art pixel, zoom included.</param>
/// <param name="OffX">Where the art's left edge sits, in the surface's own coordinates.</param>
/// <param name="OffY">Where the art's top edge sits.</param>
public readonly record struct CanvasFrame(double Scale, double OffX, double OffY);

/// <summary>
/// The mapping between art pixels and a surface's coordinates: the letterbox a
/// <c>Stretch="Uniform"</c> image leaves inside its box, times the zoom, plus the pan.
/// </summary>
/// <remarks>
/// <para><b>This is the ONLY place the zoom exists.</b> The Ingredient Editor and the Rise page both
/// map the pointer, the marquee and the backdrop lattice through it, so the pixel a click lands on,
/// the pixel an outline is drawn around and the square the lattice paints move together by
/// construction. The arithmetic is the editor's, lifted out unchanged; it is pure so both views can
/// share it and a test can state it without a window.</para>
/// <para>The pan is measured from CENTERED. Scaling a centered letterbox about its own center leaves
/// it centered, so the drawn art is a plain scale-and-translate on top of this frame.</para>
/// </remarks>
public static class CanvasMapping
{
    /// <summary>Fitted: the art exactly as large as the letterbox allows.</summary>
    public const double ZoomMin = 1;

    /// <summary>The deepest zoom, sized for a 512px drawing rather than a sprite.</summary>
    public const double ZoomMax = 16;

    /// <summary>One zoom step, as a factor.</summary>
    public const double ZoomStep = 1.25;

    /// <summary>One keyboard pan step, in control pixels.</summary>
    public const double PanStep = 24;

    /// <summary>Maps art to a surface at a zoom and pan.</summary>
    /// <param name="artW">Art width in pixels.</param>
    /// <param name="artH">Art height in pixels.</param>
    /// <param name="surfaceW">Surface width in control pixels.</param>
    /// <param name="surfaceH">Surface height in control pixels.</param>
    /// <param name="zoom">Magnification over fitted.</param>
    /// <param name="panX">Horizontal pan from centered.</param>
    /// <param name="panY">Vertical pan from centered.</param>
    /// <param name="frame">The mapping, when there is one.</param>
    /// <returns>False while the art or the surface has no size to map against.</returns>
    public static bool TryFrame(int artW, int artH, double surfaceW, double surfaceH,
        double zoom, double panX, double panY, out CanvasFrame frame)
    {
        frame = default;
        if (artW <= 0 || artH <= 0 || surfaceW <= 0 || surfaceH <= 0) return false;
        double scale = Math.Min(surfaceW / artW, surfaceH / artH) * zoom;
        frame = new CanvasFrame(scale,
            (surfaceW - artW * scale) / 2 + panX,
            (surfaceH - artH * scale) / 2 + panY);
        return true;
    }

    /// <summary>Keeps the art overlapping the surface it is drawn in.</summary>
    /// <remarks>One rule covers both directions: the art stays in contact exactly while the pan is no
    /// further than half the difference between the surface and the art. Zoomed in that reads as "the
    /// window stays inside the art"; fitted it collapses to zero.</remarks>
    /// <param name="artW">Art width in pixels.</param>
    /// <param name="artH">Art height in pixels.</param>
    /// <param name="surfaceW">Surface width in control pixels.</param>
    /// <param name="surfaceH">Surface height in control pixels.</param>
    /// <param name="zoom">Magnification over fitted.</param>
    /// <param name="panX">Horizontal pan to clamp.</param>
    /// <param name="panY">Vertical pan to clamp.</param>
    /// <returns>The clamped pan; unchanged while there is nothing to clamp against.</returns>
    public static (double X, double Y) ClampPan(int artW, int artH, double surfaceW, double surfaceH,
        double zoom, double panX, double panY)
    {
        if (artW <= 0 || artH <= 0) return (panX, panY);
        double fit = Math.Min(surfaceW / artW, surfaceH / artH);
        if (!double.IsFinite(fit) || fit <= 0) return (panX, panY);

        double scale = fit * zoom;
        double roomX = Math.Abs(surfaceW - artW * scale) / 2;
        double roomY = Math.Abs(surfaceH - artH * scale) / 2;
        return (Math.Clamp(panX, -roomX, roomX), Math.Clamp(panY, -roomY, roomY));
    }

    /// <summary>The pan that puts the art pixel under a point back under it at a new zoom.</summary>
    /// <remarks>An anchored zoom is the difference between a magnifier and a control you fight. The art
    /// coordinate under the point is the inverse of <see cref="TryFrame"/>; the new pan is whatever
    /// puts the same coordinate back under the same point at the new scale.</remarks>
    /// <param name="artW">Art width in pixels.</param>
    /// <param name="artH">Art height in pixels.</param>
    /// <param name="surfaceW">Surface width in control pixels.</param>
    /// <param name="surfaceH">Surface height in control pixels.</param>
    /// <param name="before">The frame at <paramref name="zoomBefore"/>.</param>
    /// <param name="zoomBefore">The zoom <paramref name="before"/> was taken at.</param>
    /// <param name="zoomAfter">The zoom being moved to.</param>
    /// <param name="atX">The point to hold still, in surface coordinates.</param>
    /// <param name="atY">The point to hold still, in surface coordinates.</param>
    /// <returns>The pan to apply at <paramref name="zoomAfter"/>, before clamping.</returns>
    public static (double X, double Y) PanKeeping(int artW, int artH, double surfaceW, double surfaceH,
        CanvasFrame before, double zoomBefore, double zoomAfter, double atX, double atY)
    {
        double newScale = before.Scale * (zoomAfter / zoomBefore);
        double cx = (atX - before.OffX) / before.Scale, cy = (atY - before.OffY) / before.Scale;
        return (atX - cx * newScale - (surfaceW - artW * newScale) / 2,
                atY - cy * newScale - (surfaceH - artH * newScale) / 2);
    }

    /// <summary>The art pixel a point lands on, clamped onto the art. For gestures, which keep
    /// painting when the pointer wanders off the edge.</summary>
    /// <param name="frame">The mapping.</param>
    /// <param name="x">Surface x.</param>
    /// <param name="y">Surface y.</param>
    /// <param name="artW">Art width in pixels.</param>
    /// <param name="artH">Art height in pixels.</param>
    /// <returns>A pixel inside the art.</returns>
    public static (int X, int Y) PixelAt(CanvasFrame frame, double x, double y, int artW, int artH) =>
        (Math.Clamp((int)((x - frame.OffX) / frame.Scale), 0, artW - 1),
         Math.Clamp((int)((y - frame.OffY) / frame.Scale), 0, artH - 1));

    /// <summary>The art pixel a point is over, or null off the art. For readouts, which must not
    /// name a pixel the pointer is not on.</summary>
    /// <param name="frame">The mapping.</param>
    /// <param name="x">Surface x.</param>
    /// <param name="y">Surface y.</param>
    /// <param name="artW">Art width in pixels.</param>
    /// <param name="artH">Art height in pixels.</param>
    /// <returns>The pixel, or null.</returns>
    public static (int X, int Y)? PixelUnder(CanvasFrame frame, double x, double y, int artW, int artH)
    {
        double px = (x - frame.OffX) / frame.Scale, py = (y - frame.OffY) / frame.Scale;
        // Tested on the double: (int) truncates toward zero, so -0.4 would otherwise read as pixel 0.
        if (!(px >= 0 && px < artW && py >= 0 && py < artH)) return null;
        return ((int)px, (int)py);
    }
}
