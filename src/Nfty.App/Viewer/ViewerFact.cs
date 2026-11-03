namespace Nfty.App.Viewer;

/// <summary>
/// Which tab of the info rail a fact sits under. The rail shows one group at a time, so a long
/// subject (a bundle item with a dozen traits and three links) reads as a few short lists rather than
/// one tall one that scrolls; the extended panel shows every group at once.
/// </summary>
public enum ViewerFactGroup
{
    /// <summary>What the subject IS: its number, name, recipe, size, DNA, description.</summary>
    Item,
    /// <summary>The published traits, one row per layer (and its rolled color).</summary>
    Traits,
    /// <summary>Where it points: image fields, links, cover, payee and royalty.</summary>
    Links,
    /// <summary>Where it came from: the file, the archive, how its model was found.</summary>
    File,
}

/// <summary>One label/value line in the viewer's facts panel.</summary>
/// <param name="Label">What the fact is.</param>
/// <param name="Value">Its value, already worded for display.</param>
/// <param name="Banner">A statement about the whole subject ("upload bundle - read only") rather than
/// a label/value pair: drawn as a tinted strip across the card, never squeezed into a value column.
/// A banner belongs to no tab: it stands above them.</param>
/// <param name="Group">The tab the fact is listed under.</param>
public sealed record ViewerFact(string Label, string Value, bool Banner = false,
    ViewerFactGroup Group = ViewerFactGroup.Item)
{
    /// <summary>The tab's name as the rail prints it.</summary>
    /// <param name="group">The group.</param>
    /// <returns>Its caps label.</returns>
    public static string GroupTitle(ViewerFactGroup group) => group switch
    {
        ViewerFactGroup.Traits => "TRAITS",
        ViewerFactGroup.Links => "LINKS",
        ViewerFactGroup.File => "FILE",
        _ => "ITEM",
    };

    /// <summary>The most characters a value may have and still share its label's line: what the
    /// 116px value column holds in 12px mono. Anything longer moves under its label at full width,
    /// so the rail never wraps a value into a narrow column five lines deep.</summary>
    public const int InlineChars = 15;

    /// <summary>The label as drawn: always the rail's small caps, whatever case a source spelled it in
    /// (a metadata file's <c>trait_type</c> arrives as "Lock").</summary>
    public string LabelText => Label.ToUpperInvariant();

    /// <summary>Short: label and value side by side on one line.</summary>
    public bool IsInline => !Banner && Value.Length <= InlineChars && Label.Length <= InlineLabelChars;

    /// <summary>The longest label the 72px label column holds ("COLLECTION"); a longer one ("BANDS COLOR")
    /// moves its value under it, so no label is ever trimmed.</summary>
    public const int InlineLabelChars = 10;

    /// <summary>A long unbroken value (a DNA hash, a URI, a wallet address): under its label across
    /// the whole card, in small mono, two lines at most and trimmed past that (the tooltip and the
    /// selection carry the whole value).</summary>
    public bool IsWide => !Banner && !IsInline && !Value.Contains(' ', StringComparison.Ordinal);

    /// <summary>Long text with words (a description, a sentence): under its label at full width in
    /// the reading face, three lines at most.</summary>
    public bool IsProse => !Banner && !IsInline && !IsWide;

    /// <summary>How many characters of small mono one line of the card holds.</summary>
    public const int WideLineChars = 32;

    /// <summary>
    /// A wide value as drawn. A HASH or ADDRESS (hex, optionally <c>0x</c>-prefixed) that fits two
    /// lines is split at its half, so both lines are one length (the Set browser's DNA split) and no
    /// digit is hidden. Anything else (a link, a path) stays on one line: whole when it fits, else
    /// shortened in the MIDDLE so both its scheme and its informative end (<c>…/images/1.png</c>)
    /// show. The tooltip and Copy info always carry the whole value.
    /// </summary>
    public string WideText
    {
        get
        {
            if (!IsWide) return Value;
            if (IsHex && Value.Length > WideLineChars && Value.Length <= WideLineChars * 2)
                return Value[..((Value.Length + 1) / 2)] + "\n" + Value[((Value.Length + 1) / 2)..];
            if (Value.Length <= WideLineChars) return Value;
            const int tail = 15;
            return Value[..(WideLineChars - tail - 1)] + "…" + Value[^tail..];
        }
    }

    private bool IsHex
    {
        get
        {
            var v = Value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Value[2..] : Value;
            return v.Length > 0 && v.All(Uri.IsHexDigit);
        }
    }

    /// <summary>
    /// Turns a one-line fact written for a page ("seed viewer", "canvas 64 × 64", "picture · 32 × 32")
    /// into a label and a value: the first word names it, the rest is the value. A line that does not
    /// open with a word ("1 → paints first, furthest back") becomes a NOTE, so no line is dropped and
    /// none is shown with an empty half.
    /// </summary>
    /// <param name="line">The page's own wording.</param>
    /// <returns>The fact.</returns>
    public static ViewerFact FromLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line.Trim();
        int space = text.IndexOf(' ');
        if (space > 0 && char.IsLetter(text[0]))
        {
            var rest = text[(space + 1)..].TrimStart('·', ' ');
            if (rest.Length > 0) return new ViewerFact(text[..space].ToUpperInvariant(), rest);
        }
        return new ViewerFact("NOTE", text);
    }

    /// <summary>
    /// The facts as text ready to paste: the subject's name, a rule under it, then one
    /// <c>Label:  value</c> line per fact with the values aligned in one column, so it reads the
    /// same in a chat, an issue or a plain-text file. Hashes stay whole on one line.
    /// </summary>
    /// <param name="title">The subject's name, the heading.</param>
    /// <param name="facts">The facts.</param>
    /// <returns>The text.</returns>
    public static string AsText(string title, IEnumerable<ViewerFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var all = facts.ToList();
        var rows = all.Where(f => !f.Banner).Select(f => (Label: Word(f.Label) + ":", f.Value)).ToList();
        int width = rows.Count == 0 ? 0 : rows.Max(r => r.Label.Length);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            lines.Add(title.Trim());
            lines.Add(new string('-', title.Trim().Length));
        }
        lines.AddRange(all.Where(f => f.Banner).Select(f => f.Value));   // a banner is a line of its own
        lines.AddRange(rows.Select(r => r.Label.PadRight(width + 2) + r.Value));
        return string.Join(Environment.NewLine, lines);
    }

    // RECIPE -> Recipe, DNA stays DNA: an initialism reads as itself.
    private static string Word(string label) =>
        label.Length <= 3 ? label : char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
}
