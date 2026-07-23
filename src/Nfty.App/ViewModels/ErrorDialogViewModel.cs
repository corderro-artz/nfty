using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Reusable modal for surfacing a read/open failure. Static display; closes itself via
/// <see cref="IDialogService"/>.</summary>
public partial class ErrorDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    public string Title { get; }
    public string Message { get; }

    public ErrorDialogViewModel(IDialogService dialogs, string title, string message)
    {
        _dialogs = dialogs;
        Title = title;
        Message = message;
    }

    [RelayCommand] private void Close() => _dialogs.Close(null);
}
