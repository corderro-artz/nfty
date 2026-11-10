using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Imaging;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Nfty.App.ViewModels;

/// <summary>How the Community grid is ordered.</summary>
public enum CommunitySort
{
    /// <summary>Newest change first, then by id. The default.</summary>
    RecentlyUpdated,

    /// <summary>By collection name.</summary>
    Name,

    /// <summary>Most assets first.</summary>
    Assets,
}

/// <summary>Which emptiness the grid is showing, so the page can say which and offer the click that undoes it.</summary>
public enum CommunityEmptyKind
{
    /// <summary>The grid has tiles.</summary>
    None,

    /// <summary>Entries exist but none match the search or tag.</summary>
    NoMatches,

    /// <summary>The gallery is reachable and has no entries.</summary>
    EmptyGallery,

    /// <summary>No list was ever fetched and GitHub can't be reached.</summary>
    NeverFetchedOffline,
}

/// <summary>The Community page: the shared gallery as a searchable, paged grid with a detail rail.</summary>
public sealed partial class CommunityViewModel : ViewModelBase, IDisposable
{
    /// <summary>How many tag chips the tray shows in its row; the rest go in the More tags dropdown. Four chips of
    /// the stated width, the dropdown button and the TAG label fit the header beside the search box and the sort at
    /// the page's smallest area (about 1067 px), so this is a measured budget, not a taste.</summary>
    internal const int TrayFit = 4;

    private readonly ICommunityCache _cache;
    private readonly ICommunitySeen _seen;
    private readonly IGitHubAccount _account;
    private readonly IStatusService _status;
    private readonly IUrlOpener _urls;
    private readonly ThumbnailGate _gate;
    private readonly IImageBridge _bridge;
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, CommunityTile> _byId = new(StringComparer.Ordinal);
    private List<CommunityTile> _filtered = [];
    private CommunityListing? _listing;
    private bool _refreshed;
    private Image<Rgba32>? _sheet;
    private SheetGrid? _grid;
    private CancellationTokenSource? _railCts;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private CancellationTokenSource? _downloadCts;
    private string _downloadingId = "";
    private string _failedId = "";
    private readonly ICommunitySharing? _sharing;
    private readonly HashSet<string> _standIns = new(StringComparer.Ordinal);

    /// <summary>Creates the page.</summary>
    /// <param name="cache">The list and its pictures.</param>
    /// <param name="seen">Which entries the user has seen.</param>
    /// <param name="account">The GitHub account; only its login is read, so browsing works with a revoked token.</param>
    /// <param name="nav">Navigation.</param>
    /// <param name="dialogs">Dialogs.</param>
    /// <param name="status">The status line.</param>
    /// <param name="openSet">Opens a downloaded Set by path.</param>
    /// <param name="urls">Opens a web address; null makes a recorder that opens nothing.</param>
    /// <param name="gate">The decode limit; null makes a small one.</param>
    /// <param name="bridge">Image to bitmap conversion; null makes the standard one.</param>
    /// <param name="http">The client downloads go through. It carries no token and no timeout (the download
    /// has its own stall limit); null makes one the page owns and disposes.</param>
    /// <param name="sharing">Shares, unshares and the pending list; null leaves Unshare saying it is unavailable and shows nothing pending.</param>
    public CommunityViewModel(ICommunityCache cache, ICommunitySeen seen, IGitHubAccount account,
        INavigationService nav, IDialogService dialogs, IStatusService status, Func<string, Task> openSet,
        IUrlOpener? urls = null, ThumbnailGate? gate = null, IImageBridge? bridge = null, HttpClient? http = null,
        ICommunitySharing? sharing = null)
    {
        _ownsHttp = http is null;
        _sharing = sharing;
        _http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _cache = cache;
        _seen = seen;
        _account = account;
        Nav = nav;
        Dialogs = dialogs;
        _status = status;
        OpenSet = openSet;
        _urls = urls ?? new NoopUrlOpener();
        _gate = gate ?? new ThumbnailGate(2);
        _bridge = bridge ?? new ImageBridge();
        UnshareHandler = _sharing is null
            ? _ =>
            {
                _status.Say("Unsharing isn't available here.");
                return Task.CompletedTask;
            }
            : UnshareCoreAsync;
        _account.Changed += OnAccountChanged;

        if (_cache.Cached is { } cached) Apply(cached, refreshed: false);
        else StatusLine = "Checking the gallery…";
        PendingRefresh = RefreshCoreAsync();
    }

