using Avalonia.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Viewer;

/// <summary>What a page that can be followed live must offer the viewer.</summary>
/// <remarks>Everything returned is OWNED BY THE SOURCE and only valid for the duration of the read:
/// the page frees and replaces its images on every stroke, so <see cref="LiveSubject"/> copies what it
/// reads and never keeps a reference.</remarks>
public interface ILiveViewerSource
{
    /// <summary>The window title.</summary>
    string ViewerTitle { get; }

    /// <summary>The 2D art as it stands now.</summary>
    Image<Rgba32> ViewerArt { get; }

    /// <summary>The 3D model as it stands now, or null when the page has none.</summary>
    ViewerModel? ViewerModel { get; }

    /// <summary>False when the page shows something that may not leave the app.</summary>
    bool AllowExport { get; }

    /// <summary>Raised after the art or the solid changed. May be raised many times a frame.</summary>
    event Action? ViewerChanged;

    /// <summary>Raised first thing when the page closes, while its images are still alive.</summary>
    event Action? Detached;
}

/// <summary>
/// A viewer subject that follows an open page. It owns clones of the page's art and texture, makes a
/// new pair when the page says it changed (at most once per dispatcher frame), and turns into a plain
/// snapshot when the page closes.
/// </summary>
public sealed class LiveSubject : ViewerSubject
{
    private ILiveViewerSource? _source;
    private Image<Rgba32>? _art;
    private ViewerModel? _model;
    private string _title;
    private bool _pending;
    private bool _disposed;

    /// <summary>Follows a page.</summary>
    /// <param name="source">The page; it is not kept once it detaches.</param>
    public LiveSubject(ILiveViewerSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _title = source.ViewerTitle;
        AllowExport = source.AllowExport;
        Copy(source);
        source.ViewerChanged += OnSourceChanged;
        source.Detached += OnDetached;
    }

    /// <inheritdoc />
    public override string Title => _title;
    /// <inheritdoc />
    public override IReadOnlyList<ViewerFact> Facts { get; } = Array.Empty<ViewerFact>();
    /// <inheritdoc />
    public override Image<Rgba32>? Art => _art;
    /// <inheritdoc />
    public override ViewerModel? Model => _model;
    /// <inheritdoc />
    public override bool IsLive => _source is not null;

    /// <summary>Whether a refresh is waiting for its frame; for tests of the coalescing.</summary>
    internal bool IsRefreshPending => _pending;

    // The clone is made when the frame comes round, never inside the page's own change: a stroke
    // costs the page nothing extra. Ten strokes before the frame set the flag once.
    private void OnSourceChanged()
    {
        if (_pending || _disposed) return;
        _pending = true;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    private void Refresh()
    {
        if (!_pending) return;     // a detach already took the last copy
        _pending = false;
        if (_disposed || _source is not { } source) return;
        Replace(source);
    }

    private void Replace(ILiveViewerSource source)
    {
        var oldArt = _art; var oldModel = _model;
        Copy(source);
        _title = source.ViewerTitle;
        RaiseChanged();
        // After the viewer has re-read, so nothing it still draws is freed under it.
        oldArt?.Dispose();
        oldModel?.Texture.Dispose();
    }

    private void Copy(ILiveViewerSource source)
    {
        _art = source.ViewerArt.Clone();
        _model = source.ViewerModel is { } m ? m with { Texture = m.Texture.Clone() } : null;
    }

    private void OnDetached()
    {
        if (_source is not { } source) return;
        Unhook(source);
        _source = null;
        // The page is still alive for this call: take the last state, then never touch it again.
        if (_pending && !_disposed) { _pending = false; Replace(source); }
        else if (!_disposed) RaiseChanged();   // the badge goes from live to snapshot
    }

    private void Unhook(ILiveViewerSource source)
    {
        source.ViewerChanged -= OnSourceChanged;
        source.Detached -= OnDetached;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_source is { } s) { Unhook(s); _source = null; }
        _art?.Dispose();
        _model?.Texture.Dispose();
        _art = null; _model = null;
    }
}
