using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Imaging;

/// <summary>Which corner a stamp sits in.</summary>
public enum StampCorner
{
    /// <summary>Top left.</summary>
    TopLeft,

    /// <summary>Top right.</summary>
    TopRight,

    /// <summary>Bottom left.</summary>
    BottomLeft,

    /// <summary>Bottom right.</summary>
    BottomRight,
}

/// <summary>
/// Stamps an asset's set number into a corner, for telling loose sprites apart.
/// </summary>
/// <remarks>
/// <para><b>The digits are PIXELS, not a typeface, and that is the whole design.</b> Every image
/// this project touches is generated pixel art shown at a size unrelated to its pixel count, and
/// nothing anywhere resamples, blurs or anti-aliases. A real font would need
/// <c>SixLabors.Fonts</c> and <c>ImageSharp.Drawing</c> — a new dependency beside an ImageSharp
/// deliberately pinned at 3.1.11 — and would render a grey, anti-aliased number onto art that has
/// no grey in it. A 3x5 bitmap font is twelve constants, needs nothing, and scales by an integer
/// factor, so a stamped asset is still exactly the kind of image the rest of the product promises.
/// </para>
///
/// <para><b>It is a DEBUG mark and it is destructive, so it only ever happens on the way out.</b>
/// Nothing here writes back into a Set: the export renders a stamped copy and ships that, which is
/// why the same option cannot quietly corrupt the author's collection. The number stamped is the
/// asset's own set number, so a folder of loose sprites sorts and cross-references against
/// <c>nfty/NNNN.json</c> without anything else being recorded.</para>
///
/// <para><b>White on black, both drawn.</b> An asset can be any colour, including white, and a bare
/// white number vanishes on pale art while a bare black one vanishes on dark art. The glyph is
/// drawn in white over a one-pixel black halo, which is legible on every ground and costs nothing
/// but a second pass over the same cells.</para>
/// </remarks>
public static class NumberStamp
{
    /// <summary>One glyph's width in font pixels.</summary>
    private const int GlyphWidth = 3;

    /// <summary>One glyph's height in font pixels.</summary>
    private const int GlyphHeight = 5;

    /// <summary>Font pixels between two glyphs.</summary>
    private const int Tracking = 1;

    /// <summary>
    /// The digits, one row of bits per line, most significant bit leftmost.
    /// </summary>
    /// <remarks>
    /// Written out as binary literals so the glyph is legible in the source as the shape it draws —
    /// the same reason the icon SVGs are the drawings and <c>Icons.axaml</c> is generated from them.
    /// </remarks>
    private static readonly byte[][] Digits =
    [
        [0b111, 0b101, 0b101, 0b101, 0b111],   // 0
        [0b010, 0b110, 0b010, 0b010, 0b111],   // 1
        [0b111, 0b001, 0b111, 0b100, 0b111],   // 2
        [0b111, 0b001, 0b111, 0b001, 0b111],   // 3
        [0b101, 0b101, 0b111, 0b001, 0b001],   // 4
        [0b111, 0b100, 0b111, 0b001, 0b111],   // 5
        [0b111, 0b100, 0b111, 0b101, 0b111],   // 6
        [0b111, 0b001, 0b001, 0b001, 0b001],   // 7
        [0b111, 0b101, 0b111, 0b101, 0b111],   // 8
        [0b111, 0b101, 0b111, 0b001, 0b111],   // 9
    ];

    /// <summary>
    /// How large one font pixel is drawn on a canvas of this size.
    /// </summary>
    /// <param name="canvasWidth">The art's width.</param>
    /// <param name="canvasHeight">The art's height.</param>
    /// <returns>An integer scale, at least 1.</returns>
    /// <remarks>
    /// <b>Derived rather than chosen, and capped at both ends.</b> A stamp fixed at one pixel is
    /// invisible on a 1000px asset and a stamp fixed at eight covers a 16px one entirely. A
    /// thirty-secondth of the short side keeps it roughly the same fraction of the picture at every
    /// canvas size; the floor of 1 is what a small sprite needs and the ceiling of 8 is where a
    /// debug mark stops being a mark and starts being the image.
    /// </remarks>
    public static int ScaleFor(int canvasWidth, int canvasHeight) =>
        Math.Clamp(Math.Min(canvasWidth, canvasHeight) / 32, 1, 8);

