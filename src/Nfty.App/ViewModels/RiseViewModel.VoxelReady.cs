using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Rise;

namespace Nfty.App.ViewModels;

public sealed partial class RiseViewModel
{
    // ---------------------------------------------------------------- make voxel-ready
    //
    // Three faces, never two at once: the button in the 2D ONLY header (the source has partial pixels
    // to snap), the undo line at the foot of the DEPTH rail (it just snapped them; step 7's last place
    // in the rail, so its arrival moves nothing above it), or the sentence at that same place that says
    // where the fix lives (a recipe or a Set, which only list the problem).

    private bool _voxelBusy;

    /// <summary>A voxel-ready is being written or undone. Nothing else on the page may write meanwhile.</summary>
    public bool IsVoxelBusy
    {
        get => _voxelBusy;
        private set
        {
            if (_voxelBusy == value) return;
            _voxelBusy = value;
            OnPropertyChanged();
            NotifyVoxelReady();
            SaveCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            BackCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnIsExportingChanged(bool value) => NotifyVoxelReady();

    partial void OnIsSavingChanged(bool value) => NotifyVoxelReady();

    private void NotifyVoxelReady()
    {
        OnPropertyChanged(nameof(CanMakeVoxelReady));
        OnPropertyChanged(nameof(CanUndoVoxelReady));
        OnPropertyChanged(nameof(VoxelReadyTip));
        OnPropertyChanged(nameof(VoxelReadyNote));
        OnPropertyChanged(nameof(ShowVoxelReadyButton));
        OnPropertyChanged(nameof(ShowVoxelReadyUndo));
        OnPropertyChanged(nameof(ShowVoxelReadyHint));
        OnPropertyChanged(nameof(TwoDOnlyFootnote));
        OnPropertyChanged(nameof(HasVoxelReadySlot));
        OnPropertyChanged(nameof(BookIsTwoDOnly));
        MakeVoxelReadyCommand.NotifyCanExecuteChanged();
        UndoVoxelReadyCommand.NotifyCanExecuteChanged();
    }

    // Everything the two buttons need but the source's own words: depth saved, the lock open, and no
    // other write running. The source checks the lock (and unsaved depth) again before it writes.
    private bool VoxelGate => !HasUnsavedDepth && !Source.IsLocked && !IsExporting && !IsSaving && !IsVoxelBusy;

    /// <summary>The Make voxel-ready button is inked: the source has partial pixels to snap and no undo is pending.</summary>
    public bool ShowVoxelReadyButton => Source.CanMakeVoxelReady && !Source.CanUndoVoxelReady;

    /// <summary>The undo line is inked: a voxel-ready made through this page can still be undone.</summary>
    public bool ShowVoxelReadyUndo => Source.CanUndoVoxelReady;

    /// <summary>The sentence is inked: the page lists art that cannot rise but its source has no layer to fix it on.</summary>
    public bool ShowVoxelReadyHint => !Source.CanMakeVoxelReady && !Source.CanUndoVoxelReady && HasPartialAlphaRows;

    /// <summary>The line that closes the 2D ONLY section. It explains why the pictures above cannot rise, except
    /// on a read-only book, where it says why the button beside the heading is dim instead: a clean page says
    /// so nowhere else (the footer only speaks of locks when there is unsaved depth), and the section already
    /// has this one line, so the reason costs the rail no height. The partial-alpha explanation is not lost:
    /// the depth note and the tile say it too.</summary>
    public string TwoDOnlyFootnote => Source.CanMakeVoxelReady && !Source.CanUndoVoxelReady && Source.IsLocked
        ? "The CookBook is read-only — unlock it in the Explorer."
        : "A voxel is solid or empty, so partial alpha cannot rise.";

    /// <summary>Whether the foot of the rail has anything to say: the undo line or the sentence. The button
    /// is not part of it (it sits in the 2D ONLY header). The positional exception: last in its rail.</summary>
    public bool HasVoxelReadySlot => ShowVoxelReadyUndo || ShowVoxelReadyHint;

    /// <summary>Whether the Make voxel-ready button works right now.</summary>
    public bool CanMakeVoxelReady => Source.CanMakeVoxelReady && VoxelGate;

    /// <summary>Whether the Undo button works right now.</summary>
    public bool CanUndoVoxelReady => Source.CanUndoVoxelReady && VoxelGate;

    /// <summary>The words under the button: what it does, or why it is dim.</summary>
    public string VoxelReadyTip
    {
        get
        {
            if (HasUnsavedDepth) return "Save or discard depth first.";
            if (Source.IsLocked) return "The CookBook is read-only — unlock it in the Explorer.";
            if (IsExporting || IsSaving || IsVoxelBusy) return "Wait for the running job to finish.";
            return Source is PictureRiseSource
                ? "Snaps every partial pixel in this session."
                : "Snaps every partial pixel and writes it.";
        }
    }

    /// <summary>"Voxel-ready: 9 variants snapped", or empty.</summary>
    public string VoxelReadyNote => Source.VoxelReadyNote ?? "";

    /// <summary>The sentence a recipe or a Set shows in place of the button.</summary>
    public string VoxelReadyHint => "Open a layer of this book in Rise to make it voxel-ready.";

    /// <summary>Shows what would change and, once agreed, snaps it and writes it where the art lives.</summary>
    [RelayCommand(CanExecute = nameof(CanMakeVoxelReady))]
    private async Task MakeVoxelReady()
    {
        // A disabled button is a label: the generated command does not consult CanExecute.
        if (!CanMakeVoxelReady) return;
        CancelDepthPreview();
        IsVoxelBusy = true;
        try
        {
            var plan = Source.PlanVoxelReady();
            if (plan.Count == 0) return;
            using var card = new VoxelReadyDialogViewModel(_dialogs, plan, change =>
            {
                var (before, after) = Source.PreviewVoxelReady(change);
                using (before) using (after)
                    return (_bridge.ToBitmap(before), _bridge.ToBitmap(after));
            });
            if (await _dialogs.ShowAsync<bool>(card) != true) return;
            // The page may have been closed or locked while the card was up.
            if (_disposed || !VoxelGate_ExceptBusy()) return;
            var outcome = await Source.MakeVoxelReadyAsync();
            if (outcome is not null && !_disposed) _status.Say(outcome.Note + ".");
        }
        catch (Exception ex)
        {
            if (!_disposed)
                await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not make voxel-ready", ex.Message));
        }
        finally
        {
            IsVoxelBusy = false;
            if (!_disposed) NotifyVoxelReady();
        }
    }

    /// <summary>Puts back the pixels the last voxel-ready replaced. Refuses, and says why, when any has been edited since.</summary>
    [RelayCommand(CanExecute = nameof(CanUndoVoxelReady))]
    private async Task UndoVoxelReady()
    {
        if (!CanUndoVoxelReady) return;
        CancelDepthPreview();
        IsVoxelBusy = true;
        try
        {
            await Source.UndoVoxelReadyAsync();
            if (!_disposed) _status.Say("Voxel-ready undone.");
        }
        catch (Exception ex)
        {
            if (!_disposed)
                await _dialogs.ShowAsync<object>(new ErrorDialogViewModel(_dialogs, "Could not undo", ex.Message));
        }
        finally
        {
            IsVoxelBusy = false;
            if (!_disposed) NotifyVoxelReady();
        }
    }

    private bool VoxelGate_ExceptBusy() => !HasUnsavedDepth && !Source.IsLocked && !IsExporting && !IsSaving;
}
