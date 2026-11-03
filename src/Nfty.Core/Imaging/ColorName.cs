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

    /// <summary>The trait type a layer's color is published under: the layer's name and <c> Color</c>.</summary>
    /// <param name="layerName">The layer's display name.</param>
    /// <returns>For example <c>Body Color</c>.</returns>
    public static string TraitTypeOf(string layerName) => layerName + Suffix;

    /// <summary>The words appended to a layer's name to make its color trait.</summary>
    public const string Suffix = " Color";
}
