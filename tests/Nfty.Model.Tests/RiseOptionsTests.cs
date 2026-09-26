namespace Nfty.Model.Tests;

public class RiseOptionsTests
{
    [Fact]
    public void The_defaults_are_one_unit_voxels_seated_bottom_center_written_as_glb()
    {
        var o = new RiseOptions();
        Assert.Equal(1f, o.VoxelSize);
        Assert.Equal(Pivot.BottomCenter, o.Pivot);
        Assert.Equal(ModelFormat.Glb, o.Format);
    }

    [Fact]
    public void Every_option_is_set_by_init_so_a_new_axis_is_additive()
    {
        var o = new RiseOptions { VoxelSize = 0.5f, Pivot = Pivot.MinCorner, Format = ModelFormat.GltfEmbedded };
        Assert.Equal(new RiseOptions { VoxelSize = 0.5f, Pivot = Pivot.MinCorner, Format = ModelFormat.GltfEmbedded }, o);
    }
}
