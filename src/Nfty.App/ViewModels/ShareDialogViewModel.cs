using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;

namespace Nfty.App.ViewModels;

/// <summary>Where the share card is in its life. The card builds the Editing state; the run belongs to the next step.</summary>
public enum ShareState
{
    /// <summary>The form is open and nothing has been sent.</summary>
    Editing,

    /// <summary>A share is being sent.</summary>
    Running,

    /// <summary>The share was sent.</summary>
    Done,

    /// <summary>The share could not be sent.</summary>
    Failed,
}

/// <summary>What the card's one reserved block holds. Exactly one at a time, in this order of precedence after <see cref="None"/>.</summary>
public enum ShareLowerBlock
{
    /// <summary>Nothing to decide.</summary>
    None,

    /// <summary>The repository holds things the gallery doesn't allow (or can't be used at all).</summary>
    Extras,

    /// <summary>The gallery already lists this Set, or a share of yours is in the way.</summary>
    Duplicate,

    /// <summary>This collection is one of your shares already; sharing it again updates it, if you say so.</summary>
    AlreadyShared,

    /// <summary>You share the most the gallery takes; one must go to make room.</summary>
    Replace,
}

/// <summary>One of the card's four fixed limit lines.</summary>
public sealed partial class ShareLimitRow : ObservableObject
{
    /// <summary>The line, e.g. <c>Canvas 32 x 32 of 512 x 512</c>.</summary>
    [ObservableProperty] private string _text = "";

    /// <summary>Whether the figure is past its limit, which turns the line to the warning ink.</summary>
    [ObservableProperty] private bool _isOver;

    /// <summary>The full sentence behind the line, for its tooltip.</summary>
    [ObservableProperty] private string _tip = "";

    /// <summary>What the row measures, in the card's label voice: <c>CANVAS</c>.</summary>
    [ObservableProperty] private string _label = "";

    /// <summary>The figure itself: <c>32 × 32</c>, or what stands in for it while it is unknown.</summary>
    [ObservableProperty] private string _value = "";

    /// <summary>The gallery's limit: <c>max 512 × 512</c>.</summary>
    [ObservableProperty] private string _limit = "";

    /// <summary>Whether the figure is not known yet (or can't be, signed out): neither within nor past.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsOk))] private bool _isPending;

    /// <summary>Whether the figure is known and within its limit.</summary>
    public bool IsOk => !IsPending && !IsOver;

    partial void OnIsOverChanged(bool value) => OnPropertyChanged(nameof(IsOk));

    internal void Show(string label, string value, string limit, bool over, bool pending)
    {
        Label = label;
        Value = value;
        Limit = limit;
        IsOver = over;
        IsPending = pending;
    }
}

/// <summary>One tag the listing may carry.</summary>
public sealed partial class ShareTagChip : ObservableObject
{
    /// <summary>Makes a chip.</summary>
    /// <param name="name">The tag.</param>
    public ShareTagChip(string name) => Name = name;

    /// <summary>The tag.</summary>
    public string Name { get; }

    /// <summary>Whether the listing carries it.</summary>
    [ObservableProperty] private bool _isOn;
}

/// <summary>One thing the scan found in the repository.</summary>
/// <param name="Text">What it is, with its size, or the sentence saying why it can't be removed here.</param>
/// <param name="Removable">Whether nfty can delete it.</param>
public sealed record ShareExtraRow(string Text, bool Removable);

/// <summary>One of the user's shares, offered as the one to replace, or shown as already shared.</summary>
public sealed partial class ShareShareRow : ObservableObject, IDisposable
{
    /// <summary>Makes a row.</summary>
    /// <param name="entry">The share.</param>
    /// <param name="tile">Draws the share's first preview cell.</param>
    /// <param name="onChoose">Called when the row is picked as the one to replace.</param>
    public ShareShareRow(CommunityEntry entry, CommunityTile tile, Action<ShareShareRow> onChoose)
    {
        Entry = entry;
        Tile = tile;
        ChooseCommand = new RelayCommand(() => onChoose(this));
    }

    /// <summary>Picks this share as the one to replace.</summary>
    public IRelayCommand ChooseCommand { get; }

    /// <summary>The share.</summary>
    public CommunityEntry Entry { get; }

    /// <summary>Its preview cell.</summary>
    public CommunityTile Tile { get; }

    /// <summary>Its slug.</summary>
    public string Slug => Entry.Slug;

    /// <summary>Its name.</summary>
    public string Name => Entry.Name;

    /// <summary>The Set's size.</summary>
    public string SizeText => Nfty.Core.Publish.SizeText.Of(Entry.Set.Size);

    /// <summary>When it was shared and last updated.</summary>
    public string DatesText => string.Create(CultureInfo.InvariantCulture,
        $"Shared {Entry.Created:d MMM yyyy} · updated {Entry.Updated:d MMM yyyy}");

    /// <summary>Whether it is the one picked to go.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <inheritdoc />
    public void Dispose() => Tile.Dispose();
}

/// <summary>The share card: what leaves the machine, what the gallery will say about it, and why Share is or is not ready.</summary>
/// <remarks>
/// <para>Everything slow runs off the UI thread and is observable (<see cref="PendingFacts"/>,
/// <see cref="PendingRepo"/>, <see cref="PendingBook"/>): packing the Set the way a share would and checking the result,
/// reading the user's <c>nfty-public</c>, and finding the CookBook by hash. While they run the limit lines say
/// "checking…" and Share says so; nothing ever blocks.</para>
/// <para>Every sentence is <see cref="ShareRules"/>' and the words are judged by
/// <see cref="CommunityShare.ListingProblem"/>, so this card cannot enable Share for something the run would refuse.</para>
/// </remarks>
public sealed partial class ShareDialogViewModel : ViewModelBase, IUnsavedWork, ICancellableDialog, IDisposable
{
    private const string CheckingSentence = "Checking your Set…";

