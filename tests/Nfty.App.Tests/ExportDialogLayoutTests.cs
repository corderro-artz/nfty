using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The export dialog's geometry at the smallest window the app allows.
/// </summary>
/// <remarks>
/// <para><b>"Fits the window" and "nothing scrolls" are two different claims</b>, and only the first
/// is true here. <c>ModalFitTests</c> proves the card is not cut off by the window. This file proves
/// the stronger thing that actually matters about it: the controls you have to SEE in order to use
/// it are inside the viewport, in both states, and the two things that must never move are outside
/// the scroller altogether.</para>
///
/// <para>The distinction is not academic. The first cut of this dialog fitted the window perfectly
/// and put the manifest — the one control a user must read before pressing Export — below the fold
/// whenever the seal panel was open. Every ViewModel test passed. It took a rendered frame to see,
/// and these are the assertions that stop it coming back.</para>
/// </remarks>
public class ExportDialogLayoutTests
{
    private static (Window Window, Views.ExportDialogView View) Open(bool sealing,
        double windowHeight = ShellViewModel.MinWindowHeight)
    {
        var vm = new ExportDialogViewModel(VisualCapture.ExportCaptureSet(), new FilePickerService(),
            new NoopFolderRevealer(), new FakeDialogs()) { IsSealed = sealing };
        var view = new Views.ExportDialogView { DataContext = vm };

        // The page area at the app's own minimum window, which is the worst case the card ever has
        // to draw in - not a comfortable size that would hide the problem.
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = (windowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    /// <summary>Opens the card on one page, unsealed.</summary>
    private static (Window Window, Views.ExportDialogView View) OpenTab(ExportTab tab, double windowHeight)
    {
        var vm = new ExportDialogViewModel(VisualCapture.ExportCaptureSet(), new FilePickerService(),
            new NoopFolderRevealer(), new FakeDialogs()) { Tab = tab };
        var view = new Views.ExportDialogView { DataContext = vm };
        var window = new Window
        {
            Content = view,
            Width = ShellViewModel.MinWindowWidth / ShellViewModel.BaseScale,
            Height = windowHeight,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    [AvaloniaTheory]
    // The smallest window the app allows, where the card fills the height it is given...
    [InlineData((ShellViewModel.MinWindowHeight - ShellViewModel.ChromeReserve) / ShellViewModel.BaseScale)]
    // ...and a tall one, where it HUGS. The second is the case that can differ and the reason this
    // is a theory: measuring only at the minimum tests a card that is being stretched to one height
    // by its host, which agrees with every page layout there is.
    [InlineData(1000)]
    public void SWITCHING_TABS_MOVES_NOTHING_BELOW_THEM(double windowHeight)
    {
        // The manifest and the footer are the two controls on this card a reader must never have to
        // go looking for: one says what is going, the other says what is stopping it. Three pages of
        // three different heights would slide both of them every time a tab was clicked - and a card
        // that resizes under the pointer that just acted is the geometry rule this project keeps
        // everywhere else.
        var heights = new List<double>();
        foreach (ExportTab tab in Enum.GetValues<ExportTab>())
        {
            var (window, view) = OpenTab(tab, windowHeight);
            try
            {
                var card = view.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains("modal"));
                heights.Add(card.Bounds.Height);
            }
            finally { window.Close(); }
        }

        Assert.All(heights, h => Assert.Equal(heights[0], h, 1));
    }

    private static ScrollViewer Scroller(Visual view) =>
        view.GetVisualDescendants().OfType<ScrollViewer>().First();

    /// <summary>Whether <paramref name="control"/> lies entirely inside the scroller's viewport as
    /// it is currently scrolled — i.e. whether a user can see the whole of it right now.</summary>
    private static bool FullyVisibleIn(ScrollViewer scroller, Visual control)
    {
        var topLeft = control.TranslatePoint(default, scroller);
        if (topLeft is not { } p) return false;
        var b = control.Bounds;
        return p.Y >= -0.5 && p.Y + b.Height <= scroller.Viewport.Height + 0.5
            && p.X >= -0.5 && p.X + b.Width <= scroller.Viewport.Width + 0.5;
    }

    [AvaloniaFact]
    public void At_an_ordinary_window_the_unsealed_form_needs_no_scrolling()
    {
        // 860, not the minimum. This test used to run at the minimum and assert the same thing, and
        // it was true until the window minimum came DOWN from 924 to 712 so the app could open on a
        // 1366x768 laptop. At 712 there is simply less page than this form has content, and the
        // honest response is to move the claim rather than to keep asserting it somewhere it stopped
        // holding. What is guaranteed AT the minimum is the pair below: the passphrase stays on
        // screen, and the manifest and footer are outside the scroller entirely.
        var (window, view) = Open(sealing: false, windowHeight: 860);
        try
        {
            var scroller = Scroller(view);
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"the unsealed form needs {scroller.Extent.Height:0} of {scroller.Viewport.Height:0} "
                + "available at an ordinary window size, so it scrolls when it should not");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Armed_for_sealing_the_passphrase_boxes_are_still_on_screen()
    {
        // Sealing adds two fields, a note and a caveat. That used to be about 85px more than the
        // smallest window could show; the card fits now, so at the app's own minimum nothing
        // scrolls and this passes without the reveal doing anything - which is precisely why the
        // sibling test below pins a window where it still does. What is asserted here is the claim
        // that has to hold at the size users actually get: the passphrase pair is on screen.
        //
        // ARMED AFTER LAYOUT, which is what a user does. Constructing the view already sealed skips
        // the transition, and the transition IS the behaviour: an earlier version of this test did
        // that and passed on a card that happened to be eight pixels tall enough, with nothing
        // scrolling anything into view at all.
        var (window, view) = Open(sealing: false);
        try
        {
            ((ExportDialogViewModel)view.DataContext!).IsSealed = true;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var scroller = Scroller(view);
            var boxes = view.GetVisualDescendants().OfType<TextBox>()
                .Where(t => t.PasswordChar != '\0').ToList();

            Assert.Equal(2, boxes.Count);
            Assert.All(boxes, b => Assert.True(FullyVisibleIn(scroller, b),
                "a passphrase box is not fully on screen when sealing is armed - the reveal has to "
                + "scroll it into view, because at the smallest window it does not land there"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void In_a_short_window_the_reveal_scrolls_the_passphrase_into_view()
    {
        // At today's minimum window the passphrase pair lands above the fold with room to spare, so
        // the test above passes with or without the scroll - which makes it no evidence for the
        // scroll at all. This one is set at a height where it does NOT land there, and fails
        // outright if the reveal stops scrolling to it.
        //
        // It was 780, chosen when the card was 254px taller than it is now and overflowed at every
        // window the app allows. The card has since been measured against the page area a modal is
        // actually given rather than against a capture frame, and it now fits the 720 minimum with
        // 30px to spare and stops scrolling at all above a ~700 window. So 780 stopped being short
        // and this assertion stopped meaning anything - which the check below is here to say out
        // loud rather than let it rot into a green test of nothing. 680 is measured: the card
        // clamps to 481 there and the body wants 251 in 236. The scroll path is still worth
        // guarding, because a longer caveat or one more sealed field brings it straight back.
        var (window, view) = Open(sealing: false, windowHeight: 680);
        try
        {
            ((ExportDialogViewModel)view.DataContext!).IsSealed = true;
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var scroller = Scroller(view);
            var boxes = view.GetVisualDescendants().OfType<TextBox>()
                .Where(t => t.PasswordChar != ' ').ToList();

            Assert.Equal(2, boxes.Count);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height,
                "this window is meant to be too short to show the whole form; if it is not, the "
                + "test is no longer exercising the scroll and the height needs lowering");
            Assert.All(boxes, b => Assert.True(FullyVisibleIn(scroller, b),
                "the passphrase box is off screen: arming the seal has to scroll it into view"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_manifest_and_the_footer_are_outside_the_scroller_entirely()
    {
        // Not "currently visible" - OUTSIDE it. Anything inside the scroller can be scrolled away by
        // definition, and these two are the summary of what is about to leave the machine and the
        // button that sends it. Asserted structurally rather than positionally so it cannot pass by
        // luck of the current scroll offset.
        var (window, view) = Open(sealing: true);
        try
        {
            var scroller = Scroller(view);

            var manifest = view.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("ship"));
            var footer = view.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("modal-foot"));

            Assert.DoesNotContain(manifest, scroller.GetVisualDescendants());
            Assert.DoesNotContain(footer, scroller.GetVisualDescendants());

            // ...and both are actually drawn, so "outside the scroller" cannot be satisfied by being
            // outside the card as well.
            Assert.True(manifest.Bounds.Height > 0);
            Assert.True(footer.Bounds.Height > 0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_card_never_exceeds_the_space_it_is_given()
    {
        // This card is ADAPTIVE - it takes the height it is handed rather than demanding one - so
        // the guarantee is not "the content fits" but "the card cannot overflow", which holds at any
        // size and is what stops the footer ever being sliced. Checked at the app's own minimum,
        // which is the tightest it will ever be.
        var (window, view) = Open(sealing: true);
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>()
                .First(b => b.Classes.Contains("modal"));
            Assert.True(card.Bounds.Height <= view.Bounds.Height + 0.5,
                $"the card is {card.Bounds.Height:0} in {view.Bounds.Height:0} of page");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Given_a_taller_window_the_sealed_form_stops_scrolling_altogether()
    {
        // The other half of adaptive, and the reason it is worth having: at the minimum window the
        // sealed form is about 85px more than can be shown and the note field goes under the fold.
        // Given a full-screened window it simply uses the room, and nothing is hidden at all. A
        // fixed-height card would scroll identically on a 4K monitor.
        var (window, view) = Open(sealing: true, windowHeight: 1200);
        try
        {
            var scroller = Scroller(view);
            Assert.True(scroller.Extent.Height <= scroller.Viewport.Height + 0.5,
                $"still scrolling at 1200: needs {scroller.Extent.Height:0} of "
                + $"{scroller.Viewport.Height:0}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_export_button_is_reachable_in_both_states()
    {
        // The footer is pinned, but "pinned" is a claim about the layout tree and this is the claim
        // about the user: the button that performs the action is on screen whatever the form above
        // it is doing.
        foreach (var sealing in new[] { false, true })
        {
            var (window, view) = Open(sealing);
            try
            {
                var button = view.GetVisualDescendants().OfType<Button>()
                    .Single(b => (b.Content as string) == "Export");
                var p = button.TranslatePoint(default, view);

                Assert.NotNull(p);
                Assert.True(p!.Value.Y + button.Bounds.Height <= view.Bounds.Height + 0.5,
                    $"Export is below the card's bottom edge when sealing is {sealing}");
                Assert.True(button.Bounds.Height > 0);
            }
            finally { window.Close(); }
        }
    }
}
