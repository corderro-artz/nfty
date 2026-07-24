using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
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
        Assert.Equal(4d, Resolve("RadiusXs", ThemeVariant.Light));
        Assert.Equal(8d, Resolve("RadiusLg", ThemeVariant.Light));
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
}
