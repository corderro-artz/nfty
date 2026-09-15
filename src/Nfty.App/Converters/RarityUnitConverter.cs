using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// One trait's rarity, as a percentage or as odds — the same number said two ways.
/// </summary>
/// <remarks>
/// <para>A percentage is the right unit for comparing traits to each other and the wrong one for
/// feeling how rare a thing is: 4.17% and 2.08% look like neighbours and are "one in 24" and "one in
/// 48". Both readings are useful and neither is correct on its own, so the column switches and the
/// reader picks. It is one number either way — the odds are derived from the percentage rather than
/// stored beside it, so the two cannot drift.</para>
///
/// <para>A multi-value converter rather than two properties on the row, because the row is
/// <c>RarityAttribute</c> — a Core record written into <c>nfty/NNNN.json</c> — and a display
/// preference has no business in it. The second input is the toggle itself, so every cell
/// re-renders the moment it flips.</para>
///
/// <para>Rounded to whole assets, because that is what "one in N" means: you cannot own a fraction
/// of an asset, and printing "1 in 23.98" would be arithmetic showing through. A zero share is an
/// em dash rather than a division — a trait no asset carries has no odds, and infinity is not an
/// answer anyone wants on a card.</para>
/// </remarks>
public sealed class RarityUnitConverter : IMultiValueConverter
{
    /// <summary>The shared instance; the converter holds no state.</summary>
    public static readonly RarityUnitConverter Instance = new();

    /// <summary>Renders a percentage in the unit the rail is currently showing.</summary>
    /// <param name="values">The share (0-100), then whether to show odds rather than a percentage.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>"4.17%" or "1 in 24"; an em dash when the share is zero or still unset.</returns>
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2 || values[0] is not double pct) return "—";
        bool odds = values[1] is true;

        if (!odds) return $"{pct}%";
        if (pct <= 0) return "—";
        // Invariant, like every other figure this app prints: these are read off screenshots and
        // compared across machines.
        return $"1 in {Math.Round(100.0 / pct, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}";
    }
}
