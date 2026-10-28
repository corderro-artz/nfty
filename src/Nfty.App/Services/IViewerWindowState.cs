namespace Nfty.App.Services;

/// <summary>One remembered window size: where it was, how big, and whether it was maximized.</summary>
/// <param name="Width">Width in device-independent pixels.</param>
/// <param name="Height">Height in device-independent pixels.</param>
/// <param name="X">Left edge in screen pixels, or null for "no remembered position".</param>
/// <param name="Y">Top edge in screen pixels, or null for "no remembered position".</param>
/// <param name="Maximized">Whether the window was maximized.</param>
public sealed record ViewerWindowEntry(double Width, double Height, double? X, double? Y, bool Maximized);

/// <summary>
/// What the viewer remembers: one window entry for a 2D-only layout and one for a 3D or side-by-side
/// layout, plus how the 2D art's backdrop was last set up.
/// </summary>
/// <remarks>
/// The backdrop members are optional with defaults, so a <c>viewer-window.json</c> written before
/// they existed reads exactly as it did: an additive field, per the archive rule restated for state.
/// </remarks>
/// <param name="TwoD">The entry for a window that opens with the 2D layout.</param>
/// <param name="ThreeD">The entry for a window that opens with the Both or 3D layout.</param>
/// <param name="Backdrop">The 2D backdrop, as the <see cref="ViewModels.ViewerBackdrop"/> name.</param>
/// <param name="BackdropColor">The custom backdrop color, as #RRGGBB.</param>
/// <param name="Wireframe">Whether the 3D pane draws the outline of every face over the solid.</param>
public sealed record ViewerWindowState(
    ViewerWindowEntry TwoD,
    ViewerWindowEntry ThreeD,
    string Backdrop = "Checker",
    string BackdropColor = "#808080",
    bool Wireframe = false)
{
    /// <summary>The 2D-only window size nobody has changed.</summary>
    public static readonly ViewerWindowEntry DefaultTwoD = new(900, 700, null, null, false);

    /// <summary>The side-by-side window size nobody has changed.</summary>
    public static readonly ViewerWindowEntry DefaultThreeD = new(1280, 760, null, null, false);

    /// <summary>The smallest a viewer window may be, in device-independent pixels.</summary>
    public const double MinWidth = 640;

    /// <summary>The smallest a viewer window may be, in device-independent pixels.</summary>
    public const double MinHeight = 480;

    /// <summary>Both entries at their defaults.</summary>
    public static ViewerWindowState Default => new(DefaultTwoD, DefaultThreeD);
}

/// <summary>
/// The viewer windows' remembered size and backdrop, kept between sessions.
/// </summary>
/// <remarks>
/// Convenience state, like <see cref="IEditorViewState"/>: a corrupt file reads as the defaults, a
/// failed save is swallowed. Zoom, pan and camera are deliberately not here: they belong to the
/// image, not to the reader.
/// </remarks>
public interface IViewerWindowState
{
    /// <summary>The state to open a viewer in.</summary>
    ViewerWindowState Current { get; }

    /// <summary>Remembers a new state. Saving the state already held is a no-op.</summary>
    /// <param name="state">What the viewer now looks like.</param>
    void Save(ViewerWindowState state);
}

/// <inheritdoc cref="IViewerWindowState"/>
public sealed class ViewerWindowStateService : IViewerWindowState
{
    /// <summary>The store file the state lives in.</summary>
    public const string FileName = "viewer-window.json";

    private readonly IStateStore _store;

    /// <summary>Creates the service and loads whatever the store holds.</summary>
    /// <param name="store">Where the state is persisted.</param>
    public ViewerWindowStateService(IStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        Current = Load(store.Read(FileName));
    }

    /// <inheritdoc />
    public ViewerWindowState Current { get; private set; }

    /// <inheritdoc />
    public void Save(ViewerWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state == Current) return;
        Current = state;
        try { _store.Write(FileName, StateJson.Write(state)); }
        catch (NotSupportedException) { /* convenience state: never surface */ }
    }

    // A window smaller than the minimum, or with a NaN or infinite side, cannot be opened: those
    // read as the default for that layout rather than being trusted.
    private static ViewerWindowState Load(string? json)
    {
        var read = StateJson.TryRead<ViewerWindowState>(json);
        if (read is null || read.TwoD is null || read.ThreeD is null) return ViewerWindowState.Default;
        return read with
        {
            TwoD = Sane(read.TwoD, ViewerWindowState.DefaultTwoD),
            ThreeD = Sane(read.ThreeD, ViewerWindowState.DefaultThreeD),
            Backdrop = read.Backdrop ?? "Checker",
            BackdropColor = read.BackdropColor ?? "#808080",
        };
    }

    private static ViewerWindowEntry Sane(ViewerWindowEntry e, ViewerWindowEntry fallback)
    {
        static bool Ok(double v) => double.IsFinite(v);
        if (!Ok(e.Width) || !Ok(e.Height) || e.Width < ViewerWindowState.MinWidth
            || e.Height < ViewerWindowState.MinHeight) return fallback;
        return e with
        {
            X = e.X is { } x && Ok(x) ? x : null,
            Y = e.Y is { } y && Ok(y) ? y : null,
        };
    }
}
