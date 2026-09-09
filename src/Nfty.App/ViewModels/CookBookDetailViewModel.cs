using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
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
/// <param name="Variants">How many variants the layer actually has, when that differs from the
/// factor — which it does exactly when the layer is optional. Null means they are the same.</param>
/// <param name="Optional">Whether the layer may be left out of an asset entirely, which is what
/// makes the factor one higher than the variant count.</param>
/// <param name="MoreCount">When positive, this is not a layer at all but the OVERFLOW badge —
/// the "+3" that stands for the layers past the last slot. It exists because the badge strip is a
/// fixed number of columns: a stack deeper than that spends its last column saying how many it is
/// not showing, rather than pushing the figures beside it out of their own columns.</param>
public record FactorChip(string Name, int VariantCount, LayerKind Kind, bool ShowTimes,
    int? Variants = null, bool Optional = false, int MoreCount = 0)
{
    /// <summary>Whether this badge stands for the layers that did not fit.</summary>
    public bool IsMore => MoreCount > 0;

    /// <summary>What the badge prints: a factor, or <c>+N</c> for the overflow badge.</summary>
    public string Label => IsMore
        ? $"+{MoreCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
        : VariantCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether this layer rolls its color per asset.</summary>
    public bool IsDynamic => Kind == LayerKind.Dynamic;
    /// <summary>Whether this layer applies one fixed color.</summary>
    public bool IsStatic => Kind == LayerKind.Static;
    /// <summary>Whether this layer composites as-is, without colorization.</summary>
    public bool IsCustom => Kind == LayerKind.Custom;
    /// <summary>Tooltip text: name, kind and variant count.</summary>
    /// <summary>
    /// What the chip means, in words.
    /// </summary>
    /// <remarks>
    /// It names the REAL variant count, not the factor. An optional layer's factor is one higher —
    /// "not present" is an outcome the roll can land on — so a tooltip reading the factor would say
    /// a two-variant layer has three, one hover away from the table column that correctly says two.
    /// The "+ not present" is what reconciles the chip with that column.
    /// </remarks>
    public string Tip
    {
        get
        {
            int variants = Variants ?? VariantCount;
            string plural = variants == 1 ? "variant" : "variants";
            string kind = Kind.ToString().ToLowerInvariant();
            if (IsMore)
                return $"{MoreCount} more layers — open the recipe to see them";
            return Optional
                ? $"{Name} · {kind} · {variants} {plural} + not present"
                : $"{Name} · {kind} · {variants} {plural}";
        }
    }
}

/// <summary>One recipe's row in the mint-distribution and DNA-space panels.</summary>
/// <param name="Name">The recipe's display name.</param>
/// <param name="SharePercent">Its share of mints, from the cookbook's weights.</param>
/// <param name="DnaSpaceText">Its unique-DNA figure, already formatted — shortened above a billion,
/// and an em dash when the space is undefined rather than merely large.</param>
/// <param name="DnaSpaceTip">What the row cannot fit: the exact figure, digit for digit, and the
/// derivation the arrow between the chips and the number leaves implicit. ONE tooltip rather than
/// two, because the control has one.</param>
/// <param name="Factors">The layer stack, in paint order, one chip per layer carrying its variant
/// count. Deliberately NOT a factorization of <paramref name="DnaSpaceText"/>: the DNA space is the
/// legal combinations (rules applied) times each dynamic layer's quantized colors, so the chips'
/// product is neither factor. The row draws an arrow between them rather than an equals sign.</param>
/// <param name="HueShift">How far around the color wheel this recipe's mint-distribution color is
/// turned from the palette's anchor, in degrees. A SHIFT rather than a color, and rather than an
/// index into a fixed set: the six series tokens it replaced cycled, so recipe seven repeated recipe
/// one and a bar with eight segments carried two indistinguishable pairs. This level decides only
/// how far apart the colors are; <see cref="Converters.SeriesBrushConverter"/> seats the shift on
/// <c>SeriesAnchorBrush</c>'s own hue, saturation and lightness, so the paint still comes from
/// whichever theme dictionary is live — which a <c>Color</c> chosen here could not do.</param>
public record RecipeShareRow(string Name, double SharePercent, string DnaSpaceText,
    string DnaSpaceTip, double HueShift, IReadOnlyList<FactorChip> Factors);

/// <summary>One page indicator in the DNA-space pager.</summary>
/// <param name="IsCurrent">Whether this is the page showing.</param>
public record PageDot(bool IsCurrent);

public partial class CookBookDetailViewModel : ViewModelBase
{
    /// <summary>Shown where a count cannot be computed (an unvalidatable book).</summary>
    private const string Unknown = "—";

