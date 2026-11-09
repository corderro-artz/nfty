using System.Numerics;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class PreviewLightingTests
{
    [Fact]
    public void Full_white_light_matches_the_original_overload_including_depth()
    {
        using var art = Art.FromAscii("##", ".#");
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var atlas = FaceAtlas.Build(art);
        var camera = Camera.FramedOn(mesh.Min, mesh.Max);
        var original = new RenderTarget(64, 64);
        var explicitLight = new RenderTarget(64, 64);
        original.Clear(default);
        explicitLight.Clear(default);

        Raster.Render(mesh, atlas, camera, original);
        Raster.Render(mesh, atlas, camera, explicitLight, PreviewLighting.Default);

        Assert.Contains(original.Color, p => p.A != 0);
        Assert.Equal(original.Color.ToArray(), explicitLight.Color.ToArray());
        Assert.Equal(original.Depth.ToArray(), explicitLight.Depth.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void Light_scales_RGB_without_changing_coverage_depth_or_texture(int value)
    {
        using var texture = new Image<Rgba32>(1, 1, new Rgba32(200, 120, 80, 200));
        var mesh = new Mesh([-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0],
            [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1], [0, 1, 1, 1, 1, 0, 0, 0], [0, 1, 2, 0, 2, 3], 1, 1);
        var camera = new Camera(Vector3.Zero, 4f, 0f, 0f, new Vector3(-1), new Vector3(1));
        var white = new RenderTarget(32, 32);
        var red = new RenderTarget(32, 32);
        white.Clear(default);
        red.Clear(default);
        Raster.Render(mesh, texture, camera, white);
        Raster.Render(mesh, texture, camera, red, new PreviewLighting(value, new Rgba32(255, 0, 0, 0)));

        Assert.Contains(white.Color, p => p.A == 200);
        for (int i = 0; i < white.Color.Length; i++)
        {
            var expected = white.Color[i];
            var actual = red.Color[i];
            Assert.Equal(expected.A, actual.A);
            Assert.Equal((byte)(expected.R * value / 100), actual.R);
            Assert.Equal(0, actual.G);
            Assert.Equal(0, actual.B);
            Assert.Equal(white.Depth[i], red.Depth[i]);
        }
        Assert.Equal(new Rgba32(200, 120, 80, 200), texture[0, 0]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_light_intensity_is_refused(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PreviewLighting(value, default));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_culled_or_offscreen_mesh_still_checks_its_texture_dimensions(bool offscreen)
    {
        float x = offscreen ? 100 : 0;
        var mesh = new Mesh([x - 1, -1, 0, x + 1, -1, 0, x + 1, 1, 0, x - 1, 1, 0],
            [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1], [0, 1, 1, 1, 1, 0, 0, 0], [0, 1, 2, 0, 2, 3], 1, 1);
        using var wrongTexture = new Image<Rgba32>(7, 3);
        var camera = new Camera(Vector3.Zero, 4, offscreen ? 0 : MathF.PI, 0, -Vector3.One, Vector3.One);
        var target = new RenderTarget(8, 8);
        Assert.Throws<ArgumentException>(() => Raster.Render(mesh, wrongTexture, camera, target));
    }
}
