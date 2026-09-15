using System.Text.Json;

namespace Nfty.App.Services;

/// <summary>
/// How the Ingredient editor was last set up to LOOK — nothing here is part of any layer.
/// </summary>
/// <param name="PixelGrid">Whether the backdrop draws the pixel lattice rather than a flat ground.</param>
/// <param name="GridStep">How many canvas pixels one lattice square covers.</param>
/// <param name="PreviewEnlarged">Whether the corner preview tile is at its larger size.</param>
/// <param name="ReferencesTab">Whether the rail opens on REFERENCES rather than COLORIZE.</param>
public sealed record EditorViewState(
    bool PixelGrid = true,
    int GridStep = 1,
    bool PreviewEnlarged = false,
    bool ReferencesTab = false);

/// <summary>
/// The Ingredient editor's view preferences, remembered between sessions.
/// </summary>
/// <remarks>
/// <para><b>What is here is what belongs to the READER rather than to the layer.</b> The grid is
/// already documented as view state that must never mark the editor dirty — the exact opposite of
/// the layer kind one control away — and this is the other half of that sentence: state that is not
/// part of the work should not be re-chosen every time the work is opened. An author who paints
/// 16px sprites on an 8px lattice sets that on every single layer they open, and the app forgets it
/// every single time.</para>
///
/// <para><b>Two things are deliberately NOT here.</b> Zoom and pan belong to the IMAGE, not to the
/// reader: opening a different layer at six times magnification with the view panned into a corner
/// is not a remembered preference, it is a lost canvas. And "fill pane with preview" is a mode
/// entered to LOOK at something rather than to work that way — restoring it would open the paint
/// editor with the paint canvas hidden.</para>
///
/// <para>Convenience state throughout, like <see cref="PaletteService"/> and
/// <see cref="RecentsService"/>: a corrupt file reads as the defaults, a failed save is swallowed,
/// and an unwritable store keeps the preference for the session rather than refusing it.</para>
/// </remarks>
public interface IEditorViewState
{
    /// <summary>The state to open the editor in.</summary>
    EditorViewState Current { get; }

    /// <summary>Remembers a new state. Saving the state already held is a no-op.</summary>
    /// <param name="state">What the editor now looks like.</param>
    void Save(EditorViewState state);
}

/// <inheritdoc cref="IEditorViewState"/>
public sealed class EditorViewStateService : IEditorViewState
{
    /// <summary>The store file the preferences live in.</summary>
    public const string FileName = "editor-view.json";

    // camelCase out, case-insensitive in — the rule every JSON this project writes follows, and the
    // second half is what keeps a hand-edited file from being the reason the editor will not open.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IStateStore _store;

    /// <summary>Creates the service and loads whatever the store holds.</summary>
    /// <param name="store">Where the preferences are persisted.</param>
    public EditorViewStateService(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = Load(store.Read(FileName));
    }

    /// <inheritdoc />
    public EditorViewState Current { get; private set; }

    /// <inheritdoc />
    public void Save(EditorViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state == Current) return;
        Current = state;
        try { _store.Write(FileName, JsonSerializer.Serialize(state, Json)); }
        catch (NotSupportedException) { /* convenience state — never surface */ }
    }

    /// <summary>
    /// Reads the file, or the defaults.
    /// </summary>
    /// <remarks>
    /// The step is clamped to at least one here rather than trusted: a hand-edited zero would make
    /// every lattice square zero pixels wide, and the editor's own clamp runs against a canvas size
    /// this has no idea about.
    /// </remarks>
    private static EditorViewState Load(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EditorViewState();
        try
        {
            var read = JsonSerializer.Deserialize<EditorViewState>(json, Json);
            return read is null ? new EditorViewState() : read with { GridStep = Math.Max(1, read.GridStep) };
        }
        catch (JsonException) { return new EditorViewState(); }
    }
}
