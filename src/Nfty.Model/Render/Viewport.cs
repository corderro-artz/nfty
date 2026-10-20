using System.Numerics;

namespace Nfty.Model.Render;

/// <summary>
/// The one mapping between normalized device coordinates and pixels. The rasterizer, the camera's
/// pointer-anchored zoom and its pick-ray unprojection all go through here, so the viewport
/// arithmetic exists exactly once and a change to it (a margin, a flipped axis) cannot reach one
/// consumer and miss another.
/// </summary>
public static class Viewport
{
    /// <summary>NDC (x right, y up, both −1..1) to pixel coordinates (x right, y DOWN, origin at the
    /// top-left corner of the top-left pixel).</summary>
    public static Vector2 NdcToScreen(Vector2 ndc, int width, int height) =>
        new((ndc.X * 0.5f + 0.5f) * width, (1f - (ndc.Y * 0.5f + 0.5f)) * height);

    /// <summary>The exact inverse of <see cref="NdcToScreen"/>.</summary>
    public static Vector2 ScreenToNdc(Vector2 screen, int width, int height) =>
        new(screen.X / width * 2f - 1f, 1f - screen.Y / height * 2f);
}
