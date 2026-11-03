namespace Nfty.App.Viewer;

/// <summary>One label/value line in the viewer's facts panel.</summary>
/// <param name="Label">What the fact is.</param>
/// <param name="Value">Its value, already worded for display.</param>
public sealed record ViewerFact(string Label, string Value)
{
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

    /// <summary>The facts as plain text, one <c>LABEL: value</c> line each, for the clipboard.</summary>
    /// <param name="facts">The facts.</param>
    /// <returns>The text.</returns>
    public static string AsText(IEnumerable<ViewerFact> facts) =>
        string.Join(Environment.NewLine, facts.Select(f => $"{f.Label}: {f.Value}"));
}
