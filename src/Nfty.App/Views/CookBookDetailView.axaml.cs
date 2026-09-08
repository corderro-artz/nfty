using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The cook book detail view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class CookBookDetailView : UserControl
{
    /// <summary>Loads the view.</summary>
    public CookBookDetailView()
    {
        InitializeComponent();
        LayoutUpdated += OnLayoutUpdated;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The host and row heights the page size was last computed from.</summary>
    private double _lastHost;
    /// <inheritdoc cref="_lastHost"/>
    private double _lastRow;

    /// <summary>
    /// Tells the ViewModel how many rows fit, from a row it has actually drawn.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// <para><b>Measured, never declared.</b> This is the whole "breathes with the window" behavior:
    /// two rows at the smallest window the app opens, five at the size it opens at, the whole book
    /// at full screen. A constant here would be right at one window size and wrong at every other,
    /// and the height of a row is not something the markup states — it falls out of the font, the
    /// badge strip and the row's own band.</para>
    ///
    /// <para>It measures a REAL row rather than a probe, so it cannot disagree with what is drawn —
    /// and it measures the host panel rather than the ItemsControl, because the ItemsControl hugs
    /// its rows and reports the content it has, not the room it was given.</para>
    ///
    /// <para>Writing <c>PageSize</c> from a layout pass would loop if it wrote every pass; the
    /// guard is that it only writes a DIFFERENT value. That is only sufficient because the host is a
    /// ScrollViewer, which reports the height it was GIVEN rather than the height its content wants
    /// — with a plain panel there the rows grew the row that was measured to decide how many rows
    /// fit, and Avalonia threw "Infinite layout loop detected".</para>
    /// </remarks>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not CookBookDetailViewModel vm) return;
        if (this.FindControl<ScrollViewer>("RowsHost") is not { Bounds.Height: > 0 } host) return;

        var row = host.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("crow"));
        if (row is null || row.Bounds.Height <= 0) return;

        // Recompute only when an INPUT changed. Writing PageSize schedules another layout pass, so
        // a handler that recomputed unconditionally would keep the frame busy even once it had
        // settled; this makes the steady state cost nothing and the loop impossible to re-enter on
        // its own output.
        if (host.Bounds.Height == _lastHost && row.Bounds.Height == _lastRow) return;
        _lastHost = host.Bounds.Height;
        _lastRow = row.Bounds.Height;

        int fits = Math.Max(1, (int)(host.Bounds.Height / row.Bounds.Height));
        if (fits != vm.PageSize) vm.PageSize = fits;
    }
}
