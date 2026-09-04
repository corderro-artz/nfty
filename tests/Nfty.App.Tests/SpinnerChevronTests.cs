using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The NumericUpDown stepper, measured off a laid-out frame. Its template is hand-authored
/// (<c>Controls.axaml</c>) because Fluent's own draws two side-by-side buttons about 32px each, so a
/// numeric field spent most of its width on chevrons; and because Fluent painted a DISABLED spinner
/// button lighter than its neighbours, making the one control in the app that got brighter when it
/// stopped working.
/// </summary>
public class SpinnerChevronTests
{
    private static (Window window, Views.NewCookBookView view) Render()
    {
        var vm = new NewCookBookViewModel(new FakeDialogs()) { Name = "Vapor Pets", Symbol = "VP" };
        var view = new Views.NewCookBookView { DataContext = vm };
        var window = new Window { Content = view, Width = 1180, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static RepeatButton Part(NumericUpDown n, string name) =>
        n.GetVisualDescendants().OfType<RepeatButton>().First(b => b.Name == name);

    /// <summary>
    /// A stepper at its floor reads as unavailable, never as hovered. Asserted as a RELATION — the
    /// unavailable half is dimmer than the available one — because the bug was a direction, not a
    /// value: Fluent's disabled wash is lighter than the button's own ground.
    /// </summary>
    [AvaloniaFact]
    public void A_stepper_at_its_limit_is_dimmer_than_the_half_that_still_works()
    {
        var (window, view) = Render();
        try
        {
            // Target supply opens at 0, which is its Minimum — so Decrease is the disabled half.
            var supply = view.GetVisualDescendants().OfType<NumericUpDown>()
                .First(n => n.Minimum == 0 && n.Value == 0);
            var down = Part(supply, "PART_DecreaseButton");
            var up = Part(supply, "PART_IncreaseButton");

            Assert.False(down.IsEffectivelyEnabled, "the floor half should be disabled at Minimum");
            Assert.True(up.IsEffectivelyEnabled);
            Assert.True(down.Opacity < up.Opacity,
                $"the unavailable chevron is at {down.Opacity} against the working one at {up.Opacity}");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Every spinner in the app gives its chevrons one narrow column, not half the field. Swept
    /// rather than sampled, and as a ratio rather than a number, so it survives a change to either
    /// the token or any one field's width.
    /// </summary>
    [AvaloniaFact]
    public void No_spinner_spends_more_than_a_third_of_its_field_on_chevrons()
    {
        var (window, view) = Render();
        try
        {
            var steppers = view.GetVisualDescendants().OfType<NumericUpDown>()
                .Where(n => n.Bounds.Width > 0).ToList();
            Assert.True(steppers.Count >= 3, $"expected the canvas pair and target supply; found {steppers.Count}");

            foreach (var n in steppers)
            {
                var box = n.GetVisualDescendants().OfType<TextBox>().First();
                var chevrons = n.Bounds.Width - box.Bounds.Width;
                Assert.True(chevrons / n.Bounds.Width < 0.34,
                    $"{chevrons:0.#} of {n.Bounds.Width:0.#} is chevron — the field is losing its width again");
            }
        }
        finally { window.Close(); }
    }
}
