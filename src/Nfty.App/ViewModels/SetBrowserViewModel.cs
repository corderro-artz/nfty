using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

/// <summary>One grid tile's data plus its own selection flag, so the view can paint a selected-tile
/// indicator (accent border/wash) via a bound "sel" style class rather than relying solely on the
/// detail rail to show what's selected.</summary>
public partial class SetItemRow : ObservableObject, IDisposable
{
    private const int ThumbW = 128;
    private readonly string _imagePath;
    private Bitmap? _thumbnail;

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
    public Bitmap Thumbnail => _thumbnail ??= Decode(_imagePath);

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
        _imagePath = imagePath;
        Item = item;
    }

    private static Bitmap Decode(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Bitmap.DecodeToWidth(fs, ThumbW);   // small downscaled thumbnail
        }
        catch
        {
            // Tolerant placeholder: 1x1 transparent bitmap if the image is missing or corrupt. A
            // browser over a damaged Set should show the damage, not refuse to open.
            return new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        }
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
    private SetItemRow? _selectedItem;

    /// <summary>Opens a cooked Set for browsing.</summary>
    /// <param name="set">The loaded Set; this takes ownership and disposes it.</param>
    public SetBrowserViewModel(LoadedSet set)
    {
        _set = set;
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
    partial void OnSelectedItemChanged(SetItemRow? value)
    {
        foreach (var r in Items) r.IsSelected = ReferenceEquals(r, value);
    }

    /// <summary>The selected asset's number, formatted.</summary>
    public string SelectedNumber => SelectedItem is null ? "" : $"#{SelectedItem.Number:D4}";
    /// <summary>Its DNA.</summary>
    public string SelectedDna => SelectedItem?.Item.Dna ?? "";
    /// <summary>The recipe it came from.</summary>
    public string SelectedRecipe => SelectedItem?.Item.Recipe ?? "";
    /// <summary>Its traits with collection-wide rarity.</summary>
    public IReadOnlyList<RarityAttribute> SelectedRarity => SelectedItem?.Item.Rarity ?? Array.Empty<RarityAttribute>();

    /// <summary>Frees every decoded thumbnail and the underlying Set. Rows that were never realized
    /// decoded nothing, and disposing them is a no-op — reading <c>r.Thumbnail</c> here to dispose
    /// it would have decoded the whole collection at teardown.</summary>
    public void Dispose()
    {
        foreach (var r in Items) r.Dispose();
        _set.Dispose();
    }
}
