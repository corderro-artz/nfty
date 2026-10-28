using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.App.Viewer;
using Nfty.Core.Imaging;
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
public sealed partial class ViewerViewModel : ViewModelBase
{
    private readonly IFilePickerService _picker;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;
    private ViewerLayout _layout;

    /// <summary>Builds the viewer for a subject.</summary>
    /// <param name="subject">What to show; the caller keeps ownership.</param>
    /// <param name="picker">Save dialogs.</param>
    /// <param name="clipboard">The clipboard.</param>
    /// <param name="dialogs">Error cards.</param>
    public ViewerViewModel(ViewerSubject subject, IFilePickerService picker, IClipboardService clipboard,
        IDialogService dialogs)
    {
        Subject = subject;
        _picker = picker;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _layout = subject.Model != null ? ViewerLayout.Both : ViewerLayout.TwoD;
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
        set => SetProperty(ref _layout, !CanShowThreeD ? ViewerLayout.TwoD : value);
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
