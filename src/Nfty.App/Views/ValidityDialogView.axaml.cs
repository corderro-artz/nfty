using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace Nfty.App.Views;

/// <summary>The validation dialog view. Besides loading the XAML it folds the problem list on a
/// whole row, so the last visible problem is never sliced by the footer.</summary>
public partial class ValidityDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ValidityDialogView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => FoldList();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Caps the list at the bottom edge of the last row that fits.
    /// </summary>
    /// <remarks>
    /// <para>Rows wrap, so they are not one height and the fold is a list of edges, not a multiple.
    /// The card HUGS its content, so the room cannot be read off the list's own row (that is the card
    /// shrinking round the cap it just wrote and never growing back): it is derived from what the
    /// card is offered - the view's height less the card's margin, capped at the card's
    /// <c>MaxHeight</c> - less the card's chrome (header, footer and padding), which does not depend
    /// on the list. Exact at scroll offset 0.</para>
    /// </remarks>
    private void FoldList()
    {
        if (this.FindControl<ScrollViewer>("ProblemScroll") is not { IsVisible: true } scroll) return;
        if (scroll.Content is not ItemsControl { ItemsPanelRoot: { } panel }) return;
        var card = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Classes.Contains("modal"));
        if (card is null || Bounds.Height <= 0 || scroll.Bounds.Height <= 0) return;

        double offered = Math.Min(card.MaxHeight, Bounds.Height - card.Margin.Top - card.Margin.Bottom);
        double chrome = card.Bounds.Height - scroll.Bounds.Height;
        double room = offered - chrome - scroll.Margin.Top - scroll.Margin.Bottom;
        if (room <= 0 || panel.Children.Count == 0) return;

        double target = double.PositiveInfinity;
        if (panel.Bounds.Height > room + 0.5)
        {
            double best = panel.Children[0].Bounds.Bottom;
            foreach (var row in panel.Children)
                if (row.Bounds.Bottom <= room + 0.5) best = row.Bounds.Bottom;
            target = best;
        }

        bool same = double.IsInfinity(target)
            ? double.IsInfinity(scroll.MaxHeight)
            : !double.IsInfinity(scroll.MaxHeight) && Math.Abs(scroll.MaxHeight - target) <= 0.5;
        if (!same) scroll.MaxHeight = target;
    }
}
