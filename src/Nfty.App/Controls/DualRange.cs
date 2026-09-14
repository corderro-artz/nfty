using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Nfty.App.Controls;

/// <summary>
/// Makes both handles of a two-slider range band reachable.
/// </summary>
/// <remarks>
/// <para><b>Two Sliders stacked in one Panel give the top one every pointer press.</b> Each Slider
/// is the full width of the band, so the second one declared covers the first completely: a click
/// anywhere on the track — including directly on the low handle — hit-tests to the HIGH slider and
/// drags it to the pointer. The low handle could not be moved by mouse at all, which left every
/// range in the app pinned at whatever minimum it opened with. It shipped on four bands: hue and
/// saturation in the ingredient editor, and the same pair in the New Ingredient wizard.</para>
///
/// <para><b>The fix is to decide which handle is armed while the pointer is merely hovering</b>,
/// before a press has anything to hit. On every move over the band the nearer slider is left
/// hit-testable and the other is not, so the press lands on the handle the user is pointing at and
/// Fluent then does the drag itself — with its own value mapping, its own capture and its own
/// snapping. Nothing here computes a value from a coordinate, so there is no second, slightly
/// different idea of where a given pixel sits on the track.</para>
///
/// <para><b>Arming stops while a drag is in progress.</b> Pointer moves during a capture still
/// bubble through this panel, and re-arming mid-drag would clear <c>IsHitTestVisible</c> on the
/// control that currently holds the pointer.</para>
///
/// <para>Applied by a <c>Panel.band</c> style rather than named on each band, so a band added later
/// is covered by construction. A band with anything other than exactly two sliders — the value ramp,
/// the brush hue and saturation strips — is left completely alone.</para>
/// </remarks>
public static class DualRange
{
    /// <summary>Arms the nearer of a band's two sliders as the pointer moves over it.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Panel, bool>("IsEnabled", typeof(DualRange));

    /// <summary>Turns the behavior on for one band.</summary>
    /// <param name="panel">The band.</param>
    /// <param name="value">Whether to arm its handles by proximity.</param>
    public static void SetIsEnabled(Panel panel, bool value) => panel.SetValue(IsEnabledProperty, value);

    /// <summary>Whether the behavior is on for one band.</summary>
    /// <param name="panel">The band.</param>
    /// <returns>True when its handles are armed by proximity.</returns>
    public static bool GetIsEnabled(Panel panel) => panel.GetValue(IsEnabledProperty);

    static DualRange() => IsEnabledProperty.Changed.AddClassHandler<Panel>(OnIsEnabledChanged);

    private static void OnIsEnabledChanged(Panel panel, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || e.OldValue is true) return;

        // One flag per band, held by these closures: a band is the unit that can be mid-drag.
        bool dragging = false;

        panel.PointerEntered += (_, args) => { if (!dragging) Arm(panel, args.GetPosition(panel)); };
        panel.PointerMoved += (_, args) => { if (!dragging) Arm(panel, args.GetPosition(panel)); };
        panel.AddHandler(InputElement.PointerPressedEvent, (object? _, PointerPressedEventArgs _) => dragging = true,
            RoutingStrategies.Tunnel);
        panel.AddHandler(InputElement.PointerReleasedEvent, (object? _, PointerReleasedEventArgs args) =>
        {
            dragging = false;
            Arm(panel, args.GetPosition(panel));
        }, RoutingStrategies.Bubble);
        panel.PointerCaptureLost += (_, _) => dragging = false;

        // A pointer that left the band cannot press on it, and leaving both handles live is the
        // state a band starts in - so the next entry decides, rather than the last hover lingering.
        panel.PointerExited += (_, _) =>
        {
            if (dragging) return;
            foreach (var s in Sliders(panel)) s.IsHitTestVisible = true;
        };
    }

    private static Slider[] Sliders(Panel panel) => panel.Children.OfType<Slider>().ToArray();

    /// <summary>Leaves the slider whose handle is nearer the pointer hit-testable, and the other
    /// not.</summary>
    private static void Arm(Panel panel, Point position)
    {
        var sliders = Sliders(panel);
        if (sliders.Length != 2) return;           // not a range band; every other band is untouched

        double width = panel.Bounds.Width;
        if (width <= 0) return;

        var low = sliders[0];
        var high = sliders[1];

        // Where on the axis the pointer is. Deliberately a rough mapping - it ignores the thumb
        // inset Fluent's own track uses - because it only ever decides WHICH handle, a question
        // whose answer changes at the midpoint between two handles and nowhere near an end stop.
        double at = low.Minimum + (low.Maximum - low.Minimum) * Math.Clamp(position.X / width, 0, 1);

        double toLow = Math.Abs(at - low.Value);
        double toHigh = Math.Abs(at - high.Value);

        // The tie is the case that matters: with both handles stacked, the side the pointer is on
        // says which one it wants to pull away, so a collapsed range can be opened again.
        bool armLow = toLow < toHigh || (toLow == toHigh && at < low.Value);

        low.IsHitTestVisible = armLow;
        high.IsHitTestVisible = !armLow;
    }
}
