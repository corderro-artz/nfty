using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;

namespace Nfty.App.ViewModels;

public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText, Color SegmentColor);

public partial class CookBookDetailViewModel : ViewModelBase
{
    private readonly INotYetWired _notify;

    public string Name { get; }
    public string Symbol { get; }
    public string CanvasText { get; }
    public int RecipeCount { get; }
    public int LayerCount { get; }
    public int VariantCount { get; }
    public string UniqueDnaText { get; }
    public IReadOnlyList<RecipeShareRow> Recipes { get; }

    public CookBookDetailViewModel(LoadedCookBook book, INotYetWired notify)
    {
        _notify = notify;
        Name = book.Manifest.Name;
        Symbol = book.Manifest.Collection.Symbol;
        CanvasText = $"{book.Manifest.Canvas.Width}x{book.Manifest.Canvas.Height}";
        RecipeCount = book.Recipes.Count;
        LayerCount = book.Recipes.Sum(r => r.Ingredients.Count);
        VariantCount = book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count));

        var space = UniqueSpace.Count(book);
        UniqueDnaText = space.IsExact ? space.Total.ToString() : $"more than {space.Total}";

        double totalWeight = book.Manifest.RecipeWeights.Values.Sum();
        Recipes = book.Recipes.Select(r =>
        {
            double w = book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
            double share = totalWeight > 0 ? w / totalWeight * 100 : 0;
            var rs = space[r.Manifest.Id];
            string dna = rs.IsExact ? rs.Total.ToString() : $"more than {rs.Total}";
            return new RecipeShareRow(r.Manifest.Name, Math.Round(share, 1), dna, SegmentColorFor(r.Manifest.Id));
        }).ToList();
    }

    private static Color SegmentColorFor(string recipeId)
    {
        double hue = SeedHash.ToUlong(recipeId) % 360UL;
        var rgb = ColorConvert.HsvToRgb(hue, 0.5, 0.72);
        return Color.FromRgb(rgb.R, rgb.G, rgb.B);
    }

    [RelayCommand] private void Cook() => _notify.Report("Cook");
}
