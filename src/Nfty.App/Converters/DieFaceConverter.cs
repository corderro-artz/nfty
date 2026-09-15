using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Nfty.App.Converters;

/// <summary>
/// A die face, 1 to 6, as the glyph that draws it.
/// </summary>
/// <remarks>
/// <para>The alternative was six <c>Path</c>s stacked in one cell with five of them hidden, which is
/// the app's usual "reserve the space, toggle the ink" answer — but that rule exists to stop a
/// control moving its neighbours, and these six are the same 18px box drawn in the same place, so
/// there is nothing to reserve and nothing to move. One Path whose geometry changes is what the
/// thing IS: one die, landing differently.</para>
///
/// <para>Resolved out of the app's own resources rather than held as a static array here, because
/// <c>Icons.axaml</c> is GENERATED from <c>assets/icons/*.svg</c> — a second copy of these
/// geometries in C# is exactly the drift <c>IconSourceTests</c> exists to prevent. An unknown value
/// falls back to face 1 rather than throwing: a decoration with no geometry should draw something
/// rather than take a screen down.</para>
/// </remarks>
public sealed class DieFaceConverter : IValueConverter
{
    /// <summary>The die's body and pips, for the face it is given.</summary>
    public static readonly DieFaceConverter Instance = new(false);

    /// <summary>
    /// The ONE face's single pip, and nothing on any other face — drawn over
    /// <see cref="Instance"/> in the accent.
    /// </summary>
    /// <remarks>
    /// A <c>Path.ico</c> is one stroked path with one brush, so a glyph cannot carry two inks. The
    /// red one is the oldest piece of character a die has; it needs a second Path, and this is what
    /// feeds it. Null on every other face, which a Path draws as nothing.
    /// </remarks>
    public static readonly DieFaceConverter AccentPip = new(true);

    /// <summary>How many faces a die has. The roller and the glyph set agree on this number.</summary>
    public const int Faces = 6;

    /// <summary>
    /// The resource keys, spelled out rather than interpolated.
    /// </summary>
    /// <remarks>
    /// <c>DeadCodeAuditTests.Every_icon_is_drawn_somewhere</c> derives what is used by searching the
    /// source for each key's name, and a key assembled at runtime appears nowhere — the six were
    /// reported dead the moment they were resolved as <c>$"IconDie{face}"</c>. Naming them here is
    /// both what makes that sweep honest and what lets a reader grep for the glyph they are looking
    /// at.
    /// </remarks>
    private static readonly string[] Keys =
        { "IconDie1", "IconDie2", "IconDie3", "IconDie4", "IconDie5", "IconDie6" };

    /// <summary>The one face's accent pip.</summary>
    private const string PipKey = "IconDie1Pip";

    private readonly bool _pipOnly;

    private DieFaceConverter(bool pipOnly) => _pipOnly = pipOnly;

    /// <summary>Maps a face number onto its <see cref="StreamGeometry"/>.</summary>
    /// <param name="value">The face, 1..<see cref="Faces"/>.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>The glyph, or null when the resource cannot be found — which a Path draws as
    /// nothing rather than as an error.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int face = value is int n && n >= 1 && n <= Faces ? n : 1;
        if (_pipOnly && face != 1) return null;      // only the one face carries a coloured pip
        string key = _pipOnly ? PipKey : Keys[face - 1];
        return Application.Current?.Resources.TryGetResource(key, null, out var geo) == true
            ? geo
            : null;
    }

    /// <summary>Not supported; a glyph does not name its face.</summary>
    /// <param name="value">Ignored.</param>
    /// <param name="targetType">Ignored.</param>
    /// <param name="parameter">Ignored.</param>
    /// <param name="culture">Ignored.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
