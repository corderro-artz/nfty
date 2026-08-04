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

    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, INotYetWired notify, Action editIngredient, Func<bool> isEditing)
    {
        _ing = ing; _bridge = bridge;
        _notify = notify; _editIngredient = editIngredient; _isEditing = isEditing;
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
        if (m.Colorization is null)
            return new[] { new ColorwayAxis("COLOUR", "no colorize · composited as-is", true) };
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

    [RelayCommand(CanExecute = nameof(CanEdit))] private void DeleteVariant() => _notify.Report("Delete variant");
    [RelayCommand] private void JumpToRules() { /* nav within the recipe rail */ }
    [RelayCommand] private void EditIngredient() => _editIngredient();
    private bool CanEdit() => _isEditing();

    public void Dispose()
    {
        Hero?.Dispose();
        foreach (var v in _variants) v.Thumbnail.Dispose();
        foreach (var b in Colorways) b.Dispose();
    }
}
