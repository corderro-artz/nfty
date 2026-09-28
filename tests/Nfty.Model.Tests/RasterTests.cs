using System.Numerics;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Model.Tests;

public class RasterTests
{
    [Fact]
    public void Back_face_culling_draws_from_exactly_half_a_cubes_triangles()
    {
        var frontColor = new Rgba32(255, 0, 0, 255);
        var backColor = new Rgba32(0, 255, 0, 255);
        var mesh = UnitCube();
        using var texture = TwoColorTexture(front: frontColor, back: backColor);
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0.6f, pitch: 0.4f);
        var target = new RenderTarget(64, 64);
        target.Clear(new Rgba32(0, 0, 0, 0));

        Raster.Render(mesh, texture, camera.ViewProjection(64, 64), target);

        // A generic camera angle on a closed cube shows exactly three of its six faces — this camera
        // was placed in the +X+Y+Z octant on purpose so the three UnitCube marks "front" (+X, +Y, +Z)
        // are exactly the three that are visible. Shading darkens whatever shows, so this checks
        // absence of the back texel rather than an exact lit color: backColor is never sampled at all
        // when culling is correct, regardless of what brightness multiplies it by.
        var colors = target.Color.Where(c => c.A > 0).Distinct().ToList();
        Assert.NotEmpty(colors);
        Assert.DoesNotContain(backColor, colors);
    }

    [Fact]
    public void The_z_buffer_resolves_a_solid_every_visible_pixel_comes_from_the_nearer_face()
    {
        // Two quads facing the camera, same footprint, one behind the other. Both share one face
        // normal, so shading (whatever it turns out to be) scales both identically — which lets this
        // assert on channel identity (red-only vs blue-only) rather than replicate Raster's private
        // brightness formula to predict an exact lit value.
        var mesh = TwoStackedQuads();
        using var texture = HalfSplitTexture(near: new Rgba32(200, 0, 0, 255), far: new Rgba32(0, 0, 200, 255));
        var camera = new Camera(Vector3.Zero, distance: 6f, yaw: 0f, pitch: 0f);
        var target = new RenderTarget(32, 32);
        target.Clear(new Rgba32(0, 0, 0, 0));

        Raster.Render(mesh, texture, camera.ViewProjection(32, 32), target);

        var covered = target.Color.Where(c => c.A > 0).ToList();
        Assert.NotEmpty(covered);
        Assert.All(covered, c =>
        {
            Assert.True(c.R > 0, "the near (red) face should be what's visible");
            Assert.Equal(0, c.B);
        });
    }

    [Fact]
    public void Nearest_neighbour_sampling_of_an_eight_by_eight_checker_produces_exactly_two_colours()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = Checker(8, new Rgba32(255, 255, 255, 255), new Rgba32(0, 0, 0, 255));
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0f, pitch: 0f);
        var target = new RenderTarget(256, 256);
        target.Clear(new Rgba32(50, 50, 50, 255));

        Raster.Render(mesh, texture, camera.ViewProjection(256, 256), target);

        var onFace = target.Color.Where(c => c != new Rgba32(50, 50, 50, 255)).Distinct().ToList();
        Assert.Equal(2, onFace.Count);
        // Black is invariant under the brightness multiply (0 × anything is 0), so it is the one
        // exact literal this test can still pin regardless of shading. The checker's white texel
        // becomes some shaded grey instead — asserted as "equal channels, brighter than black" rather
        // than a literal (255,255,255,255) that shading would make false.
        Assert.Contains(onFace, c => c.R == 0 && c.G == 0 && c.B == 0);
        Assert.Contains(onFace, c => c.R == c.G && c.G == c.B && c.R > 0);
    }

    [Fact]
    public void Flat_shading_makes_every_pixel_of_one_face_the_same_colour()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = SolidTexture(new Rgba32(128, 64, 200, 255));
        var camera = new Camera(Vector3.Zero, distance: 4f, yaw: 0.3f, pitch: 0.2f);
        var target = new RenderTarget(128, 128);
        target.Clear(new Rgba32(0, 0, 0, 0));

        Raster.Render(mesh, texture, camera.ViewProjection(128, 128), target);

        var faceColors = target.Color.Where(c => c.A > 0).Distinct().ToList();
        Assert.Single(faceColors);
    }

    // ---- fixtures ----
    // Hand-built the same way tests/Nfty.Model.Tests/MeshTests.cs already hand-builds small meshes:
    // literal position/normal/uv/index arrays straight into the Mesh constructor, no Riser involved
    // — far simpler than rising one from pixel art for a fixture this small.

    /// <summary>A 2x2x2 cube centered at the origin. The +X/+Y/+Z faces sample <c>TwoColorTexture</c>'s
    /// "front" texel; -X/-Y/-Z sample its "back" texel — so whichever three faces a given camera
    /// angle exposes (any generic angle on a cube shows exactly half its faces) prove culling by
    /// which color shows up, never by counting hidden triangles this method has no reason to expose.</summary>
    private static Mesh UnitCube()
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var uvs = new List<float>();
        var indices = new List<uint>();

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, bool front)
        {
            uint baseIndex = (uint)(positions.Count / 3);
            foreach (var p in new[] { a, b, c, d })
                positions.AddRange([p.X, p.Y, p.Z]);
            for (int i = 0; i < 4; i++)
                normals.AddRange([normal.X, normal.Y, normal.Z]);
            float u = front ? 0.75f : 0.25f; // TwoColorTexture is 2 wide: u<0.5 -> texel 0 (back), else texel 1 (front)
            for (int i = 0; i < 4; i++)
                uvs.AddRange([u, 0.5f]);
            indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex, baseIndex + 2, baseIndex + 3]);
        }

        // Each quad's four corners are CCW seen from outside that face.
        AddQuad(new Vector3(1, -1, 1), new Vector3(1, -1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 1), new Vector3(1, 0, 0), front: true);      // +X
        AddQuad(new Vector3(-1, -1, -1), new Vector3(-1, -1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 1, -1), new Vector3(-1, 0, 0), front: false); // -X
        AddQuad(new Vector3(-1, 1, -1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, -1), new Vector3(0, 1, 0), front: true);       // +Y
        AddQuad(new Vector3(-1, -1, 1), new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 1), new Vector3(0, -1, 0), front: false);  // -Y
        AddQuad(new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 1, 1), new Vector3(-1, 1, 1), new Vector3(0, 0, 1), front: true);       // +Z
        AddQuad(new Vector3(1, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(0, 0, -1), front: false);  // -Z

        return new Mesh(positions.ToArray(), normals.ToArray(), uvs.ToArray(), indices.ToArray(),
            textureWidth: 2, textureHeight: 1);
    }

    /// <summary>Two quads with the same footprint and the same +Z-facing normal (so both pass
    /// back-face culling identically — a planar quad's on-screen winding does not change when it
    /// moves along the camera's own look axis), one at z=1 (near) and one at z=-1 (far).</summary>
    private static Mesh TwoStackedQuads()
    {
        float[] positions =
        [
            -1, -1, 1,   1, -1, 1,   1, 1, 1,   -1, 1, 1,      // near, z=1
            -1, -1, -1,  1, -1, -1,  1, 1, -1,  -1, 1, -1,     // far, z=-1
        ];
        float[] normals =
        [
            0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
            0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1,
        ];
        float[] uvs =
        [
            0, 0.25f, 1, 0.25f, 1, 0.25f, 0, 0.25f,   // near quad samples HalfSplitTexture's near rows
            0, 0.75f, 1, 0.75f, 1, 0.75f, 0, 0.75f,   // far quad samples its far rows
        ];
        uint[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 4, textureHeight: 4);
    }

    /// <summary>One quad at z=0, normal +Z, spanning [-size/2, size/2] in x and y.</summary>
    private static Mesh SingleQuadFacingCamera(float size)
    {
        float h = size / 2f;
        float[] positions = [-h, -h, 0, h, -h, 0, h, h, 0, -h, h, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 8, textureHeight: 8);
    }

    private static Image<Rgba32> SolidTexture(Rgba32 color)
    {
        var img = new Image<Rgba32>(4, 4);
        img.Mutate(ctx => ctx.BackgroundColor(color));
        return img;
    }

    private static Image<Rgba32> TwoColorTexture(Rgba32 front, Rgba32 back)
    {
        var img = new Image<Rgba32>(2, 1);
        img[0, 0] = back;
        img[1, 0] = front;
        return img;
    }

    private static Image<Rgba32> HalfSplitTexture(Rgba32 near, Rgba32 far)
    {
        var img = new Image<Rgba32>(4, 4);
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                img[x, y] = y < 2 ? near : far;
        return img;
    }

    private static Image<Rgba32> Checker(int size, Rgba32 a, Rgba32 b)
    {
        var img = new Image<Rgba32>(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                img[x, y] = ((x + y) % 2 == 0) ? a : b;
        return img;
    }
}
