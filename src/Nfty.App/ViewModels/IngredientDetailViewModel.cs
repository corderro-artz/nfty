using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Generation;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Imaging;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

/// <summary>One row of the variant table.</summary>
/// <remarks>
/// An observable object rather than a record because the row carries SELECTION now. The table was
/// read-only: its rows could not be clicked, the hero above it was pinned to the first variant for
/// the life of the pane, and <c>SelectVariantCommand</c> - which exists to move it - was reachable
/// from nothing. (The wiring sweep matches commands by NAME across all markup, and the ingredient
/// EDITOR has a command of the same name that is bound, so the sweep was satisfied by a binding on
/// another screen.)
/// </remarks>
public partial class VariantRow : ObservableObject
{
    /// <summary>The variant's id.</summary>
    public string Id { get; }
    /// <summary>Its display name.</summary>
    public string Name { get; }
    /// <summary>Its roll weight.</summary>
    public double Weight { get; }
    /// <summary>Its share within this layer.</summary>
    public double WithinPercent { get; }
    /// <summary>Its share across the whole collection.</summary>
    public double OverallPercent { get; }
    /// <summary>A rendered swatch.</summary>
    public Bitmap Thumbnail { get; }

    /// <summary>Whether this is the row the hero and Delete variant act on.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Creates a row.</summary>
    /// <param name="id">The variant's id.</param>
    /// <param name="name">Its display name.</param>
    /// <param name="weight">Its roll weight.</param>
    /// <param name="withinPercent">Its share within this layer.</param>
    /// <param name="overallPercent">Its share across the whole collection.</param>
    /// <param name="thumbnail">A rendered swatch.</param>
    public VariantRow(string id, string name, double weight, double withinPercent,
        double overallPercent, Bitmap thumbnail)
    {
        Id = id; Name = name; Weight = weight;
        WithinPercent = withinPercent; OverallPercent = overallPercent; Thumbnail = thumbnail;
    }
}

/// <summary>One line of the Colorways panel's axis readout.</summary>
/// <param name="Label">What the axis is, e.g. "hue".</param>
/// <param name="Value">Its range or fixed value.</param>
/// <param name="Derived">Whether it was derived from a fixed color rather than stated as a range.</param>
public record ColorwayAxis(string Label, string Value, bool Derived);

public partial class IngredientDetailViewModel : ViewModelBase, IDisposable
{
    private readonly Action _editIngredient;
    private readonly Func<string, Task>? _deleteVariant;
    private readonly Action? _jumpToRecipe;
    private readonly IStatusService? _status;
    private readonly IFilePickerService? _picker;
    private readonly IDialogService? _dialogs;
    private readonly Func<bool> _isEditing;
    private readonly IImageBridge _bridge;
    private readonly LoadedIngredient _ing;
    private readonly IReadOnlyList<VariantRow> _variants;

    /// <summary>The variant table's sort. Shared machinery (<see cref="TableSort"/>), so this table
    /// and the Set browser's and the Recipe's rules all follow one rule: first click ascending,
    /// clicking the active column reverses.</summary>
    public TableSort Sort { get; }

    [ObservableProperty] private Bitmap? _hero;

    /// <summary>The ingredient's display name.</summary>
    public string Name { get; }

    /// <summary>Lowercase, because the hero renders it as one running sentence
    /// ("custom · no colorize · composited as-is") with only this word kind-colored.</summary>
    public string KindText { get; }
    /// <summary>Whether it rolls its color per asset.</summary>
    public bool IsDynamic { get; }
    /// <summary>Whether it applies one fixed color.</summary>
    public bool IsStatic { get; }
    /// <summary>Whether it composites as-is.</summary>
    public bool IsCustom { get; }
    /// <summary>The Colorways heading, naming the kind.</summary>
    public string ColorwaysText { get; }
    /// <summary>Which color model the layer is authored in.</summary>
    public string ColorwaysModelText { get; }

