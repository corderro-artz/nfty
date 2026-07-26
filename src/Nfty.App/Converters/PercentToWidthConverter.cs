using Avalonia.Data.Converters;

namespace Nfty.App.Converters;

/// <summary>Scales a 0-100 share-percent value to a pixel width inside a fixed-width bar. Used by
/// the CookBook detail view's mint-distribution bar (<see cref="Nfty.App.Views.CookBookDetailView"/>):
/// each recipe's segment <c>Width</c> is bound through this converter so segment widths stay
/// proportional to <c>SharePercent</c> without a Grid built from a variable number of star-sized
/// columns. Multiplier is barWidth/100 — 5.6 for the view's 560px-wide `Border.distbar`.</summary>
public static class PercentToWidthConverter
{
    public static readonly IValueConverter ForBarWidth560 =
        new FuncValueConverter<double, double>(share => share * 5.6);
}
