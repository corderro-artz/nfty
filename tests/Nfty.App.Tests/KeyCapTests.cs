using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Nfty.App.Controls;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The key-cap control and the one table behind it: every chord the product prints resolves, the
/// mouse and special keys reach their drawings, a stranger is still a cap, and a row holding a chord
/// is the height the token says.
/// </summary>
public class KeyCapTests
{
    private static string[] Spelled(string chord) =>
        KeyGlyphs.Parse(chord).Select(p => p.IsJoiner ? "~" + p.Text : p.Glyph is null ? "t:" + p.Text : "g:" + p.Text).ToArray();

    [Fact]
    public void A_modifier_chord_is_one_cap_per_key()
    {
        Assert.Equal(new[] { "t:Ctrl", "g:Shift", "t:Z" }, Spelled("Ctrl Shift Z"));
    }

    [Fact]
    public void A_stranger_is_a_text_cap_never_an_error()
    {
        var parts = KeyGlyphs.Parse("Hyper Banana");
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => { Assert.Null(p.Glyph); Assert.False(p.IsJoiner); });
        Assert.Empty(KeyGlyphs.Parse(null));
        Assert.Empty(KeyGlyphs.Parse("   "));
    }

    [Theory]
    [InlineData("Right click", "IconMouseRightFill", "click")]
    [InlineData("Middle drag", "IconMouseMiddleFill", "drag")]
    [InlineData("Left", "IconMouseLeftFill", null)]
    [InlineData("Mouse4", "IconMouseBackFill", null)]
    [InlineData("Mouse5", "IconMouseForwardFill", null)]
    [InlineData("Double-click", "IconMouseLeftFill", "×2")]
    public void A_mouse_token_is_the_mouse_with_its_button_filled(string chord, string fill, string? label)
    {
        var part = Assert.Single(KeyGlyphs.Parse(chord));
        Assert.Equal(new[] { "IconMouse" }, part.Glyph!.Icons);
        Assert.Equal(fill, part.Glyph.Fill);
        Assert.Equal(label, part.Glyph.Label);
    }

    [Fact]
    public void The_wheel_tokens_reach_the_wheel_drawings()
    {
        Assert.Equal("IconWheel", Assert.Single(KeyGlyphs.Parse("Wheel")).Glyph!.Icons[0]);
        Assert.Equal("IconWheelUp", Assert.Single(KeyGlyphs.Parse("wheel up")).Glyph!.Icons[0]);
        Assert.Equal("IconWheelDown", Assert.Single(KeyGlyphs.Parse("Wheel down")).Glyph!.Icons[0]);
    }

    [Fact]
    public void Several_arrows_closed_up_are_one_cap_and_spaced_arrows_are_several()
    {
        var one = Assert.Single(KeyGlyphs.Parse("↑↓"));
        Assert.Equal(new[] { "IconKeyUp", "IconKeyDown" }, one.Glyph!.Icons);
        Assert.Equal(4, KeyGlyphs.Parse("← ↑ ↓ →").Count);
    }

    [Fact]
    public void A_joining_word_is_not_a_key()
    {
        var parts = KeyGlyphs.Parse("Right or Middle drag");
        Assert.Equal(3, parts.Count);
        Assert.True(parts[1].IsJoiner);
        Assert.Equal("IconMouseMiddleFill", parts[2].Glyph!.Fill);
    }

    [Fact]
    public void A_slash_is_a_key_when_it_stands_alone_in_a_chord()
    {
        var parts = KeyGlyphs.Parse("Ctrl /");
        Assert.Equal(2, parts.Count);
        Assert.Null(parts[1].Glyph);
        Assert.False(parts[1].IsJoiner);
    }

    [Fact]
    public void Macs_closed_up_spelling_peels_into_caps()
    {
        var parts = KeyGlyphs.Parse("⌘⇧Z");
        Assert.Equal(new[] { "⌘", "⇧", "Z" }, parts.Select(p => p.Text));
        Assert.Equal("IconKeyShift", parts[1].Glyph!.Icons[0]);
    }

    private static string[] EveryChordTheProductPrints() =>
        typeof(KeyHints).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Where(p => p.Name is "Mod" or "Alt" or "Help" or "Search" or "NewCookBook" or "Open" or "Import" or "CloseDocument"
                or "Theme" or "ZoomReset" or "Undo" or "Redo" or "MarkAll" or "RedoAlt" or "Constrain" or "LowerDepth"
                or "FromCenter" or "RiseConstrain" or "StepTarget" or "ZoomKeys" or "ZoomFit" or "Arrows" or "ResetView"
                or "MoveLayer" or "DropSelection")
            .Select(p => (string)p.GetValue(null)!)
            .Concat(HelpCatalog.Entries.OfType<HelpKey>().Select(k => k.Key))
            .Distinct()
            .ToArray();

    [Fact]
    public void Every_chord_the_product_prints_resolves_to_at_least_one_cap()
    {
        var chords = EveryChordTheProductPrints();
        Assert.True(chords.Length > 40, "the scan found only " + chords.Length + " chords");
        foreach (var chord in chords)
        {
            var parts = KeyGlyphs.Parse(chord);
            Assert.True(parts.Any(p => !p.IsJoiner), $"'{chord}' resolves to no cap");
            Assert.True(parts.Where(p => p.Glyph is null).All(p => !p.Text.Any(char.IsWhiteSpace)), $"'{chord}' left whitespace inside a cap");
        }
    }

    [AvaloniaFact]
    public void Every_glyph_the_table_names_is_a_geometry_in_the_theme()
    {
        var keys = KeyGlyphs.Table.Values
            .SelectMany(g => g.Icons.Concat(g.Fill is null ? Array.Empty<string>() : new[] { g.Fill }))
            .Distinct().ToList();
        Assert.True(keys.Count > 15);
        foreach (var key in keys)
            Assert.True(Application.Current!.FindResource(key) is Geometry, $"{key} is not in Icons.axaml");
    }

    [AvaloniaFact]
    public void The_control_draws_one_cap_per_key_and_a_mouse_cap_carries_two_paths()
    {
        var chord = StyledHost.Show(new KeyChord { Chord = "Ctrl Right click" });
        var caps = chord.Children.OfType<Border>().ToList();
        Assert.Equal(2, caps.Count);
        Assert.Equal(2, caps[1].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count());
        Assert.Empty(caps[0].GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>());
        Assert.Equal("Ctrl", caps[0].GetVisualDescendants().OfType<TextBlock>().Single().Text);
    }

    [AvaloniaFact]
    public void A_cap_is_the_height_its_token_says_in_both_sizes()
    {
        var normal = StyledHost.Show(new KeyChord { Chord = "Ctrl Shift Z" });
        var small = StyledHost.Show(new KeyChord { Chord = "Ctrl Shift Z", Compact = true });
        var h = (double)Application.Current!.FindResource("KeyCapHeight")!;
        var hs = (double)Application.Current!.FindResource("KeyCapHeightSm")!;
        Assert.All(normal.Children.OfType<Border>(), c => Assert.Equal(h, c.Bounds.Height, 0.01));
        Assert.All(small.Children.OfType<Border>(), c => Assert.Equal(hs, c.Bounds.Height, 0.01));
        Assert.True(hs < h);
    }

    [AvaloniaFact]
    public void The_width_estimate_covers_what_every_catalog_chord_draws()
    {
        foreach (var chord in HelpCatalog.Entries.OfType<HelpKey>().Select(k => k.Key).Distinct())
        {
            var c = StyledHost.Show(new KeyChord { Chord = chord, Compact = true });
            var drawn = c.Children.Sum(x => x.Bounds.Width) + (c.Children.Count - 1) * KeyGlyphs.CapSpacing;
            var est = KeyGlyphs.EstimateWidth(chord, 18);
            Assert.True(est >= drawn - 0.5, $"'{chord}': estimated {est}, drawn {drawn} — the key column would clip it");
            Assert.True(est - drawn < 20, $"'{chord}': estimated {est}, drawn {drawn} — the key column would be wide for it");
        }
    }

    [AvaloniaFact]
    public void The_catalogs_key_column_holds_its_widest_chord()
    {
        foreach (var k in HelpCatalog.Entries.OfType<HelpKey>())
            Assert.True(k.KeyWidth >= KeyGlyphs.EstimateWidth(k.Key, 18), $"{k.Key} outgrows its column");
    }
}
