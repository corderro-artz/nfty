using System.Text.RegularExpressions;

namespace Nfty.Cli.Tests;

/// <summary>The collection's uniqueness figure is called "unique DNA" on every surface a person reads.</summary>
public class UniquenessWordingTests
{
    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "nfty.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("nfty.sln not found");
    }

    [Fact]
    public void User_facing_text_says_unique_DNA_never_unique_art_assets_or_space()
    {
        var root = Root();
        var bad = new Regex(@"unique[ -](art\w*|assets?|space|count)\b", RegexOptions.IgnoreCase);
        var files = new List<string> { Path.Combine(root, "README.md") };
        // reference/api is GENERATED from the libraries' /// comments, which this test exempts line by
        // line below ("unique assets" is accurate in a comment a maintainer reads). The pages mirror
        // those comments, so they are exempt as a folder; the prose the manual WRITES is not.
        var generated = Path.Combine(root, "docs", "manual", "reference", "api") + Path.DirectorySeparatorChar;
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "docs", "manual"), "*.md", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(generated, StringComparison.OrdinalIgnoreCase)));
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "src", "Nfty.App"), "*.axaml", SearchOption.AllDirectories));
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "src", "Nfty.Cli"), "*.cs", SearchOption.AllDirectories));

        var hits = new List<string>();
        foreach (var f in files)
        {
            var n = 0;
            foreach (var raw in File.ReadLines(f))
            {
                n++;
                var line = raw.TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("<!--")) continue;
                if (bad.IsMatch(Regex.Replace(line, @"]([^)]*)", "]"))) hits.Add($"{Path.GetRelativePath(root, f)}:{n}: {line}");
            }
        }
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));
    }
}
