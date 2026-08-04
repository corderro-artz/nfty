using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Imaging;
using Nfty.App.Services;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Imaging;
using Nfty.Core.Stats;

namespace Nfty.App.ViewModels;

public record VariantRow(string Id, string Name, double Weight, double WithinPercent, double OverallPercent, Bitmap Thumbnail);

public record ColorwayAxis(string Label, string Value, bool Derived);

public partial class IngredientDetailViewModel : ViewModelBase, IDisposable
{
    private readonly INotYetWired _notify;
    private readonly Action _editIngredient;
    private readonly Action? _jumpToRecipe;
    private readonly IStatusService? _status;
    private readonly Func<bool> _isEditing;
    private readonly IImageBridge _bridge;
    private readonly LoadedIngredient _ing;
    private readonly IReadOnlyList<VariantRow> _variants;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Variants))]
    private string _sortColumn = "Variant";

    [ObservableProperty] private Bitmap? _hero;

    public string Name { get; }

    /// <summary>Lowercase, because the hero renders it as one running sentence
    /// ("custom · no colorize · composited as-is") with only this word kind-coloured.</summary>
    public string KindText { get; }
    public bool IsDynamic { get; }
    public bool IsStatic { get; }
    public bool IsCustom { get; }
    public string ColorwaysText { get; }
    public string ColorwaysModelText { get; }

    /// <summary>Hue sweep for the colorways band, or null when this kind has no rolled hue (static
    /// and custom layers). Rendered as a gradient rather than as variant thumbnails, which showed
    /// the source art instead of the colour space the layer actually spans.</summary>
    public IReadOnlyList<Color>? HueBandStops { get; }
    public bool HasHueBand => HueBandStops is not null;
    public IReadOnlyList<Bitmap> Colorways { get; }
    public IReadOnlyList<ColorwayAxis> ColorwayAxes { get; }

    /// <summary>Variant rows ordered by the active sort column: "Weight" (heaviest first) or,
    /// by default, "Variant" (name, ordinal).</summary>
    public IReadOnlyList<VariantRow> Variants => SortColumn == "Weight"
        ? _variants.OrderByDescending(v => v.Weight).ThenBy(v => v.Name, StringComparer.Ordinal).ToList()
        : _variants.OrderBy(v => v.Name, StringComparer.Ordinal).ToList();

    /// <summary>The 56px swatch the Custom branch of the colorways rail shows (mockup .cwcustom).
    /// A Custom layer has no hue band to display, so the rail shows the art itself instead. Null for
    /// an ingredient with no variants, which the view treats as nothing to draw.</summary>
    public Bitmap? SelectedThumb => _variants.Count > 0 ? _variants[0].Thumbnail : null;

    /// <summary>How many of the recipe's incompatibility rules mention this layer, on either side.
    /// The mockup's .hflag pill exists to answer "is this layer entangled?" at a glance, which is
    /// otherwise only discoverable by opening the recipe and reading its rules.</summary>
    public int RuleCount { get; }
    public bool HasRules => RuleCount > 0;
    public string RuleFlagText => RuleCount == 1 ? "1 rule" : $"{RuleCount} rules";

    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INotYetWired notify, Action editIngredient, Func<bool> isEditing,
        Action? jumpToRecipe = null, IStatusService? status = null)
    {
        _ing = ing; _bridge = bridge;
        _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing;
        _jumpToRecipe = jumpToRecipe;
        _status = status;

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

    /// <summary>The hero's one-line summary, which does carry the value-map aside.</summary>
    private static string ColorwaysLabel(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => "HSV · rolled  (value ← value-map)",
        LayerKind.Static => "HSV · fixed  (value ← value-map)",
        _ => "no colorize · composited as-is",
    };

    /// <summary>The .cwmodel chip. Short, because the mockup's chip is just "HSV · rolled" — the
    /// "value comes from the value-map" idea is stated once, by the derived Value axis row below it.
    /// Both were previously bound to the hero's longer string, which put the aside on screen twice
    /// and made the one element with a direct mockup equivalent the wrong one.</summary>
    private static string ColorwaysModelLabel(IngredientManifest m) => m.Kind switch
    {
        LayerKind.Dynamic => "HSV · rolled",
        LayerKind.Static => "HSV · fixed",
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

    private static IReadOnlyList<ColorwayAxis> BuildAxes(IngredientManifest m)
    {
        // No axes for an uncolorized layer. There used to be a single synthetic
        // ColorwayAxis("COLOUR", "no colorize · composited as-is") here, which borrowed the
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
            if (fixedSpec is not null) list.Add(new ColorwayAxis("COLOUR", fixedSpec, false));
        }
        list.Add(new ColorwayAxis("VALUE", "← value-map", true));
        return list;
    }

    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();

    [RelayCommand] private void SortBy(string col) => SortColumn = col;

    [RelayCommand]
    private void SelectVariant(string id)
    {
        var old = Hero;
        Hero = VariantImagery.Render(_bridge, _ing, id);
        old?.Dispose();
    }

    /// <summary>Opens the editor, which owns variants — and therefore owns deleting one.
    ///
    /// This used to call <c>_notify.Report("Delete variant")</c>, which the shell renders as
    /// "Not wired yet: Delete variant". That was wrong twice over: the button was enabled and
    /// looked like it worked, and the feature is not unbuilt at all — the editor has a real delete
    /// with a confirm dialog and undo history. Routing here mirrors what Add does from the
    /// Explorer: variants live in the editor, so both actions take you to it rather than growing a
    /// second, separately-persisted deletion path.</summary>
    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void DeleteVariant()
    {
        _status?.Say("Delete variants in the editor, where the change can be undone.");
        _editIngredient();
    }
    /// <summary>Selects the owning recipe, whose Rules panel is where this layer's rules live. Used
    /// to be an empty body behind a permanently-visible "Jump to rules" button - a control that
    /// looked available and did nothing. It is now the .hflag pill, shown only when there is
    /// something to jump to.</summary>
    [RelayCommand] private void JumpToRules() => _jumpToRecipe?.Invoke();
    [RelayCommand] private void EditIngredient() => _editIngredient();
    private bool CanEdit() => _isEditing();

    public void Dispose()
    {
        Hero?.Dispose();
        foreach (var v in _variants) v.Thumbnail.Dispose();
        foreach (var b in Colorways) b.Dispose();
    }
}
