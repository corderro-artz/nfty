using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Core.Output;

namespace Nfty.App.ViewModels;

public record SetItemRow(int Number, Bitmap Thumbnail, SetItem Item);

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
