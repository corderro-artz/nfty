using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
        LayoutUpdated += OnDnaFigureLayoutUpdated;
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

    /// <summary>
    /// Tells the ViewModel whether the unique-DNA figure fits its cell, from a rendered frame.
    /// </summary>
    /// <param name="sender">The view.</param>
    /// <param name="e">Unused.</param>
    /// <remarks>
    /// <para><b>The cell used to carry a guarantee and now carries a budget.</b> It was widened
    /// until the widest figure a <c>long</c> could hold fitted at the smallest window, which made
    /// "print every digit" safe by construction. The totals are <c>BigInteger</c> now and have no
    /// widest figure, so the only honest rule left is to measure: print the digits when they fit
    /// the cell the window actually gives, and shorten when they do not. A maximised window on a
    /// wide monitor therefore shows a figure the smallest window abbreviates, which is the right
    /// way round — the room is real and it should be spent on the number.</para>
    ///
    /// <para><b>The INK is measured, not the control.</b> A TextBlock is arranged to its parent and
    /// clips, reporting the width it was asked for either way, so asking the control how wide it is
    /// answers a different question than the one being asked. This is the same measurement
    /// <c>UniqueDnaDisplayTests</c> makes, against the same content box.</para>
    ///
    /// <para><b>It only reads inputs it cannot change.</b> That is the rule the infinite-layout-loop
    /// crash was fixed by, and it holds here for two reasons: the string measured is the FULL
    /// figure, which is a property of the book and never of what is on screen, and the cell sits in
    /// a STAR column, whose width the Grid decides from the room it was given rather than from what
    /// is put in it. Writing the flag can change the text; it cannot change either input, so the
    /// next pass computes the same answer and the setter drops it.</para>
    /// </remarks>
    private void OnDnaFigureLayoutUpdated(object? sender, EventArgs e)
    {
        if (DataContext is not CookBookDetailViewModel vm) return;
        if (this.FindControl<TextBlock>("DnaFigure") is not { } figure) return;
        if (figure.GetVisualAncestors().OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("metric")) is not { Bounds.Width: > 0 } cell)
        {
            return;
        }

        double room = cell.Bounds.Width - cell.Padding.Left - cell.Padding.Right
            - cell.BorderThickness.Left - cell.BorderThickness.Right;

        var ink = new FormattedText(vm.UniqueDnaFullText, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(figure.FontFamily, figure.FontStyle, figure.FontWeight),
            figure.FontSize, Brushes.Black);

        vm.UniqueDnaFits = ink.Width <= room;
    }
}