    private readonly ShareSource _source;
    private readonly IDialogService _dialogs;
    private readonly IGitHubAccount _account;
    private readonly ICommunityCache _cache;
    private readonly IShareRepo _repo;
    private readonly IShareChecker _checker;
    private readonly ThumbnailGate _gate;
    private readonly IImageBridge _bridge;
    private readonly Func<Task>? _connect;
    private readonly Func<ShareRequest, ShareRunInfo, IProgress<ShareProgress>, CancellationToken, Task<ShareResult>>? _run;
    private readonly IUrlOpener _urls;
    private readonly IClipboardService? _clipboard;
    private readonly Func<IReadOnlyList<string>>? _tagsSource;
    private CancellationTokenSource? _runCts;

    private CancellationTokenSource? _factsCts;
    private CancellationTokenSource? _repoCts;
    private int _factsVersion, _repoVersion;
    private ShareCheckResult? _result;
    private RepoSnapshot? _snapshot;
    private RepoScan? _scan;
    private string? _repoNotice;
    private bool _repoLoaded;
    private IReadOnlyList<ShareShareRow> _shareRows = [];
    private bool _disposed;

    /// <summary>Opens the card for a Set.</summary>
    /// <param name="source">The Set.</param>
    /// <param name="dialogs">The modal layer; the confirmation before removing extras opens into it.</param>
    /// <param name="account">The GitHub connection.</param>
    /// <param name="cache">The community cache, whose cached list is what duplicates are checked against.</param>
    /// <param name="repo">The user's <c>nfty-public</c>.</param>
    /// <param name="checker">Packs and checks the Set.</param>
    /// <param name="gate">The decode limit for preview cells; null makes a small one.</param>
    /// <param name="bridge">Picture to bitmap conversion; null makes the standard one.</param>
    /// <param name="connect">Opens where GitHub is connected; null leaves Connect doing nothing.</param>
    /// <param name="run">Sends the share and reports its stages; returns the pull request. Null leaves Share disabled with <see cref="ShareRules.NotSetUp"/>.</param>
    /// <param name="tags">The gallery's fixed tag list (its <c>tags.json</c>), read when the card opens; null or empty offers none.</param>
    /// <param name="urls">Opens the pull request in the browser; null makes a recorder that opens nothing.</param>
    /// <param name="clipboard">Copies the pull request's address; null leaves Copy doing nothing.</param>
    /// <param name="refreshTags">Fetches the gallery's tag list; called on opening when <paramref name="tags"/> offers none. Null fetches nothing.</param>
    public ShareDialogViewModel(ShareSource source, IDialogService dialogs, IGitHubAccount account, ICommunityCache cache,
        IShareRepo repo, IShareChecker checker, ThumbnailGate? gate = null, IImageBridge? bridge = null,
        Func<Task>? connect = null,
        Func<ShareRequest, ShareRunInfo, IProgress<ShareProgress>, CancellationToken, Task<ShareResult>>? run = null,
        Func<IReadOnlyList<string>>? tags = null, IUrlOpener? urls = null, IClipboardService? clipboard = null,
        Func<CancellationToken, Task>? refreshTags = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentNullException.ThrowIfNull(checker);
        _source = source;
        _dialogs = dialogs;
        _account = account;
        _cache = cache;
        _repo = repo;
        _checker = checker;
        _gate = gate ?? new ThumbnailGate(2);
        _bridge = bridge ?? new ImageBridge();
        _connect = connect;
        _run = run;
        _urls = urls ?? new NoopUrlOpener();
        _clipboard = clipboard;

        SetHasModels = SetLayout.ModelFilesIn(source.SetDirectory).Count > 0;
        _models = SetHasModels;
        _name = source.Manifest.Name;
        _slug = ShareSlug.Of(source.Manifest.Name);
        _tagsSource = tags;
        _tagChips = ChipsOf(tags);
        LimitRows = [new ShareLimitRow(), new ShareLimitRow(), new ShareLimitRow(), new ShareLimitRow()];
        _account.Changed += OnAccountChanged;

        UpdatePlan();
        PendingBook = FindBookAsync();
        StartFacts();
        StartRepo();
        Recompute();
        if (TagChips.Count == 0 && refreshTags is not null) PendingTags = RefreshTagsAsync(refreshTags);
    }

    // ------------------------------------------------------------------------------------------ the Set

    /// <summary>Whether the Set carries 3D models at all.</summary>
    public bool SetHasModels { get; }

    /// <summary>Whether the 3D models checkbox can be ticked.</summary>
    public bool ModelsEnabled => SetHasModels;

    /// <summary>Why the models checkbox is dimmed, or empty.</summary>
    public string ModelsNote => SetHasModels ? "" : "none in this Set";

    /// <summary>Whether the share carries the Set's 3D models.</summary>
    [ObservableProperty] private bool _models;

    /// <summary>Whether the share carries the CookBook that made the Set. Only offered for the one that hashes right.</summary>
    [ObservableProperty] private bool _includeCookBook;

    /// <summary>The CookBook whose hash is the Set's recorded one, or null when none was found.</summary>
    public string? CookBookPath { get; private set; }

    /// <summary>Whether a matching CookBook was found, which is what enables its checkbox.</summary>
    public bool IncludeCookBookEnabled => CookBookPath is not null;

    /// <summary>What sits beside the CookBook checkbox: the book's name, or why there is none.</summary>
    public string CookBookNote => CookBookPath is { } p ? Path.GetFileName(p)
        : _source.Manifest.CookbookSha256 is null ? "this Set records none" : "no matching CookBook found";

    /// <summary>Finishes when the search for the CookBook has.</summary>
    public Task PendingBook { get; private set; } = Task.CompletedTask;

    private async Task FindBookAsync()
    {
        var recorded = _source.Manifest.CookbookSha256;
        var candidates = _source.BookCandidates.Concat(CookBookLocator.Nearby(_source.SetDirectory)).ToList();
        string? found = recorded is null ? null : await Task.Run(() => CookBookLocator.Find(recorded, candidates)).ConfigureAwait(true);
        if (_disposed) return;
        CookBookPath = found;
        OnPropertyChanged(nameof(CookBookPath));
        OnPropertyChanged(nameof(IncludeCookBookEnabled));
        OnPropertyChanged(nameof(CookBookNote));
    }

