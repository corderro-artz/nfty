using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.App.Services;
using Nfty.App.ViewModels;

namespace Nfty.App.Viewer;

/// <summary>
/// One tile of an archive gallery. Listing a gallery creates these and decodes nothing: the picture
/// is read and decoded only when a view first asks for <see cref="Thumbnail"/>, which is when the
/// virtualizing grid realizes the tile, and then only through the gallery's one
/// <see cref="ThumbnailGate"/>.
/// </summary>
/// <remarks>
/// A tile with no picture at all (a Kitchen's archives) passes no loader and is neither loading nor
/// missing: it shows its caption. A tile whose picture cannot be read is <see cref="ImageMissing"/>,
/// the static mark the Set browser uses, never the breathing one that means "still coming".
/// </remarks>
public sealed class GalleryItem : ObservableObject, IDisposable
{
    private readonly Func<byte[]?>? _loadPng;
    private readonly ThumbnailGate _gate;
    private volatile Bitmap? _thumbnail;
    private volatile bool _missing;
    private volatile bool _disposed;
    private bool _started;
    private int _decodesRun;

    /// <summary>Builds a tile.</summary>
    /// <param name="caption">The line under the tile.</param>
    /// <param name="loadPng">Reads the tile's encoded PNG; null for a tile with no picture. Returning
    /// null or throwing marks the tile missing. Runs on a thread-pool thread.</param>
    /// <param name="open">Builds the viewer subject for this tile. Lazy: nothing is decoded for the
    /// viewer until the tile is opened. The caller of the returned task owns the subject.</param>
    /// <param name="gate">The gallery's one decode limit.</param>
    public GalleryItem(string caption, Func<byte[]?>? loadPng, Func<Task<ViewerSubject>> open, ThumbnailGate gate)
    {
        Caption = caption;
        _loadPng = loadPng;
        Open = open;
        _gate = gate;
    }

    /// <summary>The line under the tile.</summary>
    public string Caption { get; }

    /// <summary>Builds the subject a viewer shows for this tile. Nothing is decoded before this runs.</summary>
    public Func<Task<ViewerSubject>> Open { get; }

    /// <summary>The tile picture: null until decoded. Reading it starts the decode, once.</summary>
    public Bitmap? Thumbnail
    {
        get { BeginDecode(); return _thumbnail; }
    }

    /// <summary>Whether the picture is still on its way: true for a tile with a picture whose decode
    /// has not landed.</summary>
    public bool IsLoading => _loadPng is not null && _thumbnail is null;

    /// <summary>Whether the picture could not be read, so the tile says so rather than looking blank.</summary>
    public bool ImageMissing => _missing;

    /// <summary>How many times this tile has decoded. For tests.</summary>
    internal int DecodesRun => Volatile.Read(ref _decodesRun);

    /// <summary>The decode this tile started, so a test can settle it. Completed when none ran.</summary>
    internal Task PendingDecode { get; private set; } = Task.CompletedTask;

    private void BeginDecode()
    {
        if (_started || _loadPng is null) return;
        _started = true;
        PendingDecode = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            Bitmap bitmap;
            bool missing;
            try
            {
                if (_disposed) return;
                _gate.Enter();
                try { (bitmap, missing) = DecodeCounted(); }
                finally { _gate.Exit(); }
            }
            finally { _gate.Release(); }
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) { bitmap.Dispose(); return; }
                _thumbnail = bitmap;
                _missing = missing;
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(ImageMissing));
            });
        });
    }

    /// <summary>Decodes now on the calling thread, for tests with no dispatcher to post back to.</summary>
    /// <returns>The bitmap, which this tile then owns.</returns>
    internal Bitmap DecodeNow()
    {
        _started = true;
        if (_thumbnail is not null) return _thumbnail;
        _gate.Enter();
        try
        {
            var (bitmap, missing) = DecodeCounted();
            _missing = missing;
            return _thumbnail = bitmap;
        }
        finally { _gate.Exit(); }
    }

    private (Bitmap Bitmap, bool Missing) DecodeCounted()
    {
        Interlocked.Increment(ref _decodesRun);
        try
        {
            var png = _loadPng!() ?? throw new InvalidDataException("no picture");
            using var ms = new MemoryStream(png);
            var w = Math.Min(SetItemRow.ThumbW, SetItemRow.PngWidth(ms));
            ms.Position = 0;
            return (Bitmap.DecodeToWidth(ms, w), false);
        }
        catch
        {
            // A damaged or absent picture shows as damage; it never fails the gallery.
            return (new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul), true);
        }
    }

    /// <summary>Frees the thumbnail. A decode still in flight drops its result instead of publishing it.</summary>
    public void Dispose()
    {
        _disposed = true;
        _thumbnail?.Dispose();
        _thumbnail = null;
    }
}
