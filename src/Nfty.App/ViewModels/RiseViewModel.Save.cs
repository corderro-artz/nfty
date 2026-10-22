using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Rise;
using Nfty.Core.Formats;

namespace Nfty.App.ViewModels;

public sealed partial class RiseViewModel
{
    private int DirtyCount => Items.Count(i => i.IsDirty);

    /// <summary>Whether any variant's depth differs from what was last saved. Undoing back to the saved
    /// depth is clean: dirty is a comparison, not "history is not empty".</summary>
    public bool HasUnsavedDepth => DirtyCount > 0;

    /// <summary>A save is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    /// <summary>The Save button is inked: the source is one depth can be saved into. Space is reserved
    /// either way; a locked book keeps the button, dimmed, beside the sentence that says why.</summary>
    public bool ShowSave => Source.IsEditable;

    /// <summary>Whether Save would write something right now.</summary>
    public bool CanSave => SaveGate && !IsSaving;

    // Everything Save needs except "no save is running": a second press during a save joins the first
    // (the source hands back the running task) rather than being refused.
    private bool SaveGate => Source.CanSave && HasUnsavedDepth && !IsExporting && !IsVoxelBusy;

    // What the unsaved things are called: the cells of a picture or sheet, the variants of a layer.
    private string DirtyNoun(int n) => Source is PictureRiseSource
        ? (n == 1 ? "cell" : "cells")
        : (n == 1 ? "variant" : "variants");

    /// <summary>The line in the footer: what Save would do, or why it will not.</summary>
    public string FooterText
    {
        get
        {
            if (!Source.IsEditable) return "read-only source — nothing to save";
            if (!Source.CanSave && !Source.IsLocked && Source.UnsavableNote is { } why) return why;
            int n = DirtyCount;
            if (n == 0) return "no unsaved depth";
            var text = string.Create(CultureInfo.InvariantCulture,
                $"unsaved depth on {n} {DirtyNoun(n)}");
            return Source.IsLocked
                ? text + " · the CookBook is read-only — unlock it in the Explorer to save"
                : text;
        }
    }

    /// <summary>Writes the painted depth where it came from. Any gesture still down is dropped first,
    /// or its preview would be saved as if it had been committed.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save()
    {
        // The WHOLE save, error card included, is what Back waits for: the dialog layer holds one
        // card, so a leave card asked while the error card is up would replace it unread and orphan
        // the save, leaving IsSaving stuck and Save dead for the rest of the page's life.
        if (_saveRun is { IsCompleted: false } running) return running;
        var run = SaveRunAsync();
        _saveRun = run;
        return run;
    }

    private Task? _saveRun;

    private async Task SaveRunAsync()
    {
        CancelDepthPreview();
        // A disabled button is a label: the generated command does not consult CanExecute, so the gate
        // is checked here too. The source checks the lock again before it writes.
        if (!SaveGate) return;
        IsSaving = true;
        try
        {
            await Source.SaveAsync();
            // Said only when the write happened: a canceled dialog leaves depth unsaved and the note old.
            if (!HasUnsavedDepth && Source.SaveResultNote is { } note) _status.Say(note);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not save depth", ex.Message));
        }
        finally
        {
            IsSaving = false;
            OnDepthEdited();
        }
    }

    /// <summary>Leaves the page, asking first when there is depth that leaving would lose.</summary>
    [RelayCommand(CanExecute = nameof(CanLeave))]
    private async Task Back()
    {
        // A disabled button is a label: the generated command does not consult CanExecute.
        if (!CanLeave()) return;
        CancelDepthPreview();
        // A save that is running decides what is unsaved. Asking now would offer to discard depth that
        // is about to land anyway, and leaving now would hand the Explorer a book a moment before the
        // save replaces it, so the Explorer's next whole-book write would start from the old one. It is
        // the whole save that is awaited, its error card too (see Save), never only the write.
        if (_saveRun is { IsCompleted: false } saving)
        {
            try { await saving; }
            catch (Exception) { /* Save reports its own failure; the page then asks as usual */ }
        }
        int n = DirtyCount;
        if (!Source.IsEditable || n == 0)
        {
            _nav.Back();
            return;
        }
        var message = string.Create(CultureInfo.InvariantCulture,
            $"{Source.Title} has unsaved depth on {n} {DirtyNoun(n)}. Leaving discards it.");
        var card = new LeaveDialogViewModel(_dialogs, "Leave Rise?", message, canSave: !Source.IsLocked);
        var choice = await _dialogs.ShowAsync<LeaveChoice>(card);
        switch (choice)
        {
            case LeaveChoice.Discard:
                _nav.Back();
                break;
            case LeaveChoice.SaveAndLeave:
                await Save();
                if (!HasUnsavedDepth) _nav.Back();
                break;
            default:
                break;      // Stay, and a card dismissed without an answer
        }
    }

    /// <summary>Raised with the book the session holds after a save, for the Explorer to refresh from.</summary>
    public event Action<LoadedCookBook>? Saved;

    private void OnVariantSaved(LoadedCookBook book) => Saved?.Invoke(book);

    /// <summary>Says the page's opening line. The shell calls this AFTER navigation, because it clears
    /// the status line on every page change.</summary>
    internal void SayOpeningLine() => _status.Say(Source.OpeningLine);
}
