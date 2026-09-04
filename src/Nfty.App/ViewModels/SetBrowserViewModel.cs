using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Diagnostics;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

/// <summary>One grid tile's data plus its own selection flag, so the view can paint a selected-tile
/// indicator (accent border/wash) via a bound "sel" style class rather than relying solely on the
/// detail rail to show what's selected.</summary>
public partial class SetItemRow : ObservableObject, IDisposable
{
    private const int ThumbW = 128;
    private Bitmap? _thumbnail;

    /// <summary>The asset's PNG on disk. The grid shows a 128px thumbnail of it; the inspector and
    /// Save both need the file itself.</summary>
    public string ImagePath { get; }

    /// <summary>The asset's set number.</summary>
    public int Number { get; }
    /// <summary>Its metadata.</summary>
    public SetItem Item { get; }

    /// <summary>
    /// The tile image, decoded on first access and cached.
    ///
    /// <para>Lazy because the ViewModel used to decode every thumbnail in its constructor, on the UI
    /// thread: 627 ms for 900 assets, which extrapolates to roughly seven seconds of frozen window
    /// for a 10,000-asset Set — and that is a floor, measured on 64x64 sources rather than real art.
    /// The ListBox below it virtualizes, but virtualization only limits what is <em>rendered</em>;
    /// it could do nothing about work already done up front. Deferring to the getter puts the decode
    /// back under the virtualizer, so only realized rows pay for it.</para>
    /// </summary>
    public Bitmap Thumbnail => _thumbnail ??= Decode(ImagePath);

    /// <summary>Whether this row has actually paid for its image yet. Read by the performance tests
    /// to prove the decode is still falling under the ListBox's virtualization rather than on top
    /// of it.</summary>
    internal bool IsThumbnailDecoded => _thumbnail is not null;

    /// <summary>Whether this tile is the selected one, so the grid can paint an indicator — the
    /// detail rail alone does not show which tile is selected once there are many rows.</summary>
    [ObservableProperty]
    private bool _isSelected;

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
    public void Dispose()
    {
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

    /// <summary>The collection's name.</summary>
    public string Name { get; }
    /// <summary>How many assets the Set holds.</summary>
    public int Count { get; }
    /// <summary>The seed that produced it.</summary>
    public string Seed { get; }
    /// <summary>One row per asset.</summary>
    public IReadOnlyList<SetItemRow> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDna))]
    [NotifyPropertyChangedFor(nameof(SelectedRecipe))]
    [NotifyPropertyChangedFor(nameof(SelectedRarity))]
    [NotifyPropertyChangedFor(nameof(SelectedNumber))]
    [NotifyPropertyChangedFor(nameof(SelectedDnaTop))]
    [NotifyPropertyChangedFor(nameof(SelectedDnaBottom))]
    private SetItemRow? _selectedItem;

    /// <summary>Opens a cooked Set for browsing.</summary>
    /// <param name="set">The loaded Set; this takes ownership and disposes it.</param>
    /// <param name="picker">Where Save asks for a destination. Defaults to the null picker, which
    /// reports "canceled" — the same thing every other surface does without a window.</param>
    /// <param name="dialogs">The modal layer the inspector opens into.</param>
    /// <param name="status">Where a save result is reported.</param>
    public SetBrowserViewModel(LoadedSet set, IFilePickerService? picker = null,
        IDialogService? dialogs = null, IStatusService? status = null)
    {
        _set = set;
        _picker = picker ?? new FilePickerService();
        _dialogs = dialogs ?? new DialogService();
        _status = status ?? new StatusService();
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

    /// <summary>The selected asset's number, formatted.</summary>
    public string SelectedNumber => SelectedItem is null ? "" : $"#{SelectedItem.Number:D4}";
    /// <summary>Its DNA.</summary>
    public string SelectedDna => SelectedItem?.Item.Dna ?? "";
    /// <summary>The recipe it came from.</summary>
    public string SelectedRecipe => SelectedItem?.Item.Recipe ?? "";
    /// <summary>Its traits with collection-wide rarity.</summary>
    public IReadOnlyList<RarityAttribute> SelectedRarity => SelectedItem?.Item.Rarity ?? Array.Empty<RarityAttribute>();

    /// <summary>How many Recipes the collection was rolled from.</summary>
    public int RecipeCount => _set.Manifest.Distribution.Count;

    // A DNA is a SHA-256, so it is always 64 hex characters and always splits into two rows of
    // exactly 32 -- which is why the rail can center them and have both edges line up. The split is
    // still computed rather than hard-coded at 32: a Set written by some future build with a
    // different hash would otherwise silently lose its tail.
    /// <summary>The first half of the selected DNA.</summary>
    public string SelectedDnaTop => Half(SelectedDna, top: true);
    /// <summary>The second half.</summary>
    public string SelectedDnaBottom => Half(SelectedDna, top: false);

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
        using var vm = new SetInspectViewModel(Items, IndexOf(row), _picker, _dialogs, _status);

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
    [RelayCommand]
    private async Task SaveImageAsync()
    {
        if (SelectedItem is not { } row) return;
        var target = await _picker.SaveFileAsync($"Save {SelectedNumber}", ".png");
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            File.Copy(row.ImagePath, target, overwrite: true);
            _status.Say($"Saved {SelectedNumber} to {target}.");
        }
        catch (Exception ex)
        {
            _status.Say($"Could not save {SelectedNumber}: {ex.Message}");
        }
    }

    /// <summary>Frees every decoded thumbnail and the underlying Set. Rows that were never realized
    /// decoded nothing, and disposing them is a no-op — reading <c>r.Thumbnail</c> here to dispose
    /// it would have decoded the whole collection at teardown.</summary>
    public void Dispose()
    {
        foreach (var r in Items) r.Dispose();
        _set.Dispose();
    }
}
