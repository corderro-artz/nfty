using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Imaging;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>One cell of the editor's palette strip: a ramp slot or a saved swatch.</summary>
/// <remarks>
/// The colour is <b>artwork data, not a theme token</b>. The house rule that no raw colour lives
/// outside <c>Themes/Tokens.axaml</c> governs chrome; a ramp Core computed and a swatch the author
/// mixed are neither, and moving them into a theme dictionary would make them change with the theme.
/// </remarks>
public sealed partial class PaletteSwatch : ObservableObject
{
    /// <summary>The colour, in Core's terms.</summary>
    public RgbColor Rgb { get; }

    /// <summary>The same colour as the view paints it.</summary>
    public Avalonia.Media.Color Color { get; }

    /// <summary>The prefixed spec an author would type for this colour — the tooltip, and the form
    /// the palette is persisted in, so what is shown and what is stored cannot drift.</summary>
    public string Spec { get; }

    /// <summary>Whether this swatch can be forgotten from here. True only for the app-wide saved
    /// swatches: a ramp slot is computed rather than stored, and a CookBook's own swatches travel in
    /// its archive and are not this screen's to delete.</summary>
    public bool CanForget { get; }

    /// <summary>Forgets this swatch, or null when it is not this screen's to forget.
    ///
    /// <para>Carried BY THE CELL rather than reached through <c>$parent[ItemsControl]</c>, because
    /// the control that invokes it is a <c>ContextMenu</c> — and a ContextMenu is not in the visual
    /// tree, so an ancestor lookup from inside one cannot resolve. It does not throw either: the
    /// whole item template comes up empty and the saved swatches simply do not render, the same
    /// silent failure an unresolved <c>DynamicResource</c> produces.</para></summary>
    public System.Windows.Input.ICommand? ForgetCommand { get; }

    /// <summary>Whether this is the colour the brush is currently laying down.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Creates a palette cell.</summary>
    /// <param name="rgb">The colour.</param>
    /// <param name="forget">Forgets this swatch, or null for a cell that cannot be forgotten.</param>
    public PaletteSwatch(RgbColor rgb, System.Windows.Input.ICommand? forget = null)
    {
        Rgb = rgb;
        Color = Avalonia.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B);
        Spec = ColorSpec.Format(rgb);
        ForgetCommand = forget;
        CanForget = forget is not null;
    }
}

/// <summary>
/// The editor's palette strip and opacity lock: which ramp the ten slots offer, the swatches the
/// author has saved, the colour and alpha the brush lays down, and whether partial alpha is admitted
/// at all.
/// </summary>
public partial class IngredientEditorViewModel
{
    /// <summary>How many ramp slots the strip shows — Core's number, not a second opinion.</summary>
    public static int PaletteSlots => Palette.Slots;

    private readonly IPaletteService _palette;

    /// <summary>The open CookBook's own swatches, read once when the editor opens. They show above
    /// the app-wide ones and cannot be edited from this screen — a collection's palette travels in
    /// its archive, and changing it belongs with the CookBook, not with one layer's canvas.</summary>
    private readonly IReadOnlyList<RgbColor> _bookSwatches;

    // Whether colour mode has ever been entered this session; see OnPaintModeChanged.
    private bool _everEnteredColor;

    // Shown at most once per editor session. The warning is about what partial alpha does to a
    // downstream voxel conversion, which does not become more true on the second stroke.
    private bool _partialAlphaWarned;

    /// <summary>The ten ramp slots — greys in grayscale mode, hues in colour mode. The count never
    /// changes, so swapping the mode repaints ten cells and reflows nothing.</summary>
    public ObservableCollection<PaletteSwatch> Ramp { get; } = new();

    /// <summary>The saved swatches: the open book's first, the app-wide ones beneath, deduplicated
    /// by <see cref="Palette.Combine"/> so a colour saved in both appears once.</summary>
    public ObservableCollection<PaletteSwatch> SavedSwatches { get; } = new();

    /// <summary>Which ramp the strip offers. Not a property of the artwork: switching it hands the
    /// author different colours to pick and repaints no pixel that is already down.</summary>
    [ObservableProperty] private PaletteMode _paintMode;

    /// <summary>Whether a stroke may write partial alpha. Locked is the default and the zero value.</summary>
    [ObservableProperty] private OpacityLock _opacityMode;

    /// <summary>Hue of the paint colour, 0-360. Colour mode only.</summary>
    [ObservableProperty] private double _brushHue;

    /// <summary>Saturation of the paint colour, 0-100. Colour mode only.</summary>
    [ObservableProperty] private double _brushSat = 100;

    /// <summary>The alpha every painted pixel carries. Inert while the lock is on, where the paint
    /// stack snaps it to 255 or 0 regardless — the control stays in the layout and stops responding
    /// rather than disappearing, so unlocking moves nothing.</summary>
    [ObservableProperty] private int _brushAlpha = 255;

    /// <summary>Whether the strip is offering colour rather than greys.</summary>
    public bool IsColorMode => PaintMode == PaletteMode.Color;