    /// <summary>Navigation, kept for the commands later tasks add.</summary>
    internal INavigationService Nav { get; }

    /// <summary>Dialogs, kept for the commands later tasks add.</summary>
    internal IDialogService Dialogs { get; }

    /// <summary>Opens a downloaded Set in the Set browser.</summary>
    internal Func<string, Task> OpenSet { get; }

    /// <summary>What Unshare does with the selected entry: asks, then opens the removal pull request.</summary>
    public Func<CommunityEntry, Task> UnshareHandler { get; set; }

    /// <summary>The refresh this page last started, so a test can wait for it.</summary>
    public Task PendingRefresh { get; private set; } = Task.CompletedTask;

    /// <summary>The rail's sheet load, so a test can wait for it.</summary>
    internal Task PendingRail { get; private set; } = Task.CompletedTask;

    // ---- search, tag, sort -----------------------------------------------------------------

    /// <summary>The text in the search box: <c>#tag</c>, <c>@user</c> and plain words. The ONE source of truth for
    /// what the grid shows; every chip and link only edits this text.</summary>
    [ObservableProperty]
    private string _query = "";

    /// <summary>How the grid is ordered.</summary>
    [ObservableProperty]
    private CommunitySort _sort = CommunitySort.RecentlyUpdated;

    /// <summary>The tag tray's row: the first of the gallery's fixed tags, in the gallery's order.</summary>
    [ObservableProperty]
    private IReadOnlyList<CommunityToken> _trayTags = [];

    /// <summary>The rest of the gallery's tags, offered in the More tags dropdown.</summary>
    [ObservableProperty]
    private IReadOnlyList<CommunityToken> _moreTags = [];

    /// <summary>Whether the gallery has any tags at all; the tray only loses its ink until the list arrives.</summary>
    public bool HasTags => TrayTags.Count > 0;

    /// <summary>Whether the gallery has tags beyond the tray's row, so the dropdown has something to hold.</summary>
    public bool HasMoreTags => MoreTags.Count > 0;

    /// <summary>Whether any tag in the dropdown is in the search box, so the dropdown button shows it is in force.</summary>
    public bool MoreIsOn => MoreTags.Any(t => t.IsOn);

    partial void OnQueryChanged(string value)
    {
        MarkTokens();
        Refilter(resetPage: true);
    }

    /// <summary>Marks every chip and link whose token is in the search box.</summary>
    private void MarkTokens()
    {
        foreach (var c in TrayTags.Concat(MoreTags)) c.IsOn = CommunitySearch.Has(Query, c.Token);
        foreach (var t in _byId.Values)
        {
            t.OwnerLink.IsOn = CommunitySearch.Has(Query, t.OwnerLink.Token);
            foreach (var l in t.TagLinks) l.IsOn = CommunitySearch.Has(Query, l.Token);
        }
        OnPropertyChanged(nameof(MoreIsOn));
    }

    partial void OnSortChanged(CommunitySort value)
    {
        OnPropertyChanged(nameof(SortIsUpdated));
        OnPropertyChanged(nameof(SortIsName));
        OnPropertyChanged(nameof(SortIsAssets));
        Refilter(resetPage: true);
    }

    /// <summary>Whether the grid is ordered by recent change.</summary>
    public bool SortIsUpdated => Sort == CommunitySort.RecentlyUpdated;

    /// <summary>Whether the grid is ordered by name.</summary>
    public bool SortIsName => Sort == CommunitySort.Name;

    /// <summary>Whether the grid is ordered by asset count.</summary>
    public bool SortIsAssets => Sort == CommunitySort.Assets;

    /// <summary>Adds a token (<c>#tag</c> or <c>@user</c>) to the search box, or removes it when it is already there.</summary>
    /// <param name="token">The token.</param>
    internal void ToggleToken(string token) => Query = CommunitySearch.Toggle(Query, token);

    /// <summary>Picks how the grid is ordered.</summary>
    /// <param name="sort">The order.</param>
    [RelayCommand]
    private void SortBy(CommunitySort sort) => Sort = sort;

    /// <summary>Clears the search box, which is the click that undoes "nothing matches".</summary>
    [RelayCommand]
    private void ClearSearch() => Query = "";

    // ---- the grid --------------------------------------------------------------------------

    /// <summary>The tiles on the current page.</summary>
    [ObservableProperty]
    private IReadOnlyList<CommunityTile> _tiles = [];

    /// <summary>The zero-based page.</summary>
    [ObservableProperty]
    private int _pageIndex;

    private int _pageSize = 8;

