using System.IO;
using Nfty.Core.Imaging;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// The set-number watermark. Every assertion reads PIXELS off a synthetic image, the way every
/// other imaging test here does — there are no golden files in this project.
/// </summary>
public class NumberStampTests
{
    private static Image<Rgba32> Blank(int size = 64) =>
        new(size, size, new Rgba32(20, 120, 90, 255));

    /// <summary>How many pixels in a quadrant are neither the ground nor its neighbours.</summary>
    private static int InkIn(Image<Rgba32> image, StampCorner corner)
    {
        var ground = new Rgba32(20, 120, 90, 255);
        int half = image.Width / 2, halfH = image.Height / 2;
        int x0 = corner is StampCorner.TopLeft or StampCorner.BottomLeft ? 0 : half;
        int y0 = corner is StampCorner.TopLeft or StampCorner.TopRight ? 0 : halfH;

        int n = 0;
        for (int y = y0; y < y0 + halfH; y++)
            for (int x = x0; x < x0 + half; x++)
                if (image[x, y] != ground) n++;
        return n;
    }

    [Theory]
    [InlineData(StampCorner.TopLeft)]
    [InlineData(StampCorner.TopRight)]
    [InlineData(StampCorner.BottomLeft)]
    [InlineData(StampCorner.BottomRight)]
    public void A_STAMP_LANDS_IN_THE_CORNER_IT_WAS_ASKED_FOR(StampCorner corner)
    {
        // Four corners, and the assertion is that the OTHER three are untouched - a stamp that drew
        // in the same place whatever it was told would pass a test that only looked where it asked.
        using var image = Blank();

        NumberStamp.Draw(image, 42, corner);

        Assert.True(InkIn(image, corner) > 0, "the chosen corner has no ink");
        foreach (StampCorner other in Enum.GetValues<StampCorner>())
            if (other != corner)
                Assert.Equal(0, InkIn(image, other));
    }

    [Fact]
    public void Different_numbers_draw_differently()
    {
        // The glyphs are twelve constants in a table and a transposed row would still draw SOMETHING
        // in the right corner. This is what says the table is being read.
        using var one = Blank();
        using var two = Blank();

        NumberStamp.Draw(one, 1, StampCorner.TopLeft);
        NumberStamp.Draw(two, 8, StampCorner.TopLeft);

        Assert.True(InkIn(two, StampCorner.TopLeft) > InkIn(one, StampCorner.TopLeft),
            "an 8 is a denser glyph than a 1");
    }

    [Fact]
    public void Every_pixel_it_writes_is_opaque_black_or_white()
    {
        // NOTHING BLENDS. This project resamples, blurs and anti-aliases nowhere, and a font would
        // have put grey on art that has no grey in it - which is the whole reason the digits are a
        // bitmap rather than a typeface.
        using var image = Blank();
        var ground = new Rgba32(20, 120, 90, 255);

        NumberStamp.Draw(image, 1234567890, StampCorner.TopLeft);

        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var p = image[x, y];
                if (p == ground) continue;
                Assert.True(p == new Rgba32(0, 0, 0, 255) || p == new Rgba32(255, 255, 255, 255),
                    $"({x},{y}) is {p}, which is neither the halo nor the ink");
            }
    }

    [Fact]
    public void A_stamp_too_big_for_the_canvas_draws_NOTHING_rather_than_half_a_number()
    {
        // A "12" cropped to "1" is a WRONG answer where a missing stamp is merely a missing one, and
        // the mark exists to be read.
        using var tiny = new Image<Rgba32>(4, 4, new Rgba32(20, 120, 90, 255));

        NumberStamp.Draw(tiny, 12345, StampCorner.TopLeft);

        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                Assert.Equal(new Rgba32(20, 120, 90, 255), tiny[x, y]);
    }

    [Fact]
    public void The_scale_follows_the_canvas_and_is_capped_at_both_ends()
    {
        // Fixed at one it is invisible on a 1000px asset; fixed at eight it covers a 16px one.
        Assert.Equal(1, NumberStamp.ScaleFor(16, 16));
        Assert.Equal(2, NumberStamp.ScaleFor(64, 64));
        Assert.Equal(8, NumberStamp.ScaleFor(1000, 1000));
        Assert.Equal(1, NumberStamp.ScaleFor(1000, 8));      // the SHORT side decides
    }

    [Fact]
    public void AN_EXPORT_STAMPS_ITS_COPY_AND_NEVER_THE_SOURCE()
    {
        // The stamp is destructive and irreversible - the number is painted into the pixels - so the
        // author's Set has to come out of a stamped export byte for byte unchanged.
        string dir = SpriteSheetFixture.Cook(4, canvas: 64);
        string outDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var before = SetReader.Read(dir);
            byte[] original = File.ReadAllBytes(before.Items[0].ImagePath);

            var result = SetExporter.Export(dir, outDir, new ExportOptions
            {
                NumberWatermark = true,
                WatermarkCorner = StampCorner.TopLeft,
                Shape = ExportShape.Folder,
            });

            Assert.Equal(original, File.ReadAllBytes(before.Items[0].ImagePath));

            string exported = Path.Combine(result.Path, "images", "0001.png");
            Assert.True(File.Exists(exported));
            Assert.NotEqual(original, File.ReadAllBytes(exported));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public void A_stamped_export_stitches_its_sheet_from_the_stamped_art()
    {
        // The one combination nobody wants is a clean sheet beside numbered sprites: the sheet is
        // where the numbering is easiest to read, which is most of why anyone asks for it.
        string dir = SpriteSheetFixture.Cook(4, canvas: 64);
        string plainOut = Directory.CreateTempSubdirectory().FullName;
        string stampedOut = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var plain = SetExporter.Export(dir, plainOut, new ExportOptions
            {
                SpriteSheet = true, Shape = ExportShape.Folder,
            });
            var stamped = SetExporter.Export(dir, stampedOut, new ExportOptions
            {
                SpriteSheet = true, NumberWatermark = true, Shape = ExportShape.Folder,
            });

            byte[] a = File.ReadAllBytes(Path.Combine(plain.Path, "spritesheet.png"));
            byte[] b = File.ReadAllBytes(Path.Combine(stamped.Path, "spritesheet.png"));

            Assert.NotEqual(a, b);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
            Directory.Delete(plainOut, recursive: true);
            Directory.Delete(stampedOut, recursive: true);
        }
    }
}
