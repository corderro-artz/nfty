using System.Security.Cryptography;

namespace Nfty.Core.Tests;

/// <summary>
/// Enforces, rather than merely documents, the convention in tests/fixtures/README.md and
/// CLAUDE.md: every file under tests/fixtures/ was written by an older build and its whole value
/// is that it still reads today. Nothing but convention stopped someone from "fixing" a failing
/// FixtureArchiveTests test by regenerating a fixture — which launders a format change into a
/// green suite instead of catching it. This test pins each file's SHA-256 so that any change,
/// intentional or not, fails loudly with an explanation instead of slipping through quietly.
/// </summary>
public class FixtureFreezeTests
{
    // Pinned SHA-256 of every file under tests/fixtures/, computed from the files as they exist
    // right now (2026-07-19). If this test fails, DO NOT update a hash to match a changed file:
    // either the change was accidental (restore the original file), or it is a deliberate format
    // change, in which case bump Schema.Current and add a NEW fixture beside this one — the old
    // fixture and its pinned hash stay exactly as they are.
    //
    // README.md IS THE ONE EXCEPTION, and only because it carries no format: it is prose ABOUT the
    // archives, so editing it cannot launder a format change. Its hash moved once, on 2026-09-04,
    // when the app-wide switch to US spelling reached "colour" in three of its lines. That is the
    // only reason this entry may ever be re-pinned, and it is not a precedent for the three
    // archives above it — theirs move only by being replaced, never by being rewritten.
    private static readonly IReadOnlyDictionary<string, string> ExpectedHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VaporPets.cbk"] = "9cfef410bdee4134a3bfdc51d5777073919d00fbe60c078e5eb0ddd6b501542b",
            ["cat.rcp"] = "a43d3f298dc559a7e9bbcbc271005b380fdc1959bc401331a50f57042bd57a0d",
            ["aura.igt"] = "f8b6156072fd99edcf49b2daa3457b11d5edac3c26819b625da11dc9d9703ccb",
            ["README.md"] = "014dca73500d52fc639e8bbddfc26e2c8f87ef5624a5fbf55f1990512861b7aa",
        };

    private static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    [Fact]
    public void Fixture_file_list_has_not_changed()
    {
        var actualFiles = Directory.GetFiles(FixturesDir)
            .Select(Path.GetFileName)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var expectedFiles = ExpectedHashes.Keys.ToHashSet(StringComparer.Ordinal);

        var added = actualFiles.Except(expectedFiles).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var removed = expectedFiles.Except(actualFiles).OrderBy(f => f, StringComparer.Ordinal).ToList();

        Assert.True(added.Count == 0 && removed.Count == 0,
            "tests/fixtures/ gained or lost a file. These archives are deliberately frozen (see "
            + "tests/fixtures/README.md and CLAUDE.md): an older build wrote them, and their whole "
            + "value is that they still read today. A deliberate format change bumps Schema.Current "
            + "and adds a NEW fixture beside the existing ones — it does not remove or silently add "
            + "to this set, and this test's expectations should only move as a deliberate, reviewed "
            + "part of that change, never to paper over a surprise. "
            + (added.Count > 0 ? $"Unexpected new file(s): {string.Join(", ", added)}. " : "")
            + (removed.Count > 0 ? $"Missing file(s): {string.Join(", ", removed)}. " : ""));
    }

    [Fact]
    public void Every_fixture_file_matches_its_pinned_hash()
    {
        var mismatched = new List<string>();
        foreach (var (name, expected) in ExpectedHashes)
        {
            var path = Path.Combine(FixturesDir, name);
            if (!File.Exists(path)) continue; // reported by Fixture_file_list_has_not_changed instead

            var actual = Sha256Of(path);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                mismatched.Add($"{name}: expected {expected}, got {actual}");
        }

        Assert.True(mismatched.Count == 0,
            "A file under tests/fixtures/ changed content. These archives are deliberately frozen "
            + "(see tests/fixtures/README.md and CLAUDE.md): an older build wrote them, and their "
            + "whole value is that they still read today, so regenerating one to make a failing "
            + "test pass launders a format change into a green suite instead of catching it. If you "
            + "intended a real format change, revert this file, bump Schema.Current, and add a NEW "
            + "fixture beside this one instead — do not 'fix' this test by updating the hash below "
            + "to match. Mismatches found: " + string.Join("; ", mismatched));
    }
}
