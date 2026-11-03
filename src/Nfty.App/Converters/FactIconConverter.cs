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
/// <c>DeadCodeAuditTests</c> can find them. A label the table does not name falls back to the trait diamond (or the paint drop for a
/// <c>… COLOR</c> trait), so every row carries a mark.
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
        ["COLLECTION"] = "IconTypeCookBook",
        ["ITEMS"] = "IconTypeSet",
        ["IMAGES"] = "IconTypeVariant",
        ["IMAGE"] = "IconTypeVariant",
        ["COVER"] = "IconTypeVariant",
        ["LINK"] = "IconLink",
        ["MODEL URL"] = "IconRise",
        ["FILE"] = "IconOpen",
        ["NAME"] = "IconTag",
        ["SYMBOL"] = "IconSymbol",
        ["ABOUT"] = "IconAbout",
        ["ROYALTY"] = "IconPercent",
        ["PAYEE"] = "IconWallet",
        ["PROBLEMS"] = "IconMarkFlag",
        ["TYPE"] = "IconTypeRecipe",
    };

    /// <summary>Maps a fact label onto its glyph.</summary>
    /// <param name="value">The label.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>The glyph; null only when the value is not a label or the resource is missing.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string label) return null;
        // EVERY ROW CARRIES A GLYPH, so the rail is one grid of marks: a published trait the table does
        // not name (a layer of the author's: BODY, GLOW) takes the trait diamond, and its rolled color
        // ("BODY COLOR") the paint drop.
        string key = Keys.TryGetValue(label, out var known) ? known
            : label.EndsWith(" COLOR", StringComparison.Ordinal) ? DropKey
            : TraitKey;
        return Application.Current?.Resources.TryGetResource(key, null, out var geo) == true ? geo : null;
    }

    private const string TraitKey = "IconTrait";
    private const string DropKey = "IconDrop";

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
