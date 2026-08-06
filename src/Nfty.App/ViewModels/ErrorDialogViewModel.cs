using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Reusable modal for surfacing a read/open failure. Static display; closes itself via
/// <see cref="IDialogService"/>.</summary>
public partial class ErrorDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    /// <summary>The dialog's heading.</summary>
    public string Title { get; }
    /// <summary>The engine's own message, shown verbatim.</summary>
    public string Message { get; }

    /// <summary>Creates the dialog.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="title">Heading.</param>
    /// <param name="message">The message to show.</param>
    public ErrorDialogViewModel(IDialogService dialogs, string title, string message)
    {
        _dialogs = dialogs;
        Title = title;
        Message = message;
    }

    [RelayCommand] private void Close() => _dialogs.Close(null);
}
