using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Globalization;
using Nfty.App.Converters;
using Nfty.App.Services;
using Nfty.Core.Diagnostics;
using Nfty.Core.Output;
using Nfty.Core.Publish;

namespace Nfty.App.ViewModels;

/// <summary>One grid tile's data plus its own selection flag, so the view can paint a selected-tile
/// indicator (accent border/wash) via a bound "sel" style class rather than relying solely on the
/// detail rail to show what's selected.</summary>
public partial class SetItemRow : ObservableObject, IDisposable
{
    private const int ThumbW = 128;
    private volatile Bitmap? _thumbnail;
    private bool _decodeStarted;
    private bool _disposed;

    /// <summary>The asset's PNG on disk. The grid shows a 128px thumbnail of it; the inspector and
    /// Save both need the file itself.</summary>
    public string ImagePath { get; }

    /// <summary>The asset's set number.</summary>
    public int Number { get; }
    /// <summary>Its metadata.</summary>
    public SetItem Item { get; }

    /// <summary>
    /// The tile image: null until it has been decoded, then the decoded bitmap.
    ///
    /// <para>Lazy because the ViewModel used to decode every thumbnail in its constructor, on the UI
    /// thread: 627 ms for 900 assets, which extrapolates to roughly seven seconds of frozen window
    /// for a 10,000-asset Set. The ListBox below it virtualizes, but virtualization only limits what
    /// is <em>rendered</em>; it could do nothing about work already done up front. Deferring the
    /// decode puts it back under the virtualizer, so only realized rows pay for it.</para>
    ///
    /// <para><b>And now the decode happens off the UI thread.</b> Reading this property starts one
    /// if it has not started, and returns null meanwhile — which is what the tile's placeholder is
    /// for. That matters at scale rather than in the demo: a thumbnail costs ~0.5 ms from a 64px
    /// source and <b>7.1 ms from a 1000px one</b>, so a screenful of forty large tiles was ~280 ms
    /// of frozen UI every time you scrolled into fresh rows. Decoded on the thread pool the same
    /// forty take about 27 ms and the UI thread never stops.</para>
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            BeginDecode();
            return _thumbnail;
        }
    }

    /// <summary>Whether the image is still on its way, so the tile should show its placeholder.</summary>
    public bool IsLoading => _thumbnail is null;

    /// <summary>Whether this row has actually paid for its image yet. Read by the performance tests
    /// to prove the decode is still falling under the ListBox's virtualization rather than on top
    /// of it.</summary>
    internal bool IsThumbnailDecoded => _thumbnail is not null;

    /// <summary>
    /// Starts this row's decode, once.
    /// </summary>
    /// <remarks>
    /// The guard is a plain bool because every caller is the UI thread — a binding reading
    /// <see cref="Thumbnail"/> during measure. Only the decode itself leaves that thread, and the
    /// result comes back to it before anything is published, so nothing here needs a lock.
    /// </remarks>
    private void BeginDecode()
    {
        if (_decodeStarted) return;
        _decodeStarted = true;

        var path = ImagePath;
        _ = Task.Run(() =>
        {
            var bitmap = Decode(path);
            // Back to the UI thread through the dispatcher rather than a captured
            // SynchronizationContext: a binding may read Thumbnail from a measure pass that has no
            // context to capture, and FromCurrentSynchronizationContext throws outright when there
            // is none. Post always has somewhere to land.
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) { bitmap.Dispose(); return; }   // torn down while it was decoding
                _thumbnail = bitmap;
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(IsLoading));
            });
        });
    }

    /// <summary>
    /// Decodes this row's thumbnail synchronously, for callers that cannot wait for a frame.
    /// </summary>
    /// <returns>The decoded bitmap, which this row then owns and caches.</returns>
    /// <remarks>Used by the tests, which have no dispatcher to marshal a continuation back to.</remarks>
    internal Bitmap DecodeNow()
    {
        _decodeStarted = true;
        return _thumbnail ??= Decode(ImagePath);
    }

    /// <summary>Whether this tile is the selected one, so the grid can paint an indicator — the
    /// detail rail alone does not show which tile is selected once there are many rows.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Whether the pointer is over this tile, which is the asset the detail rail is describing.
    /// </summary>
    /// <remarks>
    /// Fluent's own <c>:pointerover</c> would wash the cell without this, and it did — but the wash
    /// says "the pointer is here", not "the rail is about this one", and with the rail now following
    /// the pointer the two need to be the same statement. It is a ring on the tile, kept clearly
    /// weaker than the selection's filled ground: hovering is a glance and selecting is a decision.
    /// </remarks>
    [ObservableProperty]
    private bool _isHovered;

    /// <summary>Creates a row over one Set item.</summary>
    /// <param name="number">The asset's set number.</param>
    /// <param name="imagePath">Path to its PNG; not opened until <see cref="Thumbnail"/> is read.</param>
    /// <param name="item">The item's metadata.</param>
    public SetItemRow(int number, string imagePath, SetItem item)
    {
        Number = number;
        ImagePath = imagePath;
        Item = item;
    }

    private static Bitmap Decode(string path)
    {
        // Named so a scroll's cost splits into "decoding images" and "building controls", which are
        // two different problems with two different fixes.
        using var _ = Perf.Measure("SetItemRow.Decode");
        try
        {
            using var fs = File.OpenRead(path);
            // Never decode LARGER than the source. DecodeToWidth(128) on a 64px asset upscales it,
            // so a 500-tile Set of 64x64 art held four times the pixels it had any use for -- 32 MB
            // of bitmap for 8 MB of image. Downscaling a big asset is still the point; growing a
            // small one never was.
            var w = Math.Min(ThumbW, PngWidth(fs));
            fs.Position = 0;
            return Bitmap.DecodeToWidth(fs, w);
        }
        catch
        {
            // Tolerant placeholder: 1x1 transparent bitmap if the image is missing or corrupt. A
            // browser over a damaged Set should show the damage, not refuse to open.
            return new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        }
    }

    /// <summary>
    /// A PNG's pixel width, read from its header.
    /// </summary>
    /// <param name="fs">The open file, positioned at its start. Left wherever the read ended.</param>
    /// <returns>The width, or <see cref="ThumbW"/> for anything that is not a PNG this can read —
    /// which makes the caller's Min a no-op and restores the previous behavior exactly.</returns>
    /// <remarks>IHDR is fixed at bytes 16..19, big-endian, immediately after the 8-byte signature and
    /// the chunk's own length and type. Eight bytes off the front of a file the decoder is about to
    /// read anyway; cheaper than decoding and measuring.</remarks>
    private static int PngWidth(Stream fs)
    {
        Span<byte> head = stackalloc byte[24];
        if (fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) < head.Length) return ThumbW;
        if (head[0] != 0x89 || head[1] != 'P' || head[2] != 'N' || head[3] != 'G') return ThumbW;
        var w = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(head[16..20]);
        return w > 0 ? w : ThumbW;
    }

    /// <summary>Frees the thumbnail if one was ever decoded.</summary>
    /// <remarks>A decode already in flight is not canceled — it is cheap and nearly done — but its
    /// result is dropped rather than published, so nothing leaks and nothing resurrects a disposed
    /// row.</remarks>
    public void Dispose()
    {
        _disposed = true;
        _thumbnail?.Dispose();
        _thumbnail = null;
    }
}

