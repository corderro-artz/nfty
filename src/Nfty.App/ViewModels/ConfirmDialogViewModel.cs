using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Reusable yes/no modal. Closes with a bool: true = confirmed, false = cancelled.</summary>
public partial class ConfirmDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    /// <summary>The dialog's heading.</summary>
    public string Title { get; }
    /// <summary>What is about to happen, in the user's terms.</summary>
    public string Message { get; }
    /// <summary>The confirm button's label — "Delete" rather than "OK", so the button says what it does.</summary>
    public string ConfirmLabel { get; }

    /// <summary>Creates the dialog.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="title">Heading.</param>
    /// <param name="message">What is about to happen.</param>
    /// <param name="confirmLabel">The confirm button's label.</param>
    public ConfirmDialogViewModel(IDialogService dialogs, string title, string message, string confirmLabel)
    { _dialogs = dialogs; Title = title; Message = message; ConfirmLabel = confirmLabel; }

    [RelayCommand] private void Confirm() => _dialogs.Close(true);
    [RelayCommand] private void Cancel() => _dialogs.Close(false);
}