    /// <summary>Degrees between one recipe's mint-distribution color and the next.</summary>
    /// <remarks>
    /// 360 / φ². Any rotation that is not a rational fraction of a turn avoids repeating, but this
    /// one also keeps every prefix of the sequence spread as evenly as a sequence can be — so the
    /// first six colors are as separated as the six hand-picked tokens this replaced, and the
    /// hundredth is still separated from all ninety-nine before it.
    /// </remarks>
    public const double GoldenAngle = 137.50776405003785;

    private readonly Action _cook;
    private readonly Action? _showReports;

    /// <summary>The BOOK's name — what the titlebar, the tree and the recents list all show.</summary>
    /// <remarks>
    /// Deliberately not the collection's name, and the two are different fields: this one names the
    /// <c>.cbk</c>, and <see cref="MintName"/> names what the assets are published as. The summary
    /// here used to say "the collection's name", which is how the split went unnoticed for so long —
    /// the card read the book and the doc described the collection.
    /// </remarks>
    public string Name { get; }

    /// <summary>What every cooked asset will be called, e.g. <c>Chest Demo #1</c>.</summary>
    /// <remarks>
    /// <para><b>This is the most published string in a collection and the card never showed it.</b>
    /// <c>Collection.Name</c> is what <c>SetWriter.BuildOpenSea</c> puts in every
    /// <c>metadata/NNNN.json</c> as <c>"{name} #{n}"</c>, and it is what an export is named after —
    /// but the card's title is the BOOK's name, and its symbol and description come from the
    /// collection. Three of the four fields on that card came from one object and the title from
    /// another, with nothing saying so.</para>
    ///
    /// <para>They are equal for anything the GUI creates — the New CookBook wizard writes one typed
    /// name into both — so this is redundant most of the time and exact when it is not. It is shown
    /// unconditionally for that reason: a card that only mentions the published name when it
    /// disagrees is a card you cannot trust when it stays quiet.</para>
    /// </remarks>
    public string MintName { get; }
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

    /// <summary>How many recipes it holds.</summary>
    public int RecipeCount { get; }
    /// <summary>How many layers across all recipes.</summary>
    public int LayerCount { get; }
    /// <summary>How many variants across all layers.</summary>
    public int VariantCount { get; }
    /// <summary>
    /// The unique-DNA figure, in full, with thousands separators.
    /// </summary>
    /// <remarks>
    /// <b>Never rounded, and therefore no tooltip.</b> The cell it lives in was widened until the
    /// widest figure a <see cref="long"/> can hold fits at the SMALLEST window the app opens —
    /// measured, 255px of ink against 266px of room — so the compact form and the tooltip that used
    /// to carry the exact digits are both gone. This is the number an author tunes quantize steps
    /// against; a figure you have to hover to read is a figure you cannot compare at a glance.
    /// </remarks>
    public string UniqueDnaText { get; }

    /// <summary>How much of the space the intended supply would use, 0..100.</summary>
    /// <remarks>
    /// Clamped at 100 for the bar's width, because a target LARGER than the space is a real state
    /// and a bar wider than its track is not. <see cref="SupplyExceedsSpace"/> is what says which
    /// side of the line it fell on, and the figure beside it is never clamped.
    /// </remarks>
    public double SupplyPercent { get; }
    /// <summary>That percentage as the rail prints it.</summary>
    public string SupplyPercentText { get; }
    /// <summary>Whether the target asks for more assets than the book can produce.</summary>
    public bool SupplyExceedsSpace { get; }
    /// <summary>Whether the rail has anything to show: a target and a countable space.</summary>
    public bool HasSupplyRail { get; }

    /// <summary>Every recipe, in the book's own order. The mint bar and the counts read THIS.</summary>
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
        // "#1" and not "#0001": the number is the SHAPE of the published name, and the padding is
        // SetWriter's own business (it writes the file as NNNN and the name as "#{SetNumber}").
        MintName = $"mints as {book.Manifest.Collection.Name} #1";
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

        var whole = space is null || !space.IsCountable ? null : ((long Total, SpaceCertainty Certainty)?)(space.Total, space.Certainty);
        UniqueDnaText = Full(whole);

        var target = book.Manifest.TargetSupply;
        HasTargetSupply = target is not null;
        TargetSupplyText = target?.ToString("N0", CultureInfo.InvariantCulture) ?? Unknown;

