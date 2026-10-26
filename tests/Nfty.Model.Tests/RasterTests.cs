using System.Numerics;
using Nfty.Model.Render;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.Model.Tests;

public class RasterTests
{
    private static readonly Rgba32 Clear = new(0, 0, 0, 0);

    // A cube around the origin whose half-diagonal is `radius`: the same Radius (so the same clip
    // planes) these tests were written against.
    private static Camera Cam(float distance, float yaw = 0f, float pitch = 0f, float radius = 2f)
    {
        float h = radius / MathF.Sqrt(3f);
        return new(Vector3.Zero, distance, yaw, pitch, new Vector3(-h), new Vector3(h));
    }

    private static RenderTarget Target(int size) => Target(size, size);

    private static RenderTarget Target(int width, int height)
    {
        var target = new RenderTarget(width, height);
        target.Clear(Clear);
        return target;
    }

    // ---- culling ----

    [Fact]
    public void Back_face_culling_draws_from_exactly_half_a_cubes_triangles()
    {
        var frontColor = new Rgba32(255, 0, 0, 255);
        var backColor = new Rgba32(0, 255, 0, 255);
        var mesh = UnitCube();
        using var texture = TwoColorTexture(front: frontColor, back: backColor);
        var camera = Cam(4f, yaw: 0.6f, pitch: 0.4f);
        var target = Target(64);

        int drawn = Raster.Render(mesh, texture, camera, target);

        // The pixels cannot prove culling — the z-buffer hides back faces anyway — so the count of
        // triangles handed to the rasterizer does: three faces of a closed cube, two triangles each.
        Assert.Equal(6, drawn);

        // A generic camera angle on a closed cube shows exactly three of its six faces — this camera
        // was placed in the +X+Y+Z octant on purpose so the three UnitCube marks "front" (+X, +Y, +Z)
        // are exactly the three that are visible.
        var colors = target.Color.Where(c => c.A > 0).Distinct().ToList();
        Assert.NotEmpty(colors);

        // Assert on the channel signature, not a literal: shading scales a pure-channel color but
        // keeps its zero channels zero, so this holds at any brightness and catches a flipped cull.
        Assert.DoesNotContain(colors, c => c.R == 0 && c.B == 0 && c.G > 0);
    }

    // ---- depth ----

    [Fact]
    public void The_z_buffer_resolves_a_solid_every_visible_pixel_comes_from_the_nearer_face()
    {
        var mesh = TwoStackedQuads();
        using var texture = HalfSplitTexture(near: new Rgba32(200, 0, 0, 255), far: new Rgba32(0, 0, 200, 255));
        var target = Target(32);

        Raster.Render(mesh, texture, Cam(6f), target);

        var covered = target.Color.Where(c => c.A > 0).ToList();
        Assert.NotEmpty(covered);
        Assert.All(covered, c =>
        {
            Assert.True(c.R > 0, "the near (red) face should be what's visible");
            Assert.Equal(0, c.B);
        });
    }

    // ---- sampling ----

    [Fact]
    public void Nearest_neighbor_sampling_of_an_eight_by_eight_checker_produces_exactly_two_colors()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = Checker(8, new Rgba32(255, 255, 255, 255), new Rgba32(0, 0, 0, 255));
        var target = Target(256);
        target.Clear(new Rgba32(50, 50, 50, 255));

        Raster.Render(mesh, texture, Cam(4f), target);

