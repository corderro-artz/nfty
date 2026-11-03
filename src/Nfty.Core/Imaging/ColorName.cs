using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Core.Imaging;

/// <summary>
/// The one table that turns a rolled color into the word a marketplace shows for it.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A Dynamic layer's color is rolled per asset, but the published
/// OpenSea attributes carried only the variant's name, so two assets that differed ONLY in a layer's
/// hue had identical traits: a buyer could not filter by color and a rarity table could not rank it.
/// The color becomes a trait, <c>"{Layer} Color": "Pale Blue"</c>, and this is the mapping.</para>
///
/// <para><b>The table is small and fixed on purpose.</b> Twelve hue sectors centered on 0, 30, ... 330
/// degrees (boundaries at 15 + 30k, half-open <c>[start, end)</c> so every hue has exactly one name
/// and 15 itself is Orange, 345 is Red); saturation below <see cref="GrayBelow"/> is <c>Gray</c>
/// whatever the hue; saturation from <see cref="GrayBelow"/> up to <see cref="PaleBelow"/> prefixes
/// <c>Pale </c>. A name is a coarse bucket of the continuous color, which is what a trait is for:
/// the exact rolled color stays in <c>nfty/NNNN.json</c>.</para>
///
/// <para><b>It reads the UNROUNDED rolled (H, S)</b> (<c>exactH</c>/<c>exactS</c>), never the 0.1 degree
/// and 0.001 rounded figures printed for humans, so a color sitting on a boundary is named by what
/// was rolled and not by how it was rounded. Pure, culture-free, and independent of the DNA: names
/// never enter the hash.</para>
/// </remarks>
public static class ColorName
{
    /// <summary>Saturation below this is <c>Gray</c>, whatever the hue.</summary>
    public const double GrayBelow = 0.12;

    /// <summary>Saturation below this (and from <see cref="GrayBelow"/>) is <c>Pale</c>.</summary>
    public const double PaleBelow = 0.35;

    /// <summary>The word for a gray.</summary>
    public const string Gray = "Gray";

    /// <summary>The twelve hue names; sector <c>i</c> is centered on <c>30 * i</c> degrees.</summary>
    public static IReadOnlyList<string> Hues { get; } =
    [
        "Red", "Orange", "Yellow", "Lime", "Green", "Mint",
        "Cyan", "Azure", "Blue", "Violet", "Purple", "Magenta",
    ];

    /// <summary>Names a color.</summary>
    /// <param name="hue">Hue in degrees; any finite value, wrapped into [0, 360).</param>
    /// <param name="saturation">Saturation as a 0..1 fraction.</param>
    /// <returns>For example <c>Red</c>, <c>Pale Mint</c> or <c>Gray</c>.</returns>
    /// <remarks>A non-finite input is <c>Gray</c>: nothing meaningful can be said of it, and a metadata
    /// writer must not throw on a color it was handed.</remarks>
    public static string Of(double hue, double saturation)
    {
        if (!double.IsFinite(hue) || !double.IsFinite(saturation) || saturation < GrayBelow) return Gray;

        double wrapped = hue % 360.0;
        if (wrapped < 0) wrapped += 360.0;

        // Boundaries sit at 15 + 30k, so shifting by 15 puts them on multiples of 30 and the sector
        // is a floor. A hue that rounds to exactly 360 after the shift wraps back to Red.
        int sector = (int)Math.Floor((wrapped + 15.0) / 30.0) % 12;
        string name = Hues[sector];
        return saturation < PaleBelow ? "Pale " + name : name;
    }

