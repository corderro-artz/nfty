using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Nfty.Core.Diagnostics;
using Nfty.Core.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>Actual orbit frames, measured without wall-clock assertions. Pixel digests let a
/// before/after performance investigation check every sampled frame without committing goldens.</summary>
public class PreviewOrbitPerfTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(64, 256)]
    [InlineData(512, 512)]
    [InlineData(512, 1024)]
    public void Orbiting_a_risen_disc_reports_frame_cost_and_pixels(int sourceSize, int targetSize)
    {
        using var art = new Image<Rgba32>(sourceSize, sourceSize);
        float center = (sourceSize - 1) / 2f, radius = (sourceSize / 2f) - 1;
        for (int y = 0; y < sourceSize; y++)
        for (int x = 0; x < sourceSize; x++)
            if (((x - center) * (x - center)) + ((y - center) * (y - center)) <= radius * radius)
                art[x, y] = new Rgba32((byte)(x * 255 / sourceSize), (byte)(y * 255 / sourceSize), 128, 255);
        var depth = Art.Depth(art, (x, y) =>
        {
            float distance = MathF.Sqrt(((x - center) * (x - center)) + ((y - center) * (y - center)));
            return (Math.Clamp(8 - (int)(distance * 8 / radius), 0, 8), 1);
        });
        var mesh = Riser.TryBuild(art, depth, new RiseOptions())!;
        using var atlas = FaceAtlas.Build(art);
        var camera = Camera.FramedOn(mesh.Min, mesh.Max);
        var target = new RenderTarget(targetSize, targetSize);
        for (int i = 0; i < 5; i++) Raster.Render(mesh, atlas, camera, target);

        var times = new double[30];
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Perf.Enable();
        try
        {
            Perf.Reset();
            for (int i = 0; i < times.Length; i++)
            {
                camera = camera.Orbit(MathF.PI / 30f, 0f);
                long started = Stopwatch.GetTimestamp();
                target.Clear(default);
                Raster.Render(mesh, atlas, camera, target);
                times[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                digest.AppendData(MemoryMarshal.AsBytes<Rgba32>(target.Color));
            }
            Array.Sort(times);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"source={sourceSize}, target={targetSize}, triangles={mesh.TriangleCount}, mean={times.Average():F3}ms, median={times[15]:F3}ms, p95={times[28]:F3}ms, pixels={Convert.ToHexString(digest.GetHashAndReset())}"));
            output.WriteLine(Perf.Report());
        }
        finally { Perf.Disable(); }
    }
}
