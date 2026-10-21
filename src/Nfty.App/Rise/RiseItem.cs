using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.App.ViewModels;
using Nfty.Core.Editing;
using Nfty.Model;

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
    /// <param name="thumbnailRow">A Set browser row whose lazily decoded picture this item shows straight
    /// through; never snapshotted, so nothing decodes until a view reads <see cref="Thumbnail"/>.</param>
    public RiseItem(string id, string name, string caption, string exportBaseName, Bitmap? thumbnail,
        bool ownsThumbnail, SetItemRow? thumbnailRow = null)
    {
        _row = thumbnailRow;
        Id = id;
        Name = name;
        _caption = caption;
        _exportBaseName = exportBaseName;
        _thumbnail = thumbnail;
        _ownsThumbnail = ownsThumbnail;
    }

    /// <summary>The source's id for this row.</summary>
    public string Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    private string _caption;
    private string _exportBaseName;

    /// <summary>The line under the name.</summary>
    public string Caption => _caption;

    /// <summary>File name stem when exported.</summary>
    public string ExportBaseName => _exportBaseName;

    /// <summary>Changes the caption and export stem of a row whose subject was replaced in place (a
    /// rerolled recipe keeps its row, so the page's selection survives).</summary>
    internal void Relabel(string caption, string exportBaseName)
    {
        _caption = caption;
        _exportBaseName = exportBaseName;
        OnPropertyChanged(nameof(Caption));
        OnPropertyChanged(nameof(ExportBaseName));
    }

    /// <summary>Row picture.</summary>
    public Bitmap? Thumbnail
    {
        get => _row is { } r ? r.Thumbnail : _thumbnail;   // reading a Set row's picture is what starts its decode
        set
        {
            if (ReferenceEquals(_thumbnail, value)) return;
            _thumbnail = value;
            OnPropertyChanged();
        }
    }

    private Bitmap? _thumbnail;
    private readonly SetItemRow? _row;

    /// <summary>Tells the view the row's picture changed (its decode landed).</summary>
    internal void NotifyThumbnail() => OnPropertyChanged(nameof(Thumbnail));

    /// <summary>Whether the row is ticked for batch export.</summary>
    [ObservableProperty] private bool _isChecked;

    /// <summary>Whether the row is the one on the canvas.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>The loaded subject; null until the source loads it.</summary>
    public RiseSubject? Subject { get; private set; }

    internal void Attach(RiseSubject subject)
    {
        var old = Replace(subject);
        if (old is not null && !ReferenceEquals(old, subject)) old.Dispose();
    }

    /// <summary>Puts a new subject on the row and hands back the old one UNDISPOSED, for a caller that
    /// must tell the page before the old picture goes: the page's bindings still read it until they
    /// have been told.</summary>
    /// <param name="subject">The new subject.</param>
    /// <returns>The subject it replaced, or null.</returns>
    internal RiseSubject? Replace(RiseSubject subject)
    {
        var old = Subject;
        Subject = subject;
        RefreshDepth();
        OnPropertyChanged(nameof(IsChipShown));
        OnPropertyChanged(nameof(IsRisableOrUnknown));
        return old;
    }

    /// <summary>The depth chip: "2D only", "empty", "paper", "f · b", "varied", or "" when not loaded.</summary>
    public string DepthChip
    {
        get
        {
            if (Subject is { } s) return ChipFor(s.Depth);
            return _skipped switch { SkipReason.PartialAlpha => "2D only", SkipReason.NothingSolid => "empty", _ => _forgottenChip ?? "" };
        }
    }

    /// <summary>The chip wording for a surface: "2D only", "empty", "paper", "f · b" or "varied".</summary>
    /// <param name="depth">The surface to describe.</param>
    /// <returns>The chip text.</returns>
    public static string ChipFor(DepthSurface depth)
    {
        ArgumentNullException.ThrowIfNull(depth);
        if (depth.PartialCount > 0) return "2D only";
        var sum = depth.Summary();
        return sum.Look switch
        {
            DepthLook.Empty => "empty",
            DepthLook.Paper => "paper",
            DepthLook.Uniform => $"{sum.Front} · {sum.Back}",
            _ => "varied",
        };
    }

    /// <summary>Whether the chip warns: the picture has partial alpha and cannot rise.</summary>
    public bool IsChipWarning => Subject is { } s ? s.Depth.PartialCount > 0 : _skipped == SkipReason.PartialAlpha;

    /// <summary>Whether there is a chip to show: the subject is loaded.</summary>
    public bool IsChipShown => Subject is not null || _skipped is not null || _forgottenChip is not null;

    /// <summary>Whether the picture can rise, or is not yet loaded and so not known to be unable.</summary>
    public bool IsRisableOrUnknown => _skipped is null && (Subject?.IsRisable ?? true);

    private SkipReason? _skipped;

    /// <summary>An export built this picture, found it cannot rise, and left it out: from now on it is
    /// known to be 2D without ever having been loaded onto the page, so the manifest stops promising it.</summary>
    /// <param name="reason">Why the writer refused it.</param>
    internal void MarkSkipped(SkipReason reason)
    {
        _skipped = reason;
        IsChecked = false;
        OnPropertyChanged(nameof(IsRisableOrUnknown));
        OnPropertyChanged(nameof(IsChipShown));
        OnPropertyChanged(nameof(IsChipWarning));
        OnPropertyChanged(nameof(DepthChip));
    }

    private int _forgottenPartial;
    private string? _forgottenChip;

    /// <summary>Drops the chip remembered from a let-go subject: the depth it described is no longer
    /// what the item would load with (a Set found its book).</summary>
    internal void ForgetChip()
    {
        if (_forgottenChip is null) return;
        _forgottenChip = null;
        RefreshDepth();
        OnPropertyChanged(nameof(IsChipShown));
    }

    /// <summary>Partially transparent pixels: the loaded subject's, or what it had when it was let go.</summary>
    public int PartialCount => Subject?.Depth.PartialCount ?? _forgottenPartial;

    /// <summary>Takes the subject off the row and hands it back UNDISPOSED; what is known about it
    /// (that it cannot rise, and why) stays on the row, so its chip, the 2D ONLY list and the export
    /// manifest say the same as while it was loaded.</summary>
    /// <returns>The subject, or null when none was loaded.</returns>
    internal RiseSubject? Forget()
    {
        if (Subject is not { } old) return null;
        _forgottenPartial = old.Depth.PartialCount;
        _forgottenChip = ChipFor(old.Depth);
        if (_forgottenPartial > 0) _skipped = SkipReason.PartialAlpha;
        else if (old.Depth.SolidCount == 0) _skipped = SkipReason.NothingSolid;
        Subject = null;
        RefreshDepth();
        OnPropertyChanged(nameof(IsChipShown));
        OnPropertyChanged(nameof(IsRisableOrUnknown));
        return old;
    }

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
        if (_ownsThumbnail) _thumbnail?.Dispose();
    }
}