    /// <summary>
    /// The exact chance that one roll of <paramref name="colorization"/> is published under each color
    /// name: the book-side twin of <see cref="Of"/>, so the predicted rarity table and the cooked one
    /// speak about the same buckets.
    /// </summary>
    /// <param name="colorization">The layer's colorization (Dynamic or Static).</param>
    /// <returns>Name to probability, only names that can occur, ordinal-sorted by name, summing to 1.
    /// Empty when the entries carry no weight (the roller itself refuses that colorization).</returns>
    /// <remarks>
    /// <para>Matches <c>ColorRoller.Roll</c> exactly: an entry is picked by weight
    /// (<c>w/W</c>; a zero-weight entry never), a ranged entry then samples hue and saturation
    /// <b>independently</b>, each as <c>Min + r*(Max-Min)</c> with <c>r</c> in <c>[0,1)</c>, so each
    /// is uniform on the half-open interval and a name's chance is
    /// <c>|H and sector| / |H| * |S and band| / |S|</c>. A degenerate axis (<c>Min == Max</c>) is a
    /// point mass, named by the very comparisons <see cref="Of"/> makes. A fixed entry is a point
    /// mass on <c>ColorRoller.FromFixed</c>. Red's sector wraps (345..360 and 0..15), and a hue range
    /// wider than a turn counts every lap. The color model does not enter: it only decides how a
    /// fixed spec is read, and ranges are stated in the model's own H and S.</para>
    /// <para>Sums run in entry order and sector order, sequentially, so the figures are identical on
    /// every machine.</para>
    /// </remarks>
    public static IReadOnlyDictionary<string, double> Shares(Colorization colorization)
    {
        ArgumentNullException.ThrowIfNull(colorization);
        var shares = new SortedDictionary<string, double>(StringComparer.Ordinal);

        double total = 0;
        foreach (var e in colorization.Entries) total += Math.Max(0, e.Weight);
        if (total <= 0) return shares;

        foreach (var e in colorization.Entries)
        {
            double weight = Math.Max(0, e.Weight) / total;
            if (weight <= 0) continue;

            if (e.Fixed is not null)
            {
                var c = ColorRoller.FromFixed(e.Fixed, colorization.Model);
                Add(shares, Of(c.H, c.S), weight);
            }
            else if (e.Range is { } range)
            {
                AddRange(shares, range, weight);
            }
        }

        return shares;
    }

    private static void Add(SortedDictionary<string, double> shares, string name, double p)
    {
        if (p <= 0) return;
        shares[name] = shares.GetValueOrDefault(name) + p;
    }

    private static void AddRange(SortedDictionary<string, double> shares, ColorRange range, double weight)
    {
        // Saturation as the 0..1 fractions SampleSat produces: gray [.., 0.12), pale [0.12, 0.35), full [0.35, ..).
        double satLo = ColorRoller.SampleSat(range, 0), satHi = range.SatMax / 100.0;
        double gray, pale, full;
        if (range.SatMax == range.SatMin)
        {
            gray = satLo < GrayBelow ? 1 : 0;
            pale = gray == 0 && satLo < PaleBelow ? 1 : 0;
            full = gray == 0 && pale == 0 ? 1 : 0;
        }
        else
        {
            double width = satHi - satLo;
            gray = Overlap(satLo, satHi, double.NegativeInfinity, GrayBelow) / width;
            pale = Overlap(satLo, satHi, GrayBelow, PaleBelow) / width;
            full = Overlap(satLo, satHi, PaleBelow, double.PositiveInfinity) / width;
        }

        // Gray ignores hue, so it is taken before the hue is split.
        Add(shares, Gray, weight * gray);
        if (pale <= 0 && full <= 0) return;

        var hue = new double[Hues.Count];
        double hueLo = range.HueMin, hueHi = range.HueMax;
        if (hueHi == hueLo)
        {
            hue[SectorOf(hueLo)] = 1;
        }
        else
        {
            double width = hueHi - hueLo;
            // Sector k is [30k - 15, 30k + 15) repeated every 360 degrees; visit each lap the range touches.
            int firstLap = (int)Math.Floor((hueLo + 15.0) / 360.0) - 1;
            int lastLap = (int)Math.Floor((hueHi + 15.0) / 360.0) + 1;
            for (int lap = firstLap; lap <= lastLap; lap++)
                for (int k = 0; k < Hues.Count; k++)
                {
                    double start = 30.0 * k - 15.0 + 360.0 * lap;
                    hue[k] += Overlap(hueLo, hueHi, start, start + 30.0) / width;
                }
        }

        for (int k = 0; k < Hues.Count; k++)
        {
            Add(shares, "Pale " + Hues[k], weight * pale * hue[k]);
            Add(shares, Hues[k], weight * full * hue[k]);
        }
    }

    private static int SectorOf(double hue)
    {
        double wrapped = hue % 360.0;
        if (wrapped < 0) wrapped += 360.0;
        return (int)Math.Floor((wrapped + 15.0) / 30.0) % 12;
    }

    private static double Overlap(double aLo, double aHi, double bLo, double bHi) =>
        Math.Max(0, Math.Min(aHi, bHi) - Math.Max(aLo, bLo));

    /// <summary>The trait type a layer's color is published under: the layer's name and <c> Color</c>.</summary>
    /// <param name="layerName">The layer's display name.</param>
    /// <returns>For example <c>Body Color</c>.</returns>
    public static string TraitTypeOf(string layerName) => layerName + Suffix;

    /// <summary>The words appended to a layer's name to make its color trait.</summary>
    public const string Suffix = " Color";
}