    /// <summary>
    /// Draws a number into one corner of an image, in place.
    /// </summary>
    /// <param name="image">The image to stamp. Modified.</param>
    /// <param name="number">The number to draw. Negative is drawn as its absolute value.</param>
    /// <param name="corner">Which corner it sits in.</param>
    /// <param name="scale">Font pixel size, or null for <see cref="ScaleFor"/>.</param>
    /// <remarks>
    /// <b>It draws nothing rather than clipping</b> when the stamp will not fit the canvas. A
    /// half-drawn number is worse than none: the point of the mark is that it can be read, and a
    /// "12" cropped to "1" on a tiny sprite is a wrong answer rather than a missing one.
    /// </remarks>
    public static void Draw(Image<Rgba32> image, int number, StampCorner corner, int? scale = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        string text = Math.Abs((long)number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        int px = scale ?? ScaleFor(image.Width, image.Height);

        int inkWidth = (text.Length * GlyphWidth + (text.Length - 1) * Tracking) * px;
        int inkHeight = GlyphHeight * px;
        int margin = px;

        // The halo adds a pixel on every side, so the footprint is what has to fit, not the ink.
        if (inkWidth + 2 * (margin + px) > image.Width || inkHeight + 2 * (margin + px) > image.Height)
            return;

        int left = corner is StampCorner.TopLeft or StampCorner.BottomLeft
            ? margin
            : image.Width - inkWidth - margin;
        int top = corner is StampCorner.TopLeft or StampCorner.TopRight
            ? margin
            : image.Height - inkHeight - margin;

        var halo = new Rgba32(0, 0, 0, 255);
        var ink = new Rgba32(255, 255, 255, 255);

        // TWO PASSES, and the order matters: the halo is every lit cell grown by one pixel in each
        // direction, so drawing the ink first would let the next glyph's halo eat the previous
        // glyph's stroke.
        Stamp(image, text, left, top, px, halo, outline: true);
        Stamp(image, text, left, top, px, ink, outline: false);
    }

    /// <summary>Draws the text once, either as its outline or as its ink.</summary>
    private static void Stamp(Image<Rgba32> image, string text, int left, int top, int px,
        Rgba32 color, bool outline)
    {
        for (int c = 0; c < text.Length; c++)
        {
            if (text[c] is < '0' or > '9') continue;
            var glyph = Digits[text[c] - '0'];
            int originX = left + c * (GlyphWidth + Tracking) * px;

            for (int row = 0; row < GlyphHeight; row++)
            {
                for (int col = 0; col < GlyphWidth; col++)
                {
                    if ((glyph[row] & (1 << (GlyphWidth - 1 - col))) == 0) continue;

                    int x = originX + col * px;
                    int y = top + row * px;
                    if (outline) FillRect(image, x - px, y - px, 3 * px, 3 * px, color);
                    else FillRect(image, x, y, px, px, color);
                }
            }
        }
    }

    /// <summary>Fills a rectangle, clipped to the image. Whole pixels only — nothing blends.</summary>
    private static void FillRect(Image<Rgba32> image, int x, int y, int w, int h, Rgba32 color)
    {
        int x0 = Math.Max(0, x), y0 = Math.Max(0, y);
        int x1 = Math.Min(image.Width, x + w), y1 = Math.Min(image.Height, y + h);

        for (int yy = y0; yy < y1; yy++)
            for (int xx = x0; xx < x1; xx++)
                image[xx, yy] = color;
    }
}
