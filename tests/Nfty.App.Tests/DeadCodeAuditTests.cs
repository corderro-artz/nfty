using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Source-level audits for the two failure modes a binding test cannot see, plus a guard against
/// dead decoration accumulating.
///
/// <para><c>WiringCoverageTests</c> already proves every command is reachable from some binding, key
/// gesture or code-behind call. That is a different question from the two here: a control that looks
/// actionable and is bound to <b>nothing at all</b>, and a command that is bound but whose body does
/// <b>nothing</b>. This project has shipped both — Landing's "+ Recipe" collected a name, a weight
/// and a destination and dropped the result on the floor; the Ingredient hero carried a permanently
/// visible "Jump to rules" button over an empty method; the theme toggle existed with nothing bound
/// to it. Each was found by a person driving the app, which is not a repeatable way to find them.</para>
/// </summary>
public class DeadCodeAuditTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<string> SourceFiles(string extension, params string[] projects) =>
        projects
            .Select(p => Path.Combine(RepoRoot(), "src", p))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, extension, SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Blanks XML comments while keeping every newline, so a reported line number still points at the
    /// line the file actually has. A commented-out binding is not a binding, and prose naming a
    /// command would otherwise read as wiring one.
    /// </summary>
    private static string Uncommented(string markup) =>
        Regex.Replace(markup, "<!--.*?-->",
            m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);

    /// <summary>
    /// Every Button either carries a Command, carries a Click handler, or lives in a view whose
    /// code-behind takes <c>Button.ClickEvent</c> as it bubbles.
    /// </summary>
    /// <remarks>
    /// The third is a real pattern here and not a loophole: the Set browser's tiles are an
    /// ItemsControl of rows of tiles, so a per-tile command binding would have to hop two
    /// DataContexts up through a template — the fragile kind this codebase has a rule about. One
    /// bubbled handler reads the row's own DataContext instead.
    /// </remarks>
    [Fact]
    public void Every_button_does_something_when_it_is_pressed()
    {
        var orphans = new List<string>();

        foreach (var file in SourceFiles("*.axaml", "Nfty.App", "Nfty.Desktop"))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}"))
                continue;   // a ControlTheme's template buttons are wired by the control itself

            var markup = Uncommented(File.ReadAllText(file));
            string behind = file + ".cs";
            bool bubbles = File.Exists(behind) && File.ReadAllText(behind).Contains("Button.ClickEvent");

            // `<Button` followed by whitespace or the tag's own end — NOT `\b`, which also matches
            // `<Button.ContextMenu>`. That is a property element, not a control, and it can no more
            // carry a Command than a `<Grid.RowDefinitions>` can.
            foreach (Match m in Regex.Matches(markup, @"<Button(?=[\s/>])((?:[^>""]|""[^""]*"")*?)/?>", RegexOptions.Singleline))
            {
                var attrs = m.Groups[1].Value;
                if (attrs.Contains("Command") || attrs.Contains("Click=")) continue;
                if (bubbles) continue;

                int line = markup.Take(m.Index).Count(c => c == '\n') + 1;
                orphans.Add($"{Path.GetFileName(file)}:{line}");
            }
        }

        Assert.True(orphans.Count == 0,
            "These Buttons are bound to nothing and their view has no bubbled click handler, so "
            + "pressing them does nothing: " + string.Join(", ", orphans));
    }

    /// <summary>
    /// No command has an empty body.
    /// </summary>
    /// <remarks>
    /// The exact shape of the Ingredient hero's old "Jump to rules": a control that looked available,
    /// a binding that resolved, a command that existed, and a method with nothing in it. Every test
    /// passed. A body that is only a comment counts as empty here, because a comment does not run.
    /// </remarks>
    [Fact]
    public void No_command_has_an_empty_body()
    {
        var empties = new List<string>();

        foreach (var file in SourceFiles("*.cs", "Nfty.App"))
        {
            var src = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(src,
                @"\[RelayCommand[^\]]*\]\s*(?:\n\s*\[[^\]]*\]\s*)*"
                + @"(?:private|public|internal|protected|static|async|partial|\s|[\w<>,\?\[\]\.])*?"
                + @"(\w+)\s*\([^)]*\)\s*(\{[^{}]*\}|=>[^;]+;)",
                RegexOptions.Singleline))
            {
                var body = m.Groups[2].Value.Trim();
                var inner = body.StartsWith('{')
                    ? body[1..^1]
                    : body[2..].TrimEnd(';');
                inner = Regex.Replace(inner, @"//[^\n]*", "").Trim();

                if (inner.Length == 0)
                    empties.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        }

        Assert.True(empties.Count == 0,
            "These commands are bound to controls and do nothing when invoked: "
            + string.Join(", ", empties));
    }

    /// <summary>
    /// Every style class the theme defines is used by some markup or set from some code.
    /// </summary>
    /// <remarks>
    /// A dead style is not merely clutter: the next person reading the theme cannot tell it from a
    /// live one, and a class that once meant something is exactly what gets copied onto a new
    /// control. 258 classes is too many to keep straight by hand.
    /// </remarks>
    [Fact]
    public void Every_style_class_is_used_by_something()
    {
        string themes = Path.Combine(RepoRoot(), "src", "Nfty.App", "Themes");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(themes, "*.axaml"))
            foreach (Match sel in Regex.Matches(File.ReadAllText(file), @"Selector=""([^""]+)"""))
                foreach (Match cls in Regex.Matches(sel.Groups[1].Value, @"\.([A-Za-z][\w\-]*)"))
                    defined.Add(cls.Groups[1].Value);

        var haystack = string.Join('\n',
            SourceFiles("*.axaml", "Nfty.App", "Nfty.Desktop").Select(File.ReadAllText)
                .Concat(SourceFiles("*.cs", "Nfty.App").Select(File.ReadAllText)));

        var dead = defined
            .Where(c => !Regex.IsMatch(haystack, $@"Classes(\.{Regex.Escape(c)})?\s*=\s*""[^""]*\b{Regex.Escape(c)}\b")
                     && !Regex.IsMatch(haystack, $@"Classes\.{Regex.Escape(c)}\b")
                     && !Regex.IsMatch(haystack, $@"""{Regex.Escape(c)}"""))
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.True(dead.Count == 0,
            "These style classes are defined and never applied: " + string.Join(", ", dead));
    }

    /// <summary>Every icon geometry the theme defines is drawn somewhere.</summary>
    [Fact]
    public void Every_icon_is_drawn_somewhere()
    {
        string icons = Path.Combine(RepoRoot(), "src", "Nfty.App", "Themes", "Icons.axaml");
        var keys = Regex.Matches(File.ReadAllText(icons), @"x:Key=""(Icon\w+)""")
            .Select(m => m.Groups[1].Value).ToList();

        var haystack = string.Join('\n',
            SourceFiles("*.axaml", "Nfty.App", "Nfty.Desktop")
                .Where(f => !f.EndsWith("Icons.axaml", StringComparison.Ordinal))
                .Select(File.ReadAllText)
                .Concat(SourceFiles("*.cs", "Nfty.App").Select(File.ReadAllText)));

        var dead = keys.Where(k => !haystack.Contains(k, StringComparison.Ordinal)).ToList();
        Assert.True(dead.Count == 0,
            "These icons are defined and never drawn: " + string.Join(", ", dead));
    }

    /// <summary>Theme keys nothing in this repo names, each with the reason. Same discipline as
    /// <c>WiringCoverageTests.Exempt</c>: a name here is a decision, not a way to quiet the test.</summary>
    private static readonly Dictionary<string, string> ExemptTokens = new(StringComparer.Ordinal)
    {
        // Fluent's own TextBox template reads this key. The app overrides it so a placeholder is the
        // app's muted ink rather than the system gray; nothing here names it, and that is the point.
        ["TextControlPlaceholderForeground"] = "consumed by Fluent's TextBox template, not by us",
    };

    /// <summary>Every token the theme defines is referenced by something.</summary>
    /// <remarks>
    /// The mirror image of
    /// <c>ThemeResourceTests.Every_dynamic_resource_the_markup_references_resolves_in_both_themes</c>,
    /// which walks the other way. That one catches a reference with no definition — the
    /// <c>WarningBrush</c> bug, where an unresolved <c>DynamicResource</c> silently kept the
    /// property's default. This one catches a definition with no reference, which is how a palette
    /// grows two names for one color and they drift.
    /// </remarks>
    [Fact]
    public void Every_token_is_referenced_by_something()
    {
        string tokens = Path.Combine(RepoRoot(), "src", "Nfty.App", "Themes", "Tokens.axaml");
        var keys = Regex.Matches(File.ReadAllText(tokens), @"x:Key=""(\w+)""")
            .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

        var haystack = string.Join('\n',
            SourceFiles("*.axaml", "Nfty.App", "Nfty.Desktop")
                .Where(f => !f.EndsWith("Tokens.axaml", StringComparison.Ordinal))
                .Select(File.ReadAllText)
                .Concat(SourceFiles("*.cs", "Nfty.App").Select(File.ReadAllText)));

        var dead = keys
            .Where(k => !ExemptTokens.ContainsKey(k) && !Regex.IsMatch(haystack, $@"\b{Regex.Escape(k)}\b"))
            .ToList();
        Assert.True(dead.Count == 0,
            "These tokens are defined and never referenced: " + string.Join(", ", dead));

        // And an exemption must not outlive its reason.
        foreach (var (name, reason) in ExemptTokens)
            Assert.True(keys.Contains(name, StringComparer.Ordinal),
                $"'{name}' is exempt but Tokens.axaml no longer defines it — drop the exemption. ({reason})");
    }
}
