using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Editing;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.ViewModels;

/// <summary>Phase-1 New Ingredient wizard: collects the fields an Ingredient manifest needs (name,
/// kind, colorization config, and where it's saved). Create is a stub — Phase 2 builds the manifest
/// and writes the .igt.</summary>
public partial class NewIngredientViewModel : WizardViewModelBase
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LayerKind _kind = LayerKind.Dynamic;
    [ObservableProperty] private RecipeDestination _destination = RecipeDestination.IntoCookBook;
    [ObservableProperty] private double _hueMin, _hueMax = 360, _satMin = 40, _satMax = 100;
    [ObservableProperty] private string _fixedColor = "hex:d6249f";
    [ObservableProperty] private string _canvasSize = "512x512";

    /// <summary>Dynamic layers roll a colour per asset from a hue/sat range.</summary>
    public bool ShowColourRange => Kind == LayerKind.Dynamic;

    /// <summary>Static layers apply one fixed colour deterministically.</summary>
    public bool ShowFixedColour => Kind == LayerKind.Static;

    /// <summary>A loose Ingredient saved on its own has no CookBook canvas to inherit, so it needs
    /// its own canvas size.</summary>
    public bool ShowCanvas => Destination == RecipeDestination.LooseKitchen;

    /// <summary>Backs the "Dynamic" radio button.</summary>
    public bool IsKindDynamic
    {
        get => Kind == LayerKind.Dynamic;
        set { if (value) Kind = LayerKind.Dynamic; }
    }

    /// <summary>Backs the "Static" radio button.</summary>
    public bool IsKindStatic
    {
        get => Kind == LayerKind.Static;
        set { if (value) Kind = LayerKind.Static; }
    }

    /// <summary>Backs the "Custom" radio button.</summary>
    public bool IsKindCustom
    {
        get => Kind == LayerKind.Custom;
        set { if (value) Kind = LayerKind.Custom; }
    }

    /// <summary>Backs the "Into CookBook" radio button.</summary>
    public bool IsIntoCookBook
    {
        get => Destination == RecipeDestination.IntoCookBook;
        set { if (value) Destination = RecipeDestination.IntoCookBook; }
    }

    /// <summary>Backs the "Loose (Kitchen)" radio button.</summary>
    public bool IsLooseKitchen
    {
        get => Destination == RecipeDestination.LooseKitchen;
        set { if (value) Destination = RecipeDestination.LooseKitchen; }
    }

    public NewIngredientViewModel(IDialogService dialogs, INotYetWired notify) : base(dialogs, notify) { }

    partial void OnKindChanged(LayerKind value)
    {
        OnPropertyChanged(nameof(ShowColourRange));
        OnPropertyChanged(nameof(ShowFixedColour));
        OnPropertyChanged(nameof(IsKindDynamic));
        OnPropertyChanged(nameof(IsKindStatic));
        OnPropertyChanged(nameof(IsKindCustom));
    }

    partial void OnDestinationChanged(RecipeDestination value)
    {
        OnPropertyChanged(nameof(ShowCanvas));
        OnPropertyChanged(nameof(IsIntoCookBook));
        OnPropertyChanged(nameof(IsLooseKitchen));
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        CreateCommand.NotifyCanExecuteChanged();
    }

    partial void OnHueMinChanged(double value) => OnPropertyChanged(nameof(HueRangeText));
    partial void OnHueMaxChanged(double value) => OnPropertyChanged(nameof(HueRangeText));
    partial void OnSatMinChanged(double value) => OnPropertyChanged(nameof(SatRangeText));
    partial void OnSatMaxChanged(double value) => OnPropertyChanged(nameof(SatRangeText));

    /// <summary>Live readouts beside each range control (mockup .cv), so the span the two handles
    /// describe is legible without reading their positions off the track. Same shape as the
    /// ingredient editor's, since the wizard now shows the same dual-range control.</summary>
    public string HueRangeText => $"{HueMin:0}–{HueMax:0}°";
    public string SatRangeText => $"{SatMin:0}–{SatMax:0}%";

    /// <summary>The ingredient id derived from the name: lower-case, spaces to dashes.</summary>
    public string DerivedId => string.Join('-',
        Name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Parse the CanvasSize field ("{W}x{H}") into positive dimensions.</summary>
    public bool TryGetCanvas(out Dimensions canvas)
    {
        canvas = default!;
        var parts = CanvasSize.Split('x', 'X');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), out var w) || !int.TryParse(parts[1].Trim(), out var h)) return false;
        if (w <= 0 || h <= 0) return false;
        if ((long)w * h > 100_000_000) return false;   // cap pixels: avoids int-overflow / OOM allocating the raster
        canvas = new Dimensions(w, h);
        return true;
    }

    /// <summary>Build the colorization config from the wizard fields (quantize defaults 12/4).</summary>
    public Colorization? BuildColorization() => Kind switch
    {
        LayerKind.Dynamic => new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, new ColorRange(HueMin, HueMax, SatMin, SatMax), null) }),
        LayerKind.Static => new Colorization(ColorModel.Hsv, 12, 4,
            new[] { new ColorEntry(1, null, FixedColor) }),
        _ => null,   // Custom — composited as-is
    };

    /// <summary>Turn the wizard into a loaded ingredient with one blank starter variant at the given
    /// canvas size. The caller owns the returned image until the book adopts it.</summary>
    public LoadedIngredient Build(Dimensions canvas)
    {
        const string variantId = "variant-1";
        var manifest = new IngredientManifest(DerivedId, Name, Kind, BuildColorization(),
            new[] { new Variant(variantId, "Variant 1", 1) });
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal)
        {
            [variantId] = ValueMap.ForCanvas(canvas).ToImage(),
        };
        return new LoadedIngredient { Manifest = manifest, VariantImages = images };
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this);
}
