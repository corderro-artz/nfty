using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Publish;

namespace Nfty.App.ViewModels;

/// <summary>
/// Asks for the passphrase to a sealed export, having first said what the export is.
/// </summary>
/// <remarks>
/// <b>The header is shown BEFORE the passphrase is asked for, and that is the whole reason the
/// header sits outside the encryption.</b> A recipient holding a file they cannot open needs to be
/// told what it is and who sent it, or the only thing the format communicates is that something
/// went wrong. The collection's name, its size and the sender's note are all readable without a key
/// — which is a deliberate disclosure, and the one cost of making this dialog possible.
/// </remarks>
public partial class PassphraseDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    /// <summary>The collection's name, read from the seal in the clear.</summary>
    public string Collection { get; }

    /// <summary>How many assets are inside.</summary>
    public int Count { get; }

    /// <summary>What the sender wanted read first, or empty.</summary>
    public string Note { get; }

    /// <summary>Whether there is a note to show at all.</summary>
    public bool HasNote => Note.Length > 0;

    /// <summary>What the recipient will be allowed to do, as a sentence.</summary>
    public string Policy => AllowsExport
        ? "This export allows the assets to be saved out."
        : "View only. nfty will not save the assets out of this export.";

    /// <summary>Whether the seal permits export.</summary>
    public bool AllowsExport { get; }

    /// <summary>What was typed.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private string _passphrase = "";

    /// <summary>Why the last attempt failed, or empty.</summary>
    [ObservableProperty] private string _error = "";

    /// <summary>Creates the dialog for a sealed export's header.</summary>
    /// <param name="header">What the file says about itself without a key.</param>
    /// <param name="dialogs">The dialog layer to close through.</param>
    public PassphraseDialogViewModel(SealManifest header, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(header);
        _dialogs = dialogs;
        Collection = header.Collection;
        Count = header.Count;
        Note = header.Note ?? "";
        AllowsExport = header.Policy.AllowExport;
    }

    private bool CanOpen() => Passphrase.Length > 0;

    /// <summary>Closes with what was typed, for the caller to try.</summary>
    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Open() => _dialogs.Close(Passphrase);

    /// <summary>Closes with nothing.</summary>
    [RelayCommand]
    private void Cancel() => _dialogs.Close(null);
}
