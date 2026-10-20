using System.Buffers;
using System.Numerics;
using Nfty.Core.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>
/// The software rasterizer: transforms a <see cref="Mesh"/> by a <see cref="Camera"/>'s
/// view-projection, clips against the near plane, culls back faces, resolves visibility with a
/// z-buffer, samples the texture with nearest-neighbor filtering, and shades with flat, quantized,
/// camera-relative lighting — the whole rendering pipeline the spec lists
/// (see <c>docs/superpowers/specs/2026-09-22-model-preview-design.md</c>, "Rendering"). Pure CPU
/// pixel arithmetic: it renders identically headless and on screen, which is the entire reason this
/// exists instead of a GPU control (`Avalonia.Headless` has no GL context).
/// </summary>
/// <remarks>Single-threaded by design: the z-buffer is shared state, so the pixel loop is not a
/// <c>ParallelWork</c> candidate. Allocates nothing per call — the transformed vertices live in a
/// pooled array and the texels are read in place (or from a pooled copy).</remarks>
public static class Raster
{
    private const int ShadeLevels = 4;
    private const float Ambient = 0.35f;

    // Never divide by a clip w at or below this: after near-plane clipping every w is at least the
    // near distance, so this is a floor for degenerate input, not a working value.
    private const float MinW = 1e-6f;

    // glTF MASK at its default cutoff of 0.5: the exporter drops alpha below 128, so the preview does.
    private const byte AlphaCutoff = 128;

    /// <summary>A vertex already in pixel space: where it lands, its clip w (view depth), and the
    /// texture coordinate it carries.</summary>
    internal readonly record struct ScreenVertex(float X, float Y, float W, float U, float V);

    private readonly record struct ClipVertex(Vector4 Clip, float U, float V);

    /// <summary>Draws every triangle of <paramref name="mesh"/>, sampling <paramref name="texture"/>
    /// nearest-neighbor, into <paramref name="target"/>, as seen by <paramref name="camera"/>. The
    /// projection is built here from <paramref name="target"/>'s own size, so it can never disagree
    /// with the surface it is drawn on. Does not clear <paramref name="target"/> first — the caller
    /// owns the background and calls <see cref="RenderTarget.Clear"/> itself.</summary>
    /// <returns>How many SOURCE triangles (index triples) survived clipping and culling and were
    /// rasterized; one split in two by the near plane counts once.</returns>
    /// <exception cref="ArgumentException"><paramref name="texture"/> is not the size of the art the
    /// mesh was risen from — the same rule <c>ModelWriter</c> applies.</exception>
    public static int Render(Mesh mesh, Image<Rgba32> texture, Camera camera, RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(target);
        if (texture.Width != mesh.TextureWidth || texture.Height != mesh.TextureHeight)
            throw new ArgumentException(
                $"The mesh was risen from {mesh.TextureWidth}x{mesh.TextureHeight} art; this texture is "
                + $"{texture.Width}x{texture.Height}.", nameof(texture));

        using var _ = Perf.Measure("Raster.Render");

        var viewProjection = camera.ViewProjection(target.Width, target.Height);
        var lightDirection = camera.LightDirection;
        var positions = mesh.PositionData;
        var normals = mesh.NormalData;
        var uvs = mesh.TexCoordData;
        var indices = mesh.IndexData;

        // Every vertex goes to clip space exactly once; triangles then index into this.
        int vertexCount = mesh.VertexCount;
        var clip = ArrayPool<Vector4>.Shared.Rent(vertexCount);
        Rgba32[]? texelCopy = null;
        try
        {
            for (int v = 0; v < vertexCount; v++)
                clip[v] = Vector4.Transform(
                    new Vector4(positions[v * 3], positions[(v * 3) + 1], positions[(v * 3) + 2], 1f),
                    viewProjection);

            ReadOnlySpan<Rgba32> texels;
            if (texture.DangerousTryGetSinglePixelMemory(out var memory))
            {
                texels = memory.Span;
            }
            else
            {
                texelCopy = ArrayPool<Rgba32>.Shared.Rent(texture.Width * texture.Height);
                texture.CopyPixelDataTo(texelCopy);
                texels = texelCopy;
            }

            Span<ClipVertex> polygon = stackalloc ClipVertex[4];
            int drawn = 0;

            for (int t = 0; t < indices.Length; t += 3)
            {
                int i0 = (int)indices[t], i1 = (int)indices[t + 1], i2 = (int)indices[t + 2];
                var c0 = clip[i0];
                var c1 = clip[i1];
                var c2 = clip[i2];

                // Near plane: Matrix4x4.CreatePerspectiveFieldOfView maps it to clip z = 0, and the
                // inside (z >= 0) implies w > 0. Clip BEFORE any divide — a vertex behind the eye
                // flips through the projection and turns its triangle into a huge wrong-facing one.
                int count;
                if (c0.Z >= 0f && c1.Z >= 0f && c2.Z >= 0f)
                {
                    polygon[0] = new ClipVertex(c0, uvs[i0 * 2], uvs[(i0 * 2) + 1]);
                    polygon[1] = new ClipVertex(c1, uvs[i1 * 2], uvs[(i1 * 2) + 1]);
                    polygon[2] = new ClipVertex(c2, uvs[i2 * 2], uvs[(i2 * 2) + 1]);
                    count = 3;
                }
                else if (c0.Z < 0f && c1.Z < 0f && c2.Z < 0f)
                {
                    continue; // wholly behind the near plane
                }
                else
                {
                    count = ClipToNearPlane(
                        new ClipVertex(c0, uvs[i0 * 2], uvs[(i0 * 2) + 1]),
                        new ClipVertex(c1, uvs[i1 * 2], uvs[(i1 * 2) + 1]),
                        new ClipVertex(c2, uvs[i2 * 2], uvs[(i2 * 2) + 1]),
                        polygon);
                }

                // Face normal: all three vertices of one risen quad's triangle share one normal already
                // (Mesh.cs: a vertex is shared only within its own quad), so any vertex's normal is the
                // face normal — no cross-product needed, and flat shading falls out for free.
                var faceNormal = new Vector3(normals[i0 * 3], normals[(i0 * 3) + 1], normals[(i0 * 3) + 2]);
                float brightness = Brightness(Vector3.Dot(faceNormal, lightDirection));

                // The clipped polygon is convex and keeps the triangle's winding: fan it, cull each
                // piece exactly as an unclipped triangle would be.
                bool any = false;
                for (int k = 1; k + 1 < count; k++)
                {
                    if (!TryProject(polygon[0], target, out var s0)
                        || !TryProject(polygon[k], target, out var s1)
                        || !TryProject(polygon[k + 1], target, out var s2))
                        continue;

                    // Screen space is Y-down; the mesh's indices are CCW seen from outside in world
                    // space (Mesh.cs's own documented rule). This signed area's sign is the empirical
                    // link between those two facts — pinned by RasterTests (a directly-facing quad and
                    // an oblique cube) and never touched again without re-running them. A CCW-in-world,
                    // front-facing triangle comes out NEGATIVE by this formula once Y flips for screen
                    // space, so positive (or zero, degenerate) is culled.
                    float signedArea = ((s1.X - s0.X) * (s2.Y - s0.Y)) - ((s2.X - s0.X) * (s1.Y - s0.Y));
                    if (signedArea >= 0f)
                        continue; // back-facing or degenerate

                    FillTriangle(s0, s1, s2, brightness, texels, texture.Width, texture.Height, target);
                    any = true;
                }
                if (any) drawn++;
            }

            return drawn;
        }
        finally
        {
            ArrayPool<Vector4>.Shared.Return(clip);
            if (texelCopy is not null) ArrayPool<Rgba32>.Shared.Return(texelCopy);
        }
    }