    /// <summary>Hue sweep for the colorways band, or null when this kind has no rolled hue (static
    /// and custom layers). Rendered as a gradient rather than as variant thumbnails, which showed
    /// the source art instead of the color space the layer actually spans.</summary>
    public IReadOnlyList<Color>? HueBandStops { get; }
    /// <summary>Whether to draw the hue band — a fixed color has no range to show.</summary>
    public bool HasHueBand => HueBandStops is not null;
    /// <summary>Sample swatches across the layer's color range.</summary>
    public IReadOnlyList<Bitmap> Colorways { get; }
    /// <summary>The hue and saturation readouts.</summary>
    public IReadOnlyList<ColorwayAxis> ColorwayAxes { get; }

    /// <summary>
    /// The colorways themselves — a sample of the quantized colors this layer can actually roll.
    /// </summary>
    /// <remarks>
    /// <para>The panel is called COLORWAYS and had none: a gradient band and three text rows, then
    /// four hundred pixels of empty rail. The band shows the hue SWEEP, which is not the same thing
    /// — a layer quantized at 30/40 rolls a few dozen colors out of that sweep, and which ones is
    /// the question an author is asking when they open this pane.</para>
    ///
    /// <para>Sampled on the quantize STEPS rather than evenly across the range, so every swatch is a
    /// color the roller can really produce; the count beside them is
    /// <see cref="UniqueSpace.CountColors"/>, the same counter the DNA space is built from, so this
    /// figure and the CookBook card's cannot disagree. Value is fixed at 0.72 because the third
    /// channel comes from the value-map and is a property of the ART, not of the colorization —
    /// showing it at one lightness is what makes the strip about hue and saturation alone.</para>
    /// </remarks>
    public IReadOnlyList<Color> ColorwaySwatches { get; }

    /// <summary>How many distinct colors the layer admits, as the rail prints it.</summary>
    public string ColorwayCountText { get; }

    /// <summary>Whether the rail has colorways to show — a dynamic layer with a real range.</summary>
    public bool HasColorways => ColorwaySwatches.Count > 0;

    /// <summary>
    /// How many share bars one page of the hero strip holds: a 2×2 block.
    /// </summary>
    /// <remarks>
    /// <para>IT IS A BLOCK, NOT A RUN, AND THAT IS WHY IT CAN BE A CONSTANT. The strip used to cap
    /// at six and wrap, so the hero was two rows tall for three variants and three rows tall for
    /// five — its height was a property of the layer, and everything below it moved when a variant
    /// was added. Four in a fixed 2×2 makes the hero exactly one height for every layer in every
    /// book, which is what the app's own "geometry is fixed" rule asks for and what stops this strip
    /// pushing the pane's buttons around ever again.</para>
    ///
    /// <para>Four rather than six because a bar is read as a PROPORTION and two side by side is the
    /// comparison; six was chosen when the strip's job was to be a summary of everything, which is
    /// the job the table below actually has. The rest are a page away rather than cut off — the same
    /// pager the CookBook card's recipe table uses, for the same reason: an unbounded list pushes
    /// the things pinned under it off the card.</para>
    /// </remarks>
    public const int HeroBarsPerPage = 4;

    /// <summary>
    /// Every variant as a share bar, biggest slice first — what <see cref="VisibleHeroBars"/> pages
    /// through four at a time.
    /// </summary>
    /// <remarks>
    /// <para>IT IS PAGED BECAUSE IT PUSHED THE PANE'S OWN BUTTONS OFF THE SCREEN. The strip was a
    /// WrapPanel bound to every variant, so the hero grew by a row for every two — a layer with
    /// twelve made it 433px of a 494px pane, which left the variant table nothing and put "Delete
    /// variant" and "Export preview…" below the fold. Capping it at six fixed that and left the
    /// height still varying with the layer (one row at two variants, three at five), and a stack of
    /// six labelled percentages is a table drawn as bars — which is the job the real table below
    /// already has.</para>
    ///
    /// <para>Biggest first rather than in the table's order: the shape of a split is what dominates
    /// it, and the table below is where every variant is listed, in whatever order the reader asked
    /// for. Ties keep their input order — <c>OrderByDescending</c> is stable — so a layer of equal
    /// weights pages through them in its own order rather than an arbitrary one.</para>
    /// </remarks>
    public IReadOnlyList<VariantRow> HeroBars =>
        _variants.OrderByDescending(v => v.OverallPercent).ToArray();

