using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>Base for modal wizard ViewModels shown via <see cref="IDialogService"/>. Supplies Cancel;
/// the concrete wizard adds its own [RelayCommand] Create.</summary>
public abstract partial class WizardViewModelBase : ViewModelBase
{
    protected readonly IDialogService Dialogs;
    protected readonly INotYetWired Notify;
    protected WizardViewModelBase(IDialogService dialogs, INotYetWired notify) { Dialogs = dialogs; Notify = notify; }
    [RelayCommand] protected void Cancel() => Dialogs.Close(null);
}
