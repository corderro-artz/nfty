using Nfty.Core.Editing;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Model.Tests;

/// <summary>
/// The rectangle combine is the whole combine, run on less: after any sequence of depth edits, updating
/// the dirty rectangle in place must leave the rise byte-equal to combining everything again.
/// </summary>
public class StackCombineIntoTests
{
    private const int Size = 24;

    private sealed class Stack : IDisposable
    {
        public List<Image<Rgba32>> Layers { get; } = new();
        public List<byte[]> Depth { get; } = new();   // front/back pairs, row-major, per layer

        public IReadOnlyList<(Image<Rgba32>, DepthMap?)> Maps() =>
            Layers.Select((l, i) => (l, (DepthMap?)DepthMap.FromBytes(Size, Size, Depth[i]))).ToList();

        public IReadOnlyList<(Image<Rgba32> Layer, Func<int, int, DepthPixel>? Depth)> Readers() =>
            Layers.Select<Image<Rgba32>, (Image<Rgba32> Layer, Func<int, int, DepthPixel>? Depth)>((l, i) => (l, (x, y) =>
                new DepthPixel(Depth[i][((y * Size) + x) * 2], Depth[i][(((y * Size) + x) * 2) + 1]))).ToList();

        public void Dispose() { foreach (var l in Layers) l.Dispose(); }
    }

    private static Stack Build(int layers, Random rng)
    {
        var s = new Stack();
        for (int i = 0; i < layers; i++)
        {
            var img = new Image<Rgba32>(Size, Size);
            // A blob per layer: some pixels solid in this layer only, some shared, some clear.
            int cx = rng.Next(6, 18), cy = rng.Next(6, 18), r = rng.Next(5, 11);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (((x - cx) * (x - cx)) + ((y - cy) * (y - cy)) <= r * r)
                        img[x, y] = new Rgba32((byte)(40 + (i * 70)), (byte)rng.Next(256), (byte)(255 - (i * 60)), 255);
            s.Layers.Add(img);
            var d = new byte[Size * Size * 2];
            rng.NextBytes(d);
            for (int k = 0; k < d.Length; k++) d[k] %= 12;   // small range: ties are common, the tie rule is exercised
            s.Depth.Add(d);
        }
        return s;
    }

    private static void AssertSame(StackRise got, StackRise want)
    {
        Assert.Equal(want.Depth.ToBytes(), got.Depth.ToBytes());
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                Assert.Equal(want.FrontOwner(x, y), got.FrontOwner(x, y));
                Assert.Equal(want.BackOwner(x, y), got.BackOwner(x, y));
                Assert.Equal(want.Faces.Front[x, y], got.Faces.Front[x, y]);
                Assert.Equal(want.Faces.Back[x, y], got.Faces.Back[x, y]);
                Assert.Equal(want.Faces.BackColored(x, y), got.Faces.BackColored(x, y));
            }
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(3, 4)]
    public void Updating_the_dirty_rectangle_equals_combining_the_whole_stack_again(int layers, int seed)
    {
        var rng = new Random(seed);
        using var stack = Build(layers, rng);
        using var rise = StackDepth.Combine(stack.Maps());
        for (int stroke = 0; stroke < 40; stroke++)
        {
            int layer = rng.Next(layers);
            int x0 = rng.Next(Size), y0 = rng.Next(Size);
            var rect = new Rectangle(x0, y0, rng.Next(1, 7), rng.Next(1, 7));
            var area = Rectangle.Intersect(rect, new Rectangle(0, 0, Size, Size));
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int k = ((y * Size) + x) * 2;
                    stack.Depth[layer][k] = (byte)rng.Next(12);
                    stack.Depth[layer][k + 1] = (byte)rng.Next(12);
                }

            StackDepth.CombineInto(rise, stack.Readers(), rect);

            using var whole = StackDepth.Combine(stack.Maps());
            AssertSame(rise, whole);
        }
    }

    [Fact]
    public void A_rectangle_off_the_picture_changes_nothing_and_a_straddling_one_is_clipped()
    {
        var rng = new Random(9);
        using var stack = Build(2, rng);
        using var rise = StackDepth.Combine(stack.Maps());
        StackDepth.CombineInto(rise, stack.Readers(), new Rectangle(Size + 5, 0, 4, 4));
        using var whole = StackDepth.Combine(stack.Maps());
        AssertSame(rise, whole);

        stack.Depth[0][0] = 11;
        StackDepth.CombineInto(rise, stack.Readers(), new Rectangle(-3, -3, 6, 6));
        using var again = StackDepth.Combine(stack.Maps());
        AssertSame(rise, again);
    }
}
