using System.Globalization;
using System.Numerics;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>MEASURED, NEVER ASSERTED AS WALL-CLOCK — same discipline as <see cref="RisePerfTests"/>.
/// Establishes a baseline before step 7's Rise page starts calling this every pointer move during a
/// camera drag. Run with
/// <c>dotnet test tests/Nfty.Model.Tests --filter FullyQualifiedName~RasterPerfTests -l "console;verbosity=detailed"</c>.</summary>
public class RasterPerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(64)]
    [InlineData(512)]
    public void Rendering_a_real_risen_mesh_is_measured_at_preview_resolution(int textureSize)
    {
        using var art = Disc(textureSize);
        var depth = Dome(art);
        var mesh = Riser.TryBuild(art, depth, new RiseOptions(), $"{textureSize}px disc")!;
        var camera = Camera.FramedOn(mesh.Min, mesh.Max);
        var target = new RenderTarget(512, 512);

        Perf.Enable();
        try
        {
            // One unmeasured warm-up call (JIT, pool fill), then five measured: the mean is what a
            // drag pays per frame, the cold call is what the first frame pays.
            target.Clear(new Rgba32(0, 0, 0, 0));
            Raster.Render(mesh, art, camera, target);
            Perf.Reset();
            for (int i = 0; i < 5; i++)
            {
                target.Clear(new Rgba32(0, 0, 0, 0));
                Raster.Render(mesh, art, camera, target);
            }

            Assert.Contains("Raster.Render", Perf.Snapshot().Select(e => e.Name));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{textureSize}px source, 512px target: {mesh.TriangleCount:N0} triangles"));
            output.WriteLine(Perf.Report());
        }
        finally
        {
            Perf.Disable();
        }
    }

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