    /// <summary>Which page of the hero strip is showing, zero-based.</summary>
    /// <remarks>Reset to 0 is deliberately NOT wired to anything: the pane is rebuilt whenever the
    /// selection moves, so there is no stale index to clear — the same reason the CookBook card's
    /// own index only ever follows a page-size change.</remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VisibleHeroBars))]
    [NotifyPropertyChangedFor(nameof(HeroDots))]
    [NotifyPropertyChangedFor(nameof(HeroPageLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextHeroPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousHeroPageCommand))]
    private int _heroPageIndex;

    /// <summary>The four bars this page shows, biggest slice first.</summary>
    public IReadOnlyList<VariantRow> VisibleHeroBars =>
        HeroBars.Skip(HeroPageIndex * HeroBarsPerPage).Take(HeroBarsPerPage).ToArray();

    /// <summary>How many pages the layer's variants come to. At least one, always.</summary>
    public int HeroPageCount =>
        Math.Max(1, (int)Math.Ceiling(_variants.Count / (double)HeroBarsPerPage));

    /// <summary>Whether there is more than one page. Drives the INK on the pager, never its
    /// geometry — the controls are present on a two-variant layer as well as a twenty-variant one,
    /// so adding a variant cannot move the hero's contents under the pointer.</summary>
    public bool HasHeroPages => HeroPageCount > 1;

    /// <summary>One dot per page, the current one lit.</summary>
    public IReadOnlyList<PageDot> HeroDots =>
        Enumerable.Range(0, HeroPageCount).Select(i => new PageDot(i == HeroPageIndex)).ToArray();

    /// <summary>Which bars are showing, of how many: "1–4 of 7".</summary>
    public string HeroPageLabel
    {
        get
        {
            if (_variants.Count == 0) return string.Empty;
            int from = HeroPageIndex * HeroBarsPerPage;
            int to = Math.Min(from + HeroBarsPerPage, _variants.Count);
            return $"{from + 1}–{to} of {_variants.Count}";
        }
    }

    /// <summary>Shows the next four slices.</summary>
    [RelayCommand(CanExecute = nameof(CanHeroNext))]
    private void NextHeroPage() => HeroPageIndex++;
    private bool CanHeroNext() => HeroPageIndex < HeroPageCount - 1;

    /// <summary>Shows the previous four slices.</summary>
    [RelayCommand(CanExecute = nameof(CanHeroBack))]
    private void PreviousHeroPage() => HeroPageIndex--;
    private bool CanHeroBack() => HeroPageIndex > 0;

    /// <summary>
    /// Variant rows in the active sort order.
    /// </summary>
    /// <remarks>
    /// All four data columns sort now. Two of the five used to — and neither could be reversed, so
    /// "which is the rarest variant overall?" was a question the table could not be asked, despite
    /// carrying the column that answers it.
    /// </remarks>
    public IReadOnlyList<VariantRow> Variants => Sort.Order(_variants, static (v, col) => col switch
    {
        "Weight" => v.Weight,
        "InRecipe" => v.WithinPercent,
        "Overall" => v.OverallPercent,
        _ => v.Name,
    });

    /// <summary>The 56px swatch the Custom branch of the colorways rail shows (mockup .cwcustom).
    /// A Custom layer has no hue band to display, so the rail shows the art itself instead. Null for
    /// an ingredient with no variants, which the view treats as nothing to draw.</summary>
    /// <remarks>It follows the SELECTED row, like the hero above it. It used to be pinned to
    /// <c>_variants[0]</c>, which was correct only because nothing could select anything else.</remarks>
    public Bitmap? SelectedThumb => Selected?.Thumbnail;

    /// <summary>The row the hero, the custom swatch and Delete variant act on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedThumb))]
    [NotifyCanExecuteChangedFor(nameof(DeleteVariantCommand))]
    private VariantRow? _selected;

