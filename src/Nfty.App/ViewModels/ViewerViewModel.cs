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

    /// <summary>The live badge: "live" while linked to an open source, otherwise "snapshot".</summary>
    public string LiveBadge => Subject.IsLive ? "live" : "snapshot";

    /// <summary>Selects a layout.</summary>
    /// <param name="layout">The panes to show.</param>
    [RelayCommand]
    private void SetLayout(ViewerLayout layout) => Layout = layout;

    /// <summary>Frees what this view model made: the bitmap and the subscription. The subject is the
    /// host's to dispose.</summary>
    public void Dispose()
    {
        Subject.Changed -= OnSubjectChanged;
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
    public string ExportNote => CanExport ? "" : "View-only: this file was sealed.";

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
            await _dialogs.ShowErrorAsync("Could not save image", ex.Message);
        }
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

    /// <summary>Sets the turntable speed.</summary>
    /// <param name="speed">The speed.</param>
    [RelayCommand]
    private void SetTurntable(TurntableSpeed speed) => Preview.Turntable = speed;

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
        catch (Exception ex) { await _dialogs.ShowErrorAsync("Could not save still", ex.Message); }
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
            await _dialogs.ShowErrorAsync("Could not export model", ex.Message);
        }
    }

    private static string FileStem(string title)
    {
        var bad = Path.GetInvalidFileNameChars();
        var stem = new string(title.Select(c => Array.IndexOf(bad, c) >= 0 ? '-' : c).ToArray()).Trim();
        return stem.Length == 0 ? "model" : stem;
    }

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