    /// <summary>How many tiles a page holds. Set by the view from the measured grid.</summary>
    public int PageSize
    {
        get => _pageSize;
        set
        {
            value = Math.Max(1, value);
            if (value == _pageSize) return;
            int first = PageIndex * _pageSize;
            _pageSize = value;
            PageIndex = first / value;
            OnPropertyChanged();
            Repage();
        }
    }

    /// <summary>How many pages the current filter fills.</summary>
    public int PageCount => Math.Max(1, (_filtered.Count + PageSize - 1) / PageSize);

    /// <summary>How many entries match the search and tag.</summary>
    public int MatchCount => _filtered.Count;

    /// <summary>The pager's words, such as <c>1–8 of 23</c>.</summary>
    public string PageText
    {
        get
        {
            if (_filtered.Count == 0) return "0 collections";
            int from = PageIndex * PageSize + 1;
            int to = Math.Min(_filtered.Count, from + PageSize - 1);
            return string.Create(CultureInfo.InvariantCulture, $"{from}–{to} of {_filtered.Count:N0}");
        }
    }

    partial void OnPageIndexChanged(int value) => Repage();

    /// <summary>Whether there is an earlier page.</summary>
    public bool CanPrevPage => PageIndex > 0;

    /// <summary>Whether there is a later page.</summary>
    public bool CanNextPage => PageIndex < PageCount - 1;

    /// <summary>Shows the earlier page.</summary>
    [RelayCommand(CanExecute = nameof(CanPrevPage))]
    private void PrevPage() => PageIndex--;

    /// <summary>Shows the later page.</summary>
    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private void NextPage() => PageIndex++;

    private void Refilter(bool resetPage)
    {
        if (resetPage) PageIndex = 0;
        var q = CommunitySearch.Parse(Query);
        IEnumerable<CommunityTile> found = _byId.Values.Where(t => CommunitySearch.Matches(t.Entry, q));

        _filtered = Sort switch
        {
            CommunitySort.Name => found.OrderBy(t => t.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Entry.Id, StringComparer.Ordinal).ToList(),
            CommunitySort.Assets => found.OrderByDescending(t => t.Entry.Assets)
                .ThenBy(t => t.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Entry.Id, StringComparer.Ordinal).ToList(),
            _ => found.OrderByDescending(t => t.Entry.Updated)
                .ThenBy(t => t.Entry.Id, StringComparer.Ordinal).ToList(),
        };
        Repage();
    }

