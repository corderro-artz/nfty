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
    /// <param name="toggle">What clicking the owner or a tag link does: toggles that token in the search box. Null makes the links inert.</param>
    public CommunityTile(CommunityEntry entry, ICommunityCache cache, ThumbnailGate gate, IImageBridge bridge,
        Action<string>? toggle = null)
    {
        Entry = entry;
        var t = toggle ?? (_ => { });
        OwnerLink = new CommunityToken("@" + entry.Owner.Login, "@" + entry.Owner.Login, t);
        TagLinks = entry.Tags.Select(tag => new CommunityToken("#" + tag, "#" + tag, t)).ToList();
        int used = Math.Min(OwnerLink.Label.Length, TileOwnerChars);
        var fit = new List<CommunityToken>();
        foreach (var link in TagLinks)
        {
            used += 1 + link.Label.Length;
            if (used > TileLineChars) break;
            fit.Add(link);
        }
        TileTagLinks = fit;
        _cache = cache;
        _gate = gate;
        _bridge = bridge;
    }

    /// <summary>The gallery entry this tile shows.</summary>
    public CommunityEntry Entry { get; }

    /// <summary>The collection's name.</summary>
    public string Name => Entry.Name;

    /// <summary>The sharer as a link: clicking writes <c>@login</c> into the search box.</summary>
    public CommunityToken OwnerLink { get; }

    /// <summary>Every tag as a link: clicking writes <c>#tag</c> into the search box.</summary>
    public IReadOnlyList<CommunityToken> TagLinks { get; }

    /// <summary>The tags that fit WHOLE on the tile's one reserved line beside the owner (a link cut mid-word reads as a
    /// defect); the rail lists them all. The line is mono type, so its room is a count of characters.</summary>
    public IReadOnlyList<CommunityToken> TileTagLinks { get; }

    /// <summary>How many mono characters the tile's owner and tag line holds, a gap counted as one.</summary>
    internal const int TileLineChars = 24;

    /// <summary>How many characters the owner link may take of the tile's line; longer logins are trimmed.</summary>
    internal const int TileOwnerChars = 13;

    /// <summary>Whether the entry carries no tags, so the rail can say so.</summary>
    public bool HasNoTags => TagLinks.Count == 0;

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

    /// <summary>Whether the gallery closed this share's pull request without merging it; the rail says why until it is dismissed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkText), nameof(MarkOpacity))]
    private bool _isClosed;

    /// <summary>Whether the tile is the selected one.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the entry is new or updated since the user last saw it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkText), nameof(MarkOpacity))]
    private SeenMark _mark;

    /// <summary>The badge word. The slot always holds a word so its width is the same whether it is inked or not.</summary>
    public string MarkText => IsPending ? "Pending" : IsClosed ? "Closed" : Mark == SeenMark.Updated ? "Updated" : "New";

    /// <summary>1 when the badge slot shows a word, 0 when it is only reserved.</summary>
    public double MarkOpacity => IsPending || IsClosed || Mark != SeenMark.None ? 1 : 0;

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

    /// <summary>Whether the tile's one marker (breathing diamond while loading, static warning diamond when the
    /// preview is missing) is on screen. One control for both states, as the Set browser's tile does.</summary>
    public bool ShowsMark => IsLoading || _missing;

    /// <summary>The marker's tooltip: set only when the preview is missing, null otherwise.</summary>
    public string? MarkTip => _missing ? "This preview couldn't be fetched" : null;

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
                                           or InvalidImageContentException or ArgumentException
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
                OnPropertyChanged(nameof(ShowsMark));
                OnPropertyChanged(nameof(MarkTip));
            });
        });
    }

    /// <summary>The longest side a tile's picture is kept at: the tile draws ~120 px, up to 2x on a high-density
    /// screen under the shell's 1.2 scale. A 1000 px cell used to be kept whole for it (4 MB a tile).</summary>
    internal const int PreviewMaxSide = 240;

    /// <summary>The picture a tile keeps for a cell of this size: the smallest whole-number reduction that
    /// fits <see cref="PreviewMaxSide"/>, never an enlargement.</summary>
    /// <param name="width">The cell's width.</param>
    /// <param name="height">The cell's height.</param>
    /// <returns>The reduction factor, 1 when the cell already fits.</returns>
    internal static int ReductionFor(int width, int height) =>
        Math.Max(1, (Math.Max(width, height) + PreviewMaxSide - 1) / PreviewMaxSide);

    // One open of the file (pixels and the grid chunk together). Reduced by nearest neighbor and a whole
    // factor, never a filter: the art is pixel art and a filter would invent colors it never had.
    private Avalonia.Media.Imaging.Bitmap DecodeFirstCell(string path)
    {
        using var sheet = ImageFormats.Load(path, keepMetadata: true);
        var grid = SheetGrid.Of(sheet.Metadata);
        var rect = PreviewCells.Rect(grid, 0, sheet.Width, sheet.Height);
        int f = ReductionFor(rect.Width, rect.Height);
        using var cell = sheet.Clone(c =>
        {
            c.Crop(rect);
            if (f > 1) c.Resize((rect.Width + f - 1) / f, (rect.Height + f - 1) / f, KnownResamplers.NearestNeighbor);
        });
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