    /// <summary>Why Delete variant is unavailable, or empty when it is available.</summary>
    /// <remarks>
    /// On the tooltip rather than left to guess: a control that is dim for a reason the screen does
    /// not give is a control the user has to guess at - the same argument the export dialog's
    /// <c>Problem</c> line makes.
    /// </remarks>
    public string DeleteVariantTip => _deleteVariant is null
        ? "This layer is open on its own; delete variants in the editor."
        : !_isEditing() ? "Editing is locked. Unlock to make changes."
        : _variants.Count <= 1 ? "A layer needs at least one variant. Delete the layer instead."
        : Selected is null ? "Pick a variant in the table first."
        : $"Remove \u201c{Selected.Name}\u201d from this layer";

    /// <summary>
    /// How often the OWNING RECIPE leaves this layer out, as a percent. Zero for a layer that always
    /// appears.
    /// </summary>
    /// <remarks>
    /// It comes from the recipe, not the ingredient, because that is where it lives — the same .igt
    /// is guaranteed in one recipe and a chase item in another. This pane is always looking at a
    /// layer THROUGH a recipe, so it has one to ask.
    ///
    /// <para>It is shown because without it the variant percentages are unexplainable. They already
    /// fold absence in, so a chase item's variants correctly read 5% rather than 50% — and a reader
    /// with no idea the layer is optional would simply believe the weights are strange.</para>
    /// </remarks>
    public double AbsentPercent { get; }

    /// <summary>Whether this layer may be left out at all.</summary>
    public bool IsOptional => AbsentPercent > 0;

    /// <summary>The chance as the hero's flag prints it.</summary>
    public string AbsentFlagText => AbsentPercent >= 100
        ? "never appears"
        : $"absent {AbsentPercent:0.##}%";

    /// <summary>What that flag means, spelled out.</summary>
    public string AbsentFlagTip => AbsentPercent >= 100
        ? "This recipe never includes this layer. It is shelved rather than deleted — set the "
          + "chance back below 100 to bring it back."
        : $"This recipe leaves this layer out of {AbsentPercent:0.##}% of its assets. The shares "
          + "below already account for that, so they are the odds of getting each variant rather "
          + "than its share among the others.";

    /// <summary>How many of the recipe's incompatibility rules mention this layer, on either side.
    /// The mockup's .hflag pill exists to answer "is this layer entangled?" at a glance, which is
    /// otherwise only discoverable by opening the recipe and reading its rules.</summary>
    public int RuleCount { get; }
    /// <summary>Whether any rule mentions this layer.</summary>
    public bool HasRules => RuleCount > 0;
    /// <summary>The rule flag's label, naming how many rules touch this layer.</summary>
    public string RuleFlagText => RuleCount == 1 ? "1 rule" : $"{RuleCount} rules";

