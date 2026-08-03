using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>Scales a 0-100 share-percent value to a pixel width inside a fixed-width bar. Used by
/// the CookBook detail view's mint-distribution bar (<see cref="Nfty.App.Views.CookBookDetailView"/>):
/// each recipe's segment <c>Width</c> is bound through this converter so segment widths stay
/// proportional to <c>SharePercent</c> without a Grid built from a variable number of star-sized
/// columns. Multiplier is barWidth/100 — the distribution bar and the per-recipe share bars in the
/// combination-space column are both 310px since the pane became two columns.</summary>
public static class PercentToWidthConverter
{
    public static readonly IValueConverter ForBarWidth310 =
        new FuncValueConverter<double, double>(share => share * 3.1);

    /// <summary>For the ingredient hero's rarity tracks (120px).</summary>
    public static readonly IValueConverter ForBarWidth120 =
        new FuncValueConverter<double, double>(share => share * 1.2);
}
