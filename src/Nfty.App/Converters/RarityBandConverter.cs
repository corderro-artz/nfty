using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// Sorts a rarity percentage into one of three bands, so the rail can ink it accordingly.
/// </summary>
/// <remarks>
/// <para>Rarity is the reason anyone opens the Set browser's rail, and a flat column of grey numbers
/// makes the one that matters look exactly like the five that do not. Banding the ink — accent for
/// rare, foreground for the middle, muted for common — turns the column into something you can read
/// at a glance instead of something you have to compare digit by digit.</para>
///
/// <para>Three instances rather than one converter with a parameter, because a bound style class
/// (<c>Classes.rare="{Binding ...}"</c>) needs a plain <c>bool</c> per class and cannot pass a
/// parameter through from the class name.</para>
/// </remarks>
public sealed class RarityBandConverter : IValueConverter
{
    /// <summary>Under this share, a trait reads as rare.</summary>
    public const double RareBelow = 20;
    /// <summary>At or above this share, it reads as common.</summary>
    public const double CommonAtOrAbove = 60;

    private readonly Func<double, bool> _test;

    private RarityBandConverter(Func<double, bool> test) => _test = test;

    /// <summary>True for a share below <see cref="RareBelow"/>.</summary>
    public static readonly RarityBandConverter Rare = new(p => p < RareBelow);
    /// <summary>True for a share between the two thresholds.</summary>
    public static readonly RarityBandConverter Mid = new(p => p >= RareBelow && p < CommonAtOrAbove);
    /// <summary>True for a share at or above <see cref="CommonAtOrAbove"/>.</summary>
    public static readonly RarityBandConverter Common = new(p => p >= CommonAtOrAbove);

    /// <summary>Tests one percentage against this band.</summary>
    /// <param name="value">The share, 0-100.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Whether the share falls in this band; false for anything that is not a number, so a
    /// half-built binding paints no class rather than the wrong one.</returns>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double p && !double.IsNaN(p) && _test(p);

    /// <summary>Not supported; a band cannot be turned back into a percentage.</summary>
    /// <param name="value">Ignored.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