    /// <summary>Whether partial alpha is currently admitted — drives the alpha slider's live state.</summary>
    public bool IsAlphaEnabled => OpacityMode == OpacityLock.Unlocked;

    /// <summary>Backs the lock button's active treatment.</summary>
    public bool IsOpacityLocked => OpacityMode == OpacityLock.Locked;

    /// <summary>
    /// Whether grayscale painting is offered at all. A Custom layer exports its colour raster and
    /// nothing else, so painting one in grayscale would edit pixels no archive ever sees — the mode
    /// is refused rather than allowed and quietly discarded.
    /// </summary>
    public bool CanPaintGrayscale => !IsCustom;

    /// <summary>
    /// The colour the brush is laying down right now, whichever mode supplied it.
    /// </summary>
    /// <remarks>
    /// Colour mode is HSV over three axes the screen already had: the toolstrip's value ramp is
    /// <b>V</b>, and the colorize rail's hue and saturation tracks — dead space on a Custom layer,
    /// which rolls nothing — become <b>H</b> and <b>S</b>. Grayscale mode uses the same V axis alone,
    /// so the ramp means "how bright" in both modes and no control changes what it does under the
    /// author. Both modes answer in RGB, which is what lets one comparison drive the selected cell.
    /// </remarks>
    public RgbColor CurrentRgb => IsColorMode
        ? ColorConvert.HsvToRgb(BrushHue, BrushSat / 100.0, BrushValue / 255.0)
        : new RgbColor((byte)BrushValue, (byte)BrushValue, (byte)BrushValue);

    /// <summary>The hue axis as the rail prints it.</summary>
    public string BrushHueText => $"{BrushHue:0}°";

    /// <summary>The saturation axis as the rail prints it.</summary>
    public string BrushSatText => $"{BrushSat:0}%";

    /// <summary>The paint colour as a swatch, alpha included — under the lock alpha is always 255,
    /// so the swatch only ever looks translucent when a translucent stroke is really what is armed.</summary>
    public Avalonia.Media.Color BrushSwatch =>
        Avalonia.Media.Color.FromArgb((byte)(IsAlphaEnabled ? BrushAlpha : 255),
            CurrentRgb.R, CurrentRgb.G, CurrentRgb.B);

    /// <summary>The armed colour as a spec, for the strip's readout.</summary>
    public string BrushSpec => ColorSpec.Format(CurrentRgb);

    /// <summary>The pixel a colour-mode command paints.</summary>
    private SixLabors.ImageSharp.PixelFormats.Rgba32 ColorInk =>
        new(CurrentRgb.R, CurrentRgb.G, CurrentRgb.B, EffectiveAlpha);

    /// <summary>The pixel a grayscale command paints.</summary>
    private GrayPixel GrayInk => new((byte)BrushValue, EffectiveAlpha);

    // The lock is enforced in Core (RegionEditCommand.Admit) whatever we hand it; sending 255 while
    // locked simply means the ink and the pixel that lands agree, which is what the swatch shows.
    private byte EffectiveAlpha => IsAlphaEnabled ? (byte)BrushAlpha : (byte)255;