    /// <summary>Flat, quantized head-light shading: the lit fraction snaps DOWN to one of
    /// <see cref="ShadeLevels"/> steps, then maps linearly from <see cref="Ambient"/> up to exactly
    /// 1 — the top step is full brightness, so a face the light points at shows its texel untouched.</summary>
    private static float Brightness(float dotWithLight)
    {
        float lit = Math.Clamp(dotWithLight, 0f, 1f);
        int level = Math.Clamp((int)(lit * ShadeLevels), 0, ShadeLevels - 1);
        float t = level / (float)(ShadeLevels - 1);
        return (Ambient * (1f - t)) + t; // exactly Ambient at t = 0 and exactly 1 at t = 1
    }

    /// <summary>Sutherland–Hodgman against clip z &gt;= 0 on one triangle, interpolating clip position
    /// and UV linearly in clip space (correct before the divide). Writes 3 or 4 vertices to
    /// <paramref name="output"/> (1 outside vertex leaves a quad, 2 outside leave a triangle).</summary>
    private static int ClipToNearPlane(ClipVertex a, ClipVertex b, ClipVertex c, Span<ClipVertex> output)
    {
        int n = 0;
        Edge(a, b, output, ref n);
        Edge(b, c, output, ref n);
        Edge(c, a, output, ref n);
        return n;

        static void Edge(ClipVertex from, ClipVertex to, Span<ClipVertex> output, ref int n)
        {
            bool fromIn = from.Clip.Z >= 0f, toIn = to.Clip.Z >= 0f;
            if (fromIn) output[n++] = from;
            if (fromIn != toIn)
            {
                float t = from.Clip.Z / (from.Clip.Z - to.Clip.Z);
                output[n++] = new ClipVertex(
                    Vector4.Lerp(from.Clip, to.Clip, t),
                    from.U + ((to.U - from.U) * t),
                    from.V + ((to.V - from.V) * t));
            }
        }
    }

    private static bool TryProject(ClipVertex v, RenderTarget target, out ScreenVertex screen)
    {
        float w = v.Clip.W;
        if (w <= MinW)
        {
            screen = default;
            return false;
        }
        var s = Viewport.NdcToScreen(new Vector2(v.Clip.X / w, v.Clip.Y / w), target.Width, target.Height);
        screen = new ScreenVertex(s.X, s.Y, w, v.U, v.V);
        return true;
    }

