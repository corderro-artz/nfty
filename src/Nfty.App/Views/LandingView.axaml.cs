using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The landing view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class LandingView : UserControl
{
    /// <summary>Nominal card width. How many cards a page holds is a function of the RENDERED width,
    /// which only the view knows — so the view measures and tells the shelf, and the shelf paginates.
    /// Cards then stretch to fill the row exactly, so a wider window shows more per page rather than
    /// leaving a ragged gap at the end.</summary>
    private const double CardWidth = 176;

    /// <summary>The gap between cards, matching the card template's own right margin.</summary>
    private const double CardGap = 9;

    /// <summary>Loads the view.</summary>
    public LandingView()
    {
        AvaloniaXamlLoader.Load(this);

        var row = this.FindControl<ItemsControl>("ShelfRow")!;
        // The row's width decides the page size. Watching the row rather than the window means a
        // change to the band's padding cannot silently desynchronise the two.
        row.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(b => Remeasure(b.Width)));

        var shelf = this.FindControl<Border>("KitchenShelf")!;
        // Wheel pages, and only swallows the event when the shelf can act on it — at either end the
        // wheel keeps meaning whatever it meant before, rather than the band trapping the pointer.
        shelf.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (Shelf is not { } vm) return;
            int delta = e.Delta.Y > 0 ? -1 : 1;          // wheel up = back a page
            if (delta > 0 ? !vm.CanNext : !vm.CanPrev) return;
            vm.Page(delta);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // And a keyboard path, because a pointer-only control is the incomplete version of one.
        shelf.KeyDown += (_, e) =>
        {
            if (Shelf is not { } vm) return;
            int delta = e.Key switch
            {
                Key.PageDown or Key.Right or Key.Down => 1,
                Key.PageUp or Key.Left or Key.Up => -1,
                _ => 0,
            };
            if (delta == 0) return;
            vm.Page(delta);
            e.Handled = true;
        };
    }

    private KitchenShelfViewModel? Shelf => (DataContext as LandingViewModel)?.KitchenShelf;

    private void Remeasure(double width)
    {
        if (Shelf is not { } vm || double.IsNaN(width) || width <= 0) return;
        // +Gap on both sides of the division: N cards carry N-1 gaps between them, so adding one
        // notional gap to the available width makes the arithmetic exact rather than one-out.
        vm.PageSize = Math.Max(1, (int)((width + CardGap) / (CardWidth + CardGap)));
    }

    /// <summary>Takes initial focus once the view is on screen.</summary>
    /// <param name="e">Ignored beyond forwarding to the base implementation.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    /// <summary>Minimal <see cref="IObserver{T}"/> for an Avalonia property subscription — the
    /// framework hands out <c>IObservable</c> and the BCL has no lambda overload for it.</summary>
    private sealed class AnonymousObserver<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T value) => onNext(value);
    }
}
