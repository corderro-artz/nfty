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
/// <param name="Name">The ingredient's display name.</param>
/// <param name="VariantCount">How many variants it contributes to the product.</param>
/// <param name="Kind">The layer kind, which tints the chip.</param>
/// <param name="ShowTimes">True for every chip after the first, so the view can render the x that
/// separates the factors. An ItemsControl cannot interleave separators between items, so the
/// separator travels with the item that follows it.</param>
public record FactorChip(string Name, int VariantCount, LayerKind Kind, bool ShowTimes)
{
    /// <summary>Whether this layer rolls its color per asset.</summary>
    public bool IsDynamic => Kind == LayerKind.Dynamic;
    /// <summary>Whether this layer applies one fixed color.</summary>
    public bool IsStatic => Kind == LayerKind.Static;
    /// <summary>Whether this layer composites as-is, without colorization.</summary>
    public bool IsCustom => Kind == LayerKind.Custom;
    /// <summary>Tooltip text: name, kind and variant count.</summary>
    public string Tip => $"{Name} · {Kind.ToString().ToLowerInvariant()} · {VariantCount} variants";
}

/// <summary>One recipe's row in the mint-distribution and DNA-space panels.</summary>
/// <param name="Name">The recipe's display name.</param>
/// <param name="SharePercent">Its share of mints, from the cookbook's weights.</param>
/// <param name="DnaSpaceText">Its unique-DNA figure, already formatted — including the em dash used
/// when the space is undefined rather than merely large.</param>
/// <param name="Factors">The layer stack, in paint order, one chip per layer carrying its variant
/// count. Deliberately NOT a factorization of <paramref name="DnaSpaceText"/>: the DNA space is the
/// legal combinations (rules applied) times each dynamic layer's quantized colors, so the chips'
/// product is neither factor. The row draws an arrow between them rather than an equals sign.</param>
/// <param name="Series">Which of the six mint-distribution series colors this recipe draws, 1-based
/// and assigned by position in the book. Exposed as an index rather than a <c>Color</c> so the paint
/// itself stays a theme token: the view switches on <see cref="IsSeries1"/>…<see cref="IsSeries6"/>
/// and picks up <c>Series1Brush</c>…<c>Series6Brush</c> from whichever dictionary is live, which a
/// color computed in the ViewModel could not do — the previous version hashed the recipe id into an
/// HSV, so it was off-palette by construction and identical in both themes.</param>
public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText, int Series,
    IReadOnlyList<FactorChip> Factors)
{
    /// <summary>True when this row draws series color 1.</summary>
    public bool IsSeries1 => Series == 1;
    /// <summary>True when this row draws series color 2.</summary>
    public bool IsSeries2 => Series == 2;
    /// <summary>True when this row draws series color 3.</summary>
    public bool IsSeries3 => Series == 3;
    /// <summary>True when this row draws series color 4.</summary>
    public bool IsSeries4 => Series == 4;
    /// <summary>True when this row draws series color 5.</summary>
    public bool IsSeries5 => Series == 5;
    /// <summary>True when this row draws series color 6.</summary>
    public bool IsSeries6 => Series == 6;
}

public partial class CookBookDetailViewModel : ViewModelBase
{
    /// <summary>Shown where a count cannot be computed (an unvalidatable book).</summary>
    private const string Unknown = "—";

    private readonly Action _cook;
    private readonly Action? _showReports;

    /// <summary>The collection's name.</summary>
    public string Name { get; }
    /// <summary>Its ticker-style symbol.</summary>
    public string Symbol { get; }
    /// <summary>Its description.</summary>
    public string Description { get; }
    /// <summary>Canvas size as the card renders it, with a real multiplication sign.</summary>
    public string CanvasText { get; }

    /// <summary>The mockup's "colorize &lt;model&gt;" chip. A CookBook has no color model of its own —
    /// it lives on each colorized Ingredient — so this reports what the book's dynamic and static
    /// layers actually use, and says "mixed" when they disagree rather than silently picking one.
    /// A book of purely Custom layers colorizes nothing, hence the em-dash.</summary>
    public string ColorizeText { get; }

    /// <summary>The mockup's "status &lt;b&gt;● Valid&lt;/b&gt;" chip — the real result, not a claim.
    /// Reading an archive does not validate it, so this asks Validator, which reports rather than
    /// throws precisely so a broken book can be opened and explained.</summary>
    public bool IsValid { get; }
    /// <summary>"Valid", or the problem count.</summary>
    public string StatusText { get; }
    /// <summary>Every problem, one per line, as the status pill's tooltip. Null when valid.</summary>
    public string? StatusTip { get; }

    /// <summary>The mockup's "target supply" chip. Em-dash when the book has not committed to a
    /// number — an unset target is a real state, not zero.</summary>
    public string TargetSupplyText { get; }
    /// <summary>Whether the book states an intended supply.</summary>
    public bool HasTargetSupply { get; }

    /// <summary>The cookbar's sentence. With a target it reads the mockup's way — "Target supply 500
    /// of 2,822,400,000 unique DNA" — which is the comparison that actually matters: whether the
    /// intent fits in the space the book can generate. Without one it just states the space.</summary>
    public string CookBarText { get; }

    /// <summary>How many recipes it holds.</summary>
    public int RecipeCount { get; }
    /// <summary>How many layers across all recipes.</summary>
    public int LayerCount { get; }
    /// <summary>How many variants across all layers.</summary>
    public int VariantCount { get; }
    /// <summary>The unique-DNA figure as the card shows it, including the em dash for a space that
    /// cannot be counted.</summary>
    public string UniqueDnaText { get; }
    /// <summary>Per-recipe share and DNA-space rows.</summary>
    public IReadOnlyList<RecipeShareRow> Recipes { get; }

    /// <summary>Builds the CookBook identity card.</summary>
    /// <param name="book">The open book.</param>
    /// <param name="cook">Opens the cook dialog.</param>
    /// <param name="showReports">Opens the stats/inspect reports; null leaves that button unavailable.</param>
    public CookBookDetailViewModel(LoadedCookBook book, Action cook,
        Action? showReports = null)
    {
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
            // id: a hash gave a stable-but-arbitrary color per recipe, which sounds like a feature
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
            // layerOrder, not r.Ingredients. The archive's own order is arbitrary, so the same
            // recipe's chips came out in one order here and in paint order on the recipe panel -
            // the same five numbers, shuffled, two clicks apart. Resolved tolerantly (an entry
            // naming a removed ingredient is simply skipped) because this panel is expected to
            // render a book that is mid-edit and invalid; deciding what is legal is Validator's job.
            var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
            foreach (var i in r.Ingredients) byId[i.Manifest.Id] = i;
            var ordered = r.Manifest.LayerOrder
                .Select(id => byId.GetValueOrDefault(id))
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();
            if (ordered.Count == 0) ordered = r.Ingredients.ToList();

            var factors = ordered
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
