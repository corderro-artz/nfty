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
        files.AddRange(Directory.EnumerateFiles(Path.Combine(root, "docs", "manual"), "*.md", SearchOption.AllDirectories));
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
