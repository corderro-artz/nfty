namespace Nfty.Core.Generation;

/// <summary>
/// The one place a rolled color is folded into the quantized bucket that identifies it.
///
/// <para>Two callers must agree exactly: <see cref="Dna"/>, which decides whether two assets are the
/// same asset, and <see cref="UniqueSpace"/>, which promises how many distinct assets exist. They
/// used to compute it separately and disagreed, because <see cref="ColorRoller.Roll"/> divides
/// saturation by 100 and the DNA multiplies it back — and that round-trip is not the identity:
/// <c>(29/100.0)*100.0</c> is <c>28.999999999999996</c>, which floors to 28, not 29. The counter
/// worked from the raw percentage and said 29. On a book <c>Validator</c> called clean, the count
/// over-promised and <c>Generate</c> failed with the self-contradicting "this cookbook allows
/// exactly N unique DNA, but N were requested".</para>
///
/// <para>Sharing one function is the fix: equivalent-looking arithmetic in two files is what broke,
/// so the arithmetic now exists once. <see cref="UniqueSpace"/> reaches saturation through
/// <see cref="ColorRoller.SampleSat"/> and then <see cref="Sat"/>, rather than shortcutting from the
/// stored percentage to <c>percent / quantize</c> — the <c>/100</c> round-trip's rounding is
/// <em>load-bearing</em>, because it is what the shipped DNA of every Set ever generated already
/// encodes. Skipping it would be the tidier arithmetic and would silently invalidate every existing
/// collection.</para>
/// </summary>
public static class ColorBuckets
{
    /// <summary>The bucket a hue lands in. Hue needs no round-trip — it is carried in degrees end to
    /// end — so this is a plain floor-divide.</summary>
    /// <param name="hue">Hue in degrees, as <see cref="ColorRoller.Roll"/> produces it.</param>
    /// <param name="quantize">Bucket width in degrees; values below 1 are treated as 1.</param>
    /// <returns>The zero-based bucket index.</returns>
    public static long Hue(double hue, int quantize) =>
        (long)Math.Floor(hue / Math.Max(1, quantize));

    /// <summary>The bucket a saturation lands in, taking saturation in the 0..1 form
    /// <see cref="RolledColor.S"/> carries.</summary>
    /// <param name="saturation">Saturation as a 0..1 fraction.</param>
    /// <param name="quantize">Bucket width in percentage points; values below 1 are treated as 1.</param>
    /// <returns>The zero-based bucket index.</returns>
    public static long Sat(double saturation, int quantize) =>
        (long)Math.Floor(saturation * 100.0 / Math.Max(1, quantize));
}
