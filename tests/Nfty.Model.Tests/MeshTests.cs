using System.Numerics;

namespace Nfty.Model.Tests;

public class MeshTests
{
    // One unit quad facing +Z: the smallest mesh there is.
    private static float[] QuadPositions() => [0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0];
    private static float[] QuadNormals() => [0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1];
    private static float[] QuadUvs() => [0, 1, 1, 1, 1, 0, 0, 0];
    private static uint[] QuadIndices() => [0, 1, 2, 0, 2, 3];

    private static Mesh Quad() =>
        new(QuadPositions(), QuadNormals(), QuadUvs(), QuadIndices(), textureWidth: 1, textureHeight: 1);

    [Fact]
    public void A_quad_is_four_vertices_and_two_triangles()
    {
        var mesh = Quad();
        Assert.Equal(4, mesh.VertexCount);
        Assert.Equal(2, mesh.TriangleCount);
        Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, mesh.Indices);
    }

    [Fact]
    public void Bounds_are_computed_when_the_mesh_is_built()
    {
        // D7: computed once, eagerly. A lazily cached bound is a race the first time two threads
        // touch the same mesh, and the preview and the writer both read these.
        var mesh = Quad();
        Assert.Equal(new Vector3(0, 0, 0), mesh.Min);
        Assert.Equal(new Vector3(1, 1, 0), mesh.Max);
    }

    [Fact]
    public void The_mesh_copies_its_inputs_so_a_caller_cannot_change_it_afterwards()
    {
        float[] positions = QuadPositions();
        var mesh = new Mesh(positions, QuadNormals(), QuadUvs(), QuadIndices(), 1, 1);

        positions[0] = 99;

        Assert.Equal(0f, mesh.Positions[0]);
        Assert.Equal(0f, mesh.Min.X);
    }

    [Fact]
    public void An_index_past_the_last_vertex_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), QuadUvs(), [0, 1, 4], 1, 1));
        Assert.Contains("4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_vertex_needs_exactly_one_normal_and_one_texture_coordinate()
    {
        Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), [0, 0, 1], QuadUvs(), QuadIndices(), 1, 1));
        Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), [0, 0], QuadIndices(), 1, 1));
    }

    [Fact]
    public void Indices_come_in_whole_triangles()
    {
        Assert.Throws<ArgumentException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), QuadUvs(), [0, 1], 1, 1));
    }

    [Fact]
    public void A_position_that_is_not_a_finite_number_is_refused()
    {
        float[] positions = QuadPositions();
        positions[4] = float.NaN;
        Assert.Throws<ArgumentException>(() =>
            new Mesh(positions, QuadNormals(), QuadUvs(), QuadIndices(), 1, 1));
    }

    [Fact]
    public void The_texture_size_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Mesh(QuadPositions(), QuadNormals(), QuadUvs(), QuadIndices(), 0, 1));
    }
}
