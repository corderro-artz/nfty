using Nfty.App.Services;

namespace Nfty.App.ViewModels;

// Persist view preferences without marking artwork dirty. Zoom/pan belong to the
// image and fill-pane preview is temporary; IEditorViewState defines the saved subset.
public partial class IngredientEditorViewModel
{
    private IEditorViewState _viewState = new EditorViewStateService(StateStore.InMemory());

    /// <summary>True while <see cref="ApplyViewState"/> is writing, so the clamps it trips do not
    /// save themselves back over the preference they came from.</summary>
    /// <remarks>
    /// This is load-bearing rather than tidy. <see cref="GridSize"/> clamps to the CANVAS, so opening
    /// an 8px layer with a remembered step of 64 arrives at 8 — and saving that would throw the
    /// author's real preference away because they happened to look at a small sprite.
    /// </remarks>
    private bool _loadingViewState;

    /// <summary>Opens the editor in the view the reader last left, and keeps it up to date.</summary>
    /// <param name="viewState">Where the preference lives.</param>
    private void ApplyViewState(IEditorViewState viewState)
    {
        _viewState = viewState;
        var state = viewState.Current;

        _loadingViewState = true;
        try
        {
            ShowPixelGrid = state.PixelGrid;
            GridSize = state.GridStep;
            // The tile size is NOT restored: every open starts with the small tile in its corner,
            // the way the Rise page does. A stale record that says enlarged is read and ignored.
            PreviewEnlarged = false;
            ActiveRailTab = state.ReferencesTab ? RailTab.References : RailTab.Colorize;
        }
        finally { _loadingViewState = false; }
    }

    /// <summary>Records the current view as the one to open in next time.</summary>
    private void SaveViewState()
    {
        if (_loadingViewState) return;
        _viewState.Save(new EditorViewState(ShowPixelGrid, GridSize, PreviewEnlarged,
            ActiveRailTab == RailTab.References));
    }
}
