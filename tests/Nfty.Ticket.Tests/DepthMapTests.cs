using Nfty.Core.Model;

namespace Nfty.Ticket.Tests;

public class DepthMapTests
{
    [Fact]
    public void Bytes_are_front_then_back_row_major_top_row_first()
    {
        // 2x2: pixel (1,0) front 3 back 4; pixel (0,1) front 5 back 6
        byte[] bytes = [0, 0, 3, 4, 5, 6, 0, 0];
        var map = DepthMap.FromBytes(2, 2, bytes);
        Assert.Equal(3, map.Front(1, 0));
        Assert.Equal(4, map.Back(1, 0));
        Assert.Equal(5, map.Front(0, 1));
        Assert.Equal(6, map.Back(0, 1));
        Assert.Equal(bytes, map.ToBytes());
    }

    [Fact]
    public void A_wrong_length_is_refused_with_both_sizes_named()
    {
        var ex = Assert.Throws<ArgumentException>(() => DepthMap.FromBytes(2, 2, new byte[7]));
        Assert.Contains("8", ex.Message);
        Assert.Contains("7", ex.Message);
    }

    [Fact]
    public void Flat_is_all_zero_and_says_so()
    {
        var map = DepthMap.Flat(3, 2);
        Assert.True(map.IsFlat);
        Assert.Equal(new byte[12], map.ToBytes());
        Assert.False(map.With(2, 1, 0, 1).IsFlat);
    }

    [Fact]
    public void With_returns_a_new_map_and_leaves_the_original_alone()
    {
        var a = DepthMap.Flat(1, 1);
        var b = a.With(0, 0, 255, 7);
        Assert.Equal(0, a.Front(0, 0));
        Assert.Equal(255, b.Front(0, 0));
        Assert.Equal(7, b.Back(0, 0));
    }

    [Fact]
    public void FromBytes_copies_so_the_caller_cannot_mutate_the_map()
    {
        byte[] bytes = [1, 2];
        var map = DepthMap.FromBytes(1, 1, bytes);
        bytes[0] = 9;
        Assert.Equal(1, map.Front(0, 0));
    }
}
