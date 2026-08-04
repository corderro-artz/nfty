using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>One layer's contribution to a recipe's combination space: the mockup's .fchip, showing
/// the ingredient's variant count tinted by its kind. Chips are multiplied together (x) to reach the
/// recipe total, which is why the count alone is the chip's whole content.</summary>
/// <param name="ShowTimes">True for every chip after the first, so the view can render the x that
/// separates the factors. An ItemsControl cannot interleave separators between items, so the
/// separator travels with the item that follows it.</param>
public record FactorChip(string Name, int VariantCount, LayerKind Kind, bool ShowTimes)
{
    public bool IsDynamic => Kind == LayerKind.Dynamic;
    public bool IsStatic => Kind == LayerKind.Static;
    public bool IsCustom => Kind == LayerKind.Custom;
    public string Tip => $"{Name} · {Kind.ToString().ToLowerInvariant()} · {VariantCount} variants";
}

public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText, Color SegmentColor,
    IReadOnlyList<FactorChip> Factors);

public partial class CookBookDetailViewModel : ViewModelBase
{
    /// <summary>Shown where a count cannot be computed (an unvalidatable book).</summary>
    private const string Unknown = "—";

    private readonly INotYetWired _notify;
    private readonly Action _cook;

    public string Name { get; }
    public string Symbol { get; }
    public string Description { get; }
    public string CanvasText { get; }

    /// <summary>The mockup's "colorize &lt;model&gt;" chip. A CookBook has no colour model of its own —
    /// it lives on each colorized Ingredient — so this reports what the book's dynamic and static
    /// layers actually use, and says "mixed" when they disagree rather than silently picking one.
    /// A book of purely Custom layers colorizes nothing, hence the em-dash.</summary>
    public string ColorizeText { get; }

    /// <summary>The mockup's "status &lt;b&gt;● Valid&lt;/b&gt;" chip — the real result, not a claim.
    /// Reading an archive does not validate it, so this asks Validator, which reports rather than
    /// throws precisely so a broken book can be opened and explained.</summary>
    public bool IsValid { get; }
    public string StatusText { get; }
    public string? StatusTip { get; }

    public int RecipeCount { get; }
    public int LayerCount { get; }
    public int VariantCount { get; }
    public string UniqueDnaText { get; }
    public IReadOnlyList<RecipeShareRow> Recipes { get; }

    public CookBookDetailViewModel(LoadedCookBook book, INotYetWired notify, Action cook)
    {
        _notify = notify;
        _cook = cook;
        Name = book.Manifest.Name;
        Symbol = book.Manifest.Collection.Symbol;
        Description = book.Manifest.Collection.Description;
        // "1000 × 1000" with a real multiplication sign and spaces, as the mockup renders it.
        CanvasText = $"{book.Manifest.Canvas.Width} × {book.Manifest.Canvas.Height}";

        var problems = Validator.Validate(book);
        IsValid = problems.Count == 0;
        StatusText = IsValid ? "Valid" : problems.Count == 1 ? "1 problem" : $"{problems.Count} problems";
        StatusTip = IsValid ? null : string.Join(Environment.NewLine, problems);

        var models = book.Recipes
            .SelectMany(r => r.Ingredients)
            .Select(i => i.Manifest.Colorization?.Model)
            .Where(m => m is not null)
            .Select(m => m!.Value.ToString().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(m => m, StringComparer.Ordinal)   // Ordinal: this reaches the UI, never an output file
            .ToList();
        ColorizeText = models.Count switch
        {
            0 => Unknown,
            1 => models[0],
            _ => "mixed",
        };
        RecipeCount = book.Recipes.Count;
        LayerCount = book.Recipes.Sum(r => r.Ingredients.Count);
        VariantCount = book.Recipes.Sum(r => r.Ingredients.Sum(i => i.Manifest.Variants.Count));

        // Best-effort: the Explorer opens whatever archive it is handed, and reading one does NOT
        // validate it, so a book that Validator would reject can reach this pane (e.g. a hand-edited
        // manifest with kind "dynamic" but no colorization block - UniqueSpace dereferences it). The
        // counts are informational; a book we cannot measure must still open and show its structure.
        UniqueSpaceCount? space = null;
        try { space = UniqueSpace.Count(book); }
        catch { /* fall through to Unknown below */ }

        UniqueDnaText = space is null ? Unknown
            : space.IsExact ? space.Total.ToString() : $"more than {space.Total}";

        double totalWeight = book.Manifest.RecipeWeights.Values.Sum();
        Recipes = book.Recipes.Select(r =>
        {
            double w = book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
            double share = totalWeight > 0 ? w / totalWeight * 100 : 0;
            string dna = Unknown;
            if (space is not null)
            {
                var rs = space[r.Manifest.Id];
                dna = rs.IsExact ? rs.Total.ToString() : $"more than {rs.Total}";
            }
            var factors = r.Ingredients
                .Select((i, idx) => new FactorChip(i.Manifest.Name, i.Manifest.Variants.Count,
                                                   i.Manifest.Kind, ShowTimes: idx > 0))
                .ToList();
            return new RecipeShareRow(r.Manifest.Name, Math.Round(share, 1), dna,
                SegmentColorFor(r.Manifest.Id), factors);
        }).ToList();
    }

    private static Color SegmentColorFor(string recipeId)
    {
        double hue = SeedHash.ToUlong(recipeId) % 360UL;
        var rgb = ColorConvert.HsvToRgb(hue, 0.5, 0.72);
        return Color.FromRgb(rgb.R, rgb.G, rgb.B);
    }

    [RelayCommand] private void Cook() => _cook();
}
