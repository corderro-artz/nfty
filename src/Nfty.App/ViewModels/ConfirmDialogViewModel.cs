using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Reusable yes/no modal. Closes with a bool: true = confirmed, false = cancelled.</summary>
public partial class ConfirmDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;
    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }

    public ConfirmDialogViewModel(IDialogService dialogs, string title, string message, string confirmLabel)
    { _dialogs = dialogs; Title = title; Message = message; ConfirmLabel = confirmLabel; }

    [RelayCommand] private void Confirm() => _dialogs.Close(true);
    [RelayCommand] private void Cancel() => _dialogs.Close(false);
}