        var onFace = target.Color.Where(c => c != new Rgba32(50, 50, 50, 255)).Distinct().ToList();
        Assert.Equal(2, onFace.Count);
        Assert.Contains(onFace, c => c.R == 0 && c.G == 0 && c.B == 0);
        Assert.Contains(onFace, c => c.R == c.G && c.G == c.B && c.R > 0);
    }

    [Fact]
    public void Texture_coordinates_are_perspective_correct_not_affine()
    {
        // A floor quad (x in -1..1, z in -1..1) seen from 51 degrees up. Texture: 1 wide, 2 tall;
        // v = 0 at the far edge, v = 1 at the near edge, so the texel boundary is the WORLD midline
        // z = 0 — which passes through the camera target and so projects to the exact center row.
        // Hand math (distance 4, pitch 0.9): the far edge projects to ndc y +0.465 and the near edge
        // to -0.637, so an AFFINE blend would put the boundary at their midpoint, ndc -0.086, which is
        // 5.5 rows below center. Perspective-correct puts it at row 64.
        float[] positions = [-1, 0, 1, 1, 0, 1, 1, 0, -1, -1, 0, -1];
        float[] normals = [0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0];
        float[] uvs = [0.5f, 1, 0.5f, 1, 0.5f, 0, 0.5f, 0];
        var mesh = new Mesh(positions, normals, uvs, [0, 1, 2, 0, 2, 3], textureWidth: 1, textureHeight: 2);
        using var texture = new Image<Rgba32>(1, 2);
        texture[0, 0] = new Rgba32(255, 0, 0, 255); // far half: red
        texture[0, 1] = new Rgba32(0, 0, 255, 255); // near half: blue
        var target = Target(128);

        Raster.Render(mesh, texture, Cam(4f, 0f, 0.9f), target);

        Rgba32 At(int row) => target.Color[(row * 128) + 64];
        Assert.True(At(63).R > 0 && At(63).B == 0, "row 63 is above the world midline: far texel");
        Assert.True(At(64).B > 0 && At(64).R == 0, "row 64 is below the world midline: near texel");
        Assert.True(At(67).B > 0 && At(67).R == 0, "an affine blend would still show the far texel here");
        Assert.True(At(60).R > 0 && At(60).B == 0);
    }

    [Fact]
    public void A_mismatched_texture_size_is_refused_like_the_writer_refuses_it()
    {
        var mesh = SingleQuadFacingCamera(size: 2f); // risen from 8x8 art
        using var wrong = SolidTexture(new Rgba32(1, 2, 3, 255)); // 4x4
        var ex = Assert.Throws<ArgumentException>(() => Raster.Render(mesh, wrong, Cam(4f), Target(16)));
        Assert.Contains("8x8", ex.Message);
        Assert.Contains("4x4", ex.Message);
    }

    [Fact]
    public void Null_arguments_are_refused()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = Checker(8, new Rgba32(1, 1, 1, 255), new Rgba32(2, 2, 2, 255));
        var camera = Cam(4f);
        var target = Target(16);
        Assert.Throws<ArgumentNullException>(() => Raster.Render(null!, texture, camera, target));
        Assert.Throws<ArgumentNullException>(() => Raster.Render(mesh, null!, camera, target));
        Assert.Throws<ArgumentNullException>(() => Raster.Render(mesh, texture, null!, target));
        Assert.Throws<ArgumentNullException>(() => Raster.Render(mesh, texture, camera, null!));
    }

    [Fact]
    public void A_texel_is_cut_out_below_alpha_128_exactly_like_the_exporters_mask()
    {
        // glTF MASK at cutoff 0.5 drops alpha below 128; the preview must agree with the export.
        var mesh = QuadWithTexture(2, 1);
        using var texture = new Image<Rgba32>(2, 1);
        texture[0, 0] = new Rgba32(255, 255, 255, 100); // left: cut out
        texture[1, 0] = new Rgba32(255, 255, 255, 200); // right: drawn
        var target = Target(64);

        Raster.Render(mesh, texture, Cam(4f), target);

        int row = 32;
        Assert.Equal(0, target.Color[(row * 64) + 24].A);
        Assert.True(float.IsPositiveInfinity(target.Depth[(row * 64) + 24]));
        Assert.Equal(200, target.Color[(row * 64) + 40].A);
    }

    [Fact]
    public void A_texel_at_alpha_127_is_cut_out_and_128_is_drawn()
    {
        var mesh = QuadWithTexture(2, 1);
        using var texture = new Image<Rgba32>(2, 1);
        texture[0, 0] = new Rgba32(255, 255, 255, 127);
        texture[1, 0] = new Rgba32(255, 255, 255, 128);
        var target = Target(64);

        Raster.Render(mesh, texture, Cam(4f), target);

        Assert.Equal(0, target.Color[(32 * 64) + 24].A);
        Assert.Equal(128, target.Color[(32 * 64) + 40].A);
    }

    // ---- shading ----

    [Fact]
    public void Flat_shading_makes_every_pixel_of_one_face_the_same_color()
    {
        var mesh = SingleQuadFacingCamera(size: 2f);
        using var texture = SolidTexture(new Rgba32(128, 64, 200, 255), 8);
        var target = Target(128);

        Raster.Render(mesh, texture, Cam(4f, yaw: 0.3f, pitch: 0.2f), target);

        var faceColors = target.Color.Where(c => c.A > 0).Distinct().ToList();
        Assert.Single(faceColors);
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0.7f, -0.3f)]
    [InlineData(-2.4f, 0.9f)]
    public void A_face_looking_straight_at_the_camera_shows_its_texels_at_exactly_full_brightness(float yaw, float pitch)
    {
        // The head light puts a viewer-facing face on the top quantization level at any orbit angle,
        // so the author's art is shown as drawn. Brightness 1.0 means the texel passes through untouched.
        var camera = Cam(4f, yaw, pitch);
        var mesh = QuadFacing(Vector3.Normalize(camera.Eye - camera.Target), halfSize: 1f);
        var texel = new Rgba32(128, 64, 200, 255);
        using var texture = SolidTexture(texel);
        var target = Target(64);

        Raster.Render(mesh, texture, camera, target);

        var covered = target.Color.Where(c => c.A > 0).ToList();
        Assert.NotEmpty(covered);
        Assert.All(covered, c => Assert.Equal(texel, c));
    }

    [Fact]
    public void Shading_only_ever_uses_four_brightness_levels_however_the_camera_is_orbited()
    {
        var mesh = UnitCube();
        var white = new Rgba32(255, 255, 255, 255);
        using var texture = TwoColorTexture(front: white, back: white);
        // 255 * {0.35, 0.5667, 0.7833, 1.0}, truncated; the middle two sit on a .5 / .75 boundary.
        int[] allowed = [89, 144, 145, 199, 200, 255];
        var seen = new HashSet<int>();

        for (float yaw = -3f; yaw <= 3f; yaw += 0.45f)
        for (float pitch = -1.2f; pitch <= 1.2f; pitch += 0.4f)
        {
            var target = Target(48);
            Raster.Render(mesh, texture, Cam(4f, yaw, pitch), target);
            foreach (var c in target.Color.Where(c => c.A > 0))
            {
                Assert.True(c.R == c.G && c.G == c.B, "white stays gray");
                Assert.Contains(c.R, allowed);
                seen.Add(c.R);
            }
        }

        Assert.True(seen.Count >= 3, $"only {seen.Count} distinct levels turned up: the sweep proves nothing");
    }

    // ---- C1: near-plane clipping ----

    [Fact]
    public void A_floor_extending_behind_a_close_camera_is_clipped_not_flipped_through_the_eye()
    {
        // 20 x 20 floor at y = 0 (normal +Y), camera 3 units from the origin looking down at 30
        // degrees, so everything past z = d / cos(pitch) = 3.46 lies behind the eye plane. The whole
        // 64 x 64 view is floor (the horizon is 30 degrees above the view axis, the field is +-20).
        float[] positions = [-10, 0, 10, 10, 0, 10, 10, 0, -10, -10, 0, -10];
        float[] normals = [0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1, 0];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        var mesh = new Mesh(positions, normals, uvs, [0, 1, 2, 0, 2, 3], textureWidth: 1, textureHeight: 1);
        using var texture = SolidTexture(new Rgba32(10, 200, 10, 255), 1);
        var target = Target(64);

        int drawn = Raster.Render(mesh, texture, Cam(3f, 0f, 30f * MathF.PI / 180f), target);

        Assert.Equal(2, drawn);
        Assert.All(target.Depth, d => Assert.True(d > 0f && float.IsFinite(d), $"depth {d}"));
        Assert.All(target.Color, c => Assert.Equal(255, c.A)); // every pixel is floor

        // Center pixel looks at the target: view depth is the camera distance (half a pixel off axis).
        Assert.Equal(3f, target.Depth[(32 * 64) + 32], precision: 1);
        // Farther up the screen is farther away, and the nearest floor is at the bottom.
        Assert.True(target.Depth[(2 * 64) + 32] > target.Depth[(32 * 64) + 32]);
        Assert.True(target.Depth[(32 * 64) + 32] > target.Depth[(61 * 64) + 32]);
    }

    [Fact]
    public void A_triangle_entirely_behind_the_camera_draws_nothing_and_is_not_counted()
    {
        // A +Z-facing quad at z = 10, camera at z = 3 looking toward -Z... the quad is behind the eye.
        float[] positions = [-1, -1, 10, 1, -1, 10, 1, 1, 10, -1, 1, 10];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        var mesh = new Mesh(positions, normals, uvs, [0, 1, 2, 0, 2, 3], textureWidth: 1, textureHeight: 1);
        using var texture = SolidTexture(new Rgba32(10, 200, 10, 255), 1);
        var target = Target(32);

        int drawn = Raster.Render(mesh, texture, Cam(3f), target);

        Assert.Equal(0, drawn);
        Assert.All(target.Color, c => Assert.Equal(0, c.A));
        Assert.All(target.Depth, d => Assert.True(float.IsPositiveInfinity(d)));
    }

    [Fact]
    public void Zooming_into_a_risen_sprite_never_produces_a_pixel_at_or_behind_the_eye()
    {
        // The measured failure: a risen sprite framed, then the wheel stepped in at 0.9. The first
        // broken frame came after 17 notches; every covered pixel then carried depth <= 0.
        using var art = Art.FromAscii(
            "########",
            "########",
            "########",
            "########",
            "########",
            "########",
            "########",
            "########");
        var mesh = Riser.TryBuild(art, Art.Depth(art, (_, _) => (2, 1)), new RiseOptions(), "8px slab")!;
        using var atlas = FaceAtlas.Build(art);
        var camera = Camera.FramedOn(mesh.Min, mesh.Max);
        int coveredSomewhere = 0;

        for (int notch = 1; notch <= 30; notch++)
        {
            camera = camera.Zoom(0.9f);
            var target = Target(96);
            Raster.Render(mesh, atlas, camera, target);

            for (int i = 0; i < target.Color.Length; i++)
            {
                if (target.Color[i].A == 0) continue;
                coveredSomewhere++;
                Assert.True(target.Depth[i] > 0f && float.IsFinite(target.Depth[i]),
                    $"notch {notch}: pixel {i} has depth {target.Depth[i]}");
            }
        }

        Assert.True(coveredSomewhere > 0, "the sweep never drew anything");
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(30f, 20f)]
    [InlineData(-60f, 45f)]
    public void Fully_zoomed_in_the_frame_is_never_blank(float yawDeg, float pitchDeg)
    {
        using var art = Art.FromAscii(Enumerable.Range(0, 16).Select(y =>
            new string(Enumerable.Range(0, 16).Select(x => (x - 7.5) * (x - 7.5) + (y - 7.5) * (y - 7.5) <= 49 ? '#' : '.').ToArray())).ToArray());
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var atlas = FaceAtlas.Build(art);
        var framed = Camera.FramedOn(mesh.Min, mesh.Max);
        var camera = new Camera(framed.Target, framed.Distance, yawDeg * MathF.PI / 180f,
            pitchDeg * MathF.PI / 180f, mesh.Min, mesh.Max).Zoom(1e-6f);
        var target = new RenderTarget(64, 64);
        target.Clear(new Rgba32(0, 0, 0, 0));
        Raster.Render(mesh, atlas, camera, target);
        Assert.True(target.Color.Count(c => c.A == 255) > 0, "the eye is inside the sprite: blank frame");
    }

    [Fact]
    public void Fully_zoomed_in_the_frame_is_never_blank_at_any_orbit_angle()
    {
        using var art = Art.FromAscii(Enumerable.Range(0, 16).Select(_ => new string('#', 16)).ToArray());
        var mesh = Riser.TryBuild(art, null, new RiseOptions())!;
        using var atlas = FaceAtlas.Build(art);
        var framed = Camera.FramedOn(mesh.Min, mesh.Max);
        var target = new RenderTarget(64, 64);

        for (int yaw = -180; yaw < 180; yaw += 15)
        for (int pitch = -89; pitch <= 89; pitch += 22)
        {
            var camera = new Camera(framed.Target, framed.Distance, yaw * MathF.PI / 180f,
                pitch * MathF.PI / 180f, mesh.Min, mesh.Max).Zoom(1e-6f);
            target.Clear(new Rgba32(0, 0, 0, 0));
            Raster.Render(mesh, atlas, camera, target);
            Assert.True(target.Color.Any(c => c.A == 255), $"blank at yaw {yaw}, pitch {pitch}");
        }
    }

    [Fact]
    public void Fully_zoomed_in_on_a_512px_sprite_a_single_pixel_spans_many_screen_pixels()
    {
        var min = new Vector3(-256f, 0f, -1.5f);
        var max = new Vector3(256f, 512f, 1.5f);
        var framed = Camera.FramedOn(min, max);
        var camera = new Camera(framed.Target, framed.Distance, 0f, 0f, min, max).Zoom(1e-6f);

        // Head-on: the front face is camera.Distance - 1.5 in front of the eye. One voxel (one world
        // unit) spans height / (2 * depth * tan(fov / 2)) screen pixels.
        float depth = camera.Distance - 1.5f;
        float pixelsPerVoxel = 512f / (2f * depth * MathF.Tan(Camera.FovDegrees * MathF.PI / 360f));
        Assert.True(pixelsPerVoxel >= 8f, $"one voxel spans only {pixelsPerVoxel:0.0} px at the floor");

        // And the claim is real, not just arithmetic: project two points one voxel apart on the face.
        var vp = camera.ViewProjection(512, 512);
        static float Sx(Vector3 p, Matrix4x4 vp)
        {
            var c = Vector4.Transform(new Vector4(p, 1f), vp);
            return ((c.X / c.W) * 0.5f + 0.5f) * 512f;
        }
        float span = Sx(new Vector3(framed.Target.X + 1f, framed.Target.Y, 1.5f), vp)
                   - Sx(new Vector3(framed.Target.X, framed.Target.Y, 1.5f), vp);
        Assert.True(span >= 8f, $"projected span {span:0.0} px");
        Assert.Equal(pixelsPerVoxel, span, precision: 1);
    }

    // ---- M10: top-left fill rule ----

    [Fact]
    public void A_pixel_centered_exactly_on_a_shared_edge_is_drawn_by_the_triangle_that_owns_it()
    {
        // A 4 x 4 screen square split along its diagonal, whose pixel centers all lie ON the edge.
        // Both triangles are clockwise on a Y-down screen. B (upper right) owns the diagonal because
        // for B it is a left edge (it runs upward); for A it is a bottom-right edge. A is drawn
        // FIRST, so a rule-less rasterizer lets A keep the diagonal.
        var texels = new[] { new Rgba32(255, 0, 0, 255), new Rgba32(0, 0, 255, 255) };
        var target = Target(4);
        Raster.FillTriangle(Vtx(0, 0, 0.25f), Vtx(4, 4, 0.25f), Vtx(0, 4, 0.25f), 1f, texels, 2, 1, target); // A: red
        Raster.FillTriangle(Vtx(0, 0, 0.75f), Vtx(4, 0, 0.75f), Vtx(4, 4, 0.75f), 1f, texels, 2, 1, target); // B: blue

        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            var c = target.Color[(y * 4) + x];
            Assert.True(c.A == 255, $"pixel ({x},{y}) is a hole");
            bool expectBlue = x >= y;
            Assert.True(expectBlue ? c.B == 255 : c.R == 255, $"pixel ({x},{y}) came from the wrong triangle");
        }
    }

    [Fact]
    public void The_fill_rule_does_not_depend_on_draw_order()
    {
        var texels = new[] { new Rgba32(255, 0, 0, 255), new Rgba32(0, 0, 255, 255) };
        var target = Target(4);
        // A is drawn second AND strictly nearer (smaller view depth), so the depth test would hand it
        // the diagonal unless the top-left rule withholds it: only the rule can decide those pixels.
        Raster.FillTriangle(Vtx(0, 0, 0.75f), Vtx(4, 0, 0.75f), Vtx(4, 4, 0.75f), 1f, texels, 2, 1, target); // B first, depth 1
        Raster.FillTriangle(Vtx(0, 0, 0.25f, 0.5f), Vtx(4, 4, 0.25f, 0.5f), Vtx(0, 4, 0.25f, 0.5f), 1f, texels, 2, 1, target); // A, nearer

        for (int i = 0; i < 4; i++)
            Assert.Equal(255, target.Color[(i * 4) + i].B);
        Assert.Equal(16, target.Color.Count(c => c.A == 255));
    }

    [Fact]
    public void A_front_view_render_never_samples_the_back_half()
    {
        // A one-pixel cube whose back owner is a color the front half does not contain. Seen from
        // the front (yaw 0, pitch 0) every covered pixel must come from the front half or the gutter.
        var frontColor = new Rgba32(200, 0, 0, 255);
        var backColor = new Rgba32(0, 0, 200, 255);
        using var front = new Image<Rgba32>(4, 4, frontColor);
        using var back = new Image<Rgba32>(4, 4, backColor);
        using var art = front.Clone();
        using var faces = new StackFaces(front.Clone(), back.Clone(), new int[16]);
        var mesh = Riser.TryBuild(art, null, new RiseOptions(), "slab", faces)!;
        using var atlas = FaceAtlas.Build(art, faces);
        var camera = Camera.FramedOn(mesh.Min, mesh.Max);
        var target = new RenderTarget(64, 64);
        target.Clear(Clear);

        Raster.Render(mesh, atlas, camera, target);

        var covered = target.Color.Where(c => c.A > 0).ToList();
        Assert.NotEmpty(covered);
        Assert.All(covered, c => Assert.True(c.R > 0 && c.B == 0, $"a back-half texel reached the front view: {c}"));
    }

    [Fact]
    public void A_back_view_render_samples_the_back_half()
    {
        // The mirror of the front-view test: seen from behind (yaw half a turn) every covered pixel
        // must be the back owner's color, which the front half and the gutter do not contain.
        var frontColor = new Rgba32(200, 0, 0, 255);
        var backColor = new Rgba32(0, 0, 200, 255);
        using var front = new Image<Rgba32>(4, 4, frontColor);
        using var back = new Image<Rgba32>(4, 4, backColor);
        using var art = front.Clone();
        using var faces = new StackFaces(front.Clone(), back.Clone(), Enumerable.Repeat(1, 16).ToArray());   // the walls take the back color too
        var mesh = Riser.TryBuild(art, null, new RiseOptions(), "slab", faces)!;
        using var atlas = FaceAtlas.Build(art, faces);
        var camera = Camera.FramedOn(mesh.Min, mesh.Max).Orbit(MathF.PI, 0f);
        var target = new RenderTarget(64, 64);
        target.Clear(Clear);

        Raster.Render(mesh, atlas, camera, target);

        var covered = target.Color.Where(c => c.A > 0).ToList();
        Assert.NotEmpty(covered);
        Assert.All(covered, c => Assert.True(c.B > 0 && c.R == 0, $"a front-half texel reached the back view: {c}"));
    }

    // ---- fixtures ----

    private static Raster.ScreenVertex Vtx(float x, float y, float u, float w = 1f) => new(x, y, w, u, 0.5f);

    /// <summary>A 2x2x2 cube centered at the origin. The +X/+Y/+Z faces sample <c>TwoColorTexture</c>'s
    /// "front" texel; -X/-Y/-Z sample its "back" texel.</summary>
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

    /// <summary>Two quads with the same footprint and the same +Z-facing normal, one at z=1 (near)
    /// and one at z=-1 (far).</summary>
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

    /// <summary>One quad at z=0, normal +Z, spanning [-size/2, size/2] in x and y; risen from 8x8 art.</summary>
    private static Mesh SingleQuadFacingCamera(float size)
    {
        float h = size / 2f;
        float[] positions = [-h, -h, 0, h, -h, 0, h, h, 0, -h, h, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        uint[] indices = [0, 1, 2, 0, 2, 3];
        return new Mesh(positions, normals, uvs, indices, textureWidth: 8, textureHeight: 8);
    }

    /// <summary>The same +Z quad, 2 units across, addressing a <paramref name="w"/> x <paramref name="h"/> texture.</summary>
    private static Mesh QuadWithTexture(int w, int h)
    {
        float[] positions = [-1, -1, 0, 1, -1, 0, 1, 1, 0, -1, 1, 0];
        float[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        return new Mesh(positions, normals, uvs, [0, 1, 2, 0, 2, 3], w, h);
    }

    /// <summary>A quad whose outward normal is <paramref name="normal"/>, 4x4-texel solid art.</summary>
    private static Mesh QuadFacing(Vector3 normal, float halfSize)
    {
        var right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, normal));
        var up = Vector3.Cross(normal, right);
        Vector3[] corners =
        [
            (-right - up) * halfSize, (right - up) * halfSize, (right + up) * halfSize, (-right + up) * halfSize,
        ];
        var positions = corners.SelectMany(c => new[] { c.X, c.Y, c.Z }).ToArray();
        var normals = Enumerable.Repeat(new[] { normal.X, normal.Y, normal.Z }, 4).SelectMany(n => n).ToArray();
        float[] uvs = [0, 1, 1, 1, 1, 0, 0, 0];
        return new Mesh(positions, normals, uvs, [0, 1, 2, 0, 2, 3], textureWidth: 4, textureHeight: 4);
    }

    private static Image<Rgba32> SolidTexture(Rgba32 color, int size = 4)
    {
        var img = new Image<Rgba32>(size, size);
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
