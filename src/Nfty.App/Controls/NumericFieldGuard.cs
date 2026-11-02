using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Nfty.App.Controls;

/// <summary>
/// Puts a number field back to the value its view model holds when the user leaves it empty or
/// unreadable.
/// </summary>
/// <remarks>
/// <para><b>A field bound to a number shows the binding's own exception when its text cannot become
/// one.</b> Clearing New CookBook's target supply printed <c>System.InvalidCastException: Could not
/// convert '(null)' (null) to System.Int32</c> under the field; erasing a saturation endpoint did the
/// same in the editor rail. The view model never changed - the conversion failed before it - so the
/// value is still right; only the text is wrong. Leaving the field re-reads it, and the raw message
/// is never drawn (<c>DataValidationErrors</c> carries an empty template in <c>Styles.axaml</c>: no
/// view model here reports validation through it, so every message it could show is one of these).
/// </para>
/// <para>Range is the view model's business (the colorize endpoints clamp to their axis where they
/// are set, <c>NumericUpDown</c> to its <c>Minimum</c>/<c>Maximum</c>), so a typed 150 lands on 100
/// and only the unreadable case needs this.</para>
/// </remarks>
public static class NumericFieldGuard
{
    /// <summary>Restores the bound value when focus leaves an unreadable field.</summary>
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(NumericFieldGuard));

    /// <summary>Turns the guard on for one field.</summary>
    /// <param name="control">A <see cref="TextBox"/> or <see cref="NumericUpDown"/>.</param>
    /// <param name="value">Whether to guard it.</param>
    public static void SetIsEnabled(Control control, bool value) => control.SetValue(IsEnabledProperty, value);

    /// <summary>Whether the guard is on for one field.</summary>
    /// <param name="control">The field.</param>
    /// <returns>True when guarded.</returns>
    public static bool GetIsEnabled(Control control) => control.GetValue(IsEnabledProperty);

    static NumericFieldGuard() => IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);

    private static void OnIsEnabledChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || e.OldValue is true) return;
        // Bubble with handled events: a NumericUpDown's focus sits on its inner TextBox, so the
        // departure is raised there and reaches the outer control on the way up.
        control.AddHandler(InputElement.LostFocusEvent, (_, _) => Restore(control),
            RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Re-reads the bound value into a field left unreadable. Public for tests.</summary>
    /// <param name="control">The field.</param>
    public static void Restore(Control control)
    {
        if (control.IsKeyboardFocusWithin) return;   // focus moved inside it, not away
        switch (control)
        {
            case NumericUpDown nud when nud.Value is null || DataValidationErrors.GetHasErrors(nud):
                // A nullable binding (null = "none") has nothing to restore: null IS the value.
                if (!DataValidationErrors.GetHasErrors(nud)) return;
                Reread(nud, NumericUpDown.ValueProperty);
                DataValidationErrors.ClearErrors(nud);
                break;
            case TextBox tb when DataValidationErrors.GetHasErrors(tb):
                Reread(tb, TextBox.TextProperty);
                DataValidationErrors.ClearErrors(tb);
                break;
        }
    }

    private static void Reread(Control control, AvaloniaProperty property)
    {
        BindingOperations.GetBindingExpressionBase(control, property)?.UpdateTarget();
    }
}
