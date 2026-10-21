using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>How a leave card was answered. <see cref="Stay"/> is zero so a card dismissed without an
/// answer keeps the author editing.</summary>
public enum LeaveChoice
{
    /// <summary>Keep editing.</summary>
    Stay = 0,

    /// <summary>Leave and lose the unsaved work.</summary>
    Discard,

    /// <summary>Write it, then leave.</summary>
    SaveAndLeave,
}

/// <summary>A three-way "leave this page?" card. <see cref="ConfirmDialogViewModel"/> is yes/no and
/// shared app-wide, so it is not widened for the one page that has a third answer.</summary>
public partial class LeaveDialogViewModel : ViewModelBase
{
    private readonly IDialogService _dialogs;

    /// <summary>Creates the card.</summary>
    /// <param name="dialogs">The dialog layer to close through.</param>
    /// <param name="title">Heading.</param>
    /// <param name="message">What leaving would lose, in the author's terms.</param>
    /// <param name="canSave">Whether "Save and leave" is on offer (not when the book is locked).</param>
    public LeaveDialogViewModel(IDialogService dialogs, string title, string message, bool canSave)
    {
        _dialogs = dialogs;
        Title = title;
        Message = message;
        CanSave = canSave;
    }

    /// <summary>The heading.</summary>
    public string Title { get; }

    /// <summary>What leaving would lose.</summary>
    public string Message { get; }

    /// <summary>Whether saving first is possible.</summary>
    public bool CanSave { get; }

    /// <summary>Leave without saving.</summary>
    [RelayCommand] private void Discard() => _dialogs.Close(LeaveChoice.Discard);

    /// <summary>Save, then leave.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveAndLeave()
    {
        if (!CanSave) return;      // a disabled button is a label
        _dialogs.Close(LeaveChoice.SaveAndLeave);
    }

    /// <summary>Keep editing.</summary>
    [RelayCommand] private void Stay() => _dialogs.Close(LeaveChoice.Stay);
}
