using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nfty.App.Controls;

/// <summary>
/// A chord drawn as keys: one cap per key, the mouse and the special keys as their drawings, the
/// rest as mono text. Give it the string <c>KeyHints</c> prints (<c>Ctrl Shift Z</c>, <c>Alt ↑↓</c>,
/// <c>Right click</c>) and it draws what that string says.
/// </summary>
/// <remarks>
/// <para><b>Decoupled on purpose.</b> The only input is a string; the only table is
/// <see cref="KeyGlyphs"/>; the only things it reads from the app are theme resources
/// (<c>KeyCap*</c> tokens, the <c>Icon*</c> geometries). Lifting it into another project means
/// copying this file, <c>KeyGlyphs.cs</c>, <c>assets/icons/input/</c> and the <c>KeyCap</c> block of
/// <c>Styles.axaml</c>/<c>Tokens.axaml</c>.</para>
/// <para>Sizes are the two tokens <c>KeyCapHeight</c> and <c>KeyCapHeightSm</c>, so a row holding a
/// chord is the same height whatever the chord says. <see cref="Compact"/> picks the small one.</para>
/// </remarks>
public class KeyChord : StackPanel
{
    /// <summary>The chord, spelled as <c>KeyHints</c> spells it.</summary>
    public static readonly StyledProperty<string?> ChordProperty =
        AvaloniaProperty.Register<KeyChord, string?>(nameof(Chord));

    /// <summary>True for the dense-table size.</summary>
    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<KeyChord, bool>(nameof(Compact));

    /// <summary>The chord, spelled as <c>KeyHints</c> spells it.</summary>
    public string? Chord
    {
        get => GetValue(ChordProperty);
        set => SetValue(ChordProperty, value);
    }

    /// <summary>True for the dense-table size (<c>KeyCapHeightSm</c>).</summary>
    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(StackPanel);

    static KeyChord()
    {
        ChordProperty.Changed.AddClassHandler<KeyChord>((c, _) => c.Rebuild());
        CompactProperty.Changed.AddClassHandler<KeyChord>((c, _) => c.Rebuild());
    }

    /// <summary>Creates an empty chord.</summary>
    public KeyChord()
    {
        Orientation = Orientation.Horizontal;
        Spacing = KeyGlyphs.CapSpacing;
        VerticalAlignment = VerticalAlignment.Center;
        Classes.Add("keychord");
    }

    private void Rebuild()
    {
        Children.Clear();
        AutomationProperties.SetName(this, KeyGlyphs.Spoken(Chord));
        foreach (var part in KeyGlyphs.Parse(Chord))
            Children.Add(part.IsJoiner ? Joiner(part) : Cap(part));
    }

    private static TextBlock Joiner(ChordPart part)
    {
        var t = new TextBlock { Text = part.Text };
        t.Classes.Add("keyjoin");
        return t;
    }

    private Border Cap(ChordPart part)
    {
        var cap = new Border();
        cap.Classes.Add("keycap");
        if (Compact) cap.Classes.Add("sm");
        AutomationProperties.SetName(cap, part.Text);

        if (part.Glyph is not { } glyph)
        {
            cap.Child = new TextBlock { Text = part.Text };
            return cap;
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        for (var i = 0; i < glyph.Icons.Length; i++)
        {
            var cell = new Panel { VerticalAlignment = VerticalAlignment.Center };
            cell.Children.Add(Icon(glyph.Icons[i], "keyink"));
            if (i == 0 && glyph.Fill is { } fill) cell.Children.Add(Icon(fill, "keyfill"));
            row.Children.Add(cell);
        }
        if (glyph.Label is { } label)
        {
            var t = new TextBlock { Text = label, Margin = new Thickness(1, 0, 0, 0) };
            row.Children.Add(t);
        }
        cap.Child = row;
        return cap;
    }

    private Avalonia.Controls.Shapes.Path Icon(string resourceKey, string role)
    {
        // The geometry is the theme dictionary's own instance, like HelpGlyph's.
        var p = new Avalonia.Controls.Shapes.Path { Data = Application.Current?.FindResource(resourceKey) as Geometry };
        p.Classes.Add("ico");
        p.Classes.Add(Compact ? "xs" : "sm");
        p.Classes.Add(role);
        return p;
    }
}
