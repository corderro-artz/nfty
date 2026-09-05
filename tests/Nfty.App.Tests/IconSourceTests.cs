using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The SVG sources in <c>assets/icons/</c> and the generated <c>Icons.axaml</c> say the same thing.
/// </summary>
/// <remarks>
/// <para>Keeping source artwork beside a generated copy is only worth something if the two cannot
/// drift. Without this, the obvious repair for a wonky glyph is to nudge the path in Icons.axaml —
/// it is the file the app reads, the edit works, and the SVG quietly stops being the drawing while
/// still looking like it is. That is the failure mode of every "source of truth" that nothing
/// checks.</para>
///
/// <para>So the rule is: edit the SVG, run <c>python tools/icons/build.py</c>, commit both. This
/// test is what makes that a rule rather than a convention.</para>
/// </remarks>
public class IconSourceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string SvgDir() => Path.Combine(RepoRoot(), "assets", "icons");

    private static string AxamlPath() =>
        Path.Combine(RepoRoot(), "src", "Nfty.App", "Themes", "Icons.axaml");

    /// <summary>Key to path data, as the app will actually read it.</summary>
    private static Dictionary<string, string> Generated()
    {
        var text = File.ReadAllText(AxamlPath());
        return Regex.Matches(text, @"<StreamGeometry x:Key=""(\w+)"">(.*?)</StreamGeometry>",
                RegexOptions.Singleline)
            .ToDictionary(m => m.Groups[1].Value,
                          m => Normalize(m.Groups[2].Value), StringComparer.Ordinal);
    }

    /// <summary>Key to path data, as the drawing declares it.</summary>
    private static Dictionary<string, string> Sources()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(SvgDir(), "*.svg"))
        {
            var text = File.ReadAllText(file);
            var key = Regex.Match(text, @"<title>(\w+)</title>").Groups[1].Value;
            Assert.True(key.Length > 0, $"{Path.GetFileName(file)} has no <title> naming its key.");

            var paths = Regex.Matches(text, @"<path[^>]*\bd=""([^""]+)""");
            Assert.True(paths.Count == 1,
                $"{Path.GetFileName(file)} has {paths.Count} <path> elements. A StreamGeometry is one "
                + "geometry, so a glyph is authored as a single path — join subpaths with M.");

            map[key] = Normalize(paths[0].Groups[1].Value);
        }
        return map;
    }

    private static string Normalize(string d) =>
        string.Join(" ", d.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void Every_icon_the_app_draws_has_a_drawing_behind_it()
    {
        var generated = Generated();
        var sources = Sources();

        Assert.True(generated.Count > 30,
            $"only {generated.Count} geometries found — the scan is probably broken.");

        var missing = generated.Keys.Except(sources.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            "These are in Icons.axaml with no SVG source — add the drawing, do not hand-edit the "
            + "generated file: " + string.Join(", ", missing));

        var orphaned = sources.Keys.Except(generated.Keys, StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(orphaned.Count == 0,
            "These SVGs are not in Icons.axaml — run `python tools/icons/build.py`: "
            + string.Join(", ", orphaned));
    }

    [Fact]
    public void The_generated_geometry_is_what_the_drawing_says()
    {
        var generated = Generated();
        var sources = Sources();

        var drifted = sources
            .Where(kv => generated.TryGetValue(kv.Key, out var d) && d != kv.Value)
            .Select(kv => kv.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(drifted.Count == 0,
            "Icons.axaml disagrees with the SVG source for: " + string.Join(", ", drifted)
            + ". Edit the SVG and run `python tools/icons/build.py`; the axaml is generated.");
    }

    /// <summary>
    /// Every glyph is authored in the same 24-unit box, which is what lets one scale transform serve
    /// the whole set.
    /// </summary>
    /// <remarks>
    /// <c>Path.ico</c> maps that shared box onto the icon size the way an <c>&lt;svg viewBox&gt;</c>
    /// does. A glyph drawn in a different box would be scaled by the same factor as everything else
    /// and simply come out the wrong size, with nothing to say why.
    /// </remarks>
    [Fact]
    public void Every_drawing_is_in_the_shared_twenty_four_unit_box()
    {
        var wrong = new List<string>();
        foreach (var file in Directory.EnumerateFiles(SvgDir(), "*.svg"))
        {
            var text = File.ReadAllText(file);
            var box = Regex.Match(text, @"viewBox=""([^""]+)""").Groups[1].Value;
            if (Normalize(box) != "0 0 24 24")
                wrong.Add($"{Path.GetFileName(file)}: viewBox=\"{box}\"");
        }

        Assert.True(wrong.Count == 0,
            "These are not drawn in the shared 24-unit box: " + string.Join(", ", wrong));
    }
}