/// <summary>Read-only browsing surface over a cooked Set: the collection header (name/count/seed)
/// plus per-item rows with a decoded thumbnail, and the currently selected item's detail
/// projections. Owns the decoded thumbnails and the underlying LoadedSet.</summary>
public partial class SetBrowserViewModel : ViewModelBase, IDisposable
{
    private readonly LoadedSet _set;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly IFolderRevealer _revealer;

    /// <summary>CookBook paths the app can name - the open book, recent ones - for the export
    /// dialog to check against what the Set recorded. A Func because it is read when Export opens,
    /// not when the browser does: the open book can change in between.</summary>
    private readonly Func<IEnumerable<string>>? _bookCandidates;

    /// <summary>The collection's name.</summary>
    public string Name { get; }
    /// <summary>How many assets the Set holds.</summary>
    public int Count { get; }
    /// <summary>The seed that produced it.</summary>
    public string Seed { get; }
    /// <summary>One row per asset.</summary>
    public IReadOnlyList<SetItemRow> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    [NotifyPropertyChangedFor(nameof(IsShowingHover))]
    [NotifyPropertyChangedFor(nameof(ShownDna))]
    [NotifyPropertyChangedFor(nameof(ShownRecipe))]
    [NotifyPropertyChangedFor(nameof(ShownRarity))]
    [NotifyPropertyChangedFor(nameof(ShownNumber))]
    [NotifyPropertyChangedFor(nameof(ShownDnaTop))]
    [NotifyPropertyChangedFor(nameof(ShownDnaBottom))]
    [NotifyPropertyChangedFor(nameof(RarestText))]
    [NotifyPropertyChangedFor(nameof(CombinedText))]
    private SetItemRow? _selectedItem;