    partial void OnPaintModeChanged(PaletteMode value)
    {
        // Entering colour mode widens EVERY variant, not just the selected one: a save writes the
        // whole ingredient, and a variant left without a colour raster would make the export throw
        // on a variant the author never visited.
        if (value == PaletteMode.Color)
        {
            foreach (var v in _draft.Variants) v.EnsureColor();

            // Arm a colour the author can see in the palette. The grayscale default (V=128) reads as
            // HSV(0, 100%, 50%) once hue and saturation join it — a muddy dark red nobody picked and
            // no slot offers. Only on the FIRST entry: after that the armed colour is theirs.
            if (!_everEnteredColor)
            {
                _everEnteredColor = true;
                BrushValue = 255;
            }
        }

        RebuildRamp();
        OnPropertyChanged(nameof(IsColorMode));
        NotifyBrushChanged();
        RebuildSurfaces();
        RefreshThumbnails();
        UndoCommand?.NotifyCanExecuteChanged();
        RedoCommand?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SaveNoteText));
        // The rail's hue and saturation tracks change what they mean with the mode, so everything
        // keyed off that has to be announced here too.
        OnPropertyChanged(nameof(ShowColorizeMode));
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
    }

    partial void OnOpacityModeChanged(OpacityLock value)
    {
        OnPropertyChanged(nameof(IsAlphaEnabled));
        OnPropertyChanged(nameof(IsOpacityLocked));
        OnPropertyChanged(nameof(BrushSwatch));
    }

    partial void OnBrushHueChanged(double value) { OnPropertyChanged(nameof(BrushHueText)); NotifyBrushChanged(); }
    partial void OnBrushSatChanged(double value) { OnPropertyChanged(nameof(BrushSatText)); NotifyBrushChanged(); }
    partial void OnBrushAlphaChanged(int value) => OnPropertyChanged(nameof(BrushSwatch));

    /// <summary>Announces every projection of the armed colour at once. One method because the three
    /// axes each change all of them, and a per-axis list is three places to forget one.</summary>
    private void NotifyBrushChanged()
    {
        OnPropertyChanged(nameof(CurrentRgb));
        OnPropertyChanged(nameof(BrushSwatch));
        OnPropertyChanged(nameof(BrushSpec));
        SyncSwatchSelection();
    }

    /// <summary>Builds the ten slots for the current mode.</summary>
    private void RebuildRamp()
    {
        Ramp.Clear();
        foreach (var c in Palette.RampFor(PaintMode)) Ramp.Add(new PaletteSwatch(c));
        SyncSwatchSelection();
    }

    /// <summary>Rebuilds the saved row from both scopes. Called on open and after every save/forget,
    /// since the app palette is shared and its list is the service's, not a copy of ours.</summary>
    private void RefreshSaved()
    {
        SavedSwatches.Clear();
        foreach (var c in Palette.Combine(_bookSwatches, _palette.Swatches))
            SavedSwatches.Add(new PaletteSwatch(c,
                forget: _bookSwatches.Contains(c) ? null : ForgetSwatchCommand));
        SyncSwatchSelection();
    }

    private void SyncSwatchSelection()
    {
        var current = CurrentRgb;
        foreach (var s in Ramp) s.IsSelected = s.Rgb == current;
        foreach (var s in SavedSwatches) s.IsSelected = s.Rgb == current;
    }

    /// <summary>Arms a palette colour.</summary>
    /// <param name="swatch">The cell that was clicked.</param>
    /// <remarks>
    /// In grayscale mode a colour swatch is taken as its <b>lightness</b> (BT.709), the same
    /// reduction importing a colour PNG into a value-map performs. A value-map stores lightness and
    /// nothing else, so the alternative to converting is refusing the click, and refusing it would
    /// leave saved swatches visibly present and silently inert.
    /// </remarks>
    [RelayCommand]
    private void PickSwatch(PaletteSwatch swatch)
    {
        if (!IsColorMode) { BrushValue = Luminance(swatch.Rgb); return; }
        // Decompose onto the three axes rather than storing the colour, so the sliders show where the
        // picked colour sits and the next drag continues from there instead of jumping.
        var (h, sat, v) = ColorConvert.RgbToHsv(swatch.Rgb);
        BrushHue = h;
        BrushSat = sat * 100.0;
        BrushValue = (int)Math.Round(v * 255.0);
    }

    /// <summary>ITU-R BT.709 luminance, matching <c>ImageSharp</c>'s <c>Grayscale()</c> — which is
    /// what an imported colour PNG is reduced through, so a swatch and an import agree.</summary>
    private static byte Luminance(RgbColor c) =>
        (byte)Math.Round(0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B);

    /// <summary>Saves the armed colour to the app-wide palette. Re-saving one already there is a
    /// no-op, so the button is never a way to accumulate duplicates.</summary>
    [RelayCommand]
    private void SaveSwatch()
    {
        _palette.Add(CurrentRgb);
        RefreshSaved();
    }

    /// <summary>Forgets an app-wide saved swatch. Only ever reachable from a cell that carries this
    /// command, which is how a CookBook's own swatches are kept out of reach.</summary>
    /// <param name="swatch">The cell to forget.</param>
    [RelayCommand]
    private void ForgetSwatch(PaletteSwatch swatch)
    {
        if (!swatch.CanForget) return;   // the command is public; the rule must not live only in the view
        _palette.Remove(swatch.Rgb);
        RefreshSaved();
    }

    /// <summary>Switches the strip to the grey ramp.</summary>
    [RelayCommand]
    private void SetPaintGrayscale()
    {
        if (CanPaintGrayscale) PaintMode = PaletteMode.Grayscale;
    }

    /// <summary>Switches the strip to the rainbow ramp.</summary>
    [RelayCommand] private void SetPaintColor() => PaintMode = PaletteMode.Color;

    /// <summary>
    /// Toggles the opacity lock, warning once before partial alpha is admitted for the first time
    /// this session. Cancelling the warning leaves the lock on — the dialog is a gate, not a notice
    /// shown after the fact.
    /// </summary>
    [RelayCommand]
    private async Task ToggleOpacityLock()
    {
        if (OpacityMode == OpacityLock.Unlocked) { OpacityMode = OpacityLock.Locked; return; }

        if (!_partialAlphaWarned)
        {
            var ok = await _dialogs.ShowAsync<bool>(new ConfirmDialogViewModel(_dialogs,
                "Allow partial transparency?",
                "Semi-transparent pixels do not voxelise cleanly: a model built from this art has no way "
                + "to resolve a voxel that is only partly there, so a downstream converter will either "
                + "drop it or make it solid. Fully painted and fully erased pixels are unaffected.\n\n"
                + "This applies to what you paint from here on; nothing already on the canvas changes.",
                "Allow partial alpha"));
            if (!ok) return;
            _partialAlphaWarned = true;
        }
        OpacityMode = OpacityLock.Unlocked;
    }
}
