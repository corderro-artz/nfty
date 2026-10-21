using System.ComponentModel;
using System.Globalization;
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
    private int _current;

    /// <summary>Opens the source and starts looking for the book.</summary>
    /// <param name="set">The Set; borrowed.</param>
    /// <param name="selectedNumber">The asset that opens on the canvas (the first when none has this number).</param>
    /// <param name="bookCandidates">Paths of CookBooks the app can already name (open, recent); read
    /// HERE, on the calling thread, because it reaches into app state.</param>
    /// <param name="bridge">Required for symmetry with the other sources.</param>
    public SetRiseSource(LoadedSet set, int selectedNumber, Func<IEnumerable<string>>? bookCandidates,
        IImageBridge bridge)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(bridge);
        if (set.Items.Count == 0) throw new ArgumentException("The Set has no assets to rise.", nameof(set));
        _set = set;

        RiseItem? initial = null;
        foreach (var si in set.Items)
        {
            var row = new SetItemRow(si.Number, si.ImagePath, si);
            string id = string.Create(CultureInfo.InvariantCulture, $"{si.Number:D4}");
            var item = new RiseItem(id, $"#{id}", si.Recipe,
                $"{set.Manifest.Name}-{id}", null, ownsThumbnail: false);

            // The item shows the row's thumbnail as it lands. Reading the property is what starts the
            // off-thread decode, so it is read once here and again on every change.
            void OnRowChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(SetItemRow.Thumbnail)) item.Thumbnail = row.Thumbnail;
            }
            row.PropertyChanged += OnRowChanged;
            _detach.Add(() => row.PropertyChanged -= OnRowChanged);
            item.Thumbnail = row.Thumbnail;

            _rows[id] = row;
            _setItems[id] = si;
            _items.Add(item);
            if (si.Number == selectedNumber) initial = item;
        }
        _initial = initial ?? _items[0];
        _initial.IsSelected = true;
        _current = _setItems[_initial.Id].Number;

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
            if (_unmapped.Contains(_current))
                note += " This asset's layers could not be matched to that book by name, so it is paper.";
            return note;
        }
    }

    /// <inheritdoc />
    public override string OpeningLine =>
        $"Rising “{Title}” — assets come out of the Set read-only; depth is recovered from the CookBook that cooked it.";

    /// <inheritdoc />
    public override RiseSubject Open(RiseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _current = _setItems[item.Id].Number;
        if (item.Subject is { } loaded) return loaded;

        var art = LoadArt(_rows[item.Id].ImagePath);
        var subject = new RiseSubject(art, DepthFor(_setItems[item.Id], art), ownsArt: true);
        item.Attach(subject);
        return subject;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The Set is sealed.</exception>
    public override IEnumerable<ModelInput> ExportInputs(IReadOnlyList<RiseItem> items, IReadOnlyList<string> baseNames)
    {
        // Refused here as well as where the button is enabled: a disabled button is a label.
        if (ExportRefusal is { } refusal) throw new InvalidOperationException(refusal);
        return Inputs(items, baseNames);
    }

    private IEnumerable<ModelInput> Inputs(IReadOnlyList<RiseItem> items, IReadOnlyList<string> baseNames)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Subject is { } s)
            {
                yield return new ModelInput(baseNames[i], s.Art, s.Depth.ToDepthMap());
                continue;
            }

            // Not loaded: decode for the yield and give it back afterwards. Never attached, so the
            // page never holds a subject it did not ask for.
            using var art = LoadArt(_rows[item.Id].ImagePath);
            yield return new ModelInput(baseNames[i], art, DepthFor(_setItems[item.Id], art));
        }
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

    /// <summary>The asset's combined depth by the stack rule, or null (paper) when no book is loaded,
    /// the asset cannot be matched to it by name, or its layers have partial alpha.</summary>
    private DepthMap? DepthFor(SetItem item, Image<Rgba32> art)
    {
        if (_book is not { } book) return null;
        var recipe = book.Recipes.FirstOrDefault(r => r.Manifest.Id == item.Recipe);
        var present = recipe is null ? null : SetItemSelection.Resolve(recipe, item);
        if (recipe is null || present is null)
        {
            _unmapped.Add(item.Number);   // name ambiguity (or another book): paper, and said so
            return null;
        }
        _unmapped.Remove(item.Number);

        // An all-absent roll has no layers: it rises as nothing, and is never handed to the stack rule.
        if (present.Count == 0) return null;
        try
        {
            var map = StackDepth.ForSelection(recipe, present, $"asset #{item.Number:D4}");
            // A picture of another size than the book's canvas cannot take the book's depth.
            return map.Width == art.Width && map.Height == art.Height ? map : null;
        }
        catch (PartialAlphaException)
        {
            return null;
        }
    }

    private Task StartBookSearch(Func<IEnumerable<string>>? bookCandidates)
    {
        if (_set.Manifest.CookbookSha256 is not { Length: > 0 } hash)
        {
            _searchDone = true;   // the Set cannot say which book, so there is nothing to look for
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
                        found = CookBookArchive.Read(path);
                        name = Path.GetFileName(path);
                    }
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
        _searchDone = true;
        _book = book;
        BookName = name;
        if (book is not null)
        {
            foreach (var item in _items)
            {
                if (item.Subject is not { } s) continue;
                s.Reseed(DepthFor(_setItems[item.Id], s.Art));
                item.RefreshDepth();
            }
        }
        RaiseChanged();
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
    }
}
