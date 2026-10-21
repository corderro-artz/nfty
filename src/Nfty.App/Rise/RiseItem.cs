using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.Core.Editing;

namespace Nfty.App.Rise;

/// <summary>One row of the Rise page's left rail: a thing that can be risen, with its thumbnail,
/// its checkbox for batch export and, once the source has loaded it, its subject.</summary>
public sealed partial class RiseItem : ObservableObject, IDisposable
{
    private readonly bool _ownsThumbnail;

    /// <summary>Creates a row.</summary>
    /// <param name="id">The source's id for it (a variant id, a number).</param>
    /// <param name="name">Display name.</param>
    /// <param name="caption">The line under the name.</param>
    /// <param name="exportBaseName">File name stem when exported.</param>
    /// <param name="thumbnail">Row picture, if any.</param>
    /// <param name="ownsThumbnail">Whether disposing the row disposes the thumbnail.</param>
    public RiseItem(string id, string name, string caption, string exportBaseName, Bitmap? thumbnail,
        bool ownsThumbnail)
    {
        Id = id;
        Name = name;
        Caption = caption;
        ExportBaseName = exportBaseName;
        _thumbnail = thumbnail;
        _ownsThumbnail = ownsThumbnail;
    }

    /// <summary>The source's id for this row.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>The line under the name.</summary>
    public string Caption { get; }

    /// <summary>File name stem when exported.</summary>
    public string ExportBaseName { get; }

    /// <summary>Row picture.</summary>
    [ObservableProperty] private Bitmap? _thumbnail;

    /// <summary>Whether the row is ticked for batch export.</summary>
    [ObservableProperty] private bool _isChecked;

    /// <summary>Whether the row is the one on the canvas.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>The loaded subject; null until the source loads it.</summary>
    public RiseSubject? Subject { get; private set; }

    internal void Attach(RiseSubject subject)
    {
        Subject = subject;
        RefreshDepth();
        OnPropertyChanged(nameof(IsChipShown));
        OnPropertyChanged(nameof(IsRisableOrUnknown));
    }

    /// <summary>The depth chip: "2D only", "empty", "paper", "f · b", "varied", or "" when not loaded.</summary>
    public string DepthChip
    {
        get
        {
            if (Subject is not { } s) return "";
            if (s.Depth.PartialCount > 0) return "2D only";
            var sum = s.Depth.Summary();
            return sum.Look switch
            {
                DepthLook.Empty => "empty",
                DepthLook.Paper => "paper",
                DepthLook.Uniform => $"{sum.Front} · {sum.Back}",
                _ => "varied",
            };
        }
    }

    /// <summary>Whether the chip warns: the picture has partial alpha and cannot rise.</summary>
    public bool IsChipWarning => Subject is { } s && s.Depth.PartialCount > 0;

    /// <summary>Whether there is a chip to show: the subject is loaded.</summary>
    public bool IsChipShown => Subject is not null;

    /// <summary>Whether the picture can rise, or is not yet loaded and so not known to be unable.</summary>
    public bool IsRisableOrUnknown => Subject?.IsRisable ?? true;

    /// <summary>Whether the loaded subject has unsaved depth.</summary>
    public bool IsDirty => Subject?.IsDirty ?? false;

    /// <summary>Re-raises everything that reads the subject's depth.</summary>
    public void RefreshDepth()
    {
        OnPropertyChanged(nameof(DepthChip));
        OnPropertyChanged(nameof(IsChipWarning));
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Subject?.Dispose();
        if (_ownsThumbnail) Thumbnail?.Dispose();
    }
}