    partial void OnModelsChanged(bool value) => OptionsChanged();

    partial void OnIncludeCookBookChanged(bool value) => OptionsChanged();

    private void OptionsChanged()
    {
        UpdatePlan();
        StartFacts();
        Recompute();
    }

    // ------------------------------------------------------------------------------------------ the plan

    /// <summary>What the share carries, as <see cref="ExportPlan.Parts"/> words it.</summary>
    [ObservableProperty] private IReadOnlyList<string> _planParts = [];

    /// <summary>The plan's size, as <see cref="ExportPlan.SizeText"/> words it.</summary>
    [ObservableProperty] private string _planSize = "";

    /// <summary>The parts on one line, for the card.</summary>
    public string PlanText => string.Join(" · ", PlanParts);

    partial void OnPlanPartsChanged(IReadOnlyList<string> value) => OnPropertyChanged(nameof(PlanText));

    private bool _planFailed;

    private void UpdatePlan()
    {
        try
        {
            var plan = SetExporter.Plan(_source.SetDirectory, CommunityShare.Options(BuildRequest()),
                IncludeCookBook ? CookBookPath : null);
            PlanParts = plan.Parts();
            PlanSize = plan.SizeText();
            _modelsBytes = plan.Entries.Where(e => e.Name.StartsWith(SetLayout.ModelsDir + "/", StringComparison.Ordinal)).Sum(e => e.Bytes);
            _planFailed = false;
        }
        catch (Exception ex) when (ex is ArgumentException or CorruptSetException or IOException or UnauthorizedAccessException)
        {
            // The same plan the Export card makes; a Set it cannot plan is not one the checks can pass either.
            PlanParts = [];
            PlanSize = "";
            _modelsBytes = 0;
            _planFailed = true;
        }
    }

    private long _modelsBytes;

    // ------------------------------------------------------------------------------------------ the listing

    /// <summary>The listing's name, prefilled from the collection's.</summary>
    [ObservableProperty] private string _name;

    /// <summary>The listing's description.</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>The tags the gallery offers, with the ones in use marked.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTags))] private IReadOnlyList<ShareTagChip> _tagChips;

    /// <summary>Whether the gallery's tag list is being fetched (the card asks for it on opening when none is kept).</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TagsEmptyText))] private bool _tagsLoading;

    /// <summary>What stands in place of the chips when there are none: loading, or the empty-list sentence.</summary>
    public string TagsEmptyText => TagsLoading ? "Loading the gallery's tags…" : NoTagsText;

    /// <summary>Finishes when the card's own fetch of the tag list has.</summary>
    public Task PendingTags { get; private set; } = Task.CompletedTask;

    private async Task RefreshTagsAsync(Func<CancellationToken, Task> refresh)
    {
        TagsLoading = true;
        try { await refresh(CancellationToken.None).ConfigureAwait(true); }
        catch (Exception) { /* tags are a convenience; the empty sentence stands */ }
        if (_disposed) return;
        TagsLoading = false;
        if (TagChips.Count == 0) TagChips = ChipsOf(_tagsSource);
        Recompute();   // the fresh list is also what duplicates are checked against
    }

    private static List<ShareTagChip> ChipsOf(Func<IReadOnlyList<string>>? tags) =>
        (tags?.Invoke() ?? []).Distinct(StringComparer.Ordinal).Select(t => new ShareTagChip(t)).ToList();

    /// <summary>The tags in use.</summary>
    public IReadOnlyList<string> Tags => TagChips.Where(t => t.IsOn).Select(t => t.Name).ToList();

    /// <summary>The tags line's counter, <c>2 of 5</c>.</summary>
    public string TagsCount => string.Create(CultureInfo.InvariantCulture, $"{Tags.Count} of {ShareRules.MaxTags}");

    [ObservableProperty] private string _slug;

    partial void OnNameChanged(string value) => Recompute();

    partial void OnDescriptionChanged(string value) => Recompute();

    /// <summary>Whether the gallery has any tags to offer yet.</summary>
    public bool HasTags => TagChips.Count > 0;

    /// <summary>The reserved line under the chips: the empty-list sentence, or why a click was refused, else empty.</summary>
    [ObservableProperty] private string _tagNote = "";

    /// <summary>The shares line while signed out: the count lives on GitHub, so it can't be known yet.</summary>
    public const string SignedOutSharesText = "Shares: connect GitHub to see yours";

    /// <summary>The sentence shown in place of the chips when the list is empty.</summary>
    public const string NoTagsText = "No tags available yet.";

