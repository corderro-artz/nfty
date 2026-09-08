using System.Globalization;

namespace Nfty.Core.Generation;

/// <summary>
/// How a counted DNA space is written down, for every surface that shows one.
/// </summary>
/// <remarks>
/// <para><b>It lives in Core for the reason the reports in <c>Stats/</c> do.</b> The CLI's
/// <c>stats</c> line and the CookBook card's tile each carried their own copy of the same
/// three-branch decision, worded differently — one said "cannot be counted (the CookBook has
/// problems; run validate)" and the other said an em dash. Two copies of a rule is how they stop
/// agreeing, and this rule just gained a fourth branch.</para>
///
/// <para><b>The exact digits are always available, whatever the display shows.</b> That is the whole
/// point of the split below: a number the product refuses to print in full is a number the author
/// cannot check, and this figure is the one they are tuning quantize steps against.</para>
/// </remarks>
public static class SpaceText
{
    /// <summary>What is shown when nothing is known.</summary>
    public const string Unknown = "cannot be counted";

    /// <summary>
    /// The full figure, with thousands separators.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>e.g. <c>615,600</c>.</returns>
    /// <remarks>
    /// <b>Invariant</b>, like every figure this product prints: reports are copied into issues and
    /// diffed against a colleague's run, and a machine set to de-DE would render this as
    /// <c>615.600</c> and make two identical books look different.
    /// </remarks>
    public static string Exact(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Short scale, largest first. <see cref="long"/> tops out just past nine quintillion, so this
    /// is the whole ladder; there is deliberately no "million" rung — see <see cref="Compact"/>.
    /// </summary>
    private static readonly (long Unit, string Name)[] Scales =
    [
        (1_000_000_000_000_000_000L, "quintillion"),
        (1_000_000_000_000_000L, "quadrillion"),
        (1_000_000_000_000L, "trillion"),
        (1_000_000_000L, "billion"),
    ];

    /// <summary>
    /// The figure as a tile or a chip should show it: exact where it fits, named magnitude where it
    /// does not.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>e.g. <c>615,600</c>, <c>2,560,000</c>, or <c>4.72 billion</c>.</returns>
    /// <remarks>
    /// <para><b>The threshold is a billion, and it is deliberately high.</b> Two approaches were on
    /// the table: scientific notation everywhere (<c>2.56e6</c>), or a named magnitude. Scientific
    /// is compact and precise and is the wrong register for this product — it asks an artist sizing
    /// a collection to decode an exponent, and "1.2e7 versus 9.8e6" is a comparison nobody makes at
    /// a glance. A named magnitude reads the way the app already talks about itself.</para>
    ///
    /// <para>But the better idea is the third one: <b>degrade only when you must</b>. Everything
    /// below a billion prints in full, so essentially every real book shows its true figure, digit
    /// for digit — and the rounded form appears only where no tile could have held the digits
    /// anyway. Above the threshold the exact value is still one hover away; a surface using this is
    /// expected to put <see cref="Exact"/> on the tooltip.</para>
    /// </remarks>
    public static string Compact(long value)
    {
        if (value < 0) return Exact(value);
        foreach (var (unit, name) in Scales)
            if (value >= unit)
                return string.Create(CultureInfo.InvariantCulture, $"{value / (double)unit:0.##} {name}");
        return Exact(value);
    }

    /// <summary>
    /// The whole sentence a surface shows: the figure, and what kind of figure it is.
    /// </summary>
    /// <param name="count">The counted space.</param>
    /// <param name="compact">Whether to shorten a very large number
    /// (<see cref="Compact"/>) rather than print every digit (<see cref="Exact"/>). A tile does; a
    /// report does not.</param>
    /// <returns>Display text, never empty.</returns>
    /// <remarks>
    /// <b>"more than" and "at most" are different sentences, and that is the point.</b> Three
    /// outcomes used to share one bool and all three rendered as "more than N" — including the one
    /// where the real figure is SMALLER than what was printed. Naming the direction in
    /// <see cref="SpaceCertainty"/> is what makes each of them say something true.
    /// </remarks>
    public static string Describe(UniqueSpaceCount count, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(count);
        return Describe(count.Total, count.Certainty, compact);
    }

    /// <summary>
    /// <see cref="Describe(UniqueSpaceCount, bool)"/>, for one recipe's share.
    /// </summary>
    /// <param name="total">The figure.</param>
    /// <param name="certainty">What that figure is.</param>
    /// <param name="compact">Shorten a very large number.</param>
    /// <returns>Display text, never empty.</returns>
    public static string Describe(long total, SpaceCertainty certainty, bool compact = false)
    {
        string n = compact ? Compact(total) : Exact(total);
        return certainty switch
        {
            SpaceCertainty.Exact => n,
            SpaceCertainty.AtLeast => $"more than {n}",
            SpaceCertainty.AtMost => $"at most {n}",
            _ => Unknown,
        };
    }
}
