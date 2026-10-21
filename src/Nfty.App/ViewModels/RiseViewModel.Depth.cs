using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Rise;
using Nfty.Core.Editing;

namespace Nfty.App.ViewModels;

/// <summary>One bar of the thickness histogram.</summary>
/// <param name="Label">"3 voxels" (or "1 voxel").</param>
/// <param name="Count">Solid pixels with this thickness.</param>
/// <param name="CountText">The count as the table prints it.</param>
/// <param name="Share">The fraction of the solid pixels, 0..1; the bar's width as a fraction of its track.</param>
public sealed record ThicknessRow(string Label, int Count, string CountText, double Share);

public sealed partial class RiseViewModel
{
    // ---------------------------------------------------------------- tabs

    /// <summary>The rail shows DEPTH (true) or EXPORT (false).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsExportTab))]
    private bool _isDepthTab = true;

    /// <summary>The rail shows EXPORT.</summary>
    public bool IsExportTab => !IsDepthTab;

    /// <summary>Opens the DEPTH tab.</summary>
    [RelayCommand] private void ShowDepthTab() => IsDepthTab = true;

    /// <summary>Opens the EXPORT tab.</summary>
    [RelayCommand] private void ShowExportTab() => IsDepthTab = false;

    // ---------------------------------------------------------------- fill all

    /// <summary>Front depth the fill gives every solid pixel, 0..255.</summary>
    [ObservableProperty] private int _fillFront = 1;

    /// <summary>Back depth the fill gives every solid pixel, 0..255.</summary>
    [ObservableProperty] private int _fillBack = 1;

    // The ceiling belongs to the property, so a typed 300 and a spinner cannot disagree about it.
    partial void OnFillFrontChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 255);
        if (clamped != value) FillFront = clamped;
    }

    partial void OnFillBackChanged(int value)
    {
        int clamped = Math.Clamp(value, 0, 255);
        if (clamped != value) FillBack = clamped;
    }

    /// <summary>Gives every solid pixel the fill depth, as one undo step.</summary>
    [RelayCommand(CanExecute = nameof(IsPaintable))]
    private void FillAll()
    {
        if (!IsPaintable) return;      // RelayCommand.Execute does not consult CanExecute
        CancelDepthPreview();
        bool changed = Subject.History.Do(new DepthFillAll((byte)FillFront, (byte)FillBack), Surface);
        if (!changed) return;
        RebuildMesh();
        AfterDepthEdit();
    }

    /// <summary>The fill button's label.</summary>
    public string FillAllLabel
    {
        get
        {
            int n = Surface.SolidCount;
            return string.Create(CultureInfo.InvariantCulture, $"Apply to {n:N0} solid {(n == 1 ? "pixel" : "pixels")}");
        }
    }

    // ---------------------------------------------------------------- histogram

    /// <summary>"32 solid px".</summary>
    public string SolidHeader => string.Create(CultureInfo.InvariantCulture, $"{Surface.SolidCount:N0} solid px");

    /// <summary>One row per thickness present, thinnest first.</summary>
    public IReadOnlyList<ThicknessRow> Thickness
    {
        get
        {
            var s = Surface;
            int solid = s.SolidCount;
            return s.Histogram().Select(b => new ThicknessRow(
                string.Create(CultureInfo.InvariantCulture, $"{b.Total} {(b.Total == 1 ? "voxel" : "voxels")}"),
                b.Count,
                b.Count.ToString("N0", CultureInfo.InvariantCulture),
                solid == 0 ? 0 : (double)b.Count / solid)).ToList();
        }
    }

    /// <summary>"deepest 3", or an em dash when nothing is solid.</summary>
    public string DeepestText
    {
        get
        {
            var h = Surface.Histogram();
            return h.Count == 0 ? "—" : string.Create(CultureInfo.InvariantCulture, $"deepest {h[^1].Total}");
        }
    }

    // ---------------------------------------------------------------- 2D only

    /// <summary>Items that cannot rise, with their partial-pixel counts.</summary>
    public IReadOnlyList<PartialAlphaRow> PartialAlphaRows => Source.PartialAlphaRows;

    /// <summary>Whether any item is listed under 2D ONLY.</summary>
    public bool HasPartialAlphaRows => PartialAlphaRows.Count > 0;

    /// <summary>"1 item" / "2 items".</summary>
    public string PartialAlphaCountText
    {
        get
        {
            int n = PartialAlphaRows.Count;
            return string.Create(CultureInfo.InvariantCulture, $"{n} {(n == 1 ? "item" : "items")}");
        }
    }

    /// <summary>The explanation under the depth tools, for the selected item.</summary>
    public string DepthNote => Subject.Depth.PartialCount > 0
        ? "This item has partial alpha, so it stays 2D: a voxel is solid or empty."
        : Source.DepthNote;

    /// <summary>Every item in the source is flat 2D art.</summary>
    public bool BookIsTwoDOnly => Source.IsTwoDOnly;

    partial void OnDepthEdited()
    {
        OnPropertyChanged(nameof(Thickness));
        OnPropertyChanged(nameof(DeepestText));
        OnPropertyChanged(nameof(FillAllLabel));
        OnPropertyChanged(nameof(SolidHeader));
        OnPropertyChanged(nameof(HasUnsavedDepth));
        OnPropertyChanged(nameof(FooterText));
        SaveCommand.NotifyCanExecuteChanged();
    }
}
