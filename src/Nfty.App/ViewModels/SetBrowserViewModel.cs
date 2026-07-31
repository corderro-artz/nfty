using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

/// <summary>One grid tile's data plus its own selection flag, so the view can paint a selected-tile
/// indicator (accent border/wash) via a bound "sel" style class rather than relying solely on the
/// detail rail to show what's selected.</summary>
public partial class SetItemRow : ObservableObject
{
    public int Number { get; }
    public Bitmap Thumbnail { get; }
    public SetItem Item { get; }

    [ObservableProperty]
    private bool _isSelected;

    public SetItemRow(int number, Bitmap thumbnail, SetItem item)
    {
        Number = number;
        Thumbnail = thumbnail;
        Item = item;
    }
}

/// <summary>Read-only browsing surface over a cooked Set: the collection header (name/count/seed)
/// plus per-item rows with a decoded thumbnail, and the currently selected item's detail
/// projections. Owns the decoded thumbnails and the underlying LoadedSet.</summary>
public partial class SetBrowserViewModel : ViewModelBase, IDisposable
{
    private const int ThumbW = 128;
    private readonly LoadedSet _set;

    public string Name { get; }
    public int Count { get; }
    public string Seed { get; }
    public IReadOnlyList<SetItemRow> Items { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDna))]
    [NotifyPropertyChangedFor(nameof(SelectedRecipe))]
    [NotifyPropertyChangedFor(nameof(SelectedRarity))]
    [NotifyPropertyChangedFor(nameof(SelectedNumber))]
    private SetItemRow? _selectedItem;

    public SetBrowserViewModel(LoadedSet set)
    {
        _set = set;
        Name = set.Manifest.Name;
        Count = set.Manifest.Count;
        Seed = set.Manifest.Seed;
        Items = set.Items.Select(i => new SetItemRow(i.Number, Decode(i.ImagePath), i)).ToList();
        SelectedItem = Items.Count > 0 ? Items[0] : null;
    }

    private static Bitmap Decode(string path)
    {
        using var fs = File.OpenRead(path);
        return Bitmap.DecodeToWidth(fs, ThumbW);   // small downscaled thumbnail
    }

    // Keep each row's own IsSelected in sync so the grid can paint a selected-tile indicator —
    // the detail rail alone doesn't show which tile is selected once the grid has many rows.
    partial void OnSelectedItemChanged(SetItemRow? value)
    {
        foreach (var r in Items) r.IsSelected = ReferenceEquals(r, value);
    }

    public string SelectedNumber => SelectedItem is null ? "" : $"#{SelectedItem.Number:D4}";
    public string SelectedDna => SelectedItem?.Item.Dna ?? "";
    public string SelectedRecipe => SelectedItem?.Item.Recipe ?? "";
    public IReadOnlyList<RarityAttribute> SelectedRarity => SelectedItem?.Item.Rarity ?? Array.Empty<RarityAttribute>();

    public void Dispose()
    {
        foreach (var r in Items) r.Thumbnail.Dispose();
        _set.Dispose();
    }
}
