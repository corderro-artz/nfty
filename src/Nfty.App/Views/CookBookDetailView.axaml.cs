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

    /// <summary>The host height the page size was last computed from.</summary>
    private double _lastHost;

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
    /// <para>It measures the host panel rather than the ItemsControl, because the ItemsControl hugs
    /// its rows and reports the content it has, not the room it was given — and the host is a
    /// <see cref="ScrollViewer"/>, which reports the height it was GIVEN rather than the height its
    /// content wants. With a plain panel there the rows grew the row that was measured to decide how
    /// many rows fit, and Avalonia threw "Infinite layout loop detected".</para>
    ///
    /// <para><b>THE ROW HEIGHT IS READ FROM A TOKEN, NOT MEASURED, AND THAT IS THE SECOND HALF OF
    /// THE SAME BUG.</b> Measuring a drawn row reintroduced the loop by another route: a row's
    /// measured height wobbles for a pass after the <c>ItemsControl</c> rebuilds its containers, so
    /// a LIVE WINDOW RESIZE wrote a different <c>PageSize</c> on pass after pass inside one render
    /// and Avalonia threw again — the app died a few seconds into dragging the window edge. Only the
    /// host genuinely varies with the window; a row is one line at every size, so it is stated once
    /// in <c>Tokens.axaml</c> and read by both the <c>.crow</c> style and this arithmetic. That
    /// makes the page size a pure function of a height this handler cannot change.</para>
    /// </remarks>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not CookBookDetailViewModel vm) return;
        if (this.FindControl<ScrollViewer>("RowsHost") is not { Bounds.Height: > 0 } host) return;
        if (!this.TryFindResource("DnaRowHeight", ActualThemeVariant, out object? token)
            || token is not double rowHeight || rowHeight <= 0)
        {
            return;
        }

        // Recompute only when the HOST changed. That is the one input, and writing PageSize cannot
        // change it - a star row is sized by the Grid, not by what is put in it.
        if (host.Bounds.Height == _lastHost) return;
        _lastHost = host.Bounds.Height;

        int fits = Math.Max(1, (int)(host.Bounds.Height / rowHeight));
        if (fits != vm.PageSize) vm.PageSize = fits;
    }
}
