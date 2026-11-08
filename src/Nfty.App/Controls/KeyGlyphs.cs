using System.Text;

namespace Nfty.App.Controls;

/// <summary>What a cap draws instead of (or beside) its text: icon resource keys, an optional accent
/// fill laid over the first icon, an optional word.</summary>
/// <param name="Icons">Resource keys of the stroked glyphs, drawn left to right (<c>Icons.axaml</c>,
/// <c>assets/icons/input/</c>).</param>
/// <param name="Fill">Resource key of a second glyph filled in the accent over <c>Icons[0]</c> — the
/// highlighted mouse button — or null.</param>
/// <param name="Label">A word drawn after the icons (<c>drag</c>, <c>click</c>), or null.</param>
public sealed record KeyGlyph(string[] Icons, string? Fill = null, string? Label = null);

/// <summary>One thing in a chord: a cap, or the little word (<c>or</c>) that joins two.</summary>
/// <param name="IsJoiner">True for a joining word, drawn bare and not as a key.</param>
/// <param name="Text">The token as written; also the cap's label for a text cap and its accessible name.</param>
/// <param name="Glyph">The icon drawing, or null for a plain text cap.</param>
public sealed record ChordPart(bool IsJoiner, string Text, KeyGlyph? Glyph);

/// <summary>
/// The ONE mapping from the words a chord is spelled in to what a cap draws, and the parser that
/// walks a chord string with it. Nothing here knows an Avalonia type, a ViewModel or a service: it
/// takes the string <c>Services/KeyHints.cs</c> produces and returns parts, so the control that
/// draws them (<see cref="KeyChord"/>) and the tests that pin them share one table.
/// </summary>
/// <remarks>
/// <para><b>Adding a token</b> is one row: draw the SVG (<c>assets/icons/input/</c>), run
/// <c>python tools/icons/build.py</c>, add a row to <see cref="Table"/> naming its resource key. The
/// keys are spelled out here, not composed at runtime, so <c>DeadCodeAuditTests</c> can see which
/// glyphs are used.</para>
/// <para>A token not in the table is a plain text cap — never an error — so a new chord is correct
/// before anyone draws it.</para>
/// </remarks>
public static class KeyGlyphs
{
    private const string Mouse = "IconMouse";

    private static KeyGlyph Button(string fill, string? label = null) => new([Mouse], fill, label);
    private static KeyGlyph One(string icon) => new([icon]);

    /// <summary>Token (one word, or two joined by a space) to what its cap draws. Case-insensitive.</summary>
    public static readonly IReadOnlyDictionary<string, KeyGlyph> Table =
        new Dictionary<string, KeyGlyph>(StringComparer.OrdinalIgnoreCase)
        {
            // the mouse buttons, alone and as gestures
            ["Left"] = Button("IconMouseLeftFill"),
            ["Right"] = Button("IconMouseRightFill"),
            ["Middle"] = Button("IconMouseMiddleFill"),
            ["Mouse4"] = Button("IconMouseBackFill"),
            ["Mouse5"] = Button("IconMouseForwardFill"),
            ["Left click"] = Button("IconMouseLeftFill", "click"),
            ["Right click"] = Button("IconMouseRightFill", "click"),
            ["Middle click"] = Button("IconMouseMiddleFill", "click"),
            ["Left drag"] = Button("IconMouseLeftFill", "drag"),
            ["Right drag"] = Button("IconMouseRightFill", "drag"),
            ["Middle drag"] = Button("IconMouseMiddleFill", "drag"),
            ["Drag"] = Button("IconMouseLeftFill", "drag"),
            ["Double-click"] = Button("IconMouseLeftFill", "×2"),

            // the wheel
            ["Wheel"] = One("IconWheel"),
            ["Wheel up"] = One("IconWheelUp"),
            ["Wheel down"] = One("IconWheelDown"),

            // keys a drawing says better than a word
            ["↑"] = One("IconKeyUp"),
            ["↓"] = One("IconKeyDown"),
            ["←"] = One("IconKeyLeft"),
            ["→"] = One("IconKeyRight"),
            ["Enter"] = One("IconKeyEnter"),
            ["Return"] = One("IconKeyEnter"),
            ["Backspace"] = One("IconKeyBackspace"),
            ["Tab"] = One("IconKeyTab"),
            ["Shift"] = One("IconKeyShift"),
            ["⇧"] = One("IconKeyShift"),
            ["Space"] = One("IconKeySpace"),
        };