    /// <summary>
    /// The asset the pointer is over, or null.
    /// </summary>
    /// <remarks>
    /// <para><b>The rail describes whatever is under the pointer, and falls back to the selection.</b>
    /// A grid of 500 tiles is scanned rather than read, and clicking each one to find out what it is
    /// meant opening the inspector over the very panel that answers the question - so the rail only
    /// ever appeared to update once the modal was closed again.</para>
    /// <para>Hover is not selection: it is gone the moment the pointer moves off, and it is not what
    /// Save or the inspector act on unless it is also the selection. Moving off the grid at all
    /// clears it, so the rail cannot keep describing an asset the pointer left behind.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Shown))]
    [NotifyPropertyChangedFor(nameof(IsShowingHover))]
    [NotifyPropertyChangedFor(nameof(ShownDna))]
    [NotifyPropertyChangedFor(nameof(ShownRecipe))]
    [NotifyPropertyChangedFor(nameof(ShownRarity))]
    [NotifyPropertyChangedFor(nameof(ShownNumber))]
    [NotifyPropertyChangedFor(nameof(ShownDnaTop))]
    [NotifyPropertyChangedFor(nameof(ShownDnaBottom))]
    [NotifyPropertyChangedFor(nameof(RarestText))]
    [NotifyPropertyChangedFor(nameof(CombinedText))]
    private SetItemRow? _hoveredItem;

    /// <summary>The asset the detail rail is describing: what the pointer is over, or failing that,
    /// what is selected.</summary>
    public SetItemRow? Shown => HoveredItem ?? SelectedItem;

    /// <summary>
    /// Whether the rail is describing a hovered asset rather than the selected one.
    /// </summary>
    /// <remarks>
    /// The rail says so, because otherwise the panel changes under a pointer that is nowhere near it
    /// and the reader has no way to tell which asset Save would act on. Its line is RESERVED rather
    /// than revealed - the geometry rule - so nothing in the rail moves as the pointer crosses the
    /// grid, which would be the worst possible place for a reflow.
    /// </remarks>
    public bool IsShowingHover => HoveredItem is not null && !ReferenceEquals(HoveredItem, SelectedItem);

    /// <summary>
    /// Points the rail at an asset because the pointer is over it.
    /// </summary>
    /// <param name="row">The asset under the pointer, or null when the pointer is not over one.</param>
    public void Hover(SetItemRow? row)
    {
        if (ReferenceEquals(row, HoveredItem)) return;      // every pointer move would raise otherwise
        if (HoveredItem is not null) HoveredItem.IsHovered = false;
        HoveredItem = row;
        if (row is not null) row.IsHovered = true;
    }

    /// <summary>
    /// Makes an asset the selection without opening anything.
    /// </summary>
    /// <remarks>
    /// What the RIGHT button does on a tile. Left-click means "show me this bigger", which is the
    /// common intent on a grid of pictures and is why it opens the inspector; but keeping an asset
    /// in the rail to read its rarity against another one had no gesture at all short of opening the
    /// modal and closing it again.
    /// </remarks>
    /// <param name="row">The asset to select.</param>
    [RelayCommand]
    public void Select(SetItemRow? row)
    {
        if (row is null) return;
        SelectedItem = row;
    }

