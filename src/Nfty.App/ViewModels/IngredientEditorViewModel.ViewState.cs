using Nfty.App.Services;

namespace Nfty.App.ViewModels;

/// <summary>
/// WHAT OF THE EDITOR'S VIEW IS REMEMBERED BETWEEN SESSIONS.
/// </summary>
/// <remarks>
/// <para>The pixel grid is already documented as view state that must never mark the editor dirty.
/// This is the other half of that sentence: state that is not part of the work should not have to be
/// re-chosen every time the work is opened. An author who paints 16px sprites on an 8px lattice sets
/// that on every layer they open, and the app forgot it every time.</para>
///
/// <para><b>Two things are deliberately left out</b>, and the reasons are in
/// <see cref="IEditorViewState"/>: zoom and pan belong to the IMAGE, and "fill pane with preview"
/// is a mode entered to LOOK at something rather than to work that way.</para>
/// </remarks>
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
            PreviewEnlarged = state.PreviewEnlarged;
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
