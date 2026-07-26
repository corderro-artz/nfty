using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Models;
using Nfty.Core.Model;
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

    // Regression test for a hex-order bug found while verifying Task 4's surfaces: Tokens.axaml's
    // translucent colours were ported verbatim from the mockup's CSS custom properties, which write
    // 8-digit hex as RRGGBBAA (alpha last, CSS Color Module 4). Avalonia's Color.Parse (and
    // BoxShadows.Parse, which shares the same colour grammar) reads 8-digit hex as AARRGGBB (alpha
    // first, the .NET/WPF convention) — so every translucent brush was silently rendering as an
    // opaque, wrong-hued colour (e.g. dark LineBrush "#f2ede624" parsed to A=242 R=237 G=230 B=36,
    // an opaque yellow-gold, instead of the intended ~14%-alpha near-white). This asserts each
    // affected brush's channels land where the mockup's CSS intended: RGB from the token's first 6
    // hex digits, alpha from its last 2.
    [AvaloniaTheory]
    [InlineData("LineBrush", "Light", 0x12, 0x14, 0x18, 0x1f)]
    [InlineData("LineStrongBrush", "Light", 0x12, 0x14, 0x18, 0x33)]
    [InlineData("FgMutedBrush", "Light", 0x12, 0x14, 0x18, 0xb8)]
    [InlineData("AccentWashBrush", "Light", 0xa1, 0x1f, 0x31, 0x14)]
    [InlineData("AccentLineBrush", "Light", 0xa1, 0x1f, 0x31, 0x40)]
    [InlineData("GuideBrush", "Light", 0x12, 0x14, 0x18, 0x2b)]
    [InlineData("GuideHiBrush", "Light", 0x12, 0x14, 0x18, 0x59)]
    [InlineData("LineBrush", "Dark", 0xf2, 0xed, 0xe6, 0x24)]
    [InlineData("LineStrongBrush", "Dark", 0xf2, 0xed, 0xe6, 0x33)]
    [InlineData("FgMutedBrush", "Dark", 0xf2, 0xed, 0xe6, 0xc7)]
    [InlineData("AccentWashBrush", "Dark", 0xa1, 0x1f, 0x31, 0x26)]
    [InlineData("AccentLineBrush", "Dark", 0xa1, 0x1f, 0x31, 0x66)]
    [InlineData("GuideBrush", "Dark", 0xf2, 0xed, 0xe6, 0x1f)]
    [InlineData("GuideHiBrush", "Dark", 0xf2, 0xed, 0xe6, 0x3d)]
    public void Translucent_brush_channels_match_mockup_css_rgba_not_misparsed_argb(
        string key, string variantName, byte r, byte g, byte b, byte a)
    {
        var variant = variantName == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var color = ((ISolidColorBrush)Resolve(key, variant)!).Color;
        Assert.Equal(new Avalonia.Media.Color(a, r, g, b), color);
    }

    // Same bug, but in the shared BoxShadows grammar: WinShadow's two hex colours per theme were
    // also authored as CSS RRGGBBAA and need the same alpha-first correction.
    [AvaloniaFact]
    public void WinShadow_colours_match_mockup_css_rgba_not_misparsed_argb()
    {
        var light = (Avalonia.Media.BoxShadows)Resolve("WinShadow", ThemeVariant.Light)!;
        Assert.Equal(new Avalonia.Media.Color(0x10, 0x12, 0x14, 0x18), light[0].Color);
        Assert.Equal(new Avalonia.Media.Color(0x33, 0x12, 0x14, 0x18), light[1].Color);

        var dark = (Avalonia.Media.BoxShadows)Resolve("WinShadow", ThemeVariant.Dark)!;
        Assert.Equal(new Avalonia.Media.Color(0x60, 0x00, 0x00, 0x00), dark[0].Color);
        Assert.Equal(new Avalonia.Media.Color(0xff, 0x00, 0x00, 0x00), dark[1].Color);
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

    [AvaloniaFact]
    public void TextBox_is_restyled_with_panel_background()
    {
        var tb = StyledHost.Show(new TextBox { Text = "x" });
        // Uses the file's Resolve helper (explicit ThemeVariant) rather than
        // Application.Current.FindResource(key) directly — see the comment on
        // Accent_button_uses_accent_background_and_tbtn_uses_panel: FindResource without a
        // variant resolves against ThemeVariant.Default, which never matches the "Light"/"Dark"
        // keys under Tokens.axaml's ThemeDictionaries and always returns UnsetValue.
        Assert.Equal(
            ((ISolidColorBrush)Resolve("PanelBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)tb.Background!).Color);
    }

    [AvaloniaFact]
    public void TextBox_rest_border_is_line_strong()
    {
        var tb = StyledHost.Show(new TextBox { Text = "x" });
        var border = tb.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        Assert.Equal(
            ((ISolidColorBrush)Resolve("LineStrongBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)border.BorderBrush!).Color);
    }

    // Regression-style test for the same lesson as Tbtn_hover_sets_border_brush_on_content_presenter_template_part:
    // Fluent's own ":focus" style repaints Border#PART_BorderElement directly at Style priority, so a focus
    // accent border only shows if our ControlTheme's nested ":focus /template/ Border#PART_BorderElement" style
    // actually wins over the inherited one. Drives a real Focus() call rather than poking pseudo-classes.
    [AvaloniaFact]
    public void TextBox_focus_border_is_accent()
    {
        var textBox = new TextBox { Text = "x" };
        var window = new Window { Content = textBox, Width = 200, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var border = textBox.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        Assert.False(textBox.IsFocused);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("LineStrongBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)border.BorderBrush!).Color);

        textBox.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.True(textBox.IsFocused);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)border.BorderBrush!).Color);
    }

    [AvaloniaFact]
    public void Slider_thumb_and_filled_track_use_accent()
    {
        var slider = StyledHost.Show(new Slider { Minimum = 0, Maximum = 1, Value = 0.6 });
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().First();
        var decreaseButton = slider.GetVisualDescendants().OfType<RepeatButton>()
            .First(b => b.Name == "PART_DecreaseButton");

        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)thumb.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)decreaseButton.Background!).Color);
    }

    // Regression test for the ":not(:disabled)" technique used to reach the Thumb's rest-state
    // Background (see the comment on the Slider ControlTheme in Controls.axaml): drives a real
    // simulated pointer move so both the rest-state assertion above and the pointerover assertion
    // here exercise the actual declaration-order tie-break between the two StyleTrigger-tier
    // selectors, rather than assuming it from reading the XAML.
    [AvaloniaFact]
    public void Slider_thumb_hover_uses_accent_hover()
    {
        var slider = new Slider { Minimum = 0, Maximum = 1, Value = 0.6, Width = 200 };
        var window = new Window { Content = slider, Width = 240, Height = 100 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var thumb = slider.GetVisualDescendants().OfType<Thumb>().First();
        Assert.False(thumb.IsPointerOver);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)thumb.Background!).Color);

        var target = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window) ?? default;
        window.MouseMove(target);
        Dispatcher.UIThread.RunJobs();

        Assert.True(thumb.IsPointerOver);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentHoverBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)thumb.Background!).Color);
    }

    [AvaloniaFact]
    public void CheckBox_checked_square_and_glyph_use_accent()
    {
        var box = StyledHost.Show(new CheckBox { IsChecked = true, Content = "x" });
        var square = box.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NormalRectangle");
        var glyph = box.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First(p => p.Name == "CheckGlyph");

        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)square.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("OnAccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)glyph.Fill!).Color);
    }

    [AvaloniaFact]
    public void RadioButton_checked_ring_and_dot_use_accent()
    {
        var radio = StyledHost.Show(new RadioButton { IsChecked = true, Content = "x" });
        var ring = radio.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
            .First(e => e.Name == "CheckOuterEllipse");
        var dot = radio.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
            .First(e => e.Name == "CheckGlyph");

        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)ring.Stroke!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)dot.Fill!).Color);
    }

    [AvaloniaFact]
    public void NumericUpDown_is_restyled_with_panel_background()
    {
        var nud = StyledHost.Show(new NumericUpDown { Value = 12 });
        Assert.Equal(
            ((ISolidColorBrush)Resolve("PanelBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)nud.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("LineStrongBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)nud.BorderBrush!).Color);
    }

    [AvaloniaFact]
    public void CheckBox_unchecked_square_uses_panel_and_line_strong()
    {
        var box = StyledHost.Show(new CheckBox { IsChecked = false, Content = "x" });
        var square = box.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NormalRectangle");

        Assert.Equal(
            ((ISolidColorBrush)Resolve("PanelBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)square.Background!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("LineStrongBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)square.BorderBrush!).Color);
    }

    [AvaloniaFact]
    public void RadioButton_unchecked_ring_uses_panel_and_line_strong()
    {
        var radio = StyledHost.Show(new RadioButton { IsChecked = false, Content = "x" });
        var ring = radio.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
            .First(e => e.Name == "OuterEllipse");

        Assert.Equal(
            ((ISolidColorBrush)Resolve("PanelBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)ring.Fill!).Color);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("LineStrongBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)ring.Stroke!).Color);
    }

    // Style-load guard for Task 4's TreeViewItem ControlTheme (Controls.axaml) + node-row styles
    // (Styles.axaml Border.guide/Border.kmark/TextBlock.root). Mirrors ExplorerView's node
    // DataTemplate with a code-built FuncTreeDataTemplate (rather than parsing the XAML view,
    // which needs a full ExplorerViewModel) so the same Classes end up on the same element types
    // Fluent's TreeViewItem template actually contains — a bad selector or an unresolvable
    // DynamicResource token in either file throws here instead of silently no-op-ing. Selecting
    // the child item also exercises the ControlTheme's ":selected /template/ Border#PART_LayoutRoot"
    // override (the accent-wash background + inset accent-bar BoxShadow), not just the rest state.
    [AvaloniaFact]
    public void Tree_with_kind_marks_and_guide_renders_and_selects_without_throwing()
    {
        var child = new ExplorerNode("igt1", "Aura", ExplorerNodeKind.Ingredient,
            Array.Empty<ExplorerNode>(), null, LayerKind.Dynamic);
        var root = new ExplorerNode("cbk1", "VaporPets", ExplorerNodeKind.CookBook,
            new[] { child }, null);

        var tree = new TreeView
        {
            ItemsSource = new[] { root },
            ItemTemplate = new FuncTreeDataTemplate<ExplorerNode>(
                (node, _) =>
                {
                    var guide = new Border { Classes = { "guide" }, IsVisible = !node.IsRoot };
                    var kmark = new Border { Classes = { "kmark" }, IsVisible = node.LayerKind is not null };
                    if (node.IsDynamic) kmark.Classes.Add("kdyn");
                    if (node.IsStatic) kmark.Classes.Add("kstat");
                    if (node.IsCustom) kmark.Classes.Add("kcust");
                    var label = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
                    if (node.IsRoot) label.Classes.Add("root");

                    var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                    panel.Children.Add(guide);
                    panel.Children.Add(kmark);
                    panel.Children.Add(label);
                    return panel;
                },
                node => node.Children),
        };

        StyledHost.Show(tree);

        // The root container starts collapsed (default IsExpanded=false), so the child isn't
        // realised until it's expanded.
        var rootContainer = tree.GetVisualDescendants().OfType<TreeViewItem>().Single();
        rootContainer.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var containers = tree.GetVisualDescendants().OfType<TreeViewItem>().ToList();
        Assert.Equal(2, containers.Count);

        tree.SelectedItem = child;
        Dispatcher.UIThread.RunJobs();

        var childContainer = containers.Single(c => ReferenceEquals(c.DataContext, child));
        var layoutRoot = childContainer.GetVisualDescendants().OfType<Border>()
            .First(b => b.Name == "PART_LayoutRoot");

        Assert.True(childContainer.IsSelected);
        Assert.Equal(
            ((ISolidColorBrush)Resolve("AccentWashBrush", ThemeVariant.Light)!).Color,
            ((ISolidColorBrush)layoutRoot.Background!).Color);
        Assert.True(layoutRoot.BoxShadow.Count > 0 && layoutRoot.BoxShadow[0].IsInset);
    }
}