    /// <summary>Opens a cooked Set for browsing.</summary>
    /// <param name="set">The loaded Set; this takes ownership and disposes it.</param>
    /// <param name="picker">Where Save asks for a destination. Defaults to the null picker, which
    /// reports "canceled" — the same thing every other surface does without a window.</param>
    /// <param name="dialogs">The modal layer the inspector opens into.</param>
    /// <param name="status">Where a save result is reported.</param>
    /// <param name="revealer">Opens the folder an export landed in.</param>
    /// <param name="bookCandidates">CookBook paths the app can name - the open book, recent ones -
    /// for the export dialog to check against the hash this Set recorded. Read when Export opens
    /// rather than when the browser does, since the open book can change in between.</param>
    public SetBrowserViewModel(LoadedSet set, IFilePickerService? picker = null,
        IDialogService? dialogs = null, IStatusService? status = null,
        IFolderRevealer? revealer = null, Func<IEnumerable<string>>? bookCandidates = null)
    {
        _bookCandidates = bookCandidates;
        RaritySort = new TableSort("Trait", () => OnPropertyChanged(nameof(ShownRarity)));
        _set = set;
        _picker = picker ?? new FilePickerService();
        _dialogs = dialogs ?? new DialogService();
        _status = status ?? new StatusService();
        _revealer = revealer ?? new NoopFolderRevealer();
        Name = set.Manifest.Name;
        Count = set.Manifest.Count;
        Seed = set.Manifest.Seed;
        // Rows only: no image is opened here. Decoding is deferred to SetItemRow.Thumbnail so the
        // cost falls under the ListBox's virtualization instead of on top of it.
        Items = set.Items.Select(i => new SetItemRow(i.Number, i.ImagePath, i)).ToList();
        SelectedItem = Items.Count > 0 ? Items[0] : null;
    }

    // Keep each row's own IsSelected in sync so the grid can paint a selected-tile indicator —
    // the detail rail alone doesn't show which tile is selected once the grid has many rows.
    //
    // Exactly two rows can change, so exactly two are touched. Walking all of them raised 500
    // PropertyChanged events per click for 498 rows whose answer was already false, which measured
    // at 288 ms and 16 MB over twenty selections. Same observable result, ~250x less of it.
    partial void OnSelectedItemChanged(SetItemRow? oldValue, SetItemRow? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
    }

    /// <summary>The number of the asset the rail is describing, formatted.</summary>
    public string ShownNumber => Shown is null ? "" : $"#{Shown.Number:D4}";
    /// <summary>Its DNA.</summary>
    public string ShownDna => Shown?.Item.Dna ?? "";
    /// <summary>The recipe it came from.</summary>
    public string ShownRecipe => Shown?.Item.Recipe ?? "";
    /// <summary>Its traits with collection-wide rarity.</summary>
    public IReadOnlyList<RarityAttribute> ShownRarity => RaritySort.Order(
        Shown?.Item.Rarity ?? Array.Empty<RarityAttribute>(),
        static (r, col) => col switch
        {
            "Value" => r.Value,
            "Pct" => (object)r.RarityPct,
            _ => r.Trait_type,
        });

    /// <summary>
    /// The rarity table's sort. Its natural order is the metadata's own trait order, which is
    /// stable and meaningful — so "Trait" is the default and clicking it twice returns to it.
    /// </summary>
    /// <remarks>
    /// A rarity table that cannot be ordered by rarity is the one question it exists to answer,
    /// unanswerable: "what is rarest about this asset" meant reading every row and comparing by eye.
    /// </remarks>
    public TableSort RaritySort { get; }

