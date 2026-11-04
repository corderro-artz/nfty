using Nfty.Core.Diagnostics;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model;

/// <summary>
/// Rises one finished picture into one solid. Every fully opaque pixel becomes a column of voxels
/// spanning z ∈ [−(0.5 + back), 0.5 + front] — the paper plus its two depths — colored with the
/// picture's own pixels. Geometry shared by the writers and the step-6 preview.
/// </summary>
/// <remarks>
/// <para><b>One solid test</b> (mixel's D1): <see cref="Opacity.IsSolid"/>. A partial pixel anywhere
/// is refused FIRST, by name; a picture with nothing solid returns <c>null</c> so a batch can skip
/// and name it (D5). The picture is taken as a live <see cref="Image{TPixel}"/>, never bytes (D6).</para>
///
/// <para><b>The surface is closed and consistently oriented, not a strict 2-manifold.</b> Greedy
/// faces meet per-pixel walls at T-junctions (every T-vertex lies exactly on the longer edge — all
/// coordinates are small integers and halves), and pixels touching only at a corner pinch four faces
/// onto one edge. <c>RiserSolidityTests</c> asserts the true property; do not "fix" it back to
/// "every edge shared by exactly two triangles", which a 2×1 strip already fails.</para>
/// </remarks>
public static class Riser
{
    private static readonly (WallFace Face, int Dx, int Dy)[] Sides =
    [
        (WallFace.Left, -1, 0),
        (WallFace.Right, 1, 0),
        (WallFace.Top, 0, -1),
        (WallFace.Bottom, 0, 1),
    ];

    private readonly record struct Scan(int Solid, int Partial, int FirstX, int FirstY, byte FirstAlpha);

    /// <summary>Builds the mesh for one picture.</summary>
    /// <param name="art">The finished picture. Not modified; the caller keeps ownership.</param>
    /// <param name="depth">Its depth map, the same size, or <c>null</c> for paper. Depth on a pixel
    /// that is not solid is ignored.</param>
    /// <param name="options">Voxel size and pivot. <see cref="RiseOptions.Format"/> is not used here.</param>
    /// <param name="source">How to name the picture in a refusal, e.g. <c>variant 'red' of hat.igt</c>.</param>
    /// <param name="faces">The stack's face owners and wall seams, or <c>null</c> for a single picture
    /// (every wall takes the front color). The seam is clamped to the column.</param>
    /// <returns>The mesh, or <c>null</c> when no pixel is fully opaque. Its UVs address
    /// <see cref="FaceAtlas"/>.</returns>
    /// <exception cref="PartialAlphaException">Some pixel is partly transparent.</exception>
    /// <exception cref="ArgumentException">The depth map or the faces are another size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The voxel size is not positive and finite, or the
    /// pivot is not one of <see cref="Pivot"/>'s values.</exception>
    public static Mesh? TryBuild(Image<Rgba32> art, DepthMap? depth, RiseOptions options, string source = "the art", StackFaces? faces = null)
    {
        ArgumentNullException.ThrowIfNull(art);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (!float.IsFinite(options.VoxelSize) || options.VoxelSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(options), options.VoxelSize,
                "VoxelSize is world units per pixel and must be a positive, finite number.");
        if (!Enum.IsDefined(options.Pivot))
            throw new ArgumentOutOfRangeException(nameof(options), options.Pivot, "Unknown pivot.");
        if (depth is not null && (depth.Width != art.Width || depth.Height != art.Height))
            throw new ArgumentException(
                $"The depth map is {depth.Width}x{depth.Height}, but {source} is {art.Width}x{art.Height}.",
                nameof(depth));

        if (faces is not null && (faces.Width != art.Width || faces.Height != art.Height))
            throw new ArgumentException(
                $"The faces are {faces.Width}x{faces.Height}, but {source} is {art.Width}x{art.Height}.",
                nameof(faces));

        using var _ = Perf.Measure("Riser.TryBuild");

        int w = art.Width, h = art.Height;
        var front = new int[w * h];
        var back = new int[w * h];
        var scan = ScanPixels(art, depth, front, back);

        if (scan.Partial > 0)
            throw new PartialAlphaException(source, scan.FirstX, scan.FirstY, scan.FirstAlpha, scan.Partial);
        if (scan.Solid == 0)
            return null;

        var builder = new MeshBuilder(w, h);
        foreach (var r in GreedyRects.Merge(w, h, front)) builder.FrontFace(r);
        foreach (var r in GreedyRects.Merge(w, h, back)) builder.BackFace(r);
        EmitWalls(builder, w, h, front, back, faces);
        return builder.ToMesh(options);
    }

