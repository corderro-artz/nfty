using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// A viewer fact's label (<c>RECIPE</c>, <c>DNA</c>, <c>SEED</c> …) as the glyph the rest of the app
/// already uses for that thing, so the info rail reads with the same marks as the tree and the cards.
/// </summary>
/// <remarks>
/// Resolved out of the app's resources, as <see cref="DieFaceConverter"/> does, because
/// <c>Icons.axaml</c> is generated from <c>assets/icons/*.svg</c>. The keys are spelled out so
/// <c>DeadCodeAuditTests</c> can find them. A label with no glyph returns null, which a Path draws as
/// nothing: the column is fixed, so the label beside it does not move.
/// </remarks>
public sealed class FactIconConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static readonly FactIconConverter Instance = new();

    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["DNA"] = "IconDna",
        ["SEED"] = "IconDie5",
        ["RECIPE"] = "IconTypeRecipe",
        ["RECIPES"] = "IconTypeRecipe",
        ["ASSET"] = "IconTypeSet",
        ["SET"] = "IconTypeSet",
        ["ITEMS"] = "IconTypeSet",
        ["BOOK"] = "IconTypeCookBook",
        ["COOKBOOK"] = "IconTypeCookBook",
        ["LAYER"] = "IconTypeIngredient",
        ["LAYERS"] = "IconTypeIngredient",
        ["KIND"] = "IconTypeIngredient",
        ["VARIANT"] = "IconTypeVariant",
        ["VARIANTS"] = "IconTypeVariant",
        ["SIZE"] = "IconGrid",
        ["CANVAS"] = "IconGrid",
        ["GRID"] = "IconGrid",
        ["MODEL"] = "IconRise",
        ["DEPTH"] = "IconRise",
        ["NOTE"] = "IconHelp",
    };

    /// <summary>Maps a fact label onto its glyph.</summary>
    /// <param name="value">The label.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>The glyph, or null for a label that has none.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string label && Keys.TryGetValue(label, out var key)
            && Application.Current?.Resources.TryGetResource(key, null, out var geo) == true
            ? geo
            : null;

    /// <summary>Not supported; a glyph does not name its label.</summary>
    /// <param name="value">Ignored.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