    /// <summary>
    /// Whether the rarity column reads as odds ("1 in 24") rather than a percentage ("4.17%").
    /// </summary>
    /// <remarks>
    /// <para>Both readings are useful and neither is right on its own. A percentage compares traits
    /// to each other; odds are what "how rare is this one" actually asks, and 4.17% against 2.08%
    /// looks like a near-miss where "1 in 24" against "1 in 48" does not. It is the same number
    /// either way - the odds are derived, never stored - so the two cannot disagree.</para>
    ///
    /// <para>Session state, deliberately: it describes how this reader wants to look at the table,
    /// not anything about the Set, so it does not belong in a file and does not follow the asset.</para>
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RarityUnitLabel))]
    [NotifyPropertyChangedFor(nameof(RarestText))]
    [NotifyPropertyChangedFor(nameof(CombinedText))]
    private bool _showRarityAsOdds;

    /// <summary>What the rarity column's header says in the current unit.</summary>
    public string RarityUnitLabel => ShowRarityAsOdds ? "ODDS" : "%";

    /// <summary>Shows the rarity column as a percentage.</summary>
    [RelayCommand] private void ShowRarityPercent() => ShowRarityAsOdds = false;

    /// <summary>Shows the rarity column as odds.</summary>
    [RelayCommand] private void ShowRarityOdds() => ShowRarityAsOdds = true;

    /// <summary>
    /// The single answer to "how rare is this one": the rarest trait it carries, in the active unit.
    /// </summary>
    /// <remarks>
    /// <b>The rarest TRAIT, not a score.</b> The obvious alternative is to multiply the trait shares
    /// into a combined chance, and that number would be wrong here: it assumes the layers roll
    /// independently, which incompatibility rules and absent-percents both break. A rarity SCORE
    /// (the sum of the reciprocals) is a convention this project has never adopted and would have to
    /// invent. The rarest trait needs no assumption at all - it is a fact already in the table
    /// below, and it is the one a reader is scanning that table to find.
    /// </remarks>
    public string RarestText
    {
        get
        {
            var rarity = Shown?.Item.Rarity;
            if (rarity is null || rarity.Count == 0) return "";
            RarityAttribute? rarest = null;
            foreach (var r in rarity)
                if (r.RarityPct > 0 && (rarest is null || r.RarityPct < rarest.RarityPct)) rarest = r;
            if (rarest is null) return "";
            var unit = (string)RarityUnitConverter.Instance.Convert(
                new object?[] { rarest.RarityPct, ShowRarityAsOdds }, typeof(string), null,
                CultureInfo.InvariantCulture);
            // The word is part of the RUN, not a second TextBlock beside it: two runs of different
            // sizes on one line is the baseline trap this app already records, and one run cannot
            // mismatch itself.
            return $"rarest {rarest.Value} · {unit}";
        }
    }

    /// <summary>
    /// The chance of rolling THIS EXACT ASSET — every trait it carries, together.
    /// </summary>
    /// <remarks>
    /// <para>This is the "wow, one in fifty thousand" line, and it is the question a rarity table
    /// cannot answer by being read: a reader can see that the lock is 1 in 4 and the bands are 1 in
    /// 3 and still have no idea what the whole combination is worth. It is the product of every row
    /// in that table — the recipe's own share included, since Type is one of the rows — so the
    /// figure below is built from exactly the numbers above it.</para>
    ///
    /// <para><b>It assumes the layers roll independently, and that is why the tooltip says so.</b>
    /// Incompatibility rules and absent-percents both couple layers together, so a book that uses
    /// either makes the true chance differ from this product — usually by making forbidden
    /// combinations impossible and the surviving ones commoner. The alternative was to locate the
    /// source CookBook by hash and compute the exact joint probability from its weights, which is a
    /// real feature and not this one; what this must not do is print an exact-looking number and
    /// stay quiet about the assumption behind it.</para>
    ///
    /// <para>Computed from the shares this SET actually produced, not from the book's intent, which
    /// is also what the table above shows. On a small collection those two differ; the figure is
    /// about the assets in front of you.</para>
    /// </remarks>
    public string CombinedText
    {
        get
        {
            var rarity = Shown?.Item.Rarity;
            if (rarity is null || rarity.Count == 0) return "";

            double p = 1.0;
            foreach (var r in rarity)
            {
                if (r.RarityPct <= 0) return "";     // a trait no asset carries makes the product meaningless
                p *= r.RarityPct / 100.0;
            }
            if (p <= 0 || double.IsNaN(p)) return "";

            if (!ShowRarityAsOdds)
            {
                // Enough places to stay non-zero however deep the stack goes: a six-layer asset is
                // routinely a thousandth of a percent, and "0.00%" is not a figure.
                double pct = p * 100.0;
                string text = pct >= 0.01 ? pct.ToString("0.##", CultureInfo.InvariantCulture)
                    : pct.ToString("0.######", CultureInfo.InvariantCulture);
                return $"this combo {text}%";
            }

            double one = 1.0 / p;
            // Thousands-separated and invariant, like every figure this app prints: these get read
            // off screenshots and compared between machines.
            return one >= 1_000_000_000_000d
                ? "this combo 1 in over a trillion"
                : $"this combo 1 in {Math.Round(one, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture)}";
        }
    }

    /// <summary>How many Recipes the collection was rolled from.</summary>
    public int RecipeCount => _set.Manifest.Distribution.Count;

    /// <summary>
    /// Whether this Set came out of a sealed export, and so may be looked at but not taken.
    /// </summary>
    /// <remarks>
    /// Asked of the TYPE rather than carried as a flag alongside it, because a policy the caller has
    /// to remember to pass on is a policy that eventually arrives nowhere — which is the whole
    /// reason <c>SealedSet</c> is a <c>LoadedSet</c> that knows its own seal.
    /// </remarks>
    public bool IsSealed => _set is SealedSet;

    /// <summary>What the person who sealed it wanted read first, or empty.</summary>
    public string SealNote => (_set as SealedSet)?.Header.Note ?? "";

    /// <summary>Whether this Set can be exported at all.</summary>
    /// <remarks>
    /// Both exits are gated, not just the obvious one: Save image is an export of one asset, and a
    /// seal that closed the Export button while leaving Save working would be a label rather than a
    /// rule. <c>SetExporter</c> refuses a sealed source independently, so this is the screen
    /// agreeing with the engine rather than the only thing enforcing it.
    /// </remarks>
    public bool CanExport => !IsSealed && _set.SourceDirectory.Length > 0;

    // A DNA is a SHA-256, so it is always 64 hex characters and always splits into two rows of
    // exactly 32 -- which is why the rail can center them and have both edges line up. The split is
    // still computed rather than hard-coded at 32: a Set written by some future build with a
    // different hash would otherwise silently lose its tail.
    /// <summary>The first half of the selected DNA.</summary>
    public string ShownDnaTop => Half(ShownDna, top: true);
    /// <summary>The second half.</summary>
    public string ShownDnaBottom => Half(ShownDna, top: false);

    private int IndexOf(SetItemRow row)
    {
        for (var i = 0; i < Items.Count; i++) if (ReferenceEquals(Items[i], row)) return i;
        return 0;
    }

    private static string Half(string dna, bool top)
    {
        if (string.IsNullOrEmpty(dna)) return "";
        var cut = (dna.Length + 1) / 2;          // an odd length puts the extra character on top
        return top ? dna[..cut] : dna[cut..];
    }

    /// <summary>Opens the full-size inspector on one asset.</summary>
    /// <param name="row">The asset to open on.</param>
    [RelayCommand]
    private async Task InspectAsync(SetItemRow? row)
    {
        if (row is null) return;
        SelectedItem = row;
        using var vm = new SetInspectViewModel(Items, IndexOf(row), _picker, _dialogs, _status,
            allowExport: CanExport);

        // The inspector can walk the Set with the arrow keys, and what the user last LOOKED AT is
        // what they expect to find selected when they close it. Without this you could arrow from
        // #0007 to #0040, close, and be told you were on #0007 the whole time.
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetInspectViewModel.Index) && vm.Index < Items.Count)
                SelectedItem = Items[vm.Index];
        };

        await _dialogs.ShowAsync<object>(vm);
    }

    /// <summary>Writes the selected asset's PNG wherever the user chooses.</summary>
    /// <remarks>The source file is copied rather than re-encoded, so what lands on disk is byte-for
    /// byte the image the Set contains.</remarks>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task SaveImageAsync()
    {
        if (Shown is not { } row) return;
        var target = await _picker.SaveFileAsync($"Save {ShownNumber}", ".png");
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            File.Copy(row.ImagePath, target, overwrite: true);
            _status.Say($"Saved {ShownNumber} to {target}.");
        }
        catch (Exception ex)
        {
            _status.Say($"Could not save {ShownNumber}: {ex.Message}");
        }
    }

    /// <summary>Opens the export dialog for this Set.</summary>
    /// <remarks>
    /// The dialog is handed <c>SourceDirectory</c> — where the files already are, which for a Set
    /// opened from a packed <c>.set</c> is the temporary directory it was unpacked into. It
    /// recomputes what it is about to ship on every checkbox, and re-extracting the archive per
    /// click is not something that can be made fast afterwards.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync() =>
        await _dialogs.ShowAsync<object>(
            new ExportDialogViewModel(_set.SourceDirectory, _picker, _revealer, _dialogs,
                // A Set records its book's HASH and never the book, so the dialog cannot follow a
                // path - but it can CHECK one. These are the books this app can already name; the
                // dialog also looks beside the Set, and accepts only the one that hashes right.
                _set.Manifest.CookbookSha256, _bookCandidates?.Invoke() ?? Array.Empty<string>()));

    /// <summary>Frees every decoded thumbnail and the underlying Set. Rows that were never realized
    /// decoded nothing, and disposing them is a no-op — reading <c>r.Thumbnail</c> here to dispose
    /// it would have decoded the whole collection at teardown.</summary>
    public void Dispose()
    {
        foreach (var r in Items) r.Dispose();
        _set.Dispose();
    }
}