        // The rail answers one question — does the intended supply fit in the space? — so it needs
        // both numbers to exist. A book with no target, or one whose space cannot be counted, has
        // nothing to measure and shows no rail rather than a bar at zero.
        HasSupplyRail = target is { } t && whole is { Total: > 0 } w2 && w2.Total > 0;
        double raw = HasSupplyRail ? target!.Value / (double)whole!.Value.Total * 100 : 0;
        SupplyExceedsSpace = HasSupplyRail && target!.Value > whole!.Value.Total;
        SupplyPercent = Math.Min(100, raw);
        SupplyPercentText = !HasSupplyRail ? Unknown
            : raw >= 10 ? raw.ToString("0", CultureInfo.InvariantCulture) + "%"
            : raw >= 1 ? raw.ToString("0.0", CultureInfo.InvariantCulture) + "%"
            : raw.ToString("0.00", CultureInfo.InvariantCulture) + "%";

        double totalWeight = book.Manifest.RecipeWeights.Values.Sum();
        int seriesIndex = 0;
        Recipes = book.Recipes.Select(r =>
        {
            // By position, turned a golden angle each time. There is no cycle and so no ceiling: a
            // book may hold any number of recipes and no two land on the same color. Position
            // rather than a hash of the id, and generated rather than rolled, for the same reason
            // in both cases — a hash and an RNG each put two near-identical hues side by side about
            // as often as chance allows, and 137.5° is the rotation that keeps consecutive values
            // as far apart on the wheel as any sequence can. Adjacent segments differing is the one
            // property a categorical scale owes.
            double hueShift = seriesIndex++ * GoldenAngle % 360;
            double w = book.Manifest.RecipeWeights.GetValueOrDefault(r.Manifest.Id);
            double share = totalWeight > 0 ? w / totalWeight * 100 : 0;
            var rs = space?[r.Manifest.Id];
            var one = rs is null || !rs.IsCountable ? null : ((long, SpaceCertainty)?)(rs.Total, rs.Certainty);
            string dna = Figure(one);
            string dnaTip = Tip(one) + Environment.NewLine
                + "Legal variant combinations (this recipe's rules applied), times each dynamic "
                + "layer's quantized colors";
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

            // An optional layer's factor is one higher — "not present" is an outcome the roll can
            // land on — and a layer that never appears counts one rather than its variants. The
            // Recipe pane's own chips already do this; the two panels are one click apart and show
            // the same arithmetic, so they cannot be allowed to disagree about it.
            var factors = Slotted(ordered
                .Select((i, idx) =>
                {
                    double absent = r.Manifest.AbsentPercentOf(i.Manifest.Id);
                    int count = absent >= 100
                        ? 1
                        : i.Manifest.Variants.Count + (absent > 0 ? 1 : 0);
                    return new FactorChip(i.Manifest.Name, count, i.Manifest.Kind,
                        ShowTimes: idx > 0,
                        Variants: i.Manifest.Variants.Count, Optional: absent > 0);
                })
                .ToList());
            return new RecipeShareRow(r.Manifest.Name, Math.Round(share, 1), dna, dnaTip, hueShift,
                factors);
        }).ToList();

