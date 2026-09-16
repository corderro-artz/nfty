using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Mermaid source in <c>docs/diagrams/</c> and the SVGs rendered from it say the same thing.
/// </summary>
/// <remarks>
/// <para>This is <see cref="IconSourceTests"/>'s argument applied one directory over. The README
/// shows the SVG and captions it "Source: generation-pipeline.mmd", which is only true while
/// something checks it — the obvious repair for a wrong word in the picture is to edit the picture,
/// the edit works, and the .mmd quietly stops being the source while still being cited as one.</para>
///
/// <para>It cannot compare geometry: rendering Mermaid needs a browser, and two runs of a layout
/// engine are not expected to agree byte for byte. What it CAN compare is every word, which is the
/// half that carries the meaning and the half that goes stale. The rule is: edit the .mmd, run
/// <c>python tools/diagrams/build.py</c>, commit all three.</para>
///
/// <para>The labels live in <c>foreignObject</c> XHTML rather than in <c>text</c> elements, so they
/// are read out of the parsed document rather than matched with a regex over the raw file.</para>
/// </remarks>
public class DiagramSourceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string DiagramDir() => Path.Combine(RepoRoot(), "docs", "diagrams");

    public static TheoryData<string> Variants() => new() { "light", "dark" };

    /// <summary>Every quoted label in the .mmd, one phrase per line break.</summary>
    private static List<string> SourcePhrases(string mmd)
    {
        var phrases = new List<string>();
        foreach (Match m in Regex.Matches(mmd, "\"([^\"]+)\""))
            foreach (var part in Regex.Split(m.Groups[1].Value, @"<br\s*/?>"))
            {
                var text = WebUtility.HtmlDecode(part).Trim();
                if (text.Length > 0) phrases.Add(text);
            }

        // A diagram nobody edited down to nothing: guards against a regex that silently
        // stops matching and leaves every assertion below vacuously true.
        Assert.True(phrases.Count >= 10, $"only {phrases.Count} phrases parsed from the .mmd");
        return phrases;
    }

    /// <summary>Every run of visible text in the rendered SVG.</summary>
    private static string RenderedText(string svgPath)
    {
        var doc = XDocument.Load(svgPath);
        return string.Join(" ", doc.Descendants().Select(e => (string)e).Where(s => s.Length > 0));
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void Every_word_in_the_source_reaches_the_rendered_diagram(string variant)
    {
        var mmd = File.ReadAllText(Path.Combine(DiagramDir(), "generation-pipeline.mmd"));
        var rendered = RenderedText(Path.Combine(DiagramDir(), $"generation-pipeline-{variant}.svg"));

        foreach (var phrase in SourcePhrases(mmd))
            Assert.True(rendered.Contains(phrase, StringComparison.Ordinal),
                $"the {variant} SVG does not carry \"{phrase}\" — re-run tools/diagrams/build.py");
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public void The_rendered_diagram_is_well_formed_and_sized(string variant)
    {
        // Both halves have shipped broken. Mermaid hands back HTML-serialized markup, so an
        // unclosed <br> inside a label is a fatal XML parse error and the whole picture renders
        // as a broken-image icon; and its default "100%" width gives an <img> no intrinsic size.
        var path = Path.Combine(DiagramDir(), $"generation-pipeline-{variant}.svg");
        var doc = XDocument.Load(path);   // throws on the first of those

        var root = doc.Root!;
        Assert.True(int.TryParse((string?)root.Attribute("width"), out var w) && w > 0,
            "the SVG needs a numeric width to have an intrinsic size as an <img>");
        Assert.True(int.TryParse((string?)root.Attribute("height"), out var h) && h > 0,
            "the SVG needs a numeric height to have an intrinsic size as an <img>");
    }

    [Fact]
    public void The_diagram_speaks_US_English()
    {
        // The product's own rule, and the picture is the one place it was not being checked:
        // a spelling landed in the .mmd and was then baked into both rendered SVGs.
        string[] british = ["colour", "grey", "centre", "behaviour", "cancelled"];
        foreach (var file in Directory.GetFiles(DiagramDir()))
        {
            var text = File.ReadAllText(file);
            foreach (var word in british)
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{Path.GetFileName(file)} contains \"{word}\"");
        }
    }
}
