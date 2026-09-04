using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Diagnostics;

namespace Nfty.App.ViewModels;

/// <summary>
/// The full-size inspector for one cooked asset: zoom, bounded pan, step to the neighbouring asset,
/// and save the image out.
///
/// <para>It holds the <em>index</em> into the Set rather than a single item, because stepping is the
/// point — inspecting a collection means comparing, and closing the modal between every pair is the
/// wrong shape. The dialog layer shows one ViewModel at a time, so the step happens in here.</para>
/// </summary>
public partial class SetInspectViewModel : ViewModelBase, IDisposable
{
    /// <summary>The smallest zoom offered, as a multiple of the fitted size.</summary>
    public const double MinScale = 1.0;
    /// <summary>The largest. Sixteen times the fitted size is enough to read individual pixels on a
    /// 1000px asset and far more than enough on a 64px one; past that the viewport shows noise.</summary>
    public const double MaxScale = 16.0;

    private readonly IReadOnlyList<SetItemRow> _items;
    private readonly IFilePickerService _picker;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private Bitmap? _full;
    private int _fullFor = -1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number))]
    [NotifyPropertyChangedFor(nameof(Recipe))]
    [NotifyPropertyChangedFor(nameof(DnaShort))]
    [NotifyPropertyChangedFor(nameof(Image))]
    [NotifyPropertyChangedFor(nameof(SourceSize))]
    private int _index;

    /// <summary>How far the image is zoomed, as a multiple of the size that fits the viewport.
    /// <c>1</c> is Fit, and it is also the floor — below it there is nothing to look at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaleText))]
    [NotifyPropertyChangedFor(nameof(CanPan))]
    private double _scale = 1.0;

    /// <summary>Pan offset in control pixels. The view clamps it; nothing here may exceed what the
    /// view last allowed, which is why the view writes it rather than this.</summary>
    [ObservableProperty] private double _panX;
    /// <inheritdoc cref="PanX"/>
    [ObservableProperty] private double _panY;

    /// <summary>Creates the inspector over a Set, opened on one of its assets.</summary>
    /// <param name="items">Every asset in the Set, in order.</param>
    /// <param name="index">Which one to open on.</param>
    /// <param name="picker">Used by <see cref="SaveCommand"/>.</param>
    /// <param name="dialogs">The layer this modal lives in; used to close.</param>
    /// <param name="status">Where the save result is reported.</param>
    public SetInspectViewModel(IReadOnlyList<SetItemRow> items, int index,
        IFilePickerService picker, IDialogService dialogs, IStatusService status)
    {
        _items = items;
        _picker = picker;
        _dialogs = dialogs;
        _status = status;
        _index = Math.Clamp(index, 0, Math.Max(0, items.Count - 1));
    }

    private SetItemRow? Current => _items.Count == 0 ? null : _items[Index];

    /// <summary>The asset's number, formatted.</summary>
    public string Number => Current is null ? "" : $"#{Current.Number:D4}";
    /// <summary>The Recipe it was rolled from.</summary>
    public string Recipe => Current?.Item.Recipe ?? "";

    /// <summary>The DNA, elided. The rail shows it in full; here it is an identity check, not a
    /// field to read, so the middle goes.</summary>
    public string DnaShort
    {
        get
        {
            var d = Current?.Item.Dna ?? "";
            return d.Length <= 20 ? d : $"{d[..8]}…{d[^8..]}";
        }
    }

    /// <summary>
    /// The asset at full size — decoded here rather than reused from the grid, because the grid's
    /// bitmap is a 128px thumbnail and this is the one screen where the real pixels matter.
    /// </summary>
    /// <remarks>
    /// Cached per index, and the old bitmap is freed only when the index actually moves. The first
    /// cut disposed and re-decoded on every read, which looked harmless and killed the app: Avalonia
    /// reads <c>Image.Source</c> during measure, so the getter disposed the very bitmap it had just
    /// handed out and the next layout pass threw <c>ObjectDisposedException</c> inside
    /// <c>Image.MeasureOverride</c>. No ViewModel test could see it — rendering the view is what
    /// found it.
    /// </remarks>
    public Bitmap? Image
    {
        get
        {
            if (_fullFor == Index) return _full;
            _full?.Dispose();
            _full = null;
            _fullFor = Index;
            if (Current is null) return null;
            using var _ = Perf.Measure("SetInspect.DecodeFull");
            try { _full = new Bitmap(Current.ImagePath); }
            catch { _full = null; }   // a damaged Set should show the damage, not throw
            return _full;
        }
    }

    /// <summary>The asset's real pixel dimensions, for the footer.</summary>
    public string SourceSize =>
        Image is { } b ? $"{b.PixelSize.Width} × {b.PixelSize.Height} source" : "";

    /// <summary>The zoom readout.</summary>
    public string ScaleText => $"{Scale * 100:0}%";

    /// <summary>Whether panning does anything. At Fit the image is centered with nothing outside the
    /// viewport, so a drag would only make it drift — which is exactly the sloppiness this avoids.</summary>
    public bool CanPan => Scale > 1.0001;

    /// <summary>Whether there is an asset before this one.</summary>
    public bool HasPrevious => Index > 0;
    /// <summary>Whether there is one after.</summary>
    public bool HasNext => Index < _items.Count - 1;

    partial void OnIndexChanged(int value)
    {
        // A new asset opens at Fit. Carrying the previous one's zoom would land the next image
        // off-center at 800% with no clue where it went.
        Scale = 1.0;
        PanX = PanY = 0;
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
    }

    partial void OnScaleChanged(double value)
    {
        if (value <= 1.0001) { PanX = 0; PanY = 0; }   // Fit is always centered
    }

    /// <summary>Steps to the previous asset.</summary>
    [RelayCommand(CanExecute = nameof(HasPrevious))]
    private void Previous() => Index--;

    /// <summary>Steps to the next asset.</summary>
    [RelayCommand(CanExecute = nameof(HasNext))]
    private void Next() => Index++;

    /// <summary>Back to the fitted size.</summary>
    [RelayCommand] private void Fit() => Scale = 1.0;

    /// <summary>Zooms out one step.</summary>
    [RelayCommand] private void ZoomOut() => Scale = Math.Max(MinScale, Scale / 1.5);
    /// <summary>Zooms in one step.</summary>
    [RelayCommand] private void ZoomIn() => Scale = Math.Min(MaxScale, Scale * 1.5);

    /// <summary>Closes the inspector.</summary>
    [RelayCommand] private void Close() => _dialogs.Close(null);

    /// <summary>
    /// Writes the CURRENTLY shown asset's PNG wherever the user chooses.
    ///
    /// <para>The source file is copied rather than re-encoded. Re-encoding would hand the user a
    /// different file from the one the Set actually contains — different bytes, possibly a different
    /// color profile — for an operation whose whole point is "give me that image".</para>
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Current is null) return;
        var target = await _picker.SaveFileAsync($"Save {Number}", ".png");
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            File.Copy(Current.ImagePath, target, overwrite: true);
            _status.Say($"Saved {Number} to {target}.");
        }
        catch (Exception ex)
        {
            _status.Say($"Could not save {Number}: {ex.Message}");
        }
    }

    /// <summary>Frees the full-size bitmap.</summary>
    public void Dispose()
    {
        _full?.Dispose();
        _full = null;
        _fullFor = -1;
    }
}
