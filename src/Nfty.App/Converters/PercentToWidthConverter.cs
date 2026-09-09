using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>
/// Scales a 0-100 share percent onto a bar whose track is a STATED width.
/// </summary>
/// <remarks>
/// <para>Two live users, both with a pinned track: the ingredient hero's rarity tracks
/// (<c>.rt</c>, 120) and the New Recipe wizard's "resulting mix" bar (<c>.mixbar</c>, 270). A
/// constant multiplier is exact for those and nothing else — a bar in a track that STRETCHES needs
/// <see cref="ShareWidthConverter"/>, which multiplies by the track's own measured width.</para>
///
/// <para>The summary here used to say this served the CookBook card's mint-distribution bar, and it
/// carried a <c>ForBarWidth310</c> for it. That bar is a stretching track now and multi-binds
/// <c>ShareWidthConverter</c> instead, so the member had no callers at all and the doc described a
/// screen that had moved on. <c>DeadCodeAuditTests</c> sweeps style classes, icons and tokens; it
/// cannot see an unreferenced C# member, which is how this survived.</para>
///
/// <para>Each multiplier restates its track's width, so the pair can drift. Both are correct today
/// and both would be removable by moving these two bars to <see cref="ShareWidthConverter"/> as
/// well — worth doing, not done here.</para>
/// </remarks>
public static class PercentToWidthConverter
{
    /// <summary>For the ingredient hero's rarity tracks (120px).</summary>
    public static readonly IValueConverter ForBarWidth120 =
        new FuncValueConverter<double, double>(share => share * 1.2);

    /// <summary>For the New Recipe wizard's "Resulting mix" bar (270px inside the .share panel).</summary>
    public static readonly IValueConverter ForBarWidth270 =
        new FuncValueConverter<double, double>(share => share * 2.7);
}
