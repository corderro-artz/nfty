using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <summary>
/// Turning a picture into a layer: what it will be called, and which kind of layer it becomes.
/// </summary>
/// <remarks>
/// <para><b>The kind is the whole question, and it is not reversible by hand.</b> A <b>Custom</b>
/// layer keeps the picture exactly as it is and composites it untouched — which is what "import my
/// art" usually means. <b>Dynamic</b> and <b>Static</b> are value-maps: they keep the picture's
/// LIGHTNESS and get their color at generation time, so importing a colored picture as one of those
/// throws its colors away deliberately. That is worth doing — it is how one drawing becomes
/// thousands of colorways — and it is worth saying out loud first, which is why the form previews
/// both and says which one it is showing.</para>
///
/// <para><b>The image is not resampled and a wrong size is refused.</b> Every variant in a book
/// shares the CookBook canvas, and scaling a value-map would soften the art the DNA was built on —
/// the rule the whole product already keeps (<c>Validator</c> checks it, the editor's own variant
/// import refuses it, and nothing in Core resamples anything).</para>
///
/// <para>It loads the picture ONCE, in <see cref="TryLoad"/>, and holds it for the life of the
/// form — the preview, the color check and the built layer all come off that one decode rather than
/// three reads of a file that could change between them.</para>
/// </remarks>
public partial class ImportImageViewModel : WizardViewModelBase, IDisposable
{
    private readonly Dimensions _canvas;
    private readonly IImageBridge _bridge;
    private Image<Rgba32>? _source;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LayerKind _kind = LayerKind.Custom;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";

    /// <summary>The file this layer is being built from.</summary>
    public string SourcePath { get; }

    /// <summary>Its leaf name, which is what the form shows.</summary>
    public string SourceLeaf => Path.GetFileName(SourcePath);

    /// <summary>The picture as it will be stored, under the kind currently chosen: untouched for a
    /// Custom layer, its lightness for the other two.</summary>
    [ObservableProperty] private Bitmap? _preview;

    /// <summary>Why this cannot be imported, or empty. Stated rather than only disabling the
    /// button: a control dim for a reason the screen does not give is one the user has to guess at.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _problem = "";

    /// <summary>The picture's own size, as the form prints it.</summary>
    public string SizeText => _source is { } s ? $"{s.Width}×{s.Height}" : "—";

    /// <summary>The canvas every variant in this book has to match.</summary>
    public string CanvasText => $"{_canvas.Width}×{_canvas.Height}";

    /// <summary>The file and its size on one line, which is where the form prints them: beside the
    /// eyebrow rather than in a row of their own, because a row of their own cost the card 22px it
    /// does not have (<c>WizardFitsTests</c> measures that).</summary>
    public string SourceText => $"{SourceLeaf} · {SizeText}";

    /// <summary>Whether the picture carries color that a value-map import would discard.</summary>
    public bool LosesColor => Kind != LayerKind.Custom && _source is { } s && ImageImport.HasColor(s);

    /// <summary>Dynamic layers roll a color per asset from a hue/saturation range.</summary>
    public bool ShowColorRange => Kind == LayerKind.Dynamic;

    /// <summary>Static layers apply one fixed color.</summary>
    public bool ShowFixedColor => Kind == LayerKind.Static;

    /// <summary>Backs the "Dynamic" card.</summary>
    public bool IsKindDynamic
    {
        get => Kind == LayerKind.Dynamic;
        set { if (value) Kind = LayerKind.Dynamic; }
    }

    /// <summary>Backs the "Static" card.</summary>
    public bool IsKindStatic
    {
        get => Kind == LayerKind.Static;
        set { if (value) Kind = LayerKind.Static; }
    }

    /// <summary>Backs the "Custom" card.</summary>
    public bool IsKindCustom
    {
        get => Kind == LayerKind.Custom;
        set { if (value) Kind = LayerKind.Custom; }
    }

    /// <summary>The ingredient id derived from the name.</summary>
    public string DerivedId => DeriveId(Name);

    /// <summary>The derived id as the Identifier chip prints it.</summary>
    public string DerivedIdText => IdChipText(DerivedId);

    /// <summary>The hue range as the form prints it.</summary>
    public string HueRangeText => $"{HueMin:0}–{HueMax:0}°";

    /// <summary>The saturation range as the form prints it.</summary>
    public string SatRangeText => $"{SatMin:0}–{SatMax:0}%";

    /// <summary>Creates the form.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="path">The picture to import.</param>
    /// <param name="canvas">The CookBook canvas the picture must match.</param>
    /// <param name="bridge">Renders the preview.</param>
    /// <param name="takenIds">Ingredient ids already in the target recipe, so a clash is reported
    /// while it can still be retyped rather than after the write is attempted.</param>
    public ImportImageViewModel(IDialogService dialogs, string path, Dimensions canvas,
        IImageBridge bridge, IReadOnlyCollection<string>? takenIds = null)
        : base(dialogs)
    {
        SourcePath = path;
        _canvas = canvas;
        _bridge = bridge;
        _taken = takenIds ?? Array.Empty<string>();

        // A picture is named after its file until somebody says otherwise, which is almost always
        // the name they wanted.
        Name = Path.GetFileNameWithoutExtension(path);
    }

    private readonly IReadOnlyCollection<string> _taken;

