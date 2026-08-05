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

/// <param name="Series">Which of the six mint-distribution series colours this recipe draws, 1-based
/// and assigned by position in the book. Exposed as an index rather than a <c>Color</c> so the paint
/// itself stays a theme token: the view switches on <see cref="IsSeries1"/>…<see cref="IsSeries6"/>
/// and picks up <c>Series1Brush</c>…<c>Series6Brush</c> from whichever dictionary is live, which a
/// colour computed in the ViewModel could not do — the previous version hashed the recipe id into an
/// HSV, so it was off-palette by construction and identical in both themes.</param>
public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText, int Series,
    IReadOnlyList<FactorChip> Factors)
{
    /// <summary>True when this row draws series colour 1.</summary>
    public bool IsSeries1 => Series == 1;
    /// <summary>True when this row draws series colour 2.</summary>
    public bool IsSeries2 => Series == 2;
    /// <summary>True when this row draws series colour 3.</summary>
    public bool IsSeries3 => Series == 3;
    /// <summary>True when this row draws series colour 4.</summary>
    public bool IsSeries4 => Series == 4;
    /// <summary>True when this row draws series colour 5.</summary>
    public bool IsSeries5 => Series == 5;
    /// <summary>True when this row draws series colour 6.</summary>
    public bool IsSeries6 => Series == 6;
}

public partial class CookBookDetailViewModel : ViewModelBase
{
    /// <summary>Shown where a count cannot be computed (an unvalidatable book).</summary>
    private const string Unknown = "—";

    private readonly INotYetWired _notify;
    private readonly Action _cook;
    private readonly Action? _showReports;

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

    /// <summary>The mockup's "target supply" chip. Em-dash when the book has not committed to a
    /// number — an unset target is a real state, not zero.</summary>
    public string TargetSupplyText { get; }
    public bool HasTargetSupply { get; }

    /// <summary>The cookbar's sentence. With a target it reads the mockup's way — "Target supply 500
    /// of 2,822,400,000 unique DNA" — which is the comparison that actually matters: whether the
    /// intent fits in the space the book can generate. Without one it just states the space.</summary>
    public string CookBarText { get; }

    public int RecipeCount { get; }
    public int LayerCount { get; }
    public int VariantCount { get; }
    public string UniqueDnaText { get; }
    public IReadOnlyList<RecipeShareRow> Recipes { get; }

    public CookBookDetailViewModel(LoadedCookBook book, INotYetWired notify, Action cook,
        Action? showReports = null)
    {
        _notify = notify;
        _cook = cook;
        _showReports = showReports;
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
        // manifest with kind "dynamic" but no colorization block). UniqueSpace reports that as
        // uncountable rather than throwing now, so the catch is belt-and-braces; the counts are
        // informational and a book we cannot measure must still open and show its structure.
        UniqueSpaceCount? space = null;
        try { space = UniqueSpace.Count(book); }
        catch { /* fall through to Unknown below */ }

        UniqueDnaText = SpaceText(space is null || !space.IsCountable, space?.IsExact ?? false, space?.Total ?? 0);

        var target = book.Manifest.TargetSupply;
        HasTargetSupply = target is not null;
        TargetSupplyText = target?.ToString("N0") ?? Unknown;
        CookBarText = target is null
            ? $"{UniqueDnaText} unique DNA available"
            : $"Target supply {target.Value:N0} of {UniqueDnaText} unique DNA";

        double totalWeight = book.Manifest.RecipeWeights.Values.Sum();
        int seriesIndex = 0;
        Recipes = book.Recipes.Select(r =>
        {
            // By position, cycling through the six series tokens. Position rather than a hash of the
            // id: a hash gave a stable-but-arbitrary colour per recipe, which sounds like a feature
            // until two recipes in the same book land on near-identical hues. Cycling guarantees
            // adjacent segments differ, which is the only property a categorical scale owes.
            int series = (seriesIndex++ % 6) + 1;
            double w = book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
            double share = totalWeight > 0 ? w / totalWeight * 100 : 0;
            string dna = Unknown;
            if (space is not null)
            {
                var rs = space[r.Manifest.Id];
                dna = SpaceText(!rs.IsCountable, rs.IsExact, rs.Total);
            }
            var factors = r.Ingredients
                .Select((i, idx) => new FactorChip(i.Manifest.Name, i.Manifest.Variants.Count,
                                                   i.Manifest.Kind, ShowTimes: idx > 0))
                .ToList();
            return new RecipeShareRow(r.Manifest.Name, Math.Round(share, 1), dna, series, factors);
        }).ToList();
    }

    /// <summary>
    /// One DNA-space figure as the card shows it. Three outcomes, not two: an exact count, a
    /// saturated count that is a real lower bound, and a space that is <em>undefined</em> because
    /// the book is invalid in a way that makes the question meaningless. The third reports zero, so
    /// formatting it like the second would put "more than 0" on the card — which reads like a
    /// measurement rather than a shrug.
    /// </summary>
    /// <param name="uncountable">Whether the space is undefined rather than merely large.</param>
    /// <param name="isExact">Whether <paramref name="total"/> is the figure rather than a floor.</param>
    /// <param name="total">The counted figure.</param>
    /// <returns>Display text, never empty.</returns>
    private static string SpaceText(bool uncountable, bool isExact, long total) =>
        uncountable ? Unknown
        : isExact ? total.ToString()
        : $"more than {total}";

    /// <summary>
    /// Opens the cook dialog. Gated on <see cref="IsValid"/> because
    /// <c>Generator.Generate</c> runs <c>Validator.Validate</c> itself and throws on any problem —
    /// so on a book this pane has *already* measured as invalid, the button used to stay live, let
    /// the user choose an output folder, and only then fail. <see cref="StatusTip"/> is the
    /// disabled tooltip, so the reason is readable rather than merely implied.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Cook() => _cook();

    /// <summary>Opens the stats/identity reports — the CLI's <c>stats</c> and <c>inspect</c>. Null
    /// when no report surface was supplied (some tests), in which case the button is simply
    /// unavailable rather than throwing at click time.</summary>
    [RelayCommand(CanExecute = nameof(CanShowReports))]
    private void ShowReports() => _showReports!();
    private bool CanShowReports() => _showReports is not null;
}
