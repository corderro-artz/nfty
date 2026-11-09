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

    // Bits identify the original triangle edges on which this vertex lies. Clipping retains that
    // identity so a shortened voxel edge still uses its world line for coverage.
    private readonly record struct ClipVertex(Vector4 Clip, float U, float V, byte Edges = 0);

    /// <summary>Draws every triangle of <paramref name="mesh"/>, sampling <paramref name="texture"/>
    /// nearest-neighbor, into <paramref name="target"/>, as seen by <paramref name="camera"/>. The
    /// projection is built here from <paramref name="target"/>'s own size, so it can never disagree
    /// with the surface it is drawn on. Does not clear <paramref name="target"/> first — the caller
    /// owns the background and calls <see cref="RenderTarget.Clear"/> itself.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="texture">The face atlas the mesh was built against.</param>
    /// <param name="camera">The view to draw it from.</param>
    /// <param name="target">The surface to draw into.</param>
    /// <returns>How many SOURCE triangles (index triples) survived clipping and culling and were
    /// rasterized; one split in two by the near plane counts once.</returns>
    /// <exception cref="ArgumentException"><paramref name="texture"/> is not the size of the art the
    /// mesh was risen from — the same rule <c>ModelWriter</c> applies.</exception>
    public static int Render(Mesh mesh, Image<Rgba32> texture, Camera camera, RenderTarget target)
        => Render(mesh, texture, camera, target, PreviewLighting.Default);

    /// <summary>Draws the same clipped, nearest-neighbor preview as the original overload, with a
    /// preview-only light intensity and color. <see cref="PreviewLighting.Default"/> preserves the
    /// original pixels. Texture alpha, depth, projection and exported model data are unchanged.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="texture">The face atlas the mesh was built against.</param>
    /// <param name="camera">The view to draw it from.</param>
    /// <param name="target">The caller-cleared surface to draw into.</param>
    /// <param name="lighting">Preview light intensity and RGB color.</param>
    /// <returns>The number of source triangles surviving clipping and culling.</returns>
    /// <exception cref="ArgumentException">The texture size disagrees with the mesh.</exception>
    public static int Render(Mesh mesh, Image<Rgba32> texture, Camera camera, RenderTarget target,
        PreviewLighting lighting)
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
        var lightChannels = lighting.Channels;
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
                var a = new ClipVertex(c0, uvs[i0 * 2], uvs[(i0 * 2) + 1], 5);
                var b = new ClipVertex(c1, uvs[i1 * 2], uvs[(i1 * 2) + 1], 3);
                var c = new ClipVertex(c2, uvs[i2 * 2], uvs[(i2 * 2) + 1], 6);

                // Near plane: Matrix4x4.CreatePerspectiveFieldOfView maps it to clip z = 0, and the
                // inside (z >= 0) implies w > 0. Clip BEFORE any divide — a vertex behind the eye
                // flips through the projection and turns its triangle into a huge wrong-facing one.
                int count;
                if (c0.Z >= 0f && c1.Z >= 0f && c2.Z >= 0f)
                {
                    polygon[0] = a;
                    polygon[1] = b;
                    polygon[2] = c;
                    count = 3;
                }
                else if (c0.Z < 0f && c1.Z < 0f && c2.Z < 0f)
                {
                    continue; // wholly behind the near plane
                }
                else
                {
                    count = ClipToNearPlane(a, b, c, polygon);
                }

                // Derive coverage lines and texel bounds only when a projected fan piece survives
                // culling. Dense voxel meshes have many hidden faces; none needs this setup. The
                // equations and inputs stay exactly the same, including for clipped triangles.
                AxisLine line0 = default, line1 = default, line2 = default;
                TextureBounds sampleBounds = default;
                bool prepared = false;

                AxisLine Line(ClipVertex from, ClipVertex to) => (from.Edges & to.Edges) switch
                {
                    1 => line0,
                    2 => line1,
                    4 => line2,
                    _ => default,
                };

                // Face normal: all three vertices of one risen quad's triangle share one normal already
                // (Mesh.cs: a vertex is shared only within its own quad), so any vertex's normal is the
                // face normal — no cross-product needed, and flat shading falls out for free.
                var faceNormal = new Vector3(normals[i0 * 3], normals[(i0 * 3) + 1], normals[(i0 * 3) + 2]);
                var brightness = lightChannels * Brightness(Vector3.Dot(faceNormal, lightDirection));

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

                    if (!prepared)
                    {
                        line0 = AxisLine.For(positions, i0, i1, viewProjection, target);
                        line1 = AxisLine.For(positions, i1, i2, viewProjection, target);
                        line2 = AxisLine.For(positions, i2, i0, viewProjection, target);
                        sampleBounds = TextureBounds.For(a.U, a.V, b.U, b.V, c.U, c.V, texture.Width, texture.Height);
                        prepared = true;
                    }

                    FillTriangle(s0, s1, s2, brightness, texels, texture.Width, texture.Height, target,
                        Line(polygon[0], polygon[k]), Line(polygon[k], polygon[k + 1]), Line(polygon[k + 1], polygon[0]), sampleBounds);
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

    /// <summary>Draws the outline of every visible quad over what
    /// <see cref="Render(Mesh, Image{Rgba32}, Camera, RenderTarget)"/> already put in
    /// <paramref name="target"/>, one pixel wide: the wireframe display. A risen mesh is made of quads
    /// (four vertices, six indices each), so the outlines are the greedy faces' and walls' own edges,
    /// not the triangle diagonals. An outline is drawn only where it lies on the surface the z-buffer
    /// holds (a small relative tolerance), so edges behind the solid stay hidden, and it changes only
    /// a pixel's color, never its alpha or depth. The ink is white on a dark pixel and black on a
    /// light one, so it reads on any texture. A quad with a corner behind the near plane is skipped
    /// whole rather than clipped; the camera never enters the solid's box, so this only shows when
    /// zoomed to the surface.</summary>
    /// <param name="mesh">The solid, already rendered into <paramref name="target"/>.</param>
    /// <param name="camera">The camera that rendered it.</param>
    /// <param name="target">The surface to draw on.</param>
    public static void DrawEdges(Mesh mesh, Camera camera, RenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(target);
        using var _ = Perf.Measure("Raster.DrawEdges");

        var viewProjection = camera.ViewProjection(target.Width, target.Height);
        var positions = mesh.PositionData;
        var indices = mesh.IndexData;
        Span<ScreenVertex> quad = stackalloc ScreenVertex[4];

        for (int t = 0; t + 6 <= indices.Length; t += 6)
        {
            // Quad layout from MeshBuilder: first, first+1, first+2, first, first+2, first+3.
            bool ok = true;
            for (int k = 0; k < 4 && ok; k++)
            {
                int v = (int)indices[t + (k == 3 ? 5 : k)];
                var clip = Vector4.Transform(
                    new Vector4(positions[v * 3], positions[(v * 3) + 1], positions[(v * 3) + 2], 1f), viewProjection);
                ok = clip.Z >= 0f && TryProject(new ClipVertex(clip, 0f, 0f), target, out quad[k]);
            }
            if (!ok) continue;

            float area = ((quad[1].X - quad[0].X) * (quad[2].Y - quad[0].Y))
                         - ((quad[2].X - quad[0].X) * (quad[1].Y - quad[0].Y));
            if (area >= 0f) continue; // back-facing, as in Render

            for (int k = 0; k < 4; k++) DrawLine(quad[k], quad[(k + 1) & 3], target);
        }
    }

    private static void DrawLine(ScreenVertex a, ScreenVertex b, RenderTarget target)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        int steps = (int)MathF.Ceiling(MathF.Max(MathF.Abs(dx), MathF.Abs(dy)));
        if (steps == 0) return;
        float invA = 1f / a.W, invB = 1f / b.W;
        for (int i = 0; i <= steps; i++)
        {
            float f = i / (float)steps;
            int x = (int)MathF.Floor(a.X + (dx * f)), y = (int)MathF.Floor(a.Y + (dy * f));
            if ((uint)x >= (uint)target.Width || (uint)y >= (uint)target.Height) continue;
            int p = (y * target.Width) + x;
            float surface = target.Depth[p];
            if (float.IsPositiveInfinity(surface)) continue;
            float depth = 1f / (invA + ((invB - invA) * f));
            if (depth > surface * 1.004f) continue; // behind the visible surface
            var c = target.Color[p];
            bool dark = ((c.R * 299) + (c.G * 587) + (c.B * 114)) / 1000 < 110;
            byte Mix(byte v) => dark ? (byte)(v + ((255 - v) / 2)) : (byte)(v / 2);
            target.Color[p] = new Rgba32(Mix(c.R), Mix(c.G), Mix(c.B), c.A);
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
                // Always interpolate from the inside endpoint. Adjacent triangles visit their
                // shared edge in opposite directions; Lerp(a,b,t) and Lerp(b,a,1-t) need not round
                // to the same position. This ordering gives both triangles bit-identical vertices.
                var inside = fromIn ? from : to;
                var outside = fromIn ? to : from;
                float t = inside.Clip.Z / (inside.Clip.Z - outside.Clip.Z);
                var position = Vector4.Lerp(inside.Clip, outside.Clip, t);
                position.Z = 0f;
                output[n++] = new ClipVertex(
                    position,
                    inside.U + ((outside.U - inside.U) * t),
                    inside.V + ((outside.V - inside.V) * t),
                    (byte)(from.Edges & to.Edges));
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
        => FillTriangle(v0, v1, v2, new Vector3(brightness), texels, textureWidth, textureHeight, target, default, default, default,
            TextureBounds.For(v0.U, v0.V, v1.U, v1.V, v2.U, v2.V, textureWidth, textureHeight));

    private static void FillTriangle(
        ScreenVertex v0, ScreenVertex v1, ScreenVertex v2,
        Vector3 brightness, ReadOnlySpan<Rgba32> texels, int textureWidth, int textureHeight, RenderTarget target,
        AxisLine line01, AxisLine line12, AxisLine line20, TextureBounds sampleBounds)
    {
        // Orient so the edge functions below are positive inside; swapping two vertices swaps their
        // attributes with them, so interpolation is unaffected.
        float area = Edge(v0, v1, v2.X, v2.Y);
        if (MathF.Abs(area) < 1e-8f) return;
        if (area < 0f)
        {
            (v1, v2) = (v2, v1);
            (line01, line20) = (line20, line01);
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
        var coverage0 = line12.Valid ? new CoverageEdge(line12, v0) : new CoverageEdge(v1, v2);
        var coverage1 = line20.Valid ? new CoverageEdge(line20, v1) : new CoverageEdge(v2, v0);
        var coverage2 = line01.Valid ? new CoverageEdge(line01, v2) : new CoverageEdge(v0, v1);
        bool own0 = coverage0.OwnsBoundary, own1 = coverage1.OwnsBoundary, own2 = coverage2.OwnsBoundary;

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
                // Coverage uses canonical double arithmetic: reversing an edge negates the SAME
                // calculation, rather than recomputing it from the other endpoint. Large clipped
                // triangles otherwise lose pixels through cancellation at their shared diagonal.
                double c0 = coverage0.At(px, py), c1 = coverage1.At(px, py), c2 = coverage2.At(px, py);
                if (c0 < 0 || c1 < 0 || c2 < 0) continue;
                if ((c0 == 0 && !own0) || (c1 == 0 && !own1) || (c2 == 0 && !own2)) continue;

                // Retain the original interpolation arithmetic for ordinary texture/depth parity.
                float f0 = row0 - (d0y * (px - v1.X));
                float f1 = row1 - (d1y * (px - v2.X));
                float f2 = row2 - (d2y * (px - v0.X));

                float w0 = f0 * invArea, w1 = f1 * invArea, w2 = f2 * invArea;
                float invW = (w0 * invW0) + (w1 * invW1) + (w2 * invW2);
                float depth = 1f / invW; // view depth: smaller is nearer

                int pixel = rowStart + x;
                if (depth >= depthBuffer[pixel]) continue;

                float u = ((w0 * uw0) + (w1 * uw1) + (w2 * uw2)) * depth;
                float v = ((w0 * vw0) + (w1 * vw1) + (w2 * vw2)) * depth;
                int tx = Math.Clamp((int)(u * textureWidth), sampleBounds.MinX, sampleBounds.MaxX);
                int ty = Math.Clamp((int)(v * textureHeight), sampleBounds.MinY, sampleBounds.MaxY);
                var texel = texels[(ty * textureWidth) + tx];
                if (texel.A < AlphaCutoff) continue; // cut out: nothing drawn, depth untouched

                depthBuffer[pixel] = depth;
                color[pixel] = new Rgba32(
                    (byte)(texel.R * brightness.X),
                    (byte)(texel.G * brightness.Y),
                    (byte)(texel.B * brightness.Z),
                    texel.A);
            }
        }
    }

    // Twice the signed area of (a, b, c), positive when c lies to the right of a->b on a Y-down screen.
    private static float Edge(ScreenVertex a, ScreenVertex b, float cx, float cy) =>
        ((b.X - a.X) * (cy - a.Y)) - ((b.Y - a.Y) * (cx - a.X));

    private readonly struct CoverageEdge
    {
        private readonly double x, y, dx, dy;
        private readonly bool reversed;
        private readonly AxisLine line;

        public bool OwnsBoundary => line.Valid
            ? IsTopLeft(reversed ? -line.B : line.B, reversed ? line.A : -line.A)
            : IsTopLeft(reversed ? -dx : dx, reversed ? -dy : dy);

        public CoverageEdge(ScreenVertex a, ScreenVertex b)
        {
            reversed = a.X > b.X || (a.X == b.X && a.Y > b.Y);
            if (reversed) (a, b) = (b, a);
            x = a.X;
            y = a.Y;
            dx = (double)b.X - a.X;
            dy = (double)b.Y - a.Y;
            line = default;
        }

        public CoverageEdge(AxisLine line, ScreenVertex inside)
        {
            this.line = line;
            reversed = line.At(inside.X, inside.Y) < 0;
            x = y = dx = dy = 0;
        }

        public double At(float px, float py)
        {
            double value = line.Valid ? line.At(px, py) : (dx * (py - y)) - (dy * (px - x));
            return reversed ? -value : value;
        }
    }

    // A voxel edge's projected line depends only on its world axis and the other two coordinates,
    // never on its endpoints. Greedy rectangles can meet a long edge with two short edges: deriving
    // all three equations from independently rounded screen endpoints leaves T-junction cracks.
    // Homogeneous projection of one canonical point and the axis direction makes those equations
    // identical, including when near clipping shortens the edge. This changes coverage only.
    private readonly record struct AxisLine(double A, double B, double C)
    {
        public bool Valid => A != 0 || B != 0;

        public double At(double x, double y) => (A * x) + (B * y) + C;

        public static AxisLine For(ReadOnlySpan<float> positions, int from, int to, Matrix4x4 vp, RenderTarget target)
        {
            var a = new Vector3(positions[from * 3], positions[(from * 3) + 1], positions[(from * 3) + 2]);
            var b = new Vector3(positions[to * 3], positions[(to * 3) + 1], positions[(to * 3) + 2]);
            int axis = -1;
            for (int i = 0; i < 3; i++)
            {
                if (a[i] == b[i]) continue;
                if (axis >= 0) return default; // a face/fan diagonal, rather than a voxel edge
                axis = i;
            }
            if (axis < 0) return default;
            a[axis] = 0;

            double px = ((double)a.X * vp.M11) + ((double)a.Y * vp.M21) + ((double)a.Z * vp.M31) + vp.M41;
            double py = ((double)a.X * vp.M12) + ((double)a.Y * vp.M22) + ((double)a.Z * vp.M32) + vp.M42;
            double pw = ((double)a.X * vp.M14) + ((double)a.Y * vp.M24) + ((double)a.Z * vp.M34) + vp.M44;
            (double dx, double dy, double dw) = axis switch
            {
                0 => (vp.M11, vp.M12, vp.M14),
                1 => (vp.M21, vp.M22, vp.M24),
                _ => (vp.M31, vp.M32, vp.M34),
            };
            double lx = (py * dw) - (pw * dy);
            double ly = (pw * dx) - (px * dw);
            double lw = (px * dy) - (py * dx);
            return new AxisLine(2 * lx / target.Width, -2 * ly / target.Height, -lx + ly + lw);
        }
    }

    // NFTY face UV rectangles address half-open texel regions. At a greedy boundary, interpolation
    // can round an interior coordinate to the exclusive maximum and sample the transparent neighbor.
    // Keep nearest-neighbor samples inside the original triangle's UV range, before near clipping.
    // A constant coordinate addresses one texel (as wall strips do), rather than an empty interval.
    private readonly record struct TextureBounds(int MinX, int MaxX, int MinY, int MaxY)
    {
        public static TextureBounds For(float u0, float v0, float u1, float v1, float u2, float v2, int width, int height)
        {
            static (int Min, int Max) Range(float a, float b, float c, int size)
            {
                float min = MathF.Min(a, MathF.Min(b, c)), max = MathF.Max(a, MathF.Max(b, c));
                if (min == max)
                {
                    int texel = (int)Math.Clamp((double)min * size, 0, size - 1);
                    return (texel, texel);
                }
                int first = (int)Math.Clamp(Math.Floor(Boundary(min, size)), 0, size - 1);
                int last = (int)Math.Clamp(Math.Ceiling(Boundary(max, size)) - 1, first, size - 1);
                return (first, last);
            }

            static double Boundary(float uv, int size)
            {
                // MeshBuilder writes integer/size in float. Multiplying back can land on either
                // side of that integer (7/13 and 7/23 do). Recover it only on an exact re-encoding
                // match, without an epsilon; non-grid limits retain their exact float value.
                float integer = MathF.Round(uv * size);
                return uv == integer / size ? integer : (double)uv * size;
            }
            var x = Range(u0, u1, u2, width);
            var y = Range(v0, v1, v2, height);
            return new TextureBounds(x.Min, x.Max, y.Min, y.Max);
        }
    }

    // Top edge: horizontal, running left to right. Left edge: running upward.
    private static bool IsTopLeft(double dx, double dy) => (dy == 0 && dx > 0) || dy < 0;
}