        PageSize = Math.Max(1, Recipes.Count);
    }

    /// <summary>
    /// How many badge columns a row has. A stack deeper than this spends the last one on a
    /// <c>+N</c>.
    /// </summary>
    /// <remarks>
    /// <b>The strip is a fixed number of columns, not a run that grows.</b> Laid out as a run, a
    /// five-layer recipe put its first badge where a six-layer one put its second and nothing lined
    /// up down the table; worse, a deep stack pushed the figure beside it out of the column every
    /// other row keeps it in. Six is what the table's stated badge width holds.
    /// </remarks>
    public const int FactorSlots = 6;

    /// <summary>
    /// Caps a layer stack at <see cref="FactorSlots"/>, spending the last slot on a <c>+N</c>.
    /// </summary>
    /// <param name="all">Every layer's chip, in paint order.</param>
    /// <returns>At most <see cref="FactorSlots"/> chips.</returns>
    private static IReadOnlyList<FactorChip> Slotted(IReadOnlyList<FactorChip> all)
    {
        if (all.Count <= FactorSlots) return all;
        var kept = all.Take(FactorSlots - 1).ToList();
        kept.Add(new FactorChip(string.Empty, 0, LayerKind.Custom, ShowTimes: true,
            MoreCount: all.Count - (FactorSlots - 1)));
        return kept;
    }

    // ---- paging -------------------------------------------------------------------------------
    //
    // The table pages rather than scrolls, and HOW MANY it pages by is not a constant: the view
    // measures one rendered row against the space beneath the band and sets PageSize. That is the
    // whole "breathes with the window" behaviour - two rows at the smallest window the app opens,
    // five at the size it opens at, the whole book at full screen - and a number chosen here would
    // be right at one window size and wrong at every other.

    /// <summary>How many rows fit on a page. Set by the view from a measured row.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRecipes))]
    [NotifyPropertyChangedFor(nameof(PageCount))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(HasPages))]
    [NotifyPropertyChangedFor(nameof(Dots))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _pageSize = 1;

    /// <summary>Which page is showing, zero-based.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleRecipes))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(Dots))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    private int _pageIndex;

    /// <summary>A page that no longer exists is not a page: shrink the window and the last page
    /// goes with it, so the index follows rather than leaving the table blank.</summary>
    /// <param name="value">The new page size.</param>
    partial void OnPageSizeChanged(int value)
    {
        if (PageIndex > PageCount - 1) PageIndex = Math.Max(0, PageCount - 1);
    }

    /// <summary>The rows this page shows.</summary>
    public IReadOnlyList<RecipeShareRow> VisibleRecipes =>
        Recipes.Skip(PageIndex * Math.Max(1, PageSize)).Take(Math.Max(1, PageSize)).ToList();

    /// <summary>How many pages the book comes to at the current size. At least one, always.</summary>
    public int PageCount =>
        Math.Max(1, (int)Math.Ceiling(Recipes.Count / (double)Math.Max(1, PageSize)));

    /// <summary>Whether there is more than one page — the pager's controls stay in place either
    /// way, so this drives the ink and never the geometry.</summary>
    public bool HasPages => PageCount > 1;

    /// <summary>One dot per page, the current one lit. Reserved even at one page, so the pager's
    /// geometry does not change when a window resize adds or removes a page.</summary>
    public IReadOnlyList<PageDot> Dots =>
        Enumerable.Range(0, PageCount).Select(i => new PageDot(i == PageIndex)).ToList();

    /// <summary>Which rows are showing, of how many: "1–5 of 8".</summary>
    public string PageLabel
    {
        get
        {
            if (Recipes.Count == 0) return string.Empty;
            int from = PageIndex * Math.Max(1, PageSize);
            int to = Math.Min(from + Math.Max(1, PageSize), Recipes.Count);
            return $"{from + 1}–{to} of {Recipes.Count}";
        }
    }

    /// <summary>Shows the next page.</summary>
    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void NextPage() => PageIndex++;
    private bool CanGoNext() => PageIndex < PageCount - 1;

    /// <summary>Shows the previous page.</summary>
    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage() => PageIndex--;
    private bool CanGoBack() => PageIndex > 0;

    /// <summary>
    /// One recipe's DNA-space figure as its ROW shows it: <see cref="SpaceText"/>'s compact form.
    /// </summary>
    /// <param name="space">The figure and what it is, or null when the space is undefined — the
    /// book is invalid in a way that makes the question meaningless, or counting threw.</param>
    /// <returns>Display text, never empty.</returns>
    /// <remarks>
    /// <b>The wording is Core's, not this card's.</b> This method used to BE the rule — three
    /// branches, worded one way here and another in the CLI's <c>stats</c> line, which is how two
    /// surfaces showing one number came to describe it differently. What stays local is the
    /// <see cref="Unknown"/> em dash: a table cell has room for a figure and not for a sentence,
    /// while the report has room for both and says which problem to go and fix.
    ///
    /// <para>The compact form survives HERE and nowhere else. A row's figure column is 118px, so it
    /// still rounds past a billion and still carries the exact digits on its tooltip; the headline
    /// figure has a cell wide enough to print in full and does neither.</para>
    /// </remarks>
    private static string Figure((long Total, SpaceCertainty Certainty)? space) =>
        space is not { } s ? Unknown : SpaceText.Describe(s.Total, s.Certainty, compact: true);

    /// <summary>
    /// The full figure behind <see cref="Figure"/>, for the tooltip.
    /// </summary>
    /// <param name="space">The figure and what it is, or null when the space is undefined.</param>
    /// <returns>Tooltip text, never empty.</returns>
    /// <remarks>
    /// <b>A number the product will not print in full is a number the author cannot check</b>, and
    /// this is the figure they tune quantize steps against. The tile shortens; the tooltip never
    /// does. Below a billion the two agree, and that is deliberate — a tooltip that carries
    /// something only sometimes is one nobody learns to reach for.
    /// </remarks>
    private static string Tip((long Total, SpaceCertainty Certainty)? space) =>
        space is null ? "This DNA space cannot be counted; run validate." : Full(space) + " unique DNA";

    /// <summary>
    /// The figure written out in full, for the surfaces wide enough to hold it.
    /// </summary>
    /// <param name="space">The figure and what it is, or null when the space is undefined.</param>
    /// <returns>Display text, never empty.</returns>
    private static string Full((long Total, SpaceCertainty Certainty)? space) =>
        space is not { } s ? Unknown : SpaceText.Describe(s.Total, s.Certainty);

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
