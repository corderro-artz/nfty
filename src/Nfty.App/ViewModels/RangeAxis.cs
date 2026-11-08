namespace Nfty.App.ViewModels;

/// <summary>
/// The two colorize axes' own bounds, applied where a range endpoint is SET: hue 0..360, saturation
/// 0..100. The handles cannot leave the track, but the number boxes beside them could - typing 150
/// into a saturation box stored 150, and "NaN" parses as a double.
/// </summary>
internal static class RangeAxis
{
    /// <summary>The top of the hue axis, in degrees.</summary>
    public const double Hue = 360;

    /// <summary>The top of the saturation axis, in percent.</summary>
    public const double Sat = 100;

    /// <summary>True when a value lies off the axis <c>0..max</c> (NaN included).</summary>
    /// <param name="value">The proposed endpoint, including nonfinite values.</param>
    /// <param name="max">A finite, nonnegative inclusive maximum.</param>
    /// <returns>True for NaN or a value outside the inclusive axis, including either infinity.</returns>
    public static bool Off(double value, double max) => double.IsNaN(value) || value < 0 || value > max;

    /// <summary>The nearest point on the axis; NaN lands on 0.</summary>
    /// <param name="value">The proposed endpoint; NaN maps to zero and infinities to the nearest end.</param>
    /// <param name="max">A finite, nonnegative inclusive maximum.</param>
    /// <returns>The endpoint clamped to the inclusive axis.</returns>
    public static double Clamp(double value, double max) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, max);
}
