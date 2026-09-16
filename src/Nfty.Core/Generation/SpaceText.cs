using System.Globalization;
using System.Numerics;

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
/// cannot check, and this figure is the one they are tuning quantize steps against. Every surface
/// that shortens is expected to put <see cref="Exact"/> on its tooltip — unconditionally, so a
/// reader learns once that hovering answers the question.</para>
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
    public static string Exact(BigInteger value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Short scale, largest first, stopping where the names stop being read.
    /// </summary>
    /// <remarks>
    /// <b>The ladder ends at quintillion deliberately.</b> That is where it already ended when these
    /// totals were <see cref="long"/>, so no figure this product has ever printed changes its
    /// wording — and past it the named magnitudes are ones a reader has to stop and decode.
    /// "Sextillion" is not more legible than an exponent; it is less, because the exponent is the
    /// thing actually being compared at that size. See <see cref="Compact"/>.
    /// </remarks>
    private static readonly (BigInteger Unit, string Name)[] Scales =
    [
        (BigInteger.Pow(10, 18), "quintillion"),
        (BigInteger.Pow(10, 15), "quadrillion"),
        (BigInteger.Pow(10, 12), "trillion"),
        (BigInteger.Pow(10, 9), "billion"),
    ];

    /// <summary>
    /// The figure as a tile or a chip should show it: exact where it fits, named magnitude where it
    /// does not, and an exponent where even that stops meaning anything.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>e.g. <c>615,600</c>, <c>2,560,000</c>, <c>4.72 billion</c>, or <c>2.18e21</c>.</returns>
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
    /// anyway. The exact value is always one hover away.</para>
    ///
    /// <para><b>Past a thousand quintillion the register changes, and that is the same rule applied
    /// once more.</b> A <see cref="BigInteger"/> total has no ceiling, so a deep book really can
    /// reach <c>2.18e21</c> — six layers of ten variants at a 10°/10% quantize does. The argument
    /// against scientific notation was an argument about MILLIONS, where a name is plainly easier;
    /// at this size there is no name a reader knows, so the exponent is the more legible of the two
    /// and is also precisely what one is comparing.</para>
    ///
    /// <para><b>The mantissa is always two decimal places, including <c>1.00 billion</c>.</b> These
    /// figures are read as a COLUMN — one row per recipe, stacked — and <c>0.##</c> drops a trailing
    /// zero, so a book of 17.744 and one of 12.900 trillion printed as "17.74 trillion" over "12.9
    /// trillion" and the decimal points did not line up. A number that changes shape with its value
    /// is the same defect the colorize range endpoints were fixed for; a fixed mantissa costs one
    /// redundant zero on a round figure and buys a column that reads straight down.</para>
    ///
    /// <para><b>The rung is chosen AFTER rounding, not before.</b> Picking it first and rounding
    /// afterwards printed <c>999,999,999,999</c> as "1000.00 billion" — a mantissa that has left its
    /// own rung, in a column whose whole purpose is that the rungs line up. Every value from
    /// 999,995,000,000 up hit it, and a real book reaches that. Rounding first and promoting when
    /// the mantissa reaches 1000 makes the pair consistent by construction.</para>
    /// </remarks>
    public static string Compact(BigInteger value)
    {
        if (value.Sign < 0) return Exact(value);

        // Largest rung first, so the first one the value reaches is its own.
        for (int i = 0; i < Scales.Length; i++)
        {
            var (unit, name) = Scales[i];
            if (value < unit) continue;

            double mantissa = Round2((double)value / (double)unit);
            if (mantissa < 1000)
                return string.Create(CultureInfo.InvariantCulture, $"{mantissa:0.00} {name}");

            // Rounding carried the mantissa off the top of its own rung. Promote it to the next one
            // up, where it is 1.00 by construction. Above the last rung there is no next one, so
            // fall through to the exponent rather than print "1000.00 quintillion".
            if (i > 0) return $"1.00 {Scales[i - 1].Name}";
            break;
        }

        return value < Scales[^1].Unit ? Exact(value) : Scientific(value);
    }

    /// <summary>
    /// The figure to three significant digits and a power of ten, for a number past the named
    /// magnitudes.
    /// </summary>
    /// <param name="value">The count.</param>
    /// <returns>e.g. <c>2.18e21</c>.</returns>
    /// <remarks>
    /// <b>Read off the decimal digits rather than divided down.</b> Dividing needs
    /// <c>10^exponent</c> as a <see cref="double"/>, which is infinity past about <c>1e308</c> — so
    /// the arithmetic that exists to describe an unbounded number would itself have a bound, one
    /// rung further out. The digit string always carries the answer in its first four characters.
    /// </remarks>
    private static string Scientific(BigInteger value)
    {
        string digits = BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture);
        int exponent = digits.Length - 1;

        int take = Math.Min(4, digits.Length);
        double mantissa = Round2(
            double.Parse(digits[..take], CultureInfo.InvariantCulture) / Math.Pow(10, take - 1));

        // 9.999 rounds to 10.00, which is one rung out by definition.
        if (mantissa >= 10) { mantissa /= 10; exponent++; }

        string sign = value.Sign < 0 ? "-" : "";
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{mantissa:0.00}e{exponent}");
    }

    /// <summary>Two decimal places, rounding halves away from zero as every figure here does.</summary>
    /// <param name="value">The mantissa.</param>
    /// <returns>The rounded mantissa.</returns>
    private static double Round2(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

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
    public static string Describe(BigInteger total, SpaceCertainty certainty, bool compact = false)
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
