using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.App.Services;
using Nfty.Core.Imaging;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Nfty.App.ViewModels;

/// <summary>One collection in the Community grid: the entry, what the tile says about it, and its preview picture,
/// which is fetched and decoded off the UI thread so a screen of tiles never freezes the window.</summary>
public sealed partial class CommunityTile : ObservableObject, IDisposable
{
    private readonly ICommunityCache _cache;
    private readonly ThumbnailGate _gate;
    private readonly IImageBridge _bridge;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _lock = new();
    private volatile Bitmap? _preview;
    private volatile bool _missing;
    private bool _started;
    private volatile bool _disposed;

    /// <summary>Creates the tile for an entry.</summary>
    /// <param name="entry">The gallery entry.</param>
    /// <param name="cache">Where the preview picture comes from.</param>
    /// <param name="gate">The page's one decode limit.</param>
    /// <param name="bridge">Converts a decoded cell to an Avalonia bitmap.</param>
    public CommunityTile(CommunityEntry entry, ICommunityCache cache, ThumbnailGate gate, IImageBridge bridge)
    {
        Entry = entry;
        _cache = cache;
        _gate = gate;
        _bridge = bridge;
    }

    /// <summary>The gallery entry this tile shows.</summary>
    public CommunityEntry Entry { get; }

    /// <summary>The collection's name.</summary>
    public string Name => Entry.Name;

    /// <summary>The sharer, as <c>@login</c>.</summary>
    public string OwnerText => "@" + Entry.Owner.Login;

    /// <summary>How many assets, how big, how heavy: <c>600 · 32 x 32 · 4.0 MB</c>.</summary>
    public string FactsText => string.Create(CultureInfo.InvariantCulture,
        $"{Entry.Assets:N0} · {Entry.Canvas.Width} x {Entry.Canvas.Height} · {SizeText.Of(Entry.Set.Size)}");

    /// <summary>Whether the Set carries 3D models.</summary>
    public bool HasModels => Entry.Models;

    /// <summary>Whether the source CookBook shipped inside the Set.</summary>
    public bool HasBook => Entry.IncludesCookBook;

    /// <summary>Whether the signed-in account shared this entry.</summary>
    [ObservableProperty]
    private bool _isMine;

    /// <summary>Whether the entry is a share still waiting for the gallery to merge it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkText), nameof(MarkOpacity))]
    private bool _isPending;

    /// <summary>Whether the tile is the selected one.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the entry is new or updated since the user last saw it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkText), nameof(MarkOpacity))]
    private SeenMark _mark;

    /// <summary>The badge word. The slot always holds a word so its width is the same whether it is inked or not.</summary>
    public string MarkText => IsPending ? "Pending" : Mark == SeenMark.Updated ? "Updated" : "New";

    /// <summary>1 when the badge slot shows a word, 0 when it is only reserved.</summary>
    public double MarkOpacity => IsPending || Mark != SeenMark.None ? 1 : 0;

    /// <summary>The first cell of the preview sheet; null until it has been decoded. Reading it starts the decode.</summary>
    public Bitmap? Preview
    {
        get
        {
            BeginDecode();
            return _preview;
        }
    }

    /// <summary>Whether the picture is still on its way.</summary>
    public bool IsLoading => _preview is null && !_missing;

    /// <summary>Whether the preview could not be had, so the tile says so instead of looking like a slow one.</summary>
    public bool ImageMissing => _missing;

    /// <summary>The decode this tile started, so a test can settle it.</summary>
    internal Task PendingDecode { get; private set; } = Task.CompletedTask;

    private void BeginDecode()
    {
        if (_started || _disposed) return;
        _started = true;
        var dispatcher = Dispatcher.UIThread;
        var ct = _cts.Token;
        PendingDecode = Task.Run(async () =>
        {
            Bitmap? bitmap = null;
            bool missing;
            try
            {
                var path = await _cache.PreviewAsync(Entry.Preview, ct).ConfigureAwait(false);
                if (path is not null)
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        _gate.Enter();
                        try { if (!_disposed) bitmap = DecodeFirstCell(path); }
                        finally { _gate.Exit(); }
                    }
                    finally { _gate.Release(); }
                }
                missing = bitmap is null;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or InvalidImageContentException
                                           or SixLabors.ImageSharp.UnknownImageFormatException)
            {
                missing = true;
            }

            dispatcher.Post(() =>
            {
                lock (_lock)
                {
                    if (_disposed) { bitmap?.Dispose(); return; }
                    _preview = bitmap;
                    _missing = missing;
                }
                OnPropertyChanged(nameof(Preview));
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(ImageMissing));
            });
        });
    }

    private Avalonia.Media.Imaging.Bitmap DecodeFirstCell(string path)
    {
        using var sheet = ImageFormats.Load(path);
        var grid = SheetGrid.Read(path);
        var rect = grid is null
            ? new Rectangle(0, 0, sheet.Width, sheet.Height)
            : new Rectangle(0, 0, Math.Min(grid.CellWidth, sheet.Width), Math.Min(grid.CellHeight, sheet.Height));
        using var cell = sheet.Clone(c => c.Crop(rect));
        return _bridge.ToBitmap(cell);
    }

    /// <summary>Stops a fetch in flight and frees the picture.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _cts.Cancel();
        _preview?.Dispose();
        _preview = null;
    }
}