    [RelayCommand]
    private void ToggleTag(ShareTagChip chip)
    {
        if (!chip.IsOn && Tags.Count >= ShareRules.MaxTags)
        {
            TagNote = ShareRules.TooManyTags;
            return;
        }
        TagNote = "";
        chip.IsOn = !chip.IsOn;
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(TagsCount));
        Recompute();
    }

    // ------------------------------------------------------------------------------------------ the account

    /// <summary>Whether GitHub is connected.</summary>
    public bool IsSignedIn => _account.IsConnected && _account.Login is not null;

    /// <summary>Whether the card should offer Connect GitHub… rather than the connected chip.</summary>
    public bool IsSignedOut => !IsSignedIn;

    /// <summary>The chip: <c>Connected as @someone</c>.</summary>
    public string GitHubChip => _account.Login is { } login ? $"Connected as @{login}" : "Not connected";

    /// <summary>Finishes when the connection (the card's own Connect GitHub… press) has been handed on.</summary>
    public Task PendingConnect { get; private set; } = Task.CompletedTask;

    [RelayCommand]
    private Task ConnectGitHub() => PendingConnect = _connect is null ? Task.CompletedTask : _connect();

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsSignedOut));
        OnPropertyChanged(nameof(GitHubChip));
        StartRepo();
        Recompute();
    }

    // ------------------------------------------------------------------------------------------ the checks

    /// <summary>Finishes when the pack-and-check of the Set has.</summary>
    public Task PendingFacts { get; private set; } = Task.CompletedTask;

    /// <summary>Finishes when the read of the user's <c>nfty-public</c> has.</summary>
    public Task PendingRepo { get; private set; } = Task.CompletedTask;

    /// <summary>Whether the Set is still being packed and checked.</summary>
    public bool FactsPending { get; private set; }

    /// <summary>Whether the repository is still being read.</summary>
    public bool RepoPending { get; private set; }

    private void StartFacts()
    {
        _factsCts?.Cancel();
        _factsCts?.Dispose();
        _factsCts = new CancellationTokenSource();
        int version = ++_factsVersion;
        FactsPending = true;
        var options = CommunityShare.Options(BuildRequest());
        string? book = IncludeCookBook ? CookBookPath : null;
        PendingFacts = RunFactsAsync(options, book, version, _factsCts.Token);
    }

    private async Task RunFactsAsync(ExportOptions options, string? book, int version, CancellationToken ct)
    {
        ShareCheckResult result;
        try { result = await _checker.CheckAsync(_source, options, book, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { return; }
        catch (Exception)
        {
            // Whatever went wrong inside the check, the card says one thing; the reader's words are for a terminal.
            result = new ShareCheckResult(new ShareFacts([ShareRules.NotASet], 0, 0, 0, false, false, null, "", "", ""), 0, 0, 0);
        }
        if (version != _factsVersion || _disposed) return;
        _result = result;
        FactsPending = false;
        if (result.Facts.Collection.Length > 0) Slug = ShareSlug.Of(result.Facts.Collection);
        Recompute();
    }

    private void StartRepo()
    {
        _repoCts?.Cancel();
        _repoCts?.Dispose();
        _repoCts = new CancellationTokenSource();
        int version = ++_repoVersion;
        _repoNotice = null;
        if (!IsSignedIn)
        {
            SetRepo(null, null, loaded: false);
            RepoPending = false;
            PendingRepo = Task.CompletedTask;
            return;
        }
        RepoPending = true;
        PendingRepo = RunRepoAsync(_account.Login!, version, _repoCts.Token);
    }

    private async Task RunRepoAsync(string login, int version, CancellationToken ct)
    {
        RepoSnapshot? snapshot = null;
        string? notice = null;
        try { snapshot = await _repo.FetchAsync(login, ct).ConfigureAwait(true); }
        catch (OperationCanceledException) { return; }
        catch (CommunityException ex) { notice = ErrorText.For(ex); }
        catch (Exception ex) { notice = ErrorText.For(ex); }
        if (version != _repoVersion || _disposed) return;
        RepoPending = false;
        if (notice is not null)
        {
            SetRepo(null, null, loaded: false);
            _repoNotice = notice;
        }
        else
        {
            SetRepo(snapshot, snapshot is null ? new RepoScan([], null) : PublicRepoScan.Scan(snapshot), loaded: true);
        }
        Recompute();
    }

    private void SetRepo(RepoSnapshot? snapshot, RepoScan? scan, bool loaded)
    {
        foreach (var row in _shareRows) row.Dispose();
        _snapshot = snapshot;
        _scan = scan;
        _repoLoaded = loaded;
        _shareRows = (snapshot?.Manifest?.Shares ?? [])
            .Select(e => new ShareShareRow(e, new CommunityTile(e, _cache, _gate, _bridge), r => ReplaceSlug = r.Slug)).ToList();
    }

    // ------------------------------------------------------------------------------------------ what the checks say

    /// <summary>Canvas, assets, size, shared: four fixed lines, always present.</summary>
    public IReadOnlyList<ShareLimitRow> LimitRows { get; }

    /// <summary>What the one reserved block holds.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsNoneBlock), nameof(IsExtrasBlock), nameof(IsDuplicateBlock),
        nameof(IsAlreadySharedBlock), nameof(IsReplaceBlock))]
    private ShareLowerBlock _lowerBlock;

    /// <summary>Whether the block is empty.</summary>
    public bool IsNoneBlock => LowerBlock == ShareLowerBlock.None;

    /// <summary>Whether the block lists what the scan found.</summary>
    public bool IsExtrasBlock => LowerBlock == ShareLowerBlock.Extras;

    /// <summary>Whether the block says the Set is already listed.</summary>
    public bool IsDuplicateBlock => LowerBlock == ShareLowerBlock.Duplicate;

    /// <summary>Whether the block shows the share this collection already is.</summary>
    public bool IsAlreadySharedBlock => LowerBlock == ShareLowerBlock.AlreadyShared;

    /// <summary>Whether the block asks which share to replace.</summary>
    public bool IsReplaceBlock => LowerBlock == ShareLowerBlock.Replace;

    /// <summary>The block's heading when nothing needs deciding: where the share goes, or that GitHub must be connected.</summary>
    [ObservableProperty] private string _readyHeading = "";

    /// <summary>The sentence under <see cref="ReadyHeading"/>.</summary>
    [ObservableProperty] private string _readyText = "";

    /// <summary>Whether the block draws the destination (signed in, repository read): the address, the share slots
    /// and the three steps a share takes. Otherwise it draws <see cref="ReadyHeading"/> and <see cref="ReadyText"/>.</summary>
    [ObservableProperty] private bool _showDestination;

    /// <summary>The repository the share goes to: <c>@you/nfty-public</c>.</summary>
    [ObservableProperty] private string _destinationText = "";

    /// <summary>Which share this is: <c>share 2 of 3</c>.</summary>
    [ObservableProperty] private string _slotText = "";

    /// <summary>One pip per share the gallery allows: used, this one, or free.</summary>
    [ObservableProperty] private IReadOnlyList<ShareSlotPip> _slotPips = [];

    /// <summary>The sentence at the head of the block, whichever it holds.</summary>
    [ObservableProperty] private string _blockHeading = "";

    /// <summary>What the scan found, one row each, when the block holds them.</summary>
    [ObservableProperty] private IReadOnlyList<ShareExtraRow> _extras = [];

    /// <summary>Whether any of the extras can be removed by nfty, which is what offers <b>Remove these…</b>.</summary>
    [ObservableProperty] private bool _canRemoveExtras;

    /// <summary>Why the Set can't be shared on account of what is already in the gallery, or null.</summary>
    [ObservableProperty] private string? _duplicateReason;

    /// <summary>The share this collection already is, or null.</summary>
    [ObservableProperty] private ShareShareRow? _alreadyShared;

    /// <summary>Whether sharing again replaces the version already shared. Off until the person says so.</summary>
    [ObservableProperty] private bool _replaceSharedVersion;

    /// <summary>The user's shares, offered as the one to replace when at the limit.</summary>
    [ObservableProperty] private IReadOnlyList<ShareShareRow> _replaceChoices = [];

    /// <summary>The slug of the share picked to go, or null.</summary>
    [ObservableProperty] private string? _replaceSlug;

    partial void OnReplaceSharedVersionChanged(bool value) => Recompute();

    partial void OnReplaceSlugChanged(string? value)
    {
        foreach (var row in ReplaceChoices) row.IsSelected = row.Slug == value;
        Recompute();
    }

    /// <summary>Why Share can't be pressed yet, in <see cref="ShareRules"/>' words, or empty when it can.</summary>
    [ObservableProperty] private string _shareReason = "";

    /// <summary>Whether Share can be pressed.</summary>
    public bool CanShare => ShareReason.Length == 0 && State == ShareState.Editing;

    private bool _recomputing;

    private void Recompute()
    {
        // Recompute sets two choices back to "none" when they no longer apply; those setters must not recurse.
        if (_disposed || _recomputing) return;
        _recomputing = true;
        try { RecomputeCore(); }
        finally { _recomputing = false; }
    }

    private void RecomputeCore()
    {
        var facts = _result?.Facts;
        bool factsIn = _result is not null && !FactsPending;
        int count = _source.Manifest.Count;
        var limitSentences = new List<string>();
        var duplicatesOfFacts = new List<string>();

        // ---- the four lines
        var canvas = LimitRows[0];
        var assets = LimitRows[1];
        var size = LimitRows[2];
        var shared = LimitRows[3];
        string max = string.Create(CultureInfo.InvariantCulture, $"{ShareRules.MaxCanvas} x {ShareRules.MaxCanvas}");
        if (!factsIn)
        {
            canvas.Text = "Canvas — checking…";
            size.Text = "Size — checking…";
            canvas.IsOver = size.IsOver = false;
            canvas.Tip = size.Tip = "";
        }
        else
        {
            int w = _result!.Width, h = _result.Height;
            if (w > 0 && h > 0)
            {
                bool over = w > ShareRules.MaxCanvas || h > ShareRules.MaxCanvas;
                canvas.IsOver = over;
                canvas.Text = string.Create(CultureInfo.InvariantCulture, $"Canvas {w:N0} x {h:N0} of {max}")
                    + (over ? " — cook at a smaller canvas" : "");
                canvas.Tip = over ? ShareRules.CanvasTooLarge(w, h) : "";
                if (over) { limitSentences.Add(canvas.Tip); duplicatesOfFacts.Add(canvas.Tip); }
            }
            else
            {
                canvas.IsOver = false;
                canvas.Text = "Canvas — can't be read";
                canvas.Tip = "";
            }

            long bytes = _result.Bytes;
            if (bytes > 0)
            {
                bool over = bytes > ShareRules.MaxSetBytes;
                size.IsOver = over;
                string of = $"Size {ShareRules.SizeWords(bytes)} of {ShareRules.SizeWords(ShareRules.MaxSetBytes)}";
                bool modelsFix = Models && SetHasModels && _modelsBytes > 0;
                size.Text = of + (over
                    ? modelsFix ? $" — leave out 3D models (−{ShareRules.SizeWords(_modelsBytes)})" : " — share fewer assets"
                    : "");
                size.Tip = over ? ShareRules.SetTooLarge(bytes, modelsFix ? _modelsBytes : 0) : "";
                if (over) { limitSentences.Add(size.Tip); duplicatesOfFacts.Add(ShareRules.SetTooLarge(bytes, 0)); }
            }
            else
            {
                size.IsOver = false;
                size.Text = "Size — unknown";
                size.Tip = "";
            }
        }

        bool assetsOver = count > ShareRules.MaxAssets;
        assets.IsOver = assetsOver;
        assets.Text = string.Create(CultureInfo.InvariantCulture, $"{count:N0} of {ShareRules.MaxAssets:N0} assets")
            + (assetsOver ? " — cook fewer" : "");
        assets.Tip = assetsOver ? ShareRules.TooManyAssets(count) : "";
        if (assetsOver) { limitSentences.Insert(0, assets.Tip); duplicatesOfFacts.Add(assets.Tip); }

        // ---- the repository, as far as it is known
        var shares = _snapshot?.Manifest?.Shares ?? [];
        string slug = Slug;
        var same = _repoLoaded ? shares.FirstOrDefault(s => s.Slug == slug) : null;
        bool atLimit = _repoLoaded && same is null && shares.Count >= ShareRules.MaxShares;
        if (!IsSignedIn)
        {
            shared.Text = SignedOutSharesText;
            shared.IsOver = false;
            shared.Tip = ShareRules.NotConnected;
        }
        else if (!_repoLoaded)
        {
            shared.Text = RepoPending ? "Shared — checking…" : "Shared — can't be read";
            shared.IsOver = false;
            shared.Tip = _repoNotice ?? "";
        }
        else
        {
            shared.Text = string.Create(CultureInfo.InvariantCulture, $"{shares.Count} of {ShareRules.MaxShares} shared")
                + (atLimit && ReplaceSlug is null ? " — pick one to replace" : "");
            shared.IsOver = atLimit && ReplaceSlug is null;
            shared.Tip = atLimit ? ShareRules.AtShareLimit : "";
        }

        // ---- the same four facts as label, figure and limit, for the card's table. What is wrong and how to fix it is
        // the row's Tip, shown on hover over its warning mark, so the row never clips a sentence.
        string maxShort = string.Create(CultureInfo.InvariantCulture, $"{ShareRules.MaxCanvas} × {ShareRules.MaxCanvas}");
        if (!factsIn) canvas.Show("CANVAS", "checking…", "max " + maxShort, false, true);
        else if (_result!.Width > 0 && _result.Height > 0)
            canvas.Show("CANVAS", string.Create(CultureInfo.InvariantCulture, $"{_result.Width:N0} × {_result.Height:N0}"),
                "max " + maxShort, canvas.IsOver, false);
        else canvas.Show("CANVAS", "can't be read", "max " + maxShort, false, true);

        assets.Show("ASSETS", string.Create(CultureInfo.InvariantCulture, $"{count:N0}"),
            string.Create(CultureInfo.InvariantCulture, $"max {ShareRules.MaxAssets:N0}"), assetsOver, false);

        string maxSize = "max " + ShareRules.SizeWords(ShareRules.MaxSetBytes);
        if (!factsIn) size.Show("PACKED SIZE", "checking…", maxSize, false, true);
        else if (_result!.Bytes > 0) size.Show("PACKED SIZE", ShareRules.SizeWords(_result.Bytes), maxSize, size.IsOver, false);
        else size.Show("PACKED SIZE", "unknown", maxSize, false, true);

        string maxShares = string.Create(CultureInfo.InvariantCulture, $"max {ShareRules.MaxShares}");
        if (!IsSignedIn) shared.Show("YOUR SHARES", "connect GitHub to see", maxShares, false, true);
        else if (!_repoLoaded) shared.Show("YOUR SHARES", RepoPending ? "checking…" : "can't be read", maxShares, false, true);
        else shared.Show("YOUR SHARES", string.Create(CultureInfo.InvariantCulture, $"{shares.Count} in nfty-public"), maxShares,
            shared.IsOver, false);

        // ---- the duplicate check: against the cached list, once the Set's fingerprint is known
        string? duplicate = null;
        if (factsIn && facts is { Problems.Count: 0 })
        {
            long ownerId = _account.UserId
                ?? shares.FirstOrDefault()?.Owner.Id
                ?? _cache.Cached?.Index?.Entries.FirstOrDefault(e => string.Equals(e.Owner.Login, _account.Login, StringComparison.OrdinalIgnoreCase))?.Owner.Id
                ?? -1;
            var now = DateTimeOffset.UtcNow;
            var candidate = new CommunityEntry(CommunitySchema.Current, slug, new CommunityOwner(ownerId, _account.Login ?? ""), "", "",
                Name, facts.Collection, "", [], new CommunityCanvas(facts.Width, facts.Height), facts.Assets, facts.Models,
                facts.IncludesCookBook, facts.CookbookSha256, new CommunityFile("", _result!.Bytes, facts.FileSha256),
                new CommunityFile("", 0, ""), facts.Fingerprint, "", now, now);
            duplicate = CommunityDuplicates.Problem(candidate, _cache.Cached?.Index?.Entries ?? []);
            if (duplicate is null && same is not null && !string.Equals(same.Collection, facts.Collection, StringComparison.Ordinal))
                duplicate = ShareRules.SlugTaken(same.Name);
        }
        DuplicateReason = duplicate;

        // ---- the one reserved block
        var extras = _scan?.Extras ?? [];
        string? blocker = _scan?.Blocker;
        string? repoProblem = _repoNotice ?? blocker ?? (extras.Count > 0 ? ShareRules.RepoExtras : null);
        ShareLowerBlock block;
        if (repoProblem is not null) block = ShareLowerBlock.Extras;
        else if (duplicate is not null) block = ShareLowerBlock.Duplicate;
        else if (same is not null) block = ShareLowerBlock.AlreadyShared;
        else if (atLimit) block = ShareLowerBlock.Replace;
        else block = ShareLowerBlock.None;

        Extras = repoProblem is null ? [] : extras.Select(RowOf).ToList();
        CanRemoveExtras = _repoNotice is null && blocker is null && extras.Any(e => e.Removable);
        AlreadyShared = same is null ? null : _shareRows.FirstOrDefault(r => r.Slug == same.Slug);
        ReplaceChoices = atLimit ? _shareRows : [];
        foreach (var row in _shareRows) row.IsSelected = row.Slug == ReplaceSlug;
        if (!atLimit && ReplaceSlug is not null) ReplaceSlug = null;
        if (same is null && ReplaceSharedVersion) ReplaceSharedVersion = false;
        BlockHeading = block switch
        {
            ShareLowerBlock.Extras => repoProblem ?? "",
            ShareLowerBlock.Duplicate => duplicate ?? "",
            ShareLowerBlock.AlreadyShared => ShareRules.AlreadySharedThis,
            ShareLowerBlock.Replace => ShareRules.ReplaceOneToShare,
            _ => "",
        };
        LowerBlock = block;
        if (!IsSignedIn)
        {
            ReadyHeading = "Sharing needs a GitHub account";
            ReadyText = "Your Set goes to a public repository named nfty-public on your account; the gallery lists it once its checks pass. nfty never sees your password.";
        }
        else if (!_repoLoaded)
        {
            ReadyHeading = RepoPending ? $"Reading @{_account.Login}/nfty-public…" : $"@{_account.Login}/nfty-public can't be read";
            ReadyText = RepoPending ? "The share count, and anything that doesn't belong there, are checked first." : _repoNotice ?? "";
        }
        else
        {
            ReadyHeading = $"Goes to @{_account.Login}/nfty-public";
            ReadyText = string.Create(CultureInfo.InvariantCulture,
                $"As share {shares.Count + 1} of {ShareRules.MaxShares}. The gallery checks it and usually lists it within a few minutes.");
        }
        ShowDestination = IsSignedIn && _repoLoaded;
        if (ShowDestination)
        {
            int used = shares.Count, next = Math.Min(used + 1, ShareRules.MaxShares);
            DestinationText = $"@{_account.Login}/nfty-public";
            SlotText = string.Create(CultureInfo.InvariantCulture, $"share {next} of {ShareRules.MaxShares}");
            SlotPips = [.. Enumerable.Range(0, ShareRules.MaxShares).Select(i =>
                new ShareSlotPip(i == next - 1 ? "this" : i < used ? "used" : "free"))];
        }

        // ---- why Share can't be pressed, first failing reason in the order the card promises
        string reason = "";
        var setProblem = facts?.Problems.FirstOrDefault(p => !duplicatesOfFacts.Contains(p));
        if (!IsSignedIn) reason = ShareRules.NotConnected;
        else if (FactsPending || RepoPending || _result is null) reason = CheckingSentence;
        else if (repoProblem is not null) reason = repoProblem;
        else if (setProblem is not null) reason = setProblem;
        else if (_planFailed) reason = ShareRules.NotASet;
        else if (limitSentences.Count > 0) reason = limitSentences[0];
        else if (duplicate is not null) reason = duplicate;
        else if (same is not null && !ReplaceSharedVersion) reason = ShareRules.ReplaceNotChosen;
        else if (atLimit && ReplaceSlug is null) reason = ShareRules.AtShareLimit;
        else if (CommunityShare.ListingProblem(Name, Description, Tags) is { } words) reason = words;
        else if (_run is null) reason = ShareRules.NotSetUp;
        ShareReason = reason;
        OnPropertyChanged(nameof(CanShare));
        ShareCommand.NotifyCanExecuteChanged();
        RemoveExtrasCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShareButtonTip));
    }

    private static ShareExtraRow RowOf(RepoExtra e)
    {
        if (!e.Removable) return new ShareExtraRow(ShareRules.RepoSubmodule(e.Name), false);
        string kind = e.Kind switch
        {
            RepoExtraKind.File => "file",
            RepoExtraKind.Branch => "branch",
            RepoExtraKind.Release => "release",
            RepoExtraKind.Asset => "release file",
            _ => "tag",
        };
        return new ShareExtraRow(e.Size is { } size ? $"{kind}  {e.Name}  ·  {SizeText.Of(size)}" : $"{kind}  {e.Name}", true);
    }

    /// <summary>The tooltip on Share: the reason it waits, or what it does.</summary>
    public string ShareButtonTip => ShareReason.Length > 0 ? ShareReason : "Send this Set to the community gallery.";

    // ------------------------------------------------------------------------------------------ remove the extras

    /// <summary>Finishes when a removal (and the rescan after it) has.</summary>
    public Task PendingRemoval { get; private set; } = Task.CompletedTask;

    private bool CanRemove() => CanRemoveExtras && IsSignedIn && State == ShareState.Editing;

    partial void OnCanRemoveExtrasChanged(bool value) => RemoveExtrasCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task RemoveExtras() => PendingRemoval = RemoveExtrasCoreAsync();

    private async Task RemoveExtrasCoreAsync()
    {
        var removable = (_scan?.Extras ?? []).Where(e => e.Removable).ToList();
        if (removable.Count == 0 || _account.Login is not { } login) return;

        var names = string.Join("\n", removable.Select(e => "• " + RowOf(e).Text.Replace("  ", " ")));
        var confirm = new ConfirmDialogViewModel(_dialogs, "Remove these from your nfty-public?",
            names + "\n\nThis can't be undone from nfty.", "Remove");
        // Nothing is ever deleted without this click.
        if (await _dialogs.ShowAsync<bool>(confirm).ConfigureAwait(true) != true) return;

        try { await _repo.RemoveAsync(login, removable, CancellationToken.None).ConfigureAwait(true); }
        catch (CommunityException ex) { _repoNotice = ErrorText.For(ex); }
        catch (Exception ex) { _repoNotice = ErrorText.For(ex); }

        // Whatever happened, what is on GitHub now is what the card shows.
        StartRepo();
        Recompute();
        await PendingRepo.ConfigureAwait(true);
    }

    // ------------------------------------------------------------------------------------------ the share

    /// <summary>Where the card is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShare), nameof(IsEditing), nameof(IsRunning), nameof(IsDone), nameof(IsFailed),
        nameof(CancelLabel), nameof(IsBusy))]
    private ShareState _state = ShareState.Editing;

    /// <summary>What went wrong, in product words, when <see cref="State"/> is <see cref="ShareState.Failed"/>.</summary>
    [ObservableProperty] private string _failure = "";

    /// <summary>Whether the form is showing.</summary>
    public bool IsEditing => State == ShareState.Editing;

    /// <summary>Whether a share is being sent.</summary>
    public bool IsRunning => State == ShareState.Running;

    /// <summary>Whether the share was sent.</summary>
    public bool IsDone => State == ShareState.Done;

    /// <summary>Whether the share could not be sent.</summary>
    public bool IsFailed => State == ShareState.Failed;

    /// <summary>What the running share is doing, in words.</summary>
    [ObservableProperty] private string _stageText = "";

    /// <summary>How far the whole run has got, 0..1: one bar across every stage.</summary>
    [ObservableProperty] private double _fraction;

    /// <summary>The sentence the done state opens with.</summary>
    public const string DoneText = "Submitted. The gallery checks it now; it usually lists within a few minutes.";

    /// <summary>The gallery pull request the share opened, or null before there is one.</summary>
    [ObservableProperty] private Uri? _pullUrl;

    /// <summary>The pull request's address as text, for the done state's link.</summary>
    public string PullText => PullUrl?.ToString() ?? "";

    partial void OnPullUrlChanged(Uri? value) => OnPropertyChanged(nameof(PullText));

    /// <summary>The label of the footer's left button: it stops a running share and closes the card otherwise.</summary>
    public string CancelLabel => State switch
    {
        ShareState.Running => "Stop",
        ShareState.Done or ShareState.Failed => "Close",
        _ => "Cancel",
    };

    partial void OnStateChanged(ShareState value)
    {
        ShareCommand.NotifyCanExecuteChanged();
        RemoveExtrasCommand.NotifyCanExecuteChanged();
        TryAgainCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The words for a stage, as the card's one progress line says them.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>The words.</returns>
    public static string StageWords(ShareStage stage) => stage switch
    {
        ShareStage.Scanning => "Reading your nfty-public…",
        ShareStage.Exporting => "Packing the Set…",
        ShareStage.Checking => "Checking the pack…",
        ShareStage.Preview => "Making the preview…",
        ShareStage.Uploading => "Uploading the files…",
        ShareStage.Listing => "Writing your listing…",
        _ => "Opening the pull request…",
    };

    /// <summary>How far the whole run is: the stages share one bar equally, so it only ever moves forward.</summary>
    private static double Overall(ShareProgress p)
    {
        var stages = Enum.GetValues<ShareStage>();
        return (Array.IndexOf(stages, p.Stage) + Math.Clamp(p.Fraction, 0, 1)) / stages.Length;
    }

    /// <summary>The request the card's fields describe.</summary>
    /// <returns>The request the share run takes.</returns>
    public ShareRequest BuildRequest() => new(
        _source.SetDirectory, IncludeCookBook && CookBookPath is not null, IncludeCookBook ? CookBookPath : null,
        Models && SetHasModels, Name?.Trim() ?? "", Description?.Trim() ?? "", Tags, ReplaceSlug, ReplaceSharedVersion);

    /// <summary>Finishes when a share has.</summary>
    public Task PendingShare { get; private set; } = Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanShare))]
    private Task Share() => PendingShare = ShareCoreAsync();

    private async Task ShareCoreAsync()
    {
        if (_run is null || !CanShare) return;
        var request = BuildRequest();
        var info = new ShareRunInfo(request.Name, _source.Manifest.Count, _result?.Width ?? 0, _result?.Height ?? 0,
            request.Models, request.IncludeCookBook, _result?.Bytes ?? 0, AlreadyShared is not null);

        var cts = _runCts = new CancellationTokenSource();
        Failure = "";
        Fraction = 0;
        StageText = StageWords(ShareStage.Scanning);
        PullUrl = null;
        State = ShareState.Running;
        var progress = new Progress<ShareProgress>(p =>
        {
            // A late report from a run that has ended is not news.
            if (!ReferenceEquals(_runCts, cts) || State != ShareState.Running) return;
            StageText = StageWords(p.Stage);
            Fraction = Math.Max(Fraction, Overall(p));
        });
        try
        {
            var result = await _run(request, info, progress, cts.Token).ConfigureAwait(true);
            PullUrl = result.PullUrl;
            Fraction = 1;
            State = ShareState.Done;
        }
        catch (OperationCanceledException) { State = ShareState.Editing; }
        catch (CommunityException ex)
        {
            Failure = ErrorText.For(ex);          // a ShareRules sentence, shown verbatim
            State = ShareState.Failed;
        }
        catch (GitHubAuthException ex)
        {
            Failure = ErrorText.For(ex);
            State = ShareState.Failed;
        }
        catch (Exception ex)
        {
            Failure = ErrorText.For(ex);
            State = ShareState.Failed;
        }
        finally
        {
            if (ReferenceEquals(_runCts, cts)) _runCts = null;
            cts.Dispose();
        }
    }

    /// <summary>Goes back to the form after a failure, with everything as it was typed.</summary>
    [RelayCommand(CanExecute = nameof(IsFailed))]
    private void TryAgain()
    {
        State = ShareState.Editing;
        Recompute();
    }

    /// <summary>Copies the pull request's address.</summary>
    [RelayCommand]
    private async Task CopyPull()
    {
        if (PullUrl is { } url && _clipboard is not null) await _clipboard.SetTextAsync(url.ToString()).ConfigureAwait(true);
    }

    /// <summary>Opens the pull request in the browser.</summary>
    [RelayCommand]
    private void OpenPull()
    {
        if (PullUrl is { Scheme: "https" } url) _urls.Open(url);
    }

    /// <summary>Stops a running share (which takes back what it made), or closes the card.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (State == ShareState.Running)
        {
            StageText = "Stopping…";
            _runCts?.Cancel();
            return;
        }
        _dialogs.Close(this, null);
    }

    // ------------------------------------------------------------------------------------------ closing

    /// <inheritdoc />
    public bool HasUnsavedWork => false;

    /// <inheritdoc />
    public bool IsBusy => State == ShareState.Running;

    /// <inheritdoc />
    public async Task CancelAndWaitAsync()
    {
        _runCts?.Cancel();
        try { await PendingShare.ConfigureAwait(true); }
        catch (Exception) { /* the run reports its own end through the state */ }
    }

    /// <inheritdoc />
    public Task<bool> ConfirmLeaveAsync() => Task.FromResult(!IsBusy);

    /// <summary>Stops the checks, lets go of the account and the preview cells.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _account.Changed -= OnAccountChanged;
        _factsCts?.Cancel();
        _repoCts?.Cancel();
        _runCts?.Cancel();
        foreach (var row in _shareRows) row.Dispose();
    }
}

/// <summary>One share slot in the destination meter.</summary>
/// <param name="State"><c>used</c>, <c>this</c> (the share being made) or <c>free</c>.</param>
public sealed record ShareSlotPip(string State)
{
    /// <summary>A slot an existing share holds.</summary>
    public bool IsUsed => State == "used";

    /// <summary>The slot this share takes.</summary>
    public bool IsThis => State == "this";
}