    /// <summary>Rasterizes one screen-space triangle. Pixel centers are sampled; a center exactly on an
    /// edge belongs to the triangle for which that is a top or left edge (the standard top-left rule),
    /// so two triangles sharing an edge draw each such pixel exactly once — no pinholes, no
    /// double-blends. Either winding is accepted.</summary>
    internal static void FillTriangle(
        ScreenVertex v0, ScreenVertex v1, ScreenVertex v2,
        float brightness, ReadOnlySpan<Rgba32> texels, int textureWidth, int textureHeight, RenderTarget target)
    {
        // Orient so the edge functions below are positive inside; swapping two vertices swaps their
        // attributes with them, so interpolation is unaffected.
        float area = Edge(v0, v1, v2.X, v2.Y);
        if (MathF.Abs(area) < 1e-8f) return;
        if (area < 0f)
        {
            (v1, v2) = (v2, v1);
            area = -area;
        }

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(v0.X, MathF.Min(v1.X, v2.X))));
        int maxX = Math.Min(target.Width - 1, (int)MathF.Ceiling(MathF.Max(v0.X, MathF.Max(v1.X, v2.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(v0.Y, MathF.Min(v1.Y, v2.Y))));
        int maxY = Math.Min(target.Height - 1, (int)MathF.Ceiling(MathF.Max(v0.Y, MathF.Max(v1.Y, v2.Y))));

        // Edge i is opposite vertex i: e0 = v1->v2, e1 = v2->v0, e2 = v0->v1. In this orientation
        // (clockwise on a Y-down screen) the interior is to the right of each directed edge, so a
        // top edge runs left-to-right and a left edge runs upward.
        float d0x = v2.X - v1.X, d0y = v2.Y - v1.Y;
        float d1x = v0.X - v2.X, d1y = v0.Y - v2.Y;
        float d2x = v1.X - v0.X, d2y = v1.Y - v0.Y;
        bool own0 = IsTopLeft(d0x, d0y), own1 = IsTopLeft(d1x, d1y), own2 = IsTopLeft(d2x, d2y);

        // Perspective-correct interpolation: barycentric weights on 1/w, not on screen-space
        // position directly, or texturing would warp on any triangle not parallel to the screen.
        float invW0 = 1f / v0.W, invW1 = 1f / v1.W, invW2 = 1f / v2.W;
        float uw0 = v0.U * invW0, uw1 = v1.U * invW1, uw2 = v2.U * invW2;
        float vw0 = v0.V * invW0, vw1 = v1.V * invW1, vw2 = v2.V * invW2;
        float invArea = 1f / area;

        var color = target.Color;
        var depthBuffer = target.Depth;

        for (int y = minY; y <= maxY; y++)
        {
            float py = y + 0.5f;
            // The y-dependent half of each edge function, once per row.
            float row0 = d0x * (py - v1.Y), row1 = d1x * (py - v2.Y), row2 = d2x * (py - v0.Y);
            int rowStart = y * target.Width;

            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float f0 = row0 - (d0y * (px - v1.X));
                float f1 = row1 - (d1y * (px - v2.X));
                float f2 = row2 - (d2y * (px - v0.X));
                if (f0 < 0f || f1 < 0f || f2 < 0f) continue;
                if ((f0 == 0f && !own0) || (f1 == 0f && !own1) || (f2 == 0f && !own2)) continue;

                float w0 = f0 * invArea, w1 = f1 * invArea, w2 = f2 * invArea;
                float invW = (w0 * invW0) + (w1 * invW1) + (w2 * invW2);
                float depth = 1f / invW; // view depth: smaller is nearer

                int pixel = rowStart + x;
                if (depth >= depthBuffer[pixel]) continue;

                float u = ((w0 * uw0) + (w1 * uw1) + (w2 * uw2)) * depth;
                float v = ((w0 * vw0) + (w1 * vw1) + (w2 * vw2)) * depth;
                int tx = Math.Clamp((int)(u * textureWidth), 0, textureWidth - 1);
                int ty = Math.Clamp((int)(v * textureHeight), 0, textureHeight - 1);
                var texel = texels[(ty * textureWidth) + tx];
                if (texel.A < AlphaCutoff) continue; // cut out: nothing drawn, depth untouched

                depthBuffer[pixel] = depth;
                color[pixel] = new Rgba32(
                    (byte)(texel.R * brightness),
                    (byte)(texel.G * brightness),
                    (byte)(texel.B * brightness),
                    texel.A);
            }
        }
    }

    // Twice the signed area of (a, b, c), positive when c lies to the right of a->b on a Y-down screen.
    private static float Edge(ScreenVertex a, ScreenVertex b, float cx, float cy) =>
        ((b.X - a.X) * (cy - a.Y)) - ((b.Y - a.Y) * (cx - a.X));

    // Top edge: horizontal, running left to right. Left edge: running upward.
    private static bool IsTopLeft(float dx, float dy) => (dy == 0f && dx > 0f) || dy < 0f;
}
