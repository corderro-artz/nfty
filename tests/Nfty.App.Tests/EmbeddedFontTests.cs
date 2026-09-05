using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The app's two typefaces are shipped with it, and all four weights of each really are their own
/// face.
/// </summary>
/// <remarks>
/// <para>Both claims need proving because both used to be false in a way nothing could see. The font
/// tokens were CSS stacks copied from the archived mockups — so the app rendered in Segoe UI and
/// Cascadia Code here, in something else on Linux, and the stacks' first entry
/// (<c>-apple-system</c>) is a CSS keyword that resolves to no font in Avalonia on any platform.
/// A face that varies by machine cannot be a theme, and this app's figures get screenshotted into a
/// manual and compared across machines.</para>
///
/// <para>The weight claim is the subtler one. A legacy TTF name table can only express
/// Regular/Bold/Italic/BoldItalic within a single family, so IBM Plex ships Medium and SemiBold under
/// their OWN nameID-1 families ("IBM Plex Sans SmBld") and only the typographic name (nameID 16) says
/// "IBM Plex Sans". If Avalonia grouped by the legacy name instead, asking for SemiBold would find
/// nothing and the renderer would SYNTHESISE a bold by smearing the regular outline — which looks
/// approximately right, fails no test, and is precisely the "hard to differentiate at heavier
/// weights" complaint that started this work. SemiBold is the app's most-used weight.</para>
/// </remarks>
public class EmbeddedFontTests
{
    private const string Sans = "avares://Nfty.App/Assets/Fonts#IBM Plex Sans";
    private const string Mono = "avares://Nfty.App/Assets/Fonts#IBM Plex Mono";

    /// <summary>The face the font manager actually hands back for a family and weight.</summary>
    /// <remarks>
    /// Identity, not measured WIDTH, is the observable. Width was the first thing tried and it is
    /// wrong by construction for half the question: a monospace family has the same advances at
    /// every weight — that is what monospace means — so all four weights of the mono face measure
    /// identically whether they are four real faces or one synthesised four times.
    /// </remarks>
    private static GlyphTypeface? Face(string family, FontWeight weight) =>
        FontManager.Current.TryGetGlyphTypeface(
            new Typeface(new FontFamily(family), FontStyle.Normal, weight), out var gt) ? gt : null;

    [AvaloniaTheory]
    [InlineData(Sans, "IBM Plex Sans")]
    [InlineData(Mono, "IBM Plex Mono")]
    public void The_family_resolves_to_the_embedded_font_and_not_a_system_fallback(string uri, string expected)
    {
        var face = Face(uri, FontWeight.Normal);
        Assert.NotNull(face);
        Assert.StartsWith(expected, face!.FamilyName, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Four weights, four real faces — each reporting the weight that was asked for.
    /// </summary>
    /// <remarks>
    /// The faces come back under DIFFERENT family names ("IBM Plex Sans SmBld" for SemiBold), which
    /// is the legacy-name split this file's header describes, and is exactly why this is worth
    /// asserting: Avalonia matches across the collection by weight, and if it ever stopped, the
    /// renderer would synthesise a bold by smearing the regular outline instead of failing.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(Sans)]
    [InlineData(Mono)]
    public void Every_weight_the_app_uses_is_a_real_face_of_its_own(string family)
    {
        var weights = new[]
        {
            FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold,
        };

        var faces = new List<string>();
        foreach (var w in weights)
        {
            var face = Face(family, w);
            Assert.True(face is not null, $"'{family}' has no face for {w}.");
            Assert.True(face!.Weight == w,
                $"'{family}' at {w} came back as {face.Weight} ({face.FamilyName}) — the weight is "
                + "being synthesised rather than loaded.");
            faces.Add(face.FamilyName + "/" + face.Weight);
        }

        Assert.True(faces.Distinct().Count() == weights.Length,
            "two weights resolved to the same face: " + string.Join(", ", faces));
    }

    /// <summary>Measures a string — used only for the monospace check, where advance width IS the
    /// property under test.</summary>
    private static double Width(string family, string text) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily(family)), 24, Brushes.Black).Width;

    /// <summary>
    /// The two families are genuinely different from each other, and the mono one really is
    /// monospaced. The second is what the data tables depend on: every column-aligned report in this
    /// app is aligned by advance width alone.
    /// </summary>
    [AvaloniaFact]
    public void The_mono_face_is_monospaced_and_is_not_the_sans_face()
    {
        Assert.NotEqual(Face(Sans, FontWeight.Normal)!.FamilyName, Face(Mono, FontWeight.Normal)!.FamilyName);

        // Every character advances the same, which is the whole definition and the thing the
        // reports rely on. Compared as whole strings so kerning cannot hide a difference.
        double iii = Width(Mono, "iiiiiiiiii");
        double mmm = Width(Mono, "MMMMMMMMMM");
        Assert.Equal(iii, mmm, 1);

        // And the sans is NOT monospaced — otherwise the check above would pass on either family and
        // prove nothing about which one is which.
        Assert.NotEqual(Width(Sans, "iiiiiiiiii"), Width(Sans, "MMMMMMMMMM"));
    }

    /// <summary>
    /// Nothing in the app names a font except the two tokens.
    /// </summary>
    /// <remarks>
    /// The reason "consistent across the app" holds at all: 75 markup sites set a FontFamily and
    /// every one of them reads a token, so changing the typeface is a two-line edit rather than a
    /// sweep. A single literal would opt one control out of the theme silently.
    /// </remarks>
    [AvaloniaFact]
    public void No_control_names_a_typeface_of_its_own()
    {
        var offenders = new List<string>();
        foreach (var file in System.IO.Directory.EnumerateFiles(
                     System.IO.Path.Combine(RepoRoot(), "src"), "*.axaml",
                     System.IO.SearchOption.AllDirectories))
        {
            if (file.EndsWith("Tokens.axaml", System.StringComparison.Ordinal)) continue;
            if (file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}")) continue;

            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         System.IO.File.ReadAllText(file), @"FontFamily\s*=\s*""([^""]*)"""))
            {
                if (!m.Groups[1].Value.Contains("DynamicResource", System.StringComparison.Ordinal)
                    && !m.Groups[1].Value.Contains("StaticResource", System.StringComparison.Ordinal))
                    offenders.Add($"{System.IO.Path.GetFileName(file)}: {m.Groups[1].Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "These name a typeface directly instead of reading SansFontFamily/MonoFontFamily: "
            + string.Join(", ", offenders));
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
