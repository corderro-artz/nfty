using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>What the author chose when color art had to become a Custom ingredient.</summary>
public enum ColorSaveChoice
{
    /// <summary>Nothing is written. The zero value on purpose: a dialog dismissed rather than
    /// answered returns <see langword="default"/>, and the safe reading of "dismissed" is "don't".</summary>
    Cancel,

    /// <summary>Write a new Custom ingredient beside the original, which stays as it is on disk.</summary>
    NewIngredient,

    /// <summary>Convert the original in place, discarding its colorization.</summary>
    Overwrite,
}

/// <summary>
/// Asks what becomes of a value-map layer that was painted in color. Color art composites as-is
/// and is never recolored at generation time, so it can only be stored as a Custom layer — and a
/// Custom layer carries no colorization.
/// </summary>
/// <remarks>
/// The default is non-destructive because a colorization block is <b>not recoverable</b>: the hue and
/// saturation ranges, the entry weights and the DNA quantize steps all go, and with them the layer's
/// entire color space. That is stated in the dialog rather than in a tooltip, and the confirm button
/// renames itself so the button says which of the two things it is about to do.
/// </remarks>
public partial class ColorSaveDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    /// <summary>The layer being saved, named so the dialog is about a thing rather than about layers.</summary>
    public string LayerName { get; }

    /// <summary>What happens if the checkbox is left alone.</summary>
    public string Message { get; }

    /// <summary>What the checkbox costs.</summary>
    public string OverwriteWarning { get; }

    /// <summary>Whether to convert the original instead of adding a new layer beside it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfirmLabel))]
    private bool _overwrite;

    /// <summary>The confirm button's label — it names the chosen action, not "OK".</summary>
    public string ConfirmLabel => Overwrite ? "Overwrite" : "Save as new";

    /// <summary>Creates the dialog.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="layerName">The layer being saved.</param>
    public ColorSaveDialogViewModel(IDialogService dialogs, string layerName)
    {
        _dialogs = dialogs;
        LayerName = layerName;
        Message = $"Color artwork composites as-is and is never recolored at generation time, so "
            + $"“{layerName}” will be saved as a new Custom ingredient on top of this recipe. "
            + "The original stays exactly as it is.";
        OverwriteWarning = $"Overwrite “{layerName}” instead. Its colorization is discarded — the hue "
            + "and saturation ranges, the entry weights and the DNA quantize steps all go, and with "
            + "them this layer's entire color space. This cannot be undone from here.";
    }

    [RelayCommand]
    private void Confirm() =>
        _dialogs.Close(Overwrite ? ColorSaveChoice.Overwrite : ColorSaveChoice.NewIngredient);

    [RelayCommand] private void Cancel() => _dialogs.Close(ColorSaveChoice.Cancel);
}