    /// <summary>Words drawn bare between caps instead of as a key.</summary>
    public static readonly IReadOnlySet<string> Joiners =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "or", "then" };

    private const string Arrows = "←↑↓→";
    private const string MacModifiers = "⌘⌥⇧⌃";

    /// <summary>Splits <paramref name="chord"/> into caps and joiners. Never throws; null or blank is no parts.</summary>
    /// <param name="chord">A chord as <c>KeyHints</c> spells it: <c>Ctrl Shift Z</c>, <c>Alt ↑↓</c>,
    /// <c>Right or Middle drag</c>, <c>⌘N</c>.</param>
    /// <returns>The parts in reading order.</returns>
    public static IReadOnlyList<ChordPart> Parse(string? chord)
    {
        var parts = new List<ChordPart>();
        var words = (chord ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            if (i + 1 < words.Length && Table.TryGetValue(words[i] + " " + words[i + 1], out var phrase))
            {
                parts.Add(new ChordPart(false, words[i] + " " + words[i + 1], phrase));
                i++;
                continue;
            }
            AddWord(parts, words[i]);
        }
        return parts;
    }

    private static void AddWord(List<ChordPart> parts, string word)
    {
        if (Joiners.Contains(word)) { parts.Add(new ChordPart(true, word, null)); return; }
        if (Table.TryGetValue(word, out var known)) { parts.Add(new ChordPart(false, word, known)); return; }

        // "⌘N", "⌥↑↓": Mac spells a chord closed up, so peel the modifier symbols off the front.
        if (word.Length > 1 && MacModifiers.Contains(word[0]))
        {
            AddWord(parts, word[..1]);
            AddWord(parts, word[1..]);
            return;
        }

        // "↑↓": several arrows are one cap with several drawings in it.
        if (word.Length > 1 && word.All(c => Arrows.Contains(c)))
        {
            parts.Add(new ChordPart(false, word,
                new KeyGlyph(word.Select(c => Table[c.ToString()].Icons[0]).ToArray())));
            return;
        }

        parts.Add(new ChordPart(false, word, null));
    }

    // ---- width estimate (the Quick Reference sizes its key column from it; KeyCapTests holds it to the drawn width) ----

    /// <summary>Gap between caps.</summary>
    public const double CapSpacing = 3;
    private const double MonoChar = 6.4;     // IBM Plex Mono at 10.5px
    private const double CapPad = 12;        // 5px each side + the rim
    private const double IconBox = 13;
    private const double IconGap = 2;

    /// <summary>The width a cap of <paramref name="part"/> draws at, for a cap <paramref name="height"/> tall.</summary>
    /// <param name="part">The parsed cap or joining word.</param>
    /// <param name="height">The cap height in logical pixels.</param>
    /// <returns>The estimated logical width using the current icon and font metrics.</returns>
    public static double CapWidth(ChordPart part, double height)
    {
        if (part.IsJoiner) return part.Text.Length * MonoChar + 4;
        double inner;
        if (part.Glyph is null) inner = part.Text.Length * MonoChar;
        else
        {
            inner = part.Glyph.Icons.Length * IconBox + (part.Glyph.Icons.Length - 1) * IconGap;
            if (part.Glyph.Label is { } l) inner += IconGap + 1 + l.Length * MonoChar;
        }
        return Math.Max(height, Math.Ceiling(inner + CapPad));
    }

    /// <summary>The width <see cref="KeyChord"/> draws <paramref name="chord"/> at.</summary>
    /// <param name="chord">The chord text, or null for no caps.</param>
    /// <param name="height">Each cap's height in logical pixels.</param>
    /// <returns>The estimated width including gaps, or zero for an empty chord.</returns>
    public static double EstimateWidth(string? chord, double height)
    {
        var parts = Parse(chord);
        if (parts.Count == 0) return 0;
        return parts.Sum(p => CapWidth(p, height)) + (parts.Count - 1) * CapSpacing;
    }

    /// <summary>The chord as plain words, for an accessible name.</summary>
    /// <param name="chord">The chord text, or null.</param>
    /// <returns>Parsed labels separated by spaces, or an empty string.</returns>
    public static string Spoken(string? chord)
    {
        var sb = new StringBuilder();
        foreach (var p in Parse(chord)) sb.Append(sb.Length > 0 ? " " : "").Append(p.Text);
        return sb.ToString();
    }
}
