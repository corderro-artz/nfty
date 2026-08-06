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
    protected readonly INotYetWired Notify;
    /// <summary>Initialises the shared wizard plumbing.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="notify">The not-yet-wired channel.</param>
    protected WizardViewModelBase(IDialogService dialogs, INotYetWired notify) { Dialogs = dialogs; Notify = notify; }
    /// <summary>Dismisses the wizard with no result.</summary>
    [RelayCommand] protected void Cancel() => Dialogs.Close(null);
}
