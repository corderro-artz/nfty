using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The Make voxel-ready preview card. Code-behind is limited to loading the XAML.</summary>
public partial class VoxelReadyDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public VoxelReadyDialogView()
    {
        InitializeComponent();
    }

    // A row (Border.vrrow, 56) plus the 4px under it, both stated in markup and styles.
    private const double RowPitch = 60;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        LayoutUpdated += (_, _) => SnapToWholeRows();
    }

    // The list shows a whole number of rows, so the fold never slices one. It reads only what it cannot
    // change: the card's ceiling and the heights of the heading and the footer, which do not depend on
    // the list; writing the list's MaxHeight therefore cannot move its own input.
    private void SnapToWholeRows()
    {
        var card = this.FindControl<Border>("Card");
        var head = this.FindControl<Border>("Head");
        var foot = this.FindControl<Border>("Foot");
        var scroll = this.FindControl<ScrollViewer>("RowsScroll");
        if (card is null || head is null || foot is null || scroll is null) return;
        if (head.Bounds.Height <= 0 || foot.Bounds.Height <= 0) return;
        double room = card.MaxHeight - head.Bounds.Height - foot.Bounds.Height
            - scroll.Margin.Top - scroll.Margin.Bottom;
        double snapped = Math.Max(RowPitch, Math.Floor(room / RowPitch) * RowPitch);
        if (double.IsNaN(scroll.MaxHeight) || Math.Abs(scroll.MaxHeight - snapped) > 0.5) scroll.MaxHeight = snapped;
    }

    /// <inheritdoc />
    /// <remarks>The rows' bitmaps are freed here, when the card has actually left the screen, and not the
    /// moment the dialog's await returns: until the view detaches, an <c>Image</c> may still read its
    /// source during a layout pass, and reading a disposed bitmap there throws.</remarks>
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as IDisposable)?.Dispose();
    }
}
