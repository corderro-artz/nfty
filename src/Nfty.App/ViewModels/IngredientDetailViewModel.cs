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

/// <summary>One row of the variant table.</summary>
/// <param name="Id">The variant's id.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Weight">Its roll weight.</param>
/// <param name="WithinPercent">Its share within this layer.</param>
/// <param name="OverallPercent">Its share across the whole collection.</param>
/// <param name="Thumbnail">A rendered swatch.</param>
public record VariantRow(string Id, string Name, double Weight, double WithinPercent, double OverallPercent, Bitmap Thumbnail);

/// <summary>One line of the Colorways panel's axis readout.</summary>
/// <param name="Label">What the axis is, e.g. "hue".</param>
/// <param name="Value">Its range or fixed value.</param>
/// <param name="Derived">Whether it was derived from a fixed color rather than stated as a range.</param>
public record ColorwayAxis(string Label, string Value, bool Derived);

public partial class IngredientDetailViewModel : ViewModelBase, IDisposable
{
    private readonly Action _editIngredient;
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
    public Bitmap? SelectedThumb => _variants.Count > 0 ? _variants[0].Thumbnail : null;

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
    /// <param name="dialogs">The dialog layer, for reporting an export failure.</param>
    public IngredientDetailViewModel(LoadedIngredient ing, LoadedRecipe recipe, LoadedCookBook book,
        IImageBridge bridge, Action editIngredient, Func<bool> isEditing,
        Action? jumpToRecipe = null, IStatusService? status = null,
        IFilePickerService? picker = null, IDialogService? dialogs = null)
    {
        Sort = new TableSort("Variant", () => OnPropertyChanged(nameof(Variants)));
        _ing = ing; _bridge = bridge;
        _editIngredient = editIngredient; _isEditing = isEditing;
        _jumpToRecipe = jumpToRecipe;
        _status = status;
        _picker = picker;
        _dialogs = dialogs;

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
    public void RaiseCanExecuteChanged() => DeleteVariantCommand.NotifyCanExecuteChanged();



    [RelayCommand]
    private void SelectVariant(string id)
    {
        var old = Hero;
        Hero = VariantImagery.Render(_bridge, _ing, id);
        old?.Dispose();
    }

    /// <summary>Opens the editor, which owns variants — and therefore owns deleting one.
    ///
    /// This used to report the action as unbuilt, which the shell rendered as
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
    private bool CanEdit() => _isEditing();

    /// <summary>Frees every rendered swatch and thumbnail.</summary>
    public void Dispose()
    {
        Hero?.Dispose();
        foreach (var v in _variants) v.Thumbnail.Dispose();
        foreach (var b in Colorways) b.Dispose();
    }
}
