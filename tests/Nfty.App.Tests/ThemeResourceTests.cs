using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nfty.App.Tests;

public class ThemeResourceTests
{
    private static object? Resolve(string key, ThemeVariant variant) =>
        Application.Current!.TryGetResource(key, variant, out var v) ? v : null;

    [AvaloniaTheory]
    [InlineData("AccentHoverBrush")]
    [InlineData("SuccessBrush")]
    [InlineData("GuideBrush")]
    [InlineData("GuideHiBrush")]
    public void New_colour_tokens_resolve_in_both_themes(string key)
    {
        Assert.IsAssignableFrom<IBrush>(Resolve(key, ThemeVariant.Light));
        Assert.IsAssignableFrom<IBrush>(Resolve(key, ThemeVariant.Dark));
    }

    [AvaloniaFact]
    public void Font_and_radius_tokens_resolve()
    {
        Assert.IsType<FontFamily>(Resolve("SansFontFamily", ThemeVariant.Light));
        Assert.IsType<FontFamily>(Resolve("MonoFontFamily", ThemeVariant.Light));
        Assert.Equal(new Avalonia.CornerRadius(4), Resolve("RadiusXs", ThemeVariant.Light));
        Assert.Equal(new Avalonia.CornerRadius(8), Resolve("RadiusLg", ThemeVariant.Light));
    }

    [AvaloniaFact]
    public void Base_text_is_sans_and_mono_class_is_mono()
    {
        var plain = StyledHost.Show(new TextBlock { Text = "body" });
        var mono = StyledHost.Show(new TextBlock { Text = "id", Classes = { "mono" } });

        var sans = (FontFamily)Application.Current!.FindResource("SansFontFamily")!;
        var monoFam = (FontFamily)Application.Current!.FindResource("MonoFontFamily")!;
        Assert.Equal(sans, plain.FontFamily);   // base default is sans, not mono
        Assert.Equal(monoFam, mono.FontFamily);
    }

    [AvaloniaFact]
    public void Accent_button_uses_accent_background_and_tbtn_uses_panel()
    {
        var accent = StyledHost.Show(new Button { Classes = { "accent" }, Content = "Cook" });
        var tbtn = StyledHost.Show(new Button { Classes = { "tbtn" }, Content = "Open" });

        // Application.Current.FindResource(key) (no theme-variant argument) resolves against
        // ThemeVariant.Default, which doesn't match the "Light"/"Dark" keys under Tokens.axaml's
        // ThemeDictionaries, so it always returns UnsetValue for these theme-scoped brushes.
        // Use the file's Resolve helper (explicit ThemeVariant) instead, as the other tests here do.
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)accent.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("PanelBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)tbtn.Background!).Color);
    }

    // Regression test for Fix 1: Fluent's own "^:pointerover /template/ ContentPresenter"
    // style sets Background/BorderBrush/Foreground together on PART_ContentPresenter at
    // StyleTrigger priority, so a hover BorderBrush setter placed on the outer Button (rather
    // than on the /template/ ContentPresenter#PART_ContentPresenter selector) is silently
    // overridden and never renders. This test drives an actual simulated pointer move via
    // Avalonia.Headless's `Window.MouseMove` (see "Headless Testing Platform > Simulating user
    // input > Mouse input" in the Avalonia 11.2 docs) to activate the real ":pointerover"
    // pseudo-class, then reads the applied BorderBrush directly off the template part — so it
    // fails if the hover setter regresses back onto the outer Button selector.
    [AvaloniaFact]
    public void Tbtn_hover_sets_border_brush_on_content_presenter_template_part()
    {
        var button = new Button
        {
            Classes = { "tbtn" },
            Content = "Open",
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = new Window { Content = button, Width = 200, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var presenter = button.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(p => p.Name == "PART_ContentPresenter");

        // Sanity check: before any pointer movement, hover styling must not be active.
        Assert.False(button.IsPointerOver);
        var restBorder = (ISolidColorBrush)presenter.BorderBrush!;
        Assert.Equal(
            ((ISolidColorBrush)Resolve("LineStrongBrush", ThemeVariant.Light)!).Color,
            restBorder.Color);

        // Move the simulated mouse to a point inside the button's laid-out bounds to activate
        // the ":pointerover" pseudo-class for real, rather than poking pseudo-classes directly.
        var target = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
        window.MouseMove(target);
        Dispatcher.UIThread.RunJobs();

        Assert.True(button.IsPointerOver);
        var hoverBorder = (ISolidColorBrush)presenter.BorderBrush!;
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentLineBrush", ThemeVariant.Light)!).Color,
            hoverBorder.Color);
    }

    // Uses the file's Resolve helper (explicit ThemeVariant) rather than
    // Application.Current.FindResource(key) directly — see the comment on
    // Accent_button_uses_accent_background_and_tbtn_uses_panel: FindResource without a
    // variant resolves against ThemeVariant.Default, which never matches the "Light"/"Dark"
    // keys under Tokens.axaml's ThemeDictionaries and always returns UnsetValue.
    [AvaloniaFact]
    public void Panel_uses_panel_brush_and_kind_chip_uses_kind_colour()
    {
        var panel = StyledHost.Show(new Border { Classes = { "panel" } });
        var chip = StyledHost.Show(new Border { Classes = { "kind-dynamic" } });

        Assert.Equal(
            ((ISolidColorBrush)Resolve("PanelBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)panel.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("KindDynamicBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)chip.BorderBrush!).Color);
    }
}
