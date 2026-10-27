using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia.Threading;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Model;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.App.Rise;

/// <summary>
/// The assets of a cooked Set. A Set stores pixels and trait NAMES, never depth, so depth is
/// recovered: the CookBook that cooked it is found by hash (as the Set browser's exact chance does),
/// each asset is mapped back onto that book's layers by name, and the stack rule gives its combined
/// depth. No book, or an asset that cannot be mapped, is paper — said in <see cref="DepthNote"/>.
/// Read-only. A sealed Set can be looked at and risen here but nothing leaves it.
/// </summary>
/// <remarks>The <see cref="LoadedSet"/> is borrowed from whoever opened it and is not disposed here.</remarks>
public sealed class SetRiseSource : RiseSource
{
    private readonly LoadedSet _set;
    private readonly List<RiseItem> _items = new();
    private readonly Dictionary<string, SetItemRow> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SetItem> _setItems = new(StringComparer.Ordinal);
    private readonly List<Action> _detach = new();
    private readonly HashSet<int> _unmapped = new();
    private readonly RiseItem _initial;
    private LoadedCookBook? _book;
    private bool _searchDone;
    private bool _disposed;
    private readonly Dictionary<int, string> _refusals = new();
    private readonly Func<string, LoadedCookBook> _readBook;
    private readonly TaskCompletionSource _arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Opens the source and starts looking for the book.</summary>
    /// <param name="set">The Set; borrowed.</param>
    /// <param name="selectedNumber">The asset that opens on the canvas (the first when none has this number).</param>
    /// <param name="bookCandidates">Paths of CookBooks the app can already name (open, recent); read
    /// HERE, on the calling thread, because it reaches into app state.</param>
    /// <param name="bridge">Required for symmetry with the other sources.</param>
    /// <param name="readBook">How a found CookBook is read; null reads the archive. A seam so a test can
    /// see the book a late result is dropped with, and make the read fail.</param>
    public SetRiseSource(LoadedSet set, int selectedNumber, Func<IEnumerable<string>>? bookCandidates,
        IImageBridge bridge, Func<string, LoadedCookBook>? readBook = null)
    {
        ArgumentNullException.ThrowIfNull(set);
        _readBook = readBook ?? (path => CookBookArchive.Read(path));
        ArgumentNullException.ThrowIfNull(bridge);
        if (set.Items.Count == 0) throw new ArgumentException("The Set has no assets to rise.", nameof(set));
        _set = set;

        RiseItem? initial = null;
        foreach (var si in set.Items)
        {
            var row = new SetItemRow(si.Number, si.ImagePath, si);
            string id = string.Create(CultureInfo.InvariantCulture, $"{si.Number:D4}");
            // The item reads THROUGH to the row, never snapshots it: reading the row's picture is what
            // starts its off-thread decode, so only a view that realizes the tile pays for it. Reading
            // it here would decode every asset of a ten-thousand-asset Set at open.
            var item = new RiseItem(id, $"#{id}", si.Recipe,
                $"{set.Manifest.Name}-{id}", null, ownsThumbnail: false, thumbnailRow: row);
            void OnRowChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SetItemRow.Thumbnail)) item.NotifyThumbnail();
            }
            row.PropertyChanged += OnRowChanged;
            _detach.Add(() => row.PropertyChanged -= OnRowChanged);

            _rows[id] = row;
            _setItems[id] = si;
            _items.Add(item);
            if (si.Number == selectedNumber) initial = item;
        }
        _initial = initial ?? _items[0];
        _initial.IsSelected = true;

        // The depth note describes the SELECTED asset, so a selection change is a change the page
        // must re-read.
        foreach (var item in _items)
        {
            void OnItemChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(RiseItem.IsSelected)) RaiseChanged();
            }
            item.PropertyChanged += OnItemChanged;
            var captured = item;
            _detach.Add(() => captured.PropertyChanged -= OnItemChanged);
        }

        BookSearch = StartBookSearch(bookCandidates);
    }

    /// <summary>The off-thread search for the CookBook. Completes once the result has been posted to
    /// the UI thread (a test pumps the dispatcher after awaiting it), whether or not a book was found.</summary>
    public Task BookSearch { get; }

    /// <summary>File name of the CookBook that was found, or null.</summary>
    public string? BookName { get; private set; }

    /// <summary>Whether the Set came out of a seal.</summary>
    public bool IsSealed => _set is SealedSet;

    /// <inheritdoc />
    public override RiseSourceKind Kind => RiseSourceKind.Set;

    /// <inheritdoc />
    public override string Kicker => $"{_set.Manifest.Name.ToUpperInvariant()} · ASSETS";

    /// <inheritdoc />
    public override string Title => _set.Manifest.Name;

    /// <inheritdoc />
    public override IReadOnlyList<RiseItem> Items => _items;

    /// <inheritdoc />
    public override RiseItem InitialItem => _initial;

    /// <inheritdoc />
    public override IReadOnlyList<string> Facts => new[]
    {
        string.Create(CultureInfo.InvariantCulture, $"assets {_set.Items.Count}"),
        $"seed {_set.Manifest.Seed}",
        string.Create(CultureInfo.InvariantCulture, $"recipes {_set.Manifest.Distribution.Count}"),
    };

    /// <inheritdoc />
    public override bool CanBatch => true;

    /// <inheritdoc />
    public override string BatchStem => $"{_set.Manifest.Name}-models";

    /// <inheritdoc />
    public override string? ReadOnlyPill => "cooked Set · read-only";

    /// <inheritdoc />
    public override string? ExportRefusal => IsSealed
        ? "This Set is sealed: it can be risen and looked at here, but nothing leaves it — the rule that disables Save image."
        : null;

    /// <inheritdoc />
    public override string DepthNote
    {
        get
        {
            if (!_searchDone) return "Looking for the CookBook that cooked this Set…";
            if (_book is null) return "The CookBook that cooked this Set was not found, so every asset is paper.";
            var note = $"Depth comes from {BookName}, the book that cooked this Set.";
            int number = _setItems[(_items.FirstOrDefault(i => i.IsSelected) ?? _initial).Id].Number;
            if (_unmapped.Contains(number))
                note += " This asset's layers could not be matched to that book by name, so it is paper.";
            if (_refusals.TryGetValue(number, out var why))
                note += $" {why} The stack has no combined depth, so it is paper.";
            return note;
        }
    }

    /// <inheritdoc />
    public override string OpeningLine
    {
        get
        {
            var selected = _items.FirstOrDefault(i => i.IsSelected) ?? _initial;
            int number = _setItems[selected.Id].Number;
            return string.Create(CultureInfo.InvariantCulture, $"Viewing #{number:D4} of a cooked Set. Read-only.")
                + (IsSealed ? " Sealed: it can be viewed here, not exported." : "");
        }
    }

    /// <summary>The Set browser row behind an item (its lazily decoded picture); for tests.</summary>
    internal SetItemRow RowOf(RiseItem item) => _rows[item.Id];

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Subject is { } loaded) return loaded;

        var art = LoadArt(_rows[item.Id].ImagePath);
        var (map, faces) = DepthFor(_setItems[item.Id], art);
        RiseSubject subject;
        try { subject = new RiseSubject(art, map, ownsArt: true, faces); }
        catch
        {
            art.Dispose();
            faces?.Dispose();
            throw;
        }
        item.Attach(subject);
        return subject;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The Set is sealed.</exception>
    /// <remarks>An asset the page has not loaded is decoded only when the export reaches it, off the UI
    /// thread, against the book captured HERE: its depth is worked out by <see cref="Resolve"/>, which
    /// writes nothing back, so the depth note's bookkeeping stays the UI thread's alone. The caller
    /// waits for <see cref="ExportReady"/> first, so that book is the one the page shows.</remarks>
    public override RiseExportSnapshot SnapshotExport(IReadOnlyList<RiseItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // Refused here as well as where the button is enabled: a disabled button is a label.
        if (ExportRefusal is { } refusal) throw new InvalidOperationException(refusal);
        var book = _book;
        var entries = new List<RiseExportSnapshot.Entry>(items.Count);
        foreach (var item in items)
        {
            if (item.Subject is { } s)
            {
                entries.Add(RiseExportSnapshot.Frozen(s.Art, s.Depth.ToDepthMap(), s.Faces));
                continue;
            }
            string path = _rows[item.Id].ImagePath;
            var setItem = _setItems[item.Id];
            entries.Add(RiseExportSnapshot.Lazy(() =>
            {
                var art = LoadArt(path);
                try
                {
                    var resolved = Resolve(book, setItem, art);
                    return (art, resolved.Map, resolved.Faces);
                }
                catch
                {
                    art.Dispose();      // nobody else holds it yet: a failed resolve must not leak it
                    throw;
                }
            }));
        }
        return new RiseExportSnapshot(entries);
    }

    /// <inheritdoc />
    public override Task ExportReady => _arrived.Task;

    /// <inheritdoc />
    /// <remarks>A cooked Set is read-only and its assets reload from disk, so a subject the page has
    /// left is dropped at once: browsing two hundred 1000px assets would otherwise keep two hundred
    /// decoded pictures, depth copies and surfaces (about 11 MB each). What is known about it - that
    /// it cannot rise, its chip - stays on the row.</remarks>
    public override void Release(RiseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsSelected) return;
        item.Forget()?.Dispose();
    }

    /// <summary>The asset's PNG, or a 1×1 clear picture when it is missing or unreadable: the item is
    /// "empty", never a crash.</summary>
    private static Image<Rgba32> LoadArt(string path)
    {
        try { return Image.Load<Rgba32>(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or UnknownImageFormatException
                                       or InvalidImageContentException)
        {
            return new Image<Rgba32>(1, 1);
        }
    }

    /// <summary>The asset's combined depth, recording for the depth note why it is paper. UI thread only:
    /// it writes the note's bookkeeping.</summary>
    private (DepthMap? Map, StackFaces? Faces) DepthFor(SetItem item, Image<Rgba32> art)
    {
        if (_book is null) return (null, null);
        var (map, faces, unmapped, refusal) = Resolve(_book, item, art);
        if (unmapped) _unmapped.Add(item.Number); else _unmapped.Remove(item.Number);
        if (refusal is not null) _refusals[item.Number] = refusal; else _refusals.Remove(item.Number);
        return (map, faces);
    }

    /// <summary>The asset's combined depth by the stack rule, or null (paper) when there is no book,
    /// the asset cannot be matched to it by name, or its layers have partial alpha. Writes nothing:
    /// an export calls it off the UI thread.</summary>
    /// <returns>The map, the face owners that go with it (the caller owns them), whether the asset could
    /// not be matched to the book by name, and the reason a layer keeps it 2D, if one does.</returns>
    private static (DepthMap? Map, StackFaces? Faces, bool Unmapped, string? Refusal) Resolve(LoadedCookBook? book, SetItem item,
        Image<Rgba32> art)
    {
        if (book is null) return (null, null, false, null);
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == item.Recipe);
        var present = recipe is null ? null : SetItemSelection.Resolve(recipe, item);
        if (recipe is null || present is null)
            return (null, null, true, null);      // name ambiguity (or another book): paper, and said so

        // An all-absent roll has no layers: it rises as nothing, and is never handed to the stack rule.
        if (present.Count == 0) return (null, null, false, null);
        try
        {
            // The Set's recorded colors recolorize the layers exactly as the cook did; the Set's own PNG
            // is the finished picture.
            var picks = StackPick.WithColors(present, item.Layers);
            string source = $"asset #{item.Number:D4}";
            StackRise rise;
            try { rise = StackDepth.ForSelection(recipe, picks, source, art); }
            catch (ArgumentException)
            {
                // The PNG is not what these layers composite to (another size, or a changed pixel): the
                // book did not cook this picture, so it is paper, never a hybrid of the two. The
                // export's own checks still name a partly transparent pixel in the picture itself.
                return (null, null, false, "This asset's picture does not match the layers of that book.");
            }
            using var _ = rise;
            var map = rise.Depth;
            // A picture of another size than the book's canvas cannot take the book's depth.
            bool fits = map.Width == art.Width && map.Height == art.Height;
            return (fits ? map : null, fits ? rise.Faces.Clone() : null, false, null);
        }
        catch (PartialAlphaException ex)
        {
            return (null, null, false, ex.Message);   // which layer keeps this asset 2D, said in the note
        }
    }

    private Task StartBookSearch(Func<IEnumerable<string>>? bookCandidates)
    {
        if (_set.Manifest.CookbookSha256 is not { Length: > 0 } hash)
        {
            _searchDone = true;   // the Set cannot say which book, so there is nothing to look for
            _arrived.TrySetResult();
            return Task.CompletedTask;
        }

        // Read on the calling (UI) thread: the callback reaches into app state.
        var named = (bookCandidates?.Invoke() ?? Array.Empty<string>()).ToList();
        string directory = _set.SourceDirectory;

        return Task.Run(() =>
        {
            LoadedCookBook? found = null;
            string? name = null;
            try
            {
                string? path = CookBookLocator.Find(hash, named.Concat(CookBookLocator.Nearby(directory)));
                if (path is not null)
                {
                    // Right bytes can still be unreadable by this build: a newer schema, a file mid-write.
                    try
                    {
                        found = _readBook(path);
                        name = Path.GetFileName(path);
                    }
                    catch (JsonException) { }
                    catch (IOException) { }
                    catch (InvalidDataException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (UnsupportedSchemaVersionException) { }
                }
            }
            finally
            {
                Dispatcher.UIThread.Post(() => Arrive(found, name));
            }
        });
    }

    private void Arrive(LoadedCookBook? book, string? name)
    {
        if (_disposed) { book?.Dispose(); return; }   // a late result is dropped, not published
        // The old textures and faces go only AFTER the page has been told, as a reroll's picture does.
        var retired = new List<IDisposable>();
        try
        {
            _searchDone = true;
            _book = book;
            BookName = name;
            if (book is not null)
            {
                foreach (var item in _items)
                {
                    if (item.Subject is not { } s) { item.ForgetChip(); continue; }
                    var (map, faces) = DepthFor(_setItems[item.Id], s.Art);
                    retired.Add(s.Reseed(map, faces));
                    item.RefreshDepth();
                }
            }
            RaiseChanged();
        }
        finally
        {
            // Whatever a listener or a reseed throws, the search is over: an export waits for this
            // behind a card only its job can close, so it must never be left pending.
            _arrived.TrySetResult();
            foreach (var r in retired) r.Dispose();
        }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var d in _detach) d();
        base.Dispose();
        foreach (var r in _rows.Values) r.Dispose();
        _book?.Dispose();
        _book = null;
        _arrived.TrySetResult();      // nobody waits for a book that will never be published
    }
}
