using System.Reflection;
using Nfty.Core.Editing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// The whole justification for making the paint stack generic rather than adding a parallel color
/// command set: a ValueMap must remain incapable of holding independent R/G/B, no matter which
/// command paints it or which opacity mode it paints in.
/// </summary>
public class GrayscaleGuaranteeTests
{
    // Every command, run over one map, in one opacity mode. Deliberately exercises the two that take
    // their payload from the surface rather than from a brush (erase, move) alongside the three that
    // carry one, because those are the paths a color could sneak in through.
    private static ValueMap PaintedThroughEveryCommand(OpacityLock opacity)
    {
        var map = new ValueMap(6, 6);
        IEditSurface<GrayPixel> surface = map;   // paint only through the generic seam

        new BrushStroke<GrayPixel>(new Brush<GrayPixel>(3, new GrayPixel(200, 255)),
            new[] { (2, 2), (3, 2) }, opacity).Apply(surface);
        new DrawShape<GrayPixel>(ShapeKind.Ellipse, new PixelRect(0, 0, 4, 4),
            new GrayPixel(90, 180), opacity).Apply(surface);
        new DrawShape<GrayPixel>(ShapeKind.Triangle, new PixelRect(1, 1, 4, 4),
            new GrayPixel(30, 60), opacity).Apply(surface);
        new FloodFill<GrayPixel>(5, 5, new GrayPixel(140, 200), opacity).Apply(surface);
        new EraseStroke<GrayPixel>(2, new[] { (4, 4) }, opacity).Apply(surface);
        new MoveSelection<GrayPixel>(new PixelRect(0, 0, 3, 3), 2, 1, opacity).Apply(surface);
        return map;
    }

    [Theory]
    [InlineData(OpacityLock.Locked)]
    [InlineData(OpacityLock.Unlocked)]
    public void A_value_map_cannot_become_non_gray_through_any_command(OpacityLock opacity)
    {
        using Image<Rgba32> img = PaintedThroughEveryCommand(opacity).ToImage();
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                Rgba32 p = img[x, y];
                Assert.Equal(p.R, p.G);
                Assert.Equal(p.G, p.B);
            }
    }

    [Fact]
    public void A_value_map_is_only_ever_a_grayscale_surface()
    {
        Assert.True(typeof(IEditSurface<GrayPixel>).IsAssignableFrom(typeof(ValueMap)));
        // If it were also an Rgba32 surface, every color command would accept it and the guarantee
        // would rest on nobody ever writing `new BrushStroke<Rgba32>(...).Apply(valueMap)`.
        Assert.False(typeof(IEditSurface<Rgba32>).IsAssignableFrom(typeof(ValueMap)));
    }

    [Fact]
    public void No_public_value_map_member_moves_a_color_pixel_in_or_out()
    {
        // Whole-image import/export (Image<Rgba32>) is the documented boundary and stays. What must
        // not exist is a per-pixel color path — a Set/Get/ctor taking or returning an Rgba32 — since
        // that is the one shape that could store independent R/G/B.
        var offenders = new List<string>();
        foreach (var m in typeof(ValueMap).GetMembers(BindingFlags.Public | BindingFlags.Instance |
                                                      BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            if (m is MethodBase mb)
            {
                if (mb.GetParameters().Any(p => p.ParameterType == typeof(Rgba32))) offenders.Add(mb.Name);
                if (mb is MethodInfo mi && mi.ReturnType == typeof(Rgba32)) offenders.Add(mi.Name);
            }
            else if (m is PropertyInfo pi && pi.PropertyType == typeof(Rgba32)) offenders.Add(pi.Name);
            else if (m is FieldInfo fi && fi.FieldType == typeof(Rgba32)) offenders.Add(fi.Name);
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void The_gray_pixel_has_one_value_channel_and_one_alpha()
    {
        // GrayPixel is what carries the guarantee into the generic stack: if it ever grew a second
        // color component, every command would be able to make a value-map non-gray.
        var components = typeof(GrayPixel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Alpha", "Value" }, components);
    }
}
