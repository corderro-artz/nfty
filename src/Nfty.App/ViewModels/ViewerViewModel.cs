using System.Numerics;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.Core.Imaging;
using Nfty.Model;
using SixLabors.ImageSharp;

namespace Nfty.App.ViewModels;

/// <summary>Which panes the viewer shows.</summary>
public enum ViewerLayout
{
    /// <summary>2D only.</summary>
    TwoD,
    /// <summary>2D and 3D side by side.</summary>
    Both,
    /// <summary>3D only.</summary>
    ThreeD,
}

/// <summary>The pop-out viewer: one subject, a layout, the 2D pane's state, and save/copy.</summary>
public sealed partial class ViewerViewModel : ViewModelBase, IDisposable
{
    private readonly IFilePickerService _picker;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private readonly IImageBridge _bridge;
    private ViewerLayout _layout;
    private Bitmap? _artBitmap;
    private ModelPreviewViewModel _preview;

    /// <summary>Builds the viewer for a subject.</summary>
    /// <param name="subject">What to show; the caller keeps ownership.</param>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="dialogs">Error cards.</param>
    /// <param name="bridge">Turns the art into something the 2D pane can draw; defaults to the real one.</param>
    public ViewerViewModel(ViewerSubject subject, IFilePickerService picker, IClipboardService clipboard,
        IDialogService dialogs, IImageBridge? bridge = null)
    {
        Subject = subject;
        _bridge = bridge ?? new ImageBridge();
        _picker = picker;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _layout = subject.Model != null ? ViewerLayout.Both : ViewerLayout.TwoD;
        _preview = NewPreview(subject.Model);
        _preview.PropertyChanged += OnPreviewChanged;
        subject.Changed += OnSubjectChanged;
    }

    private static ModelPreviewViewModel NewPreview(ViewerModel? model) =>
        model is { } m
            ? new ModelPreviewViewModel(m.Mesh.Min, m.Mesh.Max)
            : new ModelPreviewViewModel(-Vector3.One, Vector3.One);

