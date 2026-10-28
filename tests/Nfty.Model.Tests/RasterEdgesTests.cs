using Nfty.Core.Model;
using Nfty.Model.Render;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

public class RasterEdgesTests
{
    private static (Mesh Mesh, SixLabors.ImageSharp.Image<Rgba32> Tex, Camera Cam) Subject()
    {
        using var art = Art.FromAscii("###", "###", "###");
        var options = new RiseOptions();
        var mesh = Riser.TryBuild(art, Art.Depth(art, (_, _) => (1, 1)), options)!;
        using var stream = new MemoryStream();
        var files = ModelWriter.Write(mesh, art, options, "s");
        var tex = SixLabors.ImageSharp.Image.Load<Rgba32>(GltfReader.Read(files[0].Bytes).TexturePng);
        return (mesh, tex, Camera.FramedOn(mesh.Min, mesh.Max).Orbit(0.5f, 0.4f));
    }

    [Fact]
    public void Edges_change_only_pixels_the_solid_already_covers_and_never_their_alpha()
    {
        var (mesh, tex, cam) = Subject();
        using var _ = tex;
        var plain = new RenderTarget(96, 96);
        plain.Clear(new Rgba32(0, 0, 0, 0));
        Raster.Render(mesh, tex, cam, plain);
        var edged = new RenderTarget(96, 96);
        edged.Clear(new Rgba32(0, 0, 0, 0));
        Raster.Render(mesh, tex, cam, edged);
        Raster.DrawEdges(mesh, cam, edged);

        int changed = 0;
        for (int i = 0; i < plain.Color.Length; i++)
        {
            if (plain.Color[i] == edged.Color[i]) continue;
            changed++;
            Assert.True(plain.Color[i].A > 0, "an edge landed on an empty pixel");
            Assert.Equal(plain.Color[i].A, edged.Color[i].A);
        }
        Assert.True(changed > 20, $"only {changed} pixels carry an edge");
        Assert.Equal(plain.Depth, edged.Depth);
    }

    [Fact]
    public void Edges_over_an_empty_target_draw_nothing()
    {
        var (mesh, tex, cam) = Subject();
        using var _ = tex;
        var target = new RenderTarget(64, 64);
        target.Clear(new Rgba32(0, 0, 0, 0));
        Raster.DrawEdges(mesh, cam, target);
        Assert.All(target.Color, c => Assert.Equal(new Rgba32(0, 0, 0, 0), c));
    }
}