    /// <summary>
    /// Reads the picture and works out whether it can be imported at all.
    /// </summary>
    /// <remarks>
    /// Separate from the constructor because it touches the disk and can fail: a form that threw
    /// while being built would take the Import command down with it, and the honest answer to an
    /// unreadable file is a message rather than a dialog that never opens.
    /// </remarks>
    /// <returns>Null when the form is ready to show, or the reason it cannot be.</returns>
    public string? TryLoad()
    {
        try { _source = Image.Load<Rgba32>(SourcePath); }
        catch (Exception ex) { return $"“{SourceLeaf}” could not be read as an image: {ex.Message}"; }

        if (_source.Width != _canvas.Width || _source.Height != _canvas.Height)
        {
            var (w, h) = (_source.Width, _source.Height);
            _source.Dispose();
            _source = null;
            return $"“{SourceLeaf}” is {w}×{h}; this CookBook's canvas is {_canvas.Width}×{_canvas.Height}. "
                + "Every variant in a book is the same size, and nfty does not resample - resizing a "
                + "value-map would soften the art the DNA was built on. Resize the picture first.";
        }

        Rebuild();
        return null;
    }

    partial void OnKindChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColorRange));
        OnPropertyChanged(nameof(ShowFixedColor));
        OnPropertyChanged(nameof(IsKindDynamic));
        OnPropertyChanged(nameof(IsKindStatic));
        OnPropertyChanged(nameof(IsKindCustom));
        OnPropertyChanged(nameof(LosesColor));
        Rebuild();
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        OnPropertyChanged(nameof(DerivedIdText));
        Revalidate();
    }

    // The range runs ascending, clamped where it is set - the rule the editor's rail and the New
    // Ingredient wizard both keep, because Validator refuses an inverted range.
    partial void OnHueMinChanged(double value)
    {
        if (value > HueMax) { HueMin = HueMax; return; }
        OnPropertyChanged(nameof(HueRangeText));
    }

    partial void OnHueMaxChanged(double value)
    {
        if (value < HueMin) { HueMax = HueMin; return; }
        OnPropertyChanged(nameof(HueRangeText));
    }

    partial void OnSatMinChanged(double value)
    {
        if (value > SatMax) { SatMin = SatMax; return; }
        OnPropertyChanged(nameof(SatRangeText));
    }

    partial void OnSatMaxChanged(double value)
    {
        if (value < SatMin) { SatMax = SatMin; return; }
        OnPropertyChanged(nameof(SatRangeText));
    }

    /// <summary>Re-renders the preview as the CHOSEN KIND would store it, so the grayscale a
    /// value-map import produces is on screen before it is agreed to rather than after.</summary>
    private void Rebuild()
    {
        if (_source is null) return;
        var old = Preview;
        using var shown = Kind == LayerKind.Custom
            ? _source.Clone()
            : ImageImport.ToValueMap(_source).ToImage();
        Preview = _bridge.ToBitmap(shown);
        old?.Dispose();
        Revalidate();
    }

    private void Revalidate()
    {
        Problem = _source is null ? "No image loaded."
            : DerivedId.Length == 0 ? "The layer needs a name."
            : _taken.Contains(DerivedId) ? $"This recipe already has a layer called “{DerivedId}”."
            : "";
    }

    /// <summary>The colorization this layer is created with.</summary>
    /// <remarks>Quantize steps of 12/4 are the wizard's own defaults; the editor is where they are
    /// tuned, and stating a different pair here would make two ways of creating a layer produce two
    /// different layers.</remarks>
    /// <returns>The colorization, or null for a Custom layer.</returns>
    public Colorization? BuildColorization() => Kind switch
    {
        LayerKind.Dynamic => new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(HueMin, HueMax, SatMin, SatMax), null) }),
        LayerKind.Static => new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, null, FixedColor) }),
        _ => null,
    };

    /// <summary>
    /// The layer this form describes, with the picture as its one variant.
    /// </summary>
    /// <remarks>The caller owns the returned ingredient's image until the book adopts it — the rule
    /// every other <c>Loaded*</c> producer in this codebase follows.</remarks>
    /// <returns>The ingredient.</returns>
    /// <exception cref="InvalidOperationException">Called before a successful <see cref="TryLoad"/>.</exception>
    public LoadedIngredient Build()
    {
        if (_source is null) throw new InvalidOperationException("No image has been loaded.");

        const string variantId = "variant-1";
        var manifest = new IngredientManifest(DerivedId, Name, Kind, BuildColorization(),
            new[] { new Variant(variantId, Name, 1) });

        // Custom keeps every channel; the value-map kinds keep lightness only. Exactly the split
        // ImageImport documents, and the same call the editor's variant import makes.
        var image = Kind == LayerKind.Custom
            ? _source.Clone()
            : ImageImport.ToValueMap(_source).ToImage();

        return new LoadedIngredient
        {
            Manifest = manifest,
            VariantImages = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
            {
                [variantId] = image,
            },
        };
    }

    private bool CanCreate() => Problem.Length == 0;

    /// <summary>Closes the form with itself as the result, which is what the caller builds from.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this);

    /// <summary>Frees the decoded picture and the preview.</summary>
    public void Dispose()
    {
        _source?.Dispose();
        _source = null;
        Preview?.Dispose();
        Preview = null;
        GC.SuppressFinalize(this);
    }
}
