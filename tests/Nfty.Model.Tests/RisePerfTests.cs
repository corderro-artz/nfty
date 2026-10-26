using System.Globalization;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// MEASURED, NEVER ASSERTED AS WALL-CLOCK. The spec keeps two questions open until measured: whether
/// a cross-quad vertex dedup is worth adding, and what <c>GreedyRects</c> costs on a large canvas.
/// This runs the real path on 64px and 512px art and prints <c>Perf</c>'s table; the numbers belong
/// in the task report, and a dedup pass is added only if they say the size matters. Run with
/// <c>dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RisePerfTests -l "console;verbosity=detailed"</c>.
/// </summary>
public class RisePerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void Rising_writing_and_combining_are_measured_on_real_sized_art(int size)
    {
        using var art = Disc(size);
        var depth = Dome(art);

        Perf.Enable();
        try
        {
            Perf.Reset();
            var options = new RiseOptions();
            var mesh = Riser.TryBuild(art, depth, options, $"{size}px disc")!;
            var files = ModelWriter.Write(mesh, art, options, "disc");
            var combined = StackDepth.Combine([(art, depth), (art, null)]);
            combined.Dispose();

            var names = Perf.Snapshot().Select(e => e.Name).ToList();
            Assert.Contains("Riser.TryBuild", names);
            Assert.Contains("ModelWriter.Write", names);
            Assert.Contains("StackDepth.Combine", names);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{size}px: {mesh.TriangleCount:N0} triangles, {mesh.VertexCount:N0} vertices, {files[0].Bytes.Length:N0} GLB bytes"));
            output.WriteLine(Perf.Report());
        }
        finally
        {
            Perf.Disable();
        }
    }

    /// <summary>An opaque disc on a clear ground, every pixel its own color.</summary>
    private static Image<Rgba32> Disc(int size)
    {
        var img = new Image<Rgba32>(size, size);
        float c = (size - 1) / 2f, r = (size / 2f) - 1;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
                img[x, y] = d <= r
                    ? new Rgba32((byte)(x * 255 / size), (byte)(y * 255 / size), 128, 255)
                    : new Rgba32(0, 0, 0, 0);
            }
        return img;
    }

    /// <summary>Eight rings rising toward the center, one voxel of back throughout.</summary>
    private static DepthMap Dome(Image<Rgba32> art)
    {
        float c = (art.Width - 1) / 2f, r = art.Width / 2f;
        return Art.Depth(art, (x, y) =>
        {
            float d = MathF.Sqrt(((x - c) * (x - c)) + ((y - c) * (y - c)));
            return (Math.Clamp(8 - (int)(d * 8 / r), 0, 8), 1);
        });
    }
}