    /// <summary>One pass: fills the front/back key grids (<see cref="GreedyRects.Unset"/> on every
    /// pixel that is not solid) and counts solid and partial pixels.</summary>
    private static Scan ScanPixels(Image<Rgba32> art, DepthMap? depth, int[] front, int[] back)
    {
        int solid = 0, partial = 0, firstX = -1, firstY = -1;
        byte firstAlpha = 0;
        int w = art.Width;
        art.ProcessPixelRows(rows =>
        {
            for (int y = 0; y < rows.Height; y++)
            {
                Span<Rgba32> row = rows.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    Rgba32 p = row[x];
                    int i = (y * w) + x;
                    if (Opacity.IsPartial(p) && partial++ == 0)
                    {
                        firstX = x;
                        firstY = y;
                        firstAlpha = p.A;
                    }
                    if (Opacity.IsSolid(p))
                    {
                        solid++;
                        front[i] = depth?.Front(x, y) ?? 0;
                        back[i] = depth?.Back(x, y) ?? 0;
                    }
                    else
                    {
                        front[i] = GreedyRects.Unset;
                        back[i] = GreedyRects.Unset;
                    }
                }
            }
        });
        return new Scan(solid, partial, firstX, firstY, firstAlpha);
    }

    /// <summary>Each column's exposed side: its interval minus its neighbor's, up to two segments
    /// per side — the back one first, then the front one. Each segment is then split at the pixel's
    /// seam: below it the wall takes the back owner's color, above it the front owner's.</summary>
    private static void EmitWalls(MeshBuilder builder, int w, int h, int[] front, int[] back, StackFaces? faces)
    {
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = (y * w) + x;
                if (front[i] == GreedyRects.Unset) continue;
                float lo = -(0.5f + back[i]), hi = 0.5f + front[i];
                float seam = lo + (faces is null ? 0 : Math.Clamp(faces.BackColored(x, y), 0, 1 + front[i] + back[i]));

                foreach (var (face, dx, dy) in Sides)
                {
                    int nx = x + dx, ny = y + dy;
                    bool neighbor = nx >= 0 && ny >= 0 && nx < w && ny < h && front[(ny * w) + nx] != GreedyRects.Unset;
                    if (!neighbor)
                    {
                        Split(builder, face, x, y, lo, hi, seam);
                        continue;
                    }
                    int n = (ny * w) + nx;
                    float nlo = -(0.5f + back[n]), nhi = 0.5f + front[n];
                    if (lo < nlo) Split(builder, face, x, y, lo, nlo, seam);
                    if (nhi < hi) Split(builder, face, x, y, nhi, hi, seam);
                }
            }
        }
    }

    /// <summary>One wall segment, cut at the seam: the part below it in the back color, the part
    /// above it in the front color; an empty part is skipped, so a segment on one side of the seam
    /// stays one quad.</summary>
    private static void Split(MeshBuilder builder, WallFace face, int x, int y, float z0, float z1, float seam)
    {
        float backTop = Math.Min(z1, seam);
        if (z0 < backTop) builder.Wall(face, x, y, z0, backTop, backColor: true);
        float frontBottom = Math.Max(z0, seam);
        if (frontBottom < z1) builder.Wall(face, x, y, frontBottom, z1, backColor: false);
    }
}