    /// <summary>Builds the Ingredient detail pane.</summary>
    /// <param name="ing">The layer to describe.</param>
    /// <param name="recipe">Its owning recipe.</param>
    /// <param name="book">The owning book, for overall odds.</param>
    /// <param name="bridge">Converts an ImageSharp frame to an Avalonia bitmap.</param>
    /// <param name="editIngredient">Opens the ingredient editor.</param>
    /// <param name="isEditing">Whether editing is currently unlocked.</param>
    /// <param name="jumpToRecipe">Selects the owning recipe and scrolls to its rules.</param>
    /// <param name="status">The status bar's guidance channel.</param>
    /// <param name="picker">Chooses where to export a preview.</param>
    /// <param name="dialogs">The dialog layer, for the delete confirmation and for reporting an
    /// export failure.</param>
    /// <param name="deleteVariant">Removes one variant from this layer and saves the book. Null
    /// where there is nothing to save back to, which disables the button rather than hiding it.</param>
    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, Action editIngredient, Func<bool> isEditing,
        Action? jumpToRecipe = null, IStatusService? status = null,
        IFilePickerService? picker = null, IDialogService? dialogs = null,
        Func<string, Task>? deleteVariant = null)
    {
        Sort = new TableSort("Variant", () => OnPropertyChanged(nameof(Variants)));
        _ing = ing; _bridge = bridge;
        _editIngredient = editIngredient; _isEditing = isEditing;
        _deleteVariant = deleteVariant;
        _jumpToRecipe = jumpToRecipe;
        _status = status;
        _picker = picker;
        _dialogs = dialogs;

        // FROM THE BOOK'S COPY of the recipe, not from the one handed in — because that is where
        // the percentages below come from. RarityCalculator is given the BOOK and filters by recipe
        // id, so reading the chance off a different object lets the flag and the numbers it exists
        // to explain disagree: a capture fixture that set the chance on a detached recipe showed
        // "absent 90%" above variants still reading their 25/75 share among siblings. They resolve
        // to the same object in the app; making them resolve to the same object BY CONSTRUCTION is
        // what stops that being a coincidence.
        var owning = book.Recipes.FirstOrDefault(r => r.Manifest.Id == recipe.Manifest.Id) ?? recipe;
        AbsentPercent = owning.Manifest.AbsentPercentOf(ing.Manifest.Id);
        RuleCount = recipe.Manifest.Rules.Count(r =>
            r.When.IngredientId == ing.Manifest.Id || r.Targets.Any(t => t.IngredientId == ing.Manifest.Id));
        Name = ing.Manifest.Name;
        KindText = ing.Manifest.Kind.ToString().ToLowerInvariant();
        IsDynamic = ing.Manifest.Kind == LayerKind.Dynamic;
        IsStatic = ing.Manifest.Kind == LayerKind.Static;
        IsCustom = ing.Manifest.Kind == LayerKind.Custom;
        ColorwaysText = ColorwaysLabel(ing.Manifest);
        ColorwaysModelText = ColorwaysModelLabel(ing.Manifest);
        HueBandStops = BuildHueBand(ing.Manifest);
        ColorwayAxes = BuildAxes(ing.Manifest);
        ColorwaySwatches = BuildColorways(ing.Manifest);
        ColorwayCountText = ColorwayCount(ing.Manifest);

        var traits = RarityCalculator.Compute(book).Traits
            .Where(t => t.RecipeId == recipe.Manifest.Id && t.IngredientId == ing.Manifest.Id)
            .ToDictionary(t => t.VariantId, StringComparer.Ordinal);

        _variants = ing.Manifest.Variants.Select(v =>
        {
            traits.TryGetValue(v.Id, out var t);
            return new VariantRow(v.Id, v.Name, v.Weight,
                Math.Round(t?.WithinRecipePercent ?? 0, 1), Math.Round(t?.OverallPercent ?? 0, 1),
                VariantImagery.Render(bridge, ing, v.Id));
        }).ToList();

        // The first row is the selection the pane opens on, which is the variant the hero already
        // showed - so nothing about the screen changes until something is clicked.
        Selected = _variants.Count > 0 ? _variants[0] : null;
        if (Selected is not null) Selected.IsSelected = true;

        // A zero-variant ingredient is invalid per Validator, but CookBookArchive.Read doesn't
        // validate, so a hand-built/mid-authoring book can open one — mirror the editor's
        // handling of this exact case rather than indexing Variants[0] and crashing browsing.
        if (ing.Manifest.Variants.Count == 0)
        {
            Colorways = Array.Empty<Bitmap>();
            _hero = null;
        }
        else
        {
            Colorways = VariantImagery.Colorways(bridge, ing);
            _hero = VariantImagery.Render(bridge, ing, ing.Manifest.Variants[0].Id);
        }
    }

    /// <summary>The color model this layer is authored in, as the panel spells it.</summary>
    /// <remarks>
    /// Read from the layer, not assumed. Both of the labels below used to hardcode "HSV" while
    /// <see cref="ColorModel"/> has two members, so an HSL layer's card read "HSV · rolled" with
    /// the CookBook panel one click away printing "colorize hsl". The third channel is named after
    /// the model too: HSV's is value, HSL's is lightness, and it is the one the grayscale map
    /// supplies — saying "value" of an HSL layer names a channel that model does not have.
    /// </remarks>
    private static string ModelName(IngredientManifest m) =>
        m.Colorization?.Model == ColorModel.Hsl ? "HSL" : "HSV";

    private static string ThirdChannel(IngredientManifest m) =>
        m.Colorization?.Model == ColorModel.Hsl ? "lightness" : "value";

    /// <summary>The hero's one-line summary, which does carry the value-map aside.</summary>
    private static string ColorwaysLabel(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => $"{ModelName(m)} · rolled  ({ThirdChannel(m)} ← value-map)",
        LayerKind.Static => $"{ModelName(m)} · fixed  ({ThirdChannel(m)} ← value-map)",
        _ => "no colorize · composited as-is",
    };

    /// <summary>The .cwmodel chip. Short, because the mockup's chip is just "HSV · rolled" — the
    /// "value comes from the value-map" idea is stated once, by the derived Value axis row below it.
    /// Both were previously bound to the hero's longer string, which put the aside on screen twice
    /// and made the one element with a direct mockup equivalent the wrong one.</summary>
    private static string ColorwaysModelLabel(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => $"{ModelName(m)} · rolled",
        LayerKind.Static => $"{ModelName(m)} · fixed",
        _ => "no colorize",
    };

    /// <summary>Samples the layer's hue range into gradient stops. Only a dynamic layer rolls a hue,
    /// so every other kind returns null and the band is hidden rather than shown as a lie.</summary>
    private static IReadOnlyList<Color>? BuildHueBand(IngredientManifest m)
    {
        if (m.Kind != LayerKind.Dynamic || m.Colorization is null) return null;
        var entry = m.Colorization.Entries.FirstOrDefault(e => e.Weight > 0);
        if (entry?.Range is not { } range) return null;

        const int steps = 12;
        var stops = new List<Color>(steps);
        for (int i = 0; i < steps; i++)
        {
            double t = steps == 1 ? 0 : i / (double)(steps - 1);
            double hue = range.HueMin + (range.HueMax - range.HueMin) * t;
            double sat = (range.SatMin + range.SatMax) / 2.0 / 100.0;
            var rgb = ColorConvert.HsvToRgb(hue, sat, 0.72);
            stops.Add(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        }
        return stops;
    }

    /// <summary>How many swatches the rail shows at most. Four rows of six at the rail's width; past
    /// that the strip stops being a sample and starts being a wall.</summary>
    private const int ColorwaySampleCap = 24;

    /// <summary>Samples the colors this layer can roll, ON ITS QUANTIZE STEPS.</summary>
    /// <remarks>
    /// Walking the steps is what makes every swatch a color the roller can produce: sampling the
    /// range evenly would draw hues between two buckets, which is a picture of the range rather than
    /// of the palette. When the layer admits more than the cap, the walk STRIDES — it still lands on
    /// real buckets and it still spans the whole range, rather than showing the first two dozen and
    /// implying the rest are elsewhere.
    /// </remarks>
    private static IReadOnlyList<Color> BuildColorways(IngredientManifest m)
    {
        if (m.Kind != LayerKind.Dynamic || m.Colorization is null) return Array.Empty<Color>();
        var entry = m.Colorization.Entries.FirstOrDefault(e => e.Weight > 0 && e.Range is not null);
        if (entry?.Range is not { } range) return Array.Empty<Color>();

        double hStep = Math.Max(1, m.Colorization.HueQuantize);
        double sStep = Math.Max(1, m.Colorization.SatQuantize);

        // ColorRoller samples [Min, Max) - half-open - so the bucket count is the number of STARTS
        // in the range, and a degenerate Min == Max is the one case that reaches its endpoint.
        var hues = Buckets(range.HueMin, range.HueMax, hStep);
        var sats = Buckets(range.SatMin, range.SatMax, sStep);

        // Six across reads as a strip; the rows follow. Striding both axes keeps the sample spread
        // over the whole rectangle instead of clustering in one corner of it.
        int cols = Math.Min(6, hues.Count);
        int rows = Math.Max(1, Math.Min(ColorwaySampleCap / Math.Max(1, cols), sats.Count));

        var swatches = new List<Color>(cols * rows);
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                double hue = hues[Pick(hues.Count, cols, c)];
                double sat = sats[Pick(sats.Count, rows, r)];
                var rgb = ColorConvert.HsvToRgb(hue, sat / 100.0, 0.72);
                swatches.Add(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            }
        return swatches;
    }

    /// <summary>The bucket STARTS in a half-open range, at a step — what the roller can land on.</summary>
    private static List<double> Buckets(double min, double max, double step)
    {
        var list = new List<double>();
        if (max <= min) { list.Add(min); return list; }
        for (double v = Math.Floor(min / step) * step; v < max; v += step)
            if (v >= min) list.Add(v);
        if (list.Count == 0) list.Add(min);
        return list;
    }

    /// <summary>Index <paramref name="i"/> of <paramref name="want"/> samples spread over
    /// <paramref name="have"/> buckets, endpoints included.</summary>
    private static int Pick(int have, int want, int i) =>
        want <= 1 ? 0 : (int)Math.Round(i * (have - 1) / (double)(want - 1));

    /// <summary>The admissible-color figure, or an empty string when the layer rolls no color.</summary>
    private static string ColorwayCount(IngredientManifest m)
    {
        if (m.Kind != LayerKind.Dynamic || m.Colorization is null) return "";
        var (count, exact) = UniqueSpace.CountColors(m.Colorization);
        return exact ? $"{count:N0} colors" : "many colors";
    }

    private static IReadOnlyList<ColorwayAxis> BuildAxes(IngredientManifest m)
    {
        // No axes for an uncolorized layer. There used to be a single synthetic
        // ColorwayAxis("COLOR", "no colorize · composited as-is") here, which borrowed the
        // axis-row shape to say "there are no axes" - and made a full sentence share a row template
        // built for "HUE  190–320°". Custom now has its own branch in the view (mockup .cwcustom:
        // a swatch of the art plus a plain-language note), so this returns nothing.
        if (m.Colorization is null) return Array.Empty<ColorwayAxis>();
        var c = m.Colorization;
        var range = c.Entries.FirstOrDefault(e => e.Range is not null)?.Range;
        var list = new List<ColorwayAxis>();
        if (range is not null)
        {
            list.Add(new ColorwayAxis("HUE", $"{range.HueMin:0}–{range.HueMax:0}°", false));
            list.Add(new ColorwayAxis("SATURATION", $"{range.SatMin:0}–{range.SatMax:0}%", false));
        }
        else
        {
            var fixedSpec = c.Entries.FirstOrDefault(e => e.Fixed is not null)?.Fixed;
            if (fixedSpec is not null) list.Add(new ColorwayAxis("COLOR", fixedSpec, false));
        }
        list.Add(new ColorwayAxis(ThirdChannel(m).ToUpperInvariant(), "← value-map", true));
        return list;
    }

    /// <summary>Re-evaluates the commands whose availability depends on the edit lock, which lives
    /// outside this pane and changes without it.</summary>
    public void RaiseCanExecuteChanged()
    {
        DeleteVariantCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(DeleteVariantTip));
    }



    /// <summary>Makes one row the selection: the hero, the custom swatch and Delete variant follow
    /// it.</summary>
    /// <param name="id">The variant to select.</param>
    [RelayCommand]
    private void SelectVariant(string id)
    {
        if (_variants.FirstOrDefault(v => v.Id == id) is not { } row || ReferenceEquals(row, Selected))
            return;

        // Exactly two rows can change, so exactly two are touched - the same rule the Set browser's
        // selection follows, for the same reason.
        if (Selected is not null) Selected.IsSelected = false;
        Selected = row;
        row.IsSelected = true;

        var old = Hero;
        Hero = VariantImagery.Render(_bridge, _ing, id);
        old?.Dispose();
        OnPropertyChanged(nameof(DeleteVariantTip));
    }

    /// <summary>
    /// Deletes the selected variant from this layer.
    /// </summary>
    /// <remarks>
    /// <para>It used to do no such thing. It said "Delete variants in the editor, where the change
    /// can be undone" and NAVIGATED there - so a button labelled Delete variant deleted nothing and
    /// left the reader on a different screen, which is what a bug report called broken. Before that
    /// it reported the action as unbuilt. Both are the same mistake: a control that names an action
    /// has to perform it.</para>
    /// <para>Deleting a variant is STRUCTURE, so it goes the way every other structural edit in this
    /// pane's owner goes - <c>CookBookEdits</c>, then <c>CookBookPersistence</c>, behind the edit
    /// lock - rather than growing a second, separately-persisted deletion path. The pane asks (it
    /// knows which row and what it is called); the Explorer writes (it owns the book, the session
    /// and the reload).</para>
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDeleteVariant))]
    private async Task DeleteVariant()
    {
        if (_deleteVariant is null || Selected is not { } row || _dialogs is null) return;
        var ok = await _dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(_dialogs,
            "Delete variant?",
            $"Remove \u201c{row.Name}\u201d from \u201c{Name}\u201d. This can\u2019t be undone.",
            "Delete"));
        if (!ok) return;
        await _deleteVariant(row.Id);
    }

    private bool CanDeleteVariant() =>
        _deleteVariant is not null && _dialogs is not null && _isEditing()
        && _variants.Count > 1 && Selected is not null;
    /// <summary>Selects the owning recipe, whose Rules panel is where this layer's rules live. Used
    /// to be an empty body behind a permanently-visible "Jump to rules" button - a control that
    /// looked available and did nothing. It is now the .hflag pill, shown only when there is
    /// something to jump to.</summary>
    [RelayCommand] private void JumpToRules() => _jumpToRecipe?.Invoke();
    [RelayCommand] private void EditIngredient() => _editIngredient();

    /// <summary>The CLI's <c>preview</c>: writes the selected variant as generation would render it.
    ///
    /// The GUI could not previously get a rendered PNG out at all short of cooking a whole Set, which
    /// is a slow and destructive way to answer "what will this layer actually look like". The render
    /// itself is <see cref="VariantPreview"/> - the same code the command runs - so the file this
    /// writes is byte-identical to the one the CLI writes.</summary>
    [RelayCommand(CanExecute = nameof(CanExportPreview))]
    private async Task ExportPreview()
    {
        if (_picker is null || _variants.Count == 0) return;

        string? path;
        try { path = await _picker.SaveFileAsync("Export preview", ".png"); }
        catch (Exception ex) { await ShowPreviewError(ex.Message); return; }
        if (path is null) return;   // canceled

        try
        {
            // A colorized layer needs a color; the ingredient's own first fixed color or range
            // start is the honest default - it is what generation would most likely roll - and the
            // editor is where a specific one gets chosen.
            using var img = VariantPreview.Render(_ing, _variants[0].Id, DefaultColorSpec());
            // Fully qualified: this file deliberately does not import SixLabors.ImageSharp, whose
            // Image type would collide with Avalonia's.
            SixLabors.ImageSharp.ImageExtensions.Save(img, path,
                new SixLabors.ImageSharp.Formats.Png.PngEncoder());
            _status?.Say($"Wrote {path}");
        }
        catch (Exception ex) { await ShowPreviewError(ex.Message); }
    }

    private bool CanExportPreview() => _picker is not null && _variants.Count > 0;

    /// <summary>Null for a Custom layer, which is never colorized and needs none.</summary>
    private string? DefaultColorSpec()
    {
        if (!VariantPreview.NeedsColor(_ing)) return null;
        var entry = _ing.Manifest.Colorization?.Entries.FirstOrDefault();
        if (entry?.Fixed is { } fixedSpec) return fixedSpec;
        // The prefix names the model the numbers are IN. They come off this layer's own range, so
        // spelling them "hsv:" for an HSL layer would hand the picker a saturation the layer never
        // meant - the two models agree on hue and disagree on saturation for the same triple.
        var model = _ing.Manifest.Colorization?.Model == ColorModel.Hsl ? "hsl" : "hsv";
        if (entry?.Range is { } range) return $"{model}:{range.HueMin:0},{range.SatMin:0},80";
        return $"{model}:0,0,80";
    }

    private Task ShowPreviewError(string message) =>
        _dialogs is null
            ? Task.CompletedTask
            : _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not export preview", message));
    // There is deliberately no CanEdit here any more. One existed, referenced by nothing — a
    // predicate written to gate the pencil and never attached to it — which read as though the
    // pencil were lock-gated while EditIngredientCommand carries no CanExecute at all. The pencil
    // stays ungated on purpose (the editor is also how you LOOK at a layer); what is gated is the
    // editor's Save, see IngredientEditorViewModel.IsReadOnly.

    /// <summary>Frees every rendered swatch and thumbnail.</summary>
    public void Dispose()
    {
        Hero?.Dispose();
        foreach (var v in _variants) v.Thumbnail.Dispose();
        foreach (var b in Colorways) b.Dispose();
    }
}
