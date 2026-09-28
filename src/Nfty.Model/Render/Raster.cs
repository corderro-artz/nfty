using System.Numerics;
using Nfty.Core.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Render;

/// <summary>
/// The software rasterizer: transforms a <see cref="Mesh"/> by a view-projection matrix, culls
/// back faces, resolves visibility with a z-buffer, samples the texture with nearest-neighbour
/// filtering, and shades with flat, quantized lighting — the whole rendering pipeline the spec lists
/// (see <c>docs/superpowers/specs/2026-09-22-model-preview-design.md</c>, "Rendering"). Pure CPU
/// pixel arithmetic: it renders identically headless and on screen, which is the entire reason this
/// exists instead of a GPU control (`Avalonia.Headless` has no GL context).
/// </summary>
public static class Raster
{
    private const int ShadeLevels = 4;
    private const float Ambient = 0.35f;
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(0.4f, 1f, 0.6f));

    /// <summary>Draws every triangle of <paramref name="mesh"/>, sampling <paramref name="texture"/>
    /// nearest-neighbour, into <paramref name="target"/>. Does not clear <paramref name="target"/>
    /// first — the caller owns the background and calls <see cref="RenderTarget.Clear"/> itself.</summary>
    public static void Render(Mesh mesh, Image<Rgba32> texture, Matrix4x4 viewProjection, RenderTarget target)
    {
        using var _ = Perf.Measure("Raster.Render");

        var indices = mesh.Indices;
        var positions = mesh.Positions;
        var normals = mesh.Normals;
        var uvs = mesh.TexCoords;

        for (int t = 0; t < indices.Count; t += 3)
        {
            uint i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
            var (clip0, screen0) = ToScreen(positions, i0, viewProjection, target.Width, target.Height);
            var (clip1, screen1) = ToScreen(positions, i1, viewProjection, target.Width, target.Height);
            var (clip2, screen2) = ToScreen(positions, i2, viewProjection, target.Width, target.Height);

            // Screen space is Y-down; the mesh's indices are CCW seen from outside in world space
            // (Mesh.cs's own documented rule). This signed area's sign is the empirical link between
            // those two facts — pinned by RasterTests (a directly-facing quad and an oblique cube)
            // and never touched again without re-running them. A CCW-in-world, front-facing triangle
            // comes out NEGATIVE by this formula once Y flips for screen space: this project's
            // CreateLookAt/CreatePerspectiveFieldOfView convention plus the Y-flip together reverse
            // the apparent 2D winding, so the front-facing case is the negative one, and positive (or
            // zero, degenerate) is culled.
            float signedArea = (screen1.X - screen0.X) * (screen2.Y - screen0.Y)
                              - (screen2.X - screen0.X) * (screen1.Y - screen0.Y);
            if (signedArea >= 0f)
                continue; // back-facing or degenerate

            // Face normal: all three vertices of one risen quad's triangle share one normal already
            // (Mesh.cs: a vertex is shared only within its own quad), so any vertex's normal is the
            // face normal — no cross-product needed, and flat shading falls out for free.
            var faceNormal = new Vector3(normals[(int)i0 * 3], normals[(int)i0 * 3 + 1], normals[(int)i0 * 3 + 2]);
            float lit = Math.Clamp(Vector3.Dot(faceNormal, LightDirection), 0f, 1f);
            int level = Math.Clamp((int)(lit * ShadeLevels), 0, ShadeLevels - 1);
            float brightness = Ambient + (1f - Ambient) * (level / (float)(ShadeLevels - 1));

            RasterizeTriangle(
                (screen0, clip0.W, uvs[(int)i0 * 2], uvs[(int)i0 * 2 + 1]),
                (screen1, clip1.W, uvs[(int)i1 * 2], uvs[(int)i1 * 2 + 1]),
                (screen2, clip2.W, uvs[(int)i2 * 2], uvs[(int)i2 * 2 + 1]),
                brightness, texture, target);
        }
    }

    private static (Vector4 Clip, Vector2 Screen) ToScreen(
        IReadOnlyList<float> positions, uint index, Matrix4x4 viewProjection, int width, int height)
    {
        var world = new Vector3(positions[(int)index * 3], positions[(int)index * 3 + 1], positions[(int)index * 3 + 2]);
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        var ndc = new Vector2(clip.X / clip.W, clip.Y / clip.W);
        var screen = new Vector2((ndc.X * 0.5f + 0.5f) * width, (1f - (ndc.Y * 0.5f + 0.5f)) * height);
        return (clip, screen);
    }

    private static void RasterizeTriangle(
        (Vector2 Screen, float W, float U, float V) v0,
        (Vector2 Screen, float W, float U, float V) v1,
        (Vector2 Screen, float W, float U, float V) v2,
        float brightness, Image<Rgba32> texture, RenderTarget target)
    {
        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(v0.Screen.X, MathF.Min(v1.Screen.X, v2.Screen.X))));
        int maxX = Math.Min(target.Width - 1, (int)MathF.Ceiling(MathF.Max(v0.Screen.X, MathF.Max(v1.Screen.X, v2.Screen.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(v0.Screen.Y, MathF.Min(v1.Screen.Y, v2.Screen.Y))));
        int maxY = Math.Min(target.Height - 1, (int)MathF.Ceiling(MathF.Max(v0.Screen.Y, MathF.Max(v1.Screen.Y, v2.Screen.Y))));

        float area = EdgeFunction(v0.Screen, v1.Screen, v2.Screen);
        if (MathF.Abs(area) < 1e-8f) return;

        // Perspective-correct interpolation: barycentric weights on 1/w, not on screen-space
        // position directly, or texturing would warp on any triangle not exactly parallel to the
        // screen.
        float invW0 = 1f / v0.W, invW1 = 1f / v1.W, invW2 = 1f / v2.W;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float w0 = EdgeFunction(v1.Screen, v2.Screen, p) / area;
                float w1 = EdgeFunction(v2.Screen, v0.Screen, p) / area;
                float w2 = EdgeFunction(v0.Screen, v1.Screen, p) / area;
                if (w0 < 0f || w1 < 0f || w2 < 0f) continue;

                float invW = w0 * invW0 + w1 * invW1 + w2 * invW2;
                float depth = 1f / invW; // linear-in-view-space depth proxy: smaller is nearer

                int pixel = y * target.Width + x;
                if (depth >= target.Depth[pixel]) continue;

                float u = (w0 * v0.U * invW0 + w1 * v1.U * invW1 + w2 * v2.U * invW2) / invW;
                float v = (w0 * v0.V * invW0 + w1 * v1.V * invW1 + w2 * v2.V * invW2) / invW;
                int tx = Math.Clamp((int)(u * texture.Width), 0, texture.Width - 1);
                int ty = Math.Clamp((int)(v * texture.Height), 0, texture.Height - 1);
                var texel = texture[tx, ty];
                if (texel.A == 0) continue; // transparent texel: nothing drawn, depth untouched

                target.Depth[pixel] = depth;
                target.Color[pixel] = new Rgba32(
                    (byte)(texel.R * brightness),
                    (byte)(texel.G * brightness),
                    (byte)(texel.B * brightness),
                    texel.A);
            }
        }
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 c) =>
        (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
}
