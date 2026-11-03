namespace Nfty.App.Viewer;

/// <summary>One label/value line in the viewer's facts panel.</summary>
/// <param name="Label">What the fact is.</param>
/// <param name="Value">Its value, already worded for display.</param>
public sealed record ViewerFact(string Label, string Value)
{
    /// <summary>A long unbroken value (a DNA hash): drawn under its label across the whole card,
    /// rather than wrapped into the value column four lines deep.</summary>
    public bool IsWide => Value.Length > 32 && !Value.Contains(' ', StringComparison.Ordinal);

    /// <summary>A wide value split at its half onto two lines of equal length, as the Set browser
    /// splits a DNA; the value itself, for anything not wide.</summary>
    public string WideText => IsWide ? $"{Value[..((Value.Length + 1) / 2)]}\n{Value[((Value.Length + 1) / 2)..]}" : Value;

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
        var rows = facts.Select(f => (Label: Word(f.Label) + ":", f.Value)).ToList();
        int width = rows.Count == 0 ? 0 : rows.Max(r => r.Label.Length);
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(title))
        {
            lines.Add(title.Trim());
            lines.Add(new string('-', title.Trim().Length));
        }
        lines.AddRange(rows.Select(r => r.Label.PadRight(width + 2) + r.Value));
        return string.Join(Environment.NewLine, lines);
    }

    // RECIPE -> Recipe, DNA stays DNA: an initialism reads as itself.
    private static string Word(string label) =>
        label.Length <= 3 ? label : char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
}