    // A live subject changed: the picture and the solid are re-read. The camera is kept, only its
    // bounds follow, so an edit upstream does not throw the reader's view away.
    private void OnSubjectChanged()
    {
        var old = _artBitmap;
        _artBitmap = null;               // rebuilt from the subject's new picture on the next read
        if (Subject.Model is { } m) _preview.SetSubject(m.Mesh.Min, m.Mesh.Max);
        OnPropertyChanged(nameof(ArtBitmap));
        OnPropertyChanged(nameof(Mesh));
        OnPropertyChanged(nameof(Texture));
        OnPropertyChanged(nameof(CanShowThreeD));
        OnPropertyChanged(nameof(LayoutTip));
        OnPropertyChanged(nameof(LiveBadge));     // a source closing turns "live" into "snapshot"
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Facts));         // a live page's selection and size move with it
        OnPropertyChanged(nameof(HasFacts));
        RaiseTabs();
        CopyFactsCommand.NotifyCanExecuteChanged();
        old?.Dispose();
    }

    /// <summary>The 2D art as a bitmap the pane draws, or null for a subject with no picture. Built on
    /// first read, so a view model nothing draws (a test of save and copy) needs no render platform.</summary>
    public Bitmap? ArtBitmap => _artBitmap ??= Subject.Art is { } art ? _bridge.ToBitmap(art) : null;

    /// <summary>The 3D pane's camera state.</summary>
    public ModelPreviewViewModel Preview => _preview;

    /// <summary>The solid the 3D pane draws, or null.</summary>
    public Nfty.Model.Mesh? Mesh => Subject.Model?.Mesh;

    /// <summary>The texture the solid's UVs address, or null.</summary>
    public SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>? Texture => Subject.Model?.Texture;

    /// <summary>The subject's name: the window's title and the overlay header's text. Follows a live
    /// subject.</summary>
    public string Title => Subject.Title;

    /// <summary>The lines of the info rail, re-read whenever a live subject changes.</summary>
    public IReadOnlyList<ViewerFact> Facts => Subject.Facts;

    /// <summary>Whether the rail has anything to say; false dims Copy info rather than leaving it
    /// copying nothing.</summary>
    public bool HasFacts => Facts.Count > 0;

    /// <summary>The rail's width, stated once: the markup reads it and the layout tests measure
    /// against it. Wide enough for the four tab names and their counts on one line.</summary>
    public const double RailWidth = 288;

    private ViewerFactGroup _selectedGroup = ViewerFactGroup.Item;

    /// <summary>The tab the reader picked. Remembered for the life of this viewer: a live subject that
    /// changes keeps it, and only falls back to the first tab while that group has nothing.</summary>
    public ViewerFactGroup SelectedGroup
    {
        get => Tabs.Any(t => t.Group == _selectedGroup) || Tabs.Count == 0 ? _selectedGroup : Tabs[0].Group;
        set
        {
            if (_selectedGroup == value) return;
            _selectedGroup = value;
            RaiseTabs();
        }
    }

    /// <summary>Shows one group's facts in the rail.</summary>
    /// <param name="group">The group.</param>
    [RelayCommand]
    private void SelectGroup(ViewerFactGroup group) => SelectedGroup = group;

    /// <summary>The rail's tabs: one per group that has facts, in group order.</summary>
    public IReadOnlyList<ViewerFactTab> Tabs
    {
        get
        {
            var rows = Facts.Where(f => !f.Banner).ToList();
            var groups = rows.Select(f => f.Group).Distinct().OrderBy(g => g).ToList();
            var chosen = groups.Contains(_selectedGroup) || groups.Count == 0 ? _selectedGroup : groups[0];
            return groups.Select(g => new ViewerFactTab(g, rows.Count(f => f.Group == g), g == chosen)).ToList();
        }
    }

    /// <summary>Whether the tab strip has anything to switch between. One group has nothing to
    /// choose, so the strip is not drawn and takes no room (author's ruling: an empty reserved band
    /// serves no purpose there); the facts move up into its place.</summary>
    public bool ShowTabs => Tabs.Count > 1;

    /// <summary>The rows the rail lists: the chosen tab's facts.</summary>
    public IReadOnlyList<ViewerFact> TabFacts
    {
        get
        {
            var group = SelectedGroup;
            return Facts.Where(f => !f.Banner && f.Group == group).ToList();
        }
    }

    /// <summary>Statements about the whole subject, drawn above the tabs.</summary>
    public IReadOnlyList<ViewerFact> BannerFacts => Facts.Where(f => f.Banner).ToList();

    /// <summary>Whether there is a banner to draw.</summary>
    public bool HasBanner => Facts.Any(f => f.Banner);

    /// <summary>Every group with facts, as the extended panel lists them.</summary>
    public IReadOnlyList<ViewerFactSection> Sections =>
        Tabs.Select(t => new ViewerFactSection(t.Group, Facts.Where(f => !f.Banner && f.Group == t.Group).ToList())).ToList();

    /// <summary>The extended panel's left column.</summary>
    public IReadOnlyList<ViewerFactSection> SectionsLeft => ViewerFactSection.Balance(Sections).Left;

    /// <summary>The extended panel's right column.</summary>
    public IReadOnlyList<ViewerFactSection> SectionsRight => ViewerFactSection.Balance(Sections).Right;

    private void RaiseTabs()
    {
        foreach (var name in new[]
                 {
                     nameof(SelectedGroup), nameof(Tabs), nameof(ShowTabs), nameof(TabFacts),
                     nameof(BannerFacts), nameof(HasBanner), nameof(Sections), nameof(SectionsLeft), nameof(SectionsRight),
                 })
            OnPropertyChanged(name);
        RowsAbove = 0;
        RowsBelow = 0;
    }

    /// <summary>Whether the info panel is extended over the panes, every group at once.</summary>
    [ObservableProperty]
    private bool _isInfoExtended;

    /// <summary>Extends the info panel over the panes, or folds it back into the rail.</summary>
    [RelayCommand]
    private void ToggleInfoExtended() => IsInfoExtended = !IsInfoExtended;

    /// <summary>The extend button's tooltip, which says what pressing it does now.</summary>
    public string ExtendTip => (IsInfoExtended ? "Back to the rail" : "Show all info")
                               + "  (" + KeyHints.ViewerInfo + ")";

    partial void OnIsInfoExtendedChanged(bool value) => OnPropertyChanged(nameof(ExtendTip));

    /// <summary>How many rows of the chosen tab sit above the rail's fold (the view counts them).</summary>
    [ObservableProperty]
    private int _rowsAbove;

    /// <summary>How many rows of the chosen tab sit below the rail's fold.</summary>
    [ObservableProperty]
    private int _rowsBelow;

    /// <summary>What the line under the list says: how many rows are below the fold, or a way back to
    /// the top once the reader has scrolled to the end. Empty when the whole tab shows.</summary>
    public string MoreText => RowsBelow > 0
        ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{RowsBelow} more")
        : RowsAbove > 0 ? "back to top" : "";

    /// <summary>Whether the more line has something to say; its row is reserved either way.</summary>
    public bool HasMore => RowsBelow > 0 || RowsAbove > 0;

    /// <summary>Ink for the more line.</summary>
    public double MoreOpacity => HasMore ? 1 : 0;

    /// <summary>Whether the more line points down (rows below) rather than back to the top.</summary>
    public bool MorePointsDown => RowsBelow > 0;

    partial void OnRowsAboveChanged(int value) => RaiseMore();

    partial void OnRowsBelowChanged(int value) => RaiseMore();

    private void RaiseMore()
    {
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(MoreOpacity));
        OnPropertyChanged(nameof(MorePointsDown));
    }

    /// <summary>Raised when the more line is pressed; the view scrolls the list, since only it knows
    /// where the rows are.</summary>
    public event Action<bool>? MoreRequested;

    /// <summary>Pages the list down by what is showing, or back to the top from the end.</summary>
    [RelayCommand]
    private void ShowMore() => MoreRequested?.Invoke(RowsBelow > 0);

    /// <summary>True when the viewer lives in its own window, whose titlebar carries the name, the
    /// badge and the layout switch; false for the overlay, whose header does.</summary>
    public bool InWindow { get; init; }

    /// <summary>The header row inside the body: shown only where no titlebar carries those.</summary>
    public bool ShowBodyHeader => !InWindow;

    /// <summary>Whether this viewer is shown as an overlay and so needs its own close button. A
    /// window has the operating system's.</summary>
    public bool ShowClose { get; init; }

    /// <summary>Raised when the viewer asks to be closed (the overlay's close button).</summary>
    public event Action? CloseRequested;

    /// <summary>Asks the host to close this viewer.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    /// <summary>The 2D pane is laid out at its share of the body; zero when the layout is 3D only.</summary>
    public GridLength TwoDColumn => Layout == ViewerLayout.ThreeD ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

    /// <summary>The 3D pane's share of the body; zero when the layout is 2D only.</summary>
    public GridLength ThreeDColumn => Layout == ViewerLayout.TwoD ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

    /// <summary>Ink for the 2D pane: 1 when shown, 0 when the layout hides it.</summary>
    public double TwoDOpacity => Layout == ViewerLayout.ThreeD ? 0 : 1;

    /// <summary>Ink for the 3D pane.</summary>
    public double ThreeDOpacity => Layout == ViewerLayout.TwoD ? 0 : 1;

    /// <summary>Whether the 2D pane takes input.</summary>
    public bool TwoDHit => Layout != ViewerLayout.ThreeD;

    /// <summary>Whether the 3D pane takes input.</summary>
    public bool ThreeDHit => Layout != ViewerLayout.TwoD;

    /// <summary>The tile grid when the subject is an archive; null otherwise.</summary>
    public ArchiveGalleryViewModel? Gallery => (Subject as GallerySubject)?.Gallery;

    /// <summary>Whether the body is a tile grid rather than a picture and a solid.</summary>
    public bool IsGallery => Subject is GallerySubject;

    /// <summary>Whether the body is a picture and a solid: shows the panes and the footer.</summary>
    public bool IsNotGallery => !IsGallery;

    /// <summary>Whether the 2D pane names a picture it could not find.</summary>
    public bool ImageMissing => Subject.ImageMissing;

    /// <summary>Whether the layout is 2D only.</summary>
    public bool IsTwoD => Layout == ViewerLayout.TwoD;
    /// <summary>Whether the layout is side by side.</summary>
    public bool IsBoth => Layout == ViewerLayout.Both;
    /// <summary>Whether the layout is 3D only.</summary>
    public bool IsThreeD => Layout == ViewerLayout.ThreeD;

    /// <summary>Ink for the layouts that need a model: dimmed, not disabled, when there is none.</summary>
    public double ThreeDAvailableOpacity => CanShowThreeD ? 1 : 0.45;

    /// <summary>Why the layout control is dimmed; empty when a model exists.</summary>
    public string LayoutNote => CanShowThreeD ? "" : "This subject has no 3D model.";

    /// <summary>The layout switch's tooltip: its keys, or why 3D is dimmed.</summary>
    public string LayoutTip => CanShowThreeD ? KeyHints.ViewerLayoutTip : LayoutNote;

    /// <summary>The live badge: "live" while linked to an open source, otherwise "snapshot".</summary>
    public string LiveBadge => Subject.IsLive ? "live" : "snapshot";

    /// <summary>Whether the viewer still follows its source page: inks the badge's dot.</summary>
    public bool IsLive => Subject.IsLive;

    /// <summary>Selects a layout.</summary>
    /// <param name="layout">The panes to show.</param>
    [RelayCommand]
    private void SetLayout(ViewerLayout layout) => Layout = layout;

    /// <summary>Frees what this view model made: the bitmap and the subscription. The subject is the
    /// host's to dispose.</summary>
    public void Dispose()
    {
        Subject.Changed -= OnSubjectChanged;
        _preview.PropertyChanged -= OnPreviewChanged;
        _artBitmap?.Dispose();
        _artBitmap = null;
    }

    /// <summary>What is shown.</summary>
    public ViewerSubject Subject { get; }

    /// <summary>The 2D pane's state.</summary>
    public TwoDPaneViewModel TwoD { get; } = new();

    /// <summary>Whether there is a model to show; false dims the layout control.</summary>
    public bool CanShowThreeD => Subject.Model != null;

    /// <summary>The panes shown. A layout needing a model is refused to 2D without one.</summary>
    public ViewerLayout Layout
    {
        get => _layout;
        set
        {
            if (!SetProperty(ref _layout, !CanShowThreeD ? ViewerLayout.TwoD : value)) return;
            foreach (var name in new[]
                     {
                         nameof(TwoDColumn), nameof(ThreeDColumn), nameof(TwoDOpacity), nameof(ThreeDOpacity),
                         nameof(TwoDHit), nameof(ThreeDHit), nameof(IsTwoD), nameof(IsBoth), nameof(IsThreeD),
                     })
                OnPropertyChanged(name);
        }
    }

    /// <summary>Whether saving and copying are allowed.</summary>
    public bool CanExport => Subject.AllowExport;

    /// <summary>Says why export is blocked; empty when it is not.</summary>
    public string ExportNote => CanExport ? "" : "sealed · view-only";

    /// <summary>Ink for the sealed note and its lock, on the export heading's own line.</summary>
    public double SealOpacity => CanExport ? 0 : 1;

    /// <summary>Whether the subject is sealed: the titlebar badge row adds a view-only chip.</summary>
    public bool IsSealed => !CanExport;

    /// <summary>Saves the art as a PNG enlarged by a whole-pixel factor (1, 2, 4 or 8).</summary>
    /// <param name="factor">The enlargement factor.</param>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task SaveImage(int factor)
    {
        if (!CanExport || Subject.Art is not { } art) return;
        var target = await _picker.SaveFileAsync($"Save {Subject.Title}", ".png");
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            using var big = Upscale.By(art, factor);
            await big.SaveAsPngAsync(target);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Could not save image", ErrorText.For(ex));
        }
    }

    /// <summary>The enlargement Save image writes at: 1, 2, 4 or 8.</summary>
    [ObservableProperty]
    private int _imageScale = 1;

    /// <summary>Picks the enlargement for Save image.</summary>
    /// <param name="factor">1, 2, 4 or 8.</param>
    [RelayCommand]
    private void SetImageScale(int factor) => ImageScale = factor;

    /// <summary>Whether 1x is the chosen enlargement.</summary>
    public bool IsScale1 => ImageScale == 1;
    /// <summary>Whether 2x is the chosen enlargement.</summary>
    public bool IsScale2 => ImageScale == 2;
    /// <summary>Whether 4x is the chosen enlargement.</summary>
    public bool IsScale4 => ImageScale == 4;
    /// <summary>Whether 8x is the chosen enlargement.</summary>
    public bool IsScale8 => ImageScale == 8;

    partial void OnImageScaleChanged(int value)
    {
        OnPropertyChanged(nameof(IsScale1));
        OnPropertyChanged(nameof(IsScale2));
        OnPropertyChanged(nameof(IsScale4));
        OnPropertyChanged(nameof(IsScale8));
    }

    /// <summary>Gives the current 3D frame as PNG bytes, or null when nothing is drawn. The 3D view
    /// sets this, since only it holds the raster target.</summary>
    public Func<byte[]?>? StillSource { get; set; }

    /// <summary>The format an in-memory model is exported in. A model that came from a file is copied
    /// in the format it has, whatever this says.</summary>
    [ObservableProperty]
    private ModelFormat _exportFormat = ModelFormat.Glb;

    /// <summary>Whether the model being shown is a file whose bytes are copied as they are.</summary>
    public bool HasModelFiles => Subject.Model?.Files is { Count: > 0 };

    /// <summary>Ink for the format choice: dimmed when the file's own format wins.</summary>
    public double ExportFormatOpacity => HasModelFiles ? 0.45 : 1;

    /// <summary>Whether a model can be exported: allowed by the subject and there is one.</summary>
    public bool CanExportModel => CanExport && CanShowThreeD;

    /// <summary>Selects the format for an in-memory model.</summary>
    /// <param name="format">The format.</param>
    [RelayCommand]
    private void SetExportFormat(ModelFormat format) => ExportFormat = format;

    /// <summary>Whether glb is the chosen export format.</summary>
    public bool IsGlb => ExportFormat == ModelFormat.Glb;
    /// <summary>Whether glTF with a .bin is the chosen export format.</summary>
    public bool IsGltf => ExportFormat == ModelFormat.GltfWithBin;
    /// <summary>Whether embedded glTF is the chosen export format.</summary>
    public bool IsGltfEmbedded => ExportFormat == ModelFormat.GltfEmbedded;

    partial void OnExportFormatChanged(ModelFormat value)
    {
        OnPropertyChanged(nameof(IsGlb));
        OnPropertyChanged(nameof(IsGltf));
        OnPropertyChanged(nameof(IsGltfEmbedded));
    }

    /// <summary>Jumps the 3D camera to a named angle.</summary>
    /// <param name="angle">Which side to look from.</param>
    [RelayCommand]
    private void ShowAngle(ViewAngle angle) => Preview.Preset(angle, Preview.ViewportAspect);

    /// <summary>Steps the turntable to the next speed: off, slow, medium, fast, off. One button in the
    /// footer rather than a tray of four, so the strip stays one line at the default window.</summary>
    [RelayCommand]
    private void CycleTurntable() => Preview.Turntable = Preview.Turntable switch
    {
        TurntableSpeed.Off => TurntableSpeed.Slow,
        TurntableSpeed.Slow => TurntableSpeed.Medium,
        TurntableSpeed.Medium => TurntableSpeed.Fast,
        _ => TurntableSpeed.Off,
    };

    /// <summary>The spin button's word for the current speed.</summary>
    public string TurntableText => Preview.Turntable switch
    {
        TurntableSpeed.Slow => "Slow",
        TurntableSpeed.Medium => "Med",
        TurntableSpeed.Fast => "Fast",
        _ => "Off",
    };

    /// <summary>Whether the model is turning: inks the spin button.</summary>
    public bool IsSpinning => Preview.Turntable != TurntableSpeed.Off;

    private void OnPreviewChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModelPreviewViewModel.Turntable)) return;
        OnPropertyChanged(nameof(TurntableText));
        OnPropertyChanged(nameof(IsSpinning));
    }

    /// <summary>Shows or hides the quad outlines over the solid.</summary>
    [RelayCommand]
    private void ToggleWireframe() => Preview.Wireframe = !Preview.Wireframe;

    /// <summary>Saves the current 3D frame as a PNG with a transparent background.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task SaveStill()
    {
        if (!CanExport || StillSource?.Invoke() is not { } png) return;
        var target = await _picker.SaveFileAsync($"Save {Subject.Title} still", ".png");
        if (string.IsNullOrWhiteSpace(target)) return;
        try { await File.WriteAllBytesAsync(target, png); }
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Could not save still", ErrorText.For(ex)); }
    }

    /// <summary>Exports the 3D model. A model that came from a file is copied byte for byte; one built
    /// in memory is written in <see cref="ExportFormat"/>. A model of several files (glTF plus its
    /// .bin) goes to a folder, since the .gltf names the .bin and a rename would break it.</summary>
    [RelayCommand(CanExecute = nameof(CanExportModel))]
    private async Task ExportModel()
    {
        if (!CanExportModel || Subject.Model is not { } model) return;
        try
        {
            var files = model.Files is { Count: > 0 } own
                ? own
                : ModelWriter.WriteAtlas(model.Mesh, model.Texture, new RiseOptions { Format = ExportFormat },
                    FileStem(Subject.Title));
            if (files.Count == 1)
            {
                var target = await _picker.SaveFileAsync($"Export {Subject.Title} model", Path.GetExtension(files[0].FileName));
                if (string.IsNullOrWhiteSpace(target)) return;
                await File.WriteAllBytesAsync(target, files[0].Bytes);
            }
            else
            {
                var folder = await _picker.PickFolderAsync($"Export {Subject.Title} model to");
                if (string.IsNullOrWhiteSpace(folder)) return;
                foreach (var f in files) await File.WriteAllBytesAsync(Path.Combine(folder, f.FileName), f.Bytes);
            }
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Could not export model", ErrorText.For(ex));
        }
    }

    private static string FileStem(string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var stem = new string(title.Select(c => Array.IndexOf(bad, c) >= 0 ? '-' : c).ToArray()).Trim();
        return stem.Length == 0 ? "model" : stem;
    }

    /// <summary>Copies the info rail to the clipboard as text. Refused for a sealed subject, like every
    /// other way of taking something out of it.</summary>
    [RelayCommand(CanExecute = nameof(CanCopyFacts))]
    private async Task CopyFacts()
    {
        if (!CanCopyFacts) return;     // RelayCommand.Execute does not consult CanExecute
        await _clipboard.SetTextAsync(ViewerFact.AsText(Subject.Title, Facts));
    }

    /// <summary>Whether Copy info can do anything: there is something to copy and the subject may leave.</summary>
    public bool CanCopyFacts => CanExport && HasFacts;

    /// <summary>Copies the art to the clipboard as a PNG.</summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Copy()
    {
        if (!CanExport || Subject.Art is not { } art) return;
        using var ms = new MemoryStream();
        await art.SaveAsPngAsync(ms);
        await _clipboard.SetImageAsync(ms.ToArray());
    }
}