    private void Repage()
    {
        int last = Math.Max(0, PageCount - 1);
        if (PageIndex > last) { PageIndex = last; return; }
        Tiles = _filtered.Skip(PageIndex * PageSize).Take(PageSize).ToList();
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanPrevPage));
        OnPropertyChanged(nameof(CanNextPage));
        PrevPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        RaiseEmpty();
    }

    // ---- which emptiness -------------------------------------------------------------------

    /// <summary>Which emptiness the grid is in, if any.</summary>
    public CommunityEmptyKind EmptyKind
    {
        get
        {
            if (_filtered.Count > 0) return CommunityEmptyKind.None;
            if (_listing?.Index is null) return _refreshed ? CommunityEmptyKind.NeverFetchedOffline : CommunityEmptyKind.None;
            return _byId.Count == 0 ? CommunityEmptyKind.EmptyGallery : CommunityEmptyKind.NoMatches;
        }
    }

    /// <summary>The sentence for the emptiness, naming the search when that is the cause.</summary>
    public string EmptyText => EmptyKind switch
    {
        CommunityEmptyKind.NoMatches => $"No collection matches “{Query.Trim()}”.",
        CommunityEmptyKind.EmptyGallery => "Nobody has shared a collection yet.",
        CommunityEmptyKind.NeverFetchedOffline => "The list hasn't been fetched yet, and GitHub can't be reached.",
        _ => "",
    };

    /// <summary>The label of the button that undoes the emptiness.</summary>
    public string EmptyActionText => EmptyKind == CommunityEmptyKind.NoMatches ? "Clear search" : "Try again";

    /// <summary>Whether the emptiness message shows.</summary>
    public bool IsEmpty => EmptyKind != CommunityEmptyKind.None;

    /// <summary>Does what the emptiness button says: clears the search, or fetches the list again.</summary>
    [RelayCommand]
    private async Task EmptyAction()
    {
        if (EmptyKind == CommunityEmptyKind.NoMatches) ClearSearch();
        else await Refresh();
    }

    private void RaiseEmpty()
    {
        OnPropertyChanged(nameof(EmptyKind));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(EmptyActionText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    // ---- the list, the notice, the counts --------------------------------------------------

    /// <summary>How many entries are new since the user last looked.</summary>
    [ObservableProperty]
    private int _newCount;

    /// <summary>How many entries changed since the user last looked.</summary>
    [ObservableProperty]
    private int _updatedCount;

    /// <summary>The reserved line under the header: when the list was updated and what is new, or a notice.</summary>
    [ObservableProperty]
    private string _statusLine = "";

    /// <summary>Whether the status line is a notice (offline, list too new, list damaged) and so reads in warning ink.</summary>
    [ObservableProperty]
    private bool _statusIsWarning;

    /// <summary>Whether Mark all seen has anything to do.</summary>
    public bool HasMarks => NewCount + UpdatedCount > 0;

    /// <summary>Whether the list on screen is a cached one because GitHub could not be reached.</summary>
    public bool IsOffline => _listing?.Notice is { } n && n != ShareRules.IndexTooNew && n != ShareRules.ListDamaged;

    /// <summary>Fetches the list again.</summary>
    [RelayCommand]
    private Task Refresh() => PendingRefresh = RefreshCoreAsync();

    /// <summary>Marks every entry seen.</summary>
    [RelayCommand(CanExecute = nameof(HasMarks))]
    private void MarkAllSeen()
    {
        if (_listing?.Index is not { } index) return;
        _seen.SeeAll(index);
        foreach (var t in _byId.Values) t.Mark = SeenMark.None;
        RecountMarks();
    }

    private async Task RefreshCoreAsync()
    {
        CommunityListing listing;
        try { listing = await _cache.RefreshAsync(_cts.Token); }
        catch (OperationCanceledException) { return; }
        catch (Exception)
        {
            // The cache turns every network failure into a notice; anything else is still not worth
            // an exception's words on screen.
            listing = _cache.Cached ?? new CommunityListing(null, DateTimeOffset.UtcNow, ShareRules.Unreachable);
            if (listing.Notice is null) listing = listing with { Notice = ShareRules.Unreachable };
        }
        Apply(listing, refreshed: true);
        await CheckPendingAsync(listing).ConfigureAwait(true);
    }

    /// <summary>Looks at the pending pull requests behind the list that just arrived. GitHub trouble never blocks browsing: whatever
    /// fails is silently tried again at the next refresh.</summary>
    private async Task CheckPendingAsync(CommunityListing listing)
    {
        if (_sharing is null || _sharing.Pending.Count == 0) return;
        var before = _sharing.Pending;
        try { await _sharing.CheckPendingAsync(listing.Index, _cts.Token).ConfigureAwait(true); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { return; }
        if (!ReferenceEquals(before, _sharing.Pending) && !_cts.IsCancellationRequested) Apply(listing, refreshed: true);
    }

    private void Apply(CommunityListing listing, bool refreshed)
    {
        _listing = listing;
        _refreshed |= refreshed;
        var selectedId = Selected?.Entry.Id;
        var keep = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in listing.Index?.Entries ?? [])
        {
            keep.Add(e.Id);
            if (_byId.TryGetValue(e.Id, out var old) && e.Updated == old.Entry.Updated
                && CommunityEntry.SameShare(e, old.Entry))
            {
                continue;
            }
            old?.Dispose();
            _byId[e.Id] = new CommunityTile(e, _cache, _gate, _bridge, token => ToggleToken(token));
        }
        // A share the gallery does not list yet is drawn from what was submitted, until the list shows it.
        var pending = LatestPending();
        _standIns.Clear();
        foreach (var rec in pending.Values.Where(r => !r.IsUnshare && !keep.Contains(r.Id)))
        {
            keep.Add(rec.Id);
            _standIns.Add(rec.Id);
            if (_byId.TryGetValue(rec.Id, out var old) && old.Entry.Updated == rec.SubmittedAt && old.Entry.Name == rec.Name) continue;
            old?.Dispose();
            _byId[rec.Id] = new CommunityTile(rec.ToEntry(), _cache, _gate, _bridge, token => ToggleToken(token));
        }
        foreach (var id in _byId.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _byId[id].Dispose();
            _byId.Remove(id);
        }
        foreach (var t in _byId.Values)
        {
            t.Mark = _standIns.Contains(t.Entry.Id) ? SeenMark.None : _seen.MarkOf(t.Entry);
            t.IsMine = IsMineEntry(t.Entry);
            if (_sharing is not null)
            {
                var rec = pending.GetValueOrDefault(t.Entry.Id);
                t.IsPending = rec is { Closed: false };
                t.IsClosed = rec is { Closed: true };
            }
        }
        if (selectedId is not null) Selected = _byId.GetValueOrDefault(selectedId);

        BuildTagTray();
        MarkTokens();
        RecountMarks();
        Refilter(resetPage: false);
        OnPropertyChanged(nameof(IsOffline));
        NotifyActions();
    }

    private void BuildTagTray()
    {
        // The gallery's fixed list, in its own order - not the most used ones. Rebuilt only when it changed, so a
        // refresh does not replace the chips under the pointer.
        var all = _cache.Tags;
        if (all.SequenceEqual(TrayTags.Concat(MoreTags).Select(c => c.Label), StringComparer.Ordinal)) return;
        CommunityToken Chip(string t) => new(t, "#" + t, token => ToggleToken(token));
        TrayTags = all.Take(TrayFit).Select(Chip).ToList();
        MoreTags = all.Skip(TrayFit).Select(Chip).ToList();
        OnPropertyChanged(nameof(HasMoreTags));
        OnPropertyChanged(nameof(HasTags));
    }

    private void RecountMarks()
    {
        var (n, u) = _listing?.Index is { } index ? _seen.Count(index) : (0, 0);
        NewCount = n;
        UpdatedCount = u;
        OnPropertyChanged(nameof(HasMarks));
        MarkAllSeenCommand.NotifyCanExecuteChanged();
        StatusLine = StatusFor(_listing, n, u);
        StatusIsWarning = _listing?.Notice is not null;
    }

    private static string StatusFor(CommunityListing? listing, int fresh, int updated)
    {
        if (listing is null) return "Checking the gallery…";
        if (listing.Notice is { } notice) return notice;
        var line = "Updated " + listing.FetchedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        if (fresh > 0) line += string.Create(CultureInfo.InvariantCulture, $" · {fresh} new");
        if (updated > 0) line += string.Create(CultureInfo.InvariantCulture, $" · {updated} updated");
        return line;
    }

    /// <summary>Marks the entries that are shares waiting for the gallery to merge them.</summary>
    /// <param name="ids">The entry ids, <c>ownerId/slug</c>.</param>
    public void SetPending(IEnumerable<string> ids)
    {
        var set = new HashSet<string>(ids, StringComparer.Ordinal);
        foreach (var t in _byId.Values) t.IsPending = set.Contains(t.Entry.Id);
    }

    // ---- the account -----------------------------------------------------------------------

    private bool IsMineEntry(CommunityEntry e) =>
        _account.UserId is { } id ? e.Owner.Id == id
        : _account.Login is { Length: > 0 } login && string.Equals(login, e.Owner.Login, StringComparison.OrdinalIgnoreCase);

    /// <summary>The newest pending record for each entry id.</summary>
    private Dictionary<string, PendingRecord> LatestPending() =>
        (_sharing?.Pending ?? []).GroupBy(r => r.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SubmittedAt).First(), StringComparer.Ordinal);

    private PendingRecord? PendingFor(CommunityTile? tile) =>
        tile is null || _sharing is null ? null : LatestPending().GetValueOrDefault(tile.Entry.Id);

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        foreach (var t in _byId.Values) t.IsMine = IsMineEntry(t.Entry);
        NotifyActions();
    }

    // ---- selection and the rail ------------------------------------------------------------

    /// <summary>The selected tile, or null.</summary>
    [ObservableProperty]
    private CommunityTile? _selected;

    /// <summary>Selects a tile, which also counts it as seen.</summary>
    /// <param name="tile">The tile clicked.</param>
    [RelayCommand]
    private void Select(CommunityTile? tile) => Selected = tile;

    partial void OnSelectedChanged(CommunityTile? oldValue, CommunityTile? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            _seen.See(newValue.Entry);
            newValue.Mark = SeenMark.None;
            RecountMarks();
        }
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(DatesText));
        OnPropertyChanged(nameof(DescriptionText));
        OnPropertyChanged(nameof(RailFacts));
        NotifyActions();
        BeginRail(newValue);
    }

    /// <summary>Whether a collection is selected; the rail says how to pick one when not.</summary>
    public bool HasSelection => Selected is not null;

    /// <summary>The rail's dates line: <c>Shared 3 Nov 2026 · updated 9 Nov 2026</c>.</summary>
    public string DatesText
    {
        get
        {
            if (Selected?.Entry is not { } e) return "";
            var shared = e.Created.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            var updated = e.Updated.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            return shared == updated ? $"Shared {shared}" : $"Shared {shared} · updated {updated}";
        }
    }

    /// <summary>The description, or a plain line when the sharer wrote none.</summary>
    public string DescriptionText => Selected?.Entry.Description is { Length: > 0 } d ? d : "No description.";

    /// <summary>The rail's fact lines.</summary>
    public IReadOnlyList<string> RailFacts
    {
        get
        {
            if (Selected is not { } t) return [];
            var e = t.Entry;
            return
            [
                string.Create(CultureInfo.InvariantCulture, $"{e.Assets:N0} assets"),
                $"{e.Canvas.Width} x {e.Canvas.Height} px",
                SizeText.Of(e.Set.Size),
                e.Models ? "3D models included" : "No 3D models",
                e.IncludesCookBook ? "CookBook included" : "No CookBook",
            ];
        }
    }

    /// <summary>The large preview: the current cell of the sheet.</summary>
    [ObservableProperty]
    private Bitmap? _railPreview;

    /// <summary>Whether the sheet is still on its way.</summary>
    [ObservableProperty]
    private bool _railLoading;

    /// <summary>Whether the sheet could not be had.</summary>
    [ObservableProperty]
    private bool _railMissing;

    /// <summary>The zero-based cell shown.</summary>
    [ObservableProperty]
    private int _cell;

    /// <summary>How many cells the sheet holds.</summary>
    [ObservableProperty]
    private int _cellCount;

    /// <summary>The cell caption, such as <c>cell 3 of 12</c>; blank for a one-cell sheet.</summary>
    public string CellText => CellCount > 1
        ? string.Create(CultureInfo.InvariantCulture, $"cell {Cell + 1} of {CellCount}")
        : "";

    partial void OnCellChanged(int value) => OnPropertyChanged(nameof(CellText));

    partial void OnCellCountChanged(int value) => OnPropertyChanged(nameof(CellText));

    /// <summary>Steps the preview to another cell, wrapping at the ends.</summary>
    /// <param name="delta">+1 for the next cell, -1 for the one before.</param>
    public void StepCell(int delta)
    {
        if (CellCount <= 1 || _sheet is null) return;
        Cell = ((Cell + delta) % CellCount + CellCount) % CellCount;
        ShowCell();
    }

    private void BeginRail(CommunityTile? tile)
    {
        _railCts?.Cancel();
        _railCts = null;
        _sheet?.Dispose();
        _sheet = null;
        _grid = null;
        RailPreview = null;
        Cell = 0;
        CellCount = 0;
        RailMissing = false;
        RailLoading = tile is not null;
        if (tile is null) { PendingRail = Task.CompletedTask; return; }

        var cts = _railCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        var ct = cts.Token;
        var dispatcher = Dispatcher.UIThread;
        PendingRail = Task.Run(async () =>
        {
            Image<Rgba32>? sheet = null;
            SheetGrid? grid = null;
            try
            {
                var path = await _cache.PreviewAsync(tile.Entry.Preview, ct).ConfigureAwait(false);
                if (path is not null)
                {
                    await _gate.WaitAsync().ConfigureAwait(false);
                    try
                    {
                        sheet = ImageFormats.Load(path);
                        grid = SheetGrid.Read(path);
                    }
                    finally { _gate.Release(); }
                }
            }
            catch (OperationCanceledException) { sheet?.Dispose(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                           or InvalidImageContentException
                                           or SixLabors.ImageSharp.UnknownImageFormatException)
            {
                sheet?.Dispose();
                sheet = null;
            }

            dispatcher.Post(() =>
            {
                if (ct.IsCancellationRequested || !ReferenceEquals(Selected, tile)) { sheet?.Dispose(); return; }
                _sheet = sheet;
                _grid = grid;
                RailLoading = false;
                RailMissing = sheet is null;
                CellCount = sheet is null ? 0 : Math.Max(1, grid?.Count ?? 1);
                ShowCell();
            });
        });
    }

    private void ShowCell()
    {
        var old = RailPreview;
        if (_sheet is null) { RailPreview = null; old?.Dispose(); return; }
        Rectangle rect;
        if (_grid is null) rect = new Rectangle(0, 0, _sheet.Width, _sheet.Height);
        else
        {
            int col = Cell % _grid.Columns, row = Cell / _grid.Columns;
            rect = Rectangle.Intersect(new Rectangle(col * _grid.CellWidth, row * _grid.CellHeight,
                _grid.CellWidth, _grid.CellHeight), new Rectangle(0, 0, _sheet.Width, _sheet.Height));
        }
        using var cell = _sheet.Clone(c => c.Crop(rect));
        RailPreview = _bridge.ToBitmap(cell);
        old?.Dispose();
    }

    // ---- download, report, unshare ---------------------------------------------------------

    /// <summary>Whether the selected entry is the signed-in account's own.</summary>
    public bool CanUnshareSelected => Selected is { IsMine: true } t && !_standIns.Contains(t.Entry.Id) && PendingFor(t) is null;

    /// <summary>Whether the selected collection has a closed request whose reasons can be dismissed.</summary>
    public bool CanDismissSelected => PendingFor(Selected) is { Closed: true };

    /// <summary>
    /// The sentence under the rail's buttons: why the last download of the selected collection failed
    /// (the gallery's own words), else why Download is off, else empty. One reserved line says all three.
    /// </summary>
    public string DownloadNote =>
        DownloadFailure.Length > 0 && Selected?.Entry.Id == _failedId ? DownloadFailure
        : PendingNote.Length > 0 ? PendingNote
        : IsOffline && _listing?.Notice is { } n ? n : "";

    /// <summary>What the rail says about a pull request still open for the selected collection, or why the gallery turned it
    /// away (its comment's lines, until dismissed); empty when nothing is pending for it.</summary>
    public string PendingNote => PendingFor(Selected) switch
    {
        null => "",
        { Closed: true, ClosedReasons.Count: > 0 } r => string.Join(" ", r.ClosedReasons),
        { Closed: true } => "The gallery closed this request without listing it.",
        { IsUnshare: true } => "Removal submitted. The gallery takes it out of the list within a few minutes.",
        _ => "Submitted. The gallery lists it within a few minutes.",
    };

    /// <summary>The sentence the last failed download ended with; empty after a success or a stop.</summary>
    [ObservableProperty]
    private string _downloadFailure = "";

    /// <summary>Whether a download is running. Only one runs at a time, so Download is off meanwhile.</summary>
    [ObservableProperty]
    private bool _isDownloading;

    /// <summary>How much of the running download has arrived, 0 to 1.</summary>
    [ObservableProperty]
    private double _downloadFraction;

    /// <summary>Whether the rail's progress row is inked: a download is running and it is the selected collection's.</summary>
    public bool ShowsDownloadProgress => IsDownloading && Selected?.Entry.Id == _downloadingId;

    /// <summary>The download this page last started, so a test (or a caller) can wait for it.</summary>
    internal Task PendingDownload { get; private set; } = Task.CompletedTask;

    partial void OnDownloadFailureChanged(string value) => OnPropertyChanged(nameof(DownloadNote));

    partial void OnIsDownloadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowsDownloadProgress));
        DownloadCommand.NotifyCanExecuteChanged();
        StopDownloadCommand.NotifyCanExecuteChanged();
    }

    private bool CanDownload() => Selected is { } t && !_standIns.Contains(t.Entry.Id) && !IsOffline && !IsDownloading;

    private bool CanReport() => Selected is { } t && !_standIns.Contains(t.Entry.Id) && ReportUrl(t.Entry) is not null;

    private bool CanUnshare() => CanUnshareSelected && !IsUnsharing;

    /// <summary>Downloads the selected collection, proves it is what the gallery lists, and opens it in the Set browser.</summary>
    [RelayCommand(CanExecute = nameof(CanDownload))]
    private Task Download()
    {
        if (Selected is not { } t || IsDownloading) return Task.CompletedTask;   // a disabled button is a label
        return PendingDownload = DownloadCoreAsync(t.Entry);
    }

    /// <summary>Stops the running download. What it had received is thrown away.</summary>
    [RelayCommand(CanExecute = nameof(IsDownloading))]
    private void StopDownload() => _downloadCts?.Cancel();

    private async Task DownloadCoreAsync(CommunityEntry entry)
    {
        var cts = _downloadCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        _downloadingId = _failedId = entry.Id;
        DownloadFailure = "";
        DownloadFraction = 0;
        IsDownloading = true;
        string? path = null;
        try
        {
            path = await CommunityDownload.FetchAsync(_http, entry, _cache.SetsDir,
                new Progress<double>(f => DownloadFraction = f), cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!_cts.IsCancellationRequested) _status.Say("Download stopped.");
        }
        catch (CommunityException ex)
        {
            DownloadFailure = ErrorText.For(ex);        // a ShareRules sentence, shown verbatim
        }
        catch (Exception)
        {
            // Not worth an exception's words on screen.
            DownloadFailure = ShareRules.Unreachable;
        }
        finally
        {
            IsDownloading = false;
            DownloadFraction = 0;
            if (ReferenceEquals(_downloadCts, cts)) _downloadCts = null;
            cts.Dispose();
        }
        // Opens behind its own busy card (and reports its own failure), after the download row has gone.
        if (path is not null && !_cts.IsCancellationRequested) await OpenSet(path);
    }

    /// <summary>Opens the gallery's report form for the selected collection.</summary>
    [RelayCommand(CanExecute = nameof(CanReport))]
    private void Report()
    {
        if (Selected is { } t && ReportUrl(t.Entry) is { } url) _urls.Open(url);
    }

    /// <summary>Removes the signed-in account's own share.</summary>
    [RelayCommand(CanExecute = nameof(CanUnshare))]
    private Task Unshare() => Selected is { IsMine: true } t ? UnshareHandler(t.Entry) : Task.CompletedTask;

    /// <summary>Lets go of a closed request's reasons, and of its stand-in tile when the gallery never listed it.</summary>
    [RelayCommand(CanExecute = nameof(CanDismissSelected))]
    private void DismissPending()
    {
        if (PendingFor(Selected) is not { Closed: true } rec || _sharing is null) return;
        _sharing.Dismiss(rec);
        if (_listing is { } listing) Apply(listing, refreshed: false);
        else NotifyActions();
    }

    /// <summary>Whether an unshare is being sent; only one runs at a time.</summary>
    [ObservableProperty]
    private bool _isUnsharing;

    partial void OnIsUnsharingChanged(bool value) => NotifyActions();

    /// <summary>Finishes when the last unshare has, so a test can wait for it.</summary>
    internal Task PendingUnshare { get; private set; } = Task.CompletedTask;

    private Task UnshareCoreAsync(CommunityEntry entry) => PendingUnshare = UnshareRunAsync(entry);

    private async Task UnshareRunAsync(CommunityEntry entry)
    {
        if (_sharing is null || IsUnsharing) return;
        var confirm = new ConfirmDialogViewModel(Dialogs, "Unshare this collection?",
            $"Remove \"{entry.Name}\" from the gallery? Your nfty-public repository stays.", "Remove");
        if (await Dialogs.ShowAsync<bool>(confirm).ConfigureAwait(true) != true) return;

        IsUnsharing = true;
        try
        {
            // A slow job with no screen of its own gets the busy card, which is also what keeps the window from closing under it.
            var record = await BusyViewModel.RunAsync(Dialogs, "Unsharing", "Opening the removal request…",
                (_, ct) => _sharing.UnshareAsync(entry, ct), cancellable: true).ConfigureAwait(true);
            _status.Say($"Removal submitted as pull request #{record.PullNumber}. The gallery takes it out of the list within a few minutes.");
            if (_listing is { } listing) Apply(listing, refreshed: false);
        }
        catch (OperationCanceledException) { _status.Say("Unsharing stopped. Nothing was changed."); }
                catch (Exception ex) { await Dialogs.ShowErrorAsync("Couldn't unshare", ErrorText.For(ex)).ConfigureAwait(true); }
        finally { IsUnsharing = false; }
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(PendingNote));
        OnPropertyChanged(nameof(CanDismissSelected));
        DismissPendingCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanUnshareSelected));
        OnPropertyChanged(nameof(DownloadNote));
        OnPropertyChanged(nameof(ShowsDownloadProgress));
        DownloadCommand.NotifyCanExecuteChanged();
        ReportCommand.NotifyCanExecuteChanged();
        UnshareCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The gallery's report form for one entry, naming nothing but the entry's id.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The address, or null when the entry's id is not one nfty will put in a URL.</returns>
    public static Uri? ReportUrl(CommunityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Owner.Id <= 0 || !ShareSlug.IsValid(entry.Slug)) return null;
        return new Uri(string.Create(CultureInfo.InvariantCulture,
            $"https://github.com/{CommunityEndpoints.Default.GalleryRepo}/issues/new?template=report.yml&entry={entry.Owner.Id}/{entry.Slug}"));
    }

    /// <summary>Stops everything in flight and frees the pictures.</summary>
    public void Dispose()
    {
        _account.Changed -= OnAccountChanged;
        _cts.Cancel();
        _railCts?.Cancel();
        if (_ownsHttp) _http.Dispose();
        foreach (var t in _byId.Values) t.Dispose();
        _byId.Clear();
        _sheet?.Dispose();
        _sheet = null;
        RailPreview?.Dispose();
        RailPreview = null;
    }
}
