using System.Globalization;

namespace Nfty.Core.Stats;

/// <summary>
/// How a share is written down, for every surface that shows one.
/// </summary>
/// <remarks>
/// <para><b>It exists for the reason <c>SpaceText</c> does, and it was needed for the same reason
/// too.</b> Three surfaces printed a rarity and each carried its own copy of the decision: the
/// report formatted <c>0.00</c>, the ingredient pane rounded to one place in its view model and
/// interpolated the bare double, and the Set browser's converter interpolated it bare as well.
/// Two of the three were CULTURE-SENSITIVE — string interpolation and Avalonia's
/// <c>StringFormat</c> both take the current culture — so a machine set to de-DE printed
/// <c>4,17%</c> on a screen whose own comment said every figure it shows is invariant.</para>
///
/// <para><b>A rare trait is the case this is really about.</b> Rounding a share to two places and
/// printing the result turns every trait under a hundredth of a percent into <c>0%</c> — the same
/// string an ABSENT trait gets — so a one-in-a-million chase item and a trait no asset carries read
/// identically. That matters more the bigger a collection is, which is precisely when someone is
/// looking. <see cref="Percent"/> floors at <c>&lt;0.01%</c> instead: five characters, so a fixed
/// column still lines up, and unmistakably not zero.</para>
/// </remarks>
public static class RarityText
{
    /// <summary>What is shown where there is no share to show.</summary>
    public const string None = "—";

    /// <summary>The smallest share printed as a figure; anything smaller floors to a marker.</summary>
    /// <remarks>Two decimal places is the precision <c>RarityAttribute</c> is already stored at, so
    /// this is the resolution the data actually carries rather than a display choice made here.</remarks>
    public const double Smallest = 0.01;

    /// <summary>
    /// A share, 0..100, as every surface prints one.
    /// </summary>
    /// <param name="percent">The share, out of 100.</param>
    /// <returns>e.g. <c>50%</c>, <c>4.17%</c>, <c>&lt;0.01%</c>, or <c>0%</c>.</returns>
    /// <remarks>
    /// Trailing zeros are dropped (<c>0.##</c>) because these are read down a column of mixed
    /// magnitudes rather than compared digit for digit — the opposite of the DNA-space figures,
    /// which state a fixed mantissa so their decimal points line up. Non-finite input prints as no
    /// share at all: a share that is not a number is not a small number.
    /// </remarks>
    public static string Percent(double percent)
    {
        if (!double.IsFinite(percent)) return None;
        if (percent <= 0) return "0%";
        if (percent < Smallest)
            return string.Create(CultureInfo.InvariantCulture, $"<{Smallest:0.##}%");
        return string.Create(CultureInfo.InvariantCulture, $"{percent:0.##}%");
    }

    /// <summary>
    /// The same share read as odds: one in this many.
    /// </summary>
    /// <param name="percent">The share, out of 100.</param>
    /// <returns>e.g. <c>1 in 24</c>, or <see cref="None"/> when there are no odds to state.</returns>
    /// <remarks>
    /// <para><b>A percentage compares traits to each other; odds are what "how rare is this one"
    /// actually asks.</b> 4.17% against 2.08% reads as a near-miss where "1 in 24" against "1 in 48"
    /// does not. Derived from the percentage rather than stored beside it, so the two cannot drift.
    /// </para>
    ///
    /// <para><b>Thousands-separated</b>, which is where this came from: the Set browser's converter
    /// printed <c>1 in 100000</c> while the combined-chance line one inch above it printed
    /// <c>1 in 100,000</c> through <c>SelectionOdds.Describe</c> — the same unit, on the same rail,
    /// in two shapes. Only visible once the odds reach five digits, so only on a big collection.
    /// </para>
    ///
    /// <para>A zero share has no odds and gets <see cref="None"/>: a trait no asset carries is not
    /// "1 in infinity", and infinity is not an answer anyone wants on a card.</para>
    /// </remarks>
    public static string Odds(double percent)
    {
        if (!double.IsFinite(percent) || percent <= 0) return None;
        double one = Math.Round(100.0 / percent, MidpointRounding.AwayFromZero);

        // 100.0/percent is finite for every positive finite input, but it can exceed what a long
        // holds once the share is denormal-small, and "N0" on a double past 2^53 prints digits it
        // does not have. Past a trillion the digits are noise anyway; say so instead.
        if (one >= 1e12) return "1 in over a trillion";
        return string.Create(CultureInfo.InvariantCulture, $"1 in {one:N0}");
    }
}
