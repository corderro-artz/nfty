using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Base for modal wizard ViewModels shown via <see cref="IDialogService"/>. Supplies Cancel;
/// the concrete wizard adds its own [RelayCommand] Create.</summary>
public abstract partial class WizardViewModelBase : ViewModelBase
{
    /// <summary>The dialog layer this wizard closes through.</summary>
    protected readonly IDialogService Dialogs;
    /// <summary>The not-yet-wired channel, for actions a wizard cannot complete.</summary>
    /// <summary>Initializes the shared wizard plumbing.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    protected WizardViewModelBase(IDialogService dialogs) { Dialogs = dialogs; }
    /// <summary>Dismisses the wizard with no result.</summary>
    [RelayCommand] protected void Cancel() => Dialogs.Close(null);

    /// <summary>The id a display name is saved under: lower-case, runs of spaces to single dashes.</summary>
    /// <param name="name">The name the user typed.</param>
    /// <returns>Its derived id, empty when the name is blank.</returns>
    /// <remarks>Three wizards and the Landing screen each carried their own byte-identical copy of
    /// this, one of them under a comment admitting as much.</remarks>
    public static string DeriveId(string name) => string.Join('-',
        name.ToLowerInvariant().Split(' ', System.StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A derived id as its chip prints it.</summary>
    /// <param name="id">The derived id.</param>
    /// <returns>The id, or an em dash while it is empty.</returns>
    /// <remarks>The chip is a bordered cell, so binding a bare empty id drew an EMPTY BOX on a line
    /// reading "Identifier [] — derived from the name" — which is how all three wizards open. A
    /// control that looks broken rather than one waiting for input. The id itself stays exact,
    /// because each wizard's CanCreate tests it.</remarks>
    protected static string IdChipText(string id) => id.Length == 0 ? "—" : id;
}
