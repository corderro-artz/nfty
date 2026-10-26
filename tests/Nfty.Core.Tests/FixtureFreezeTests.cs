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
    // right now (2026-07-19; the three archives replaced 2026-09-25, see below). If this test
    // fails, DO NOT update a hash to match a changed file: either the change was accidental
    // (restore the original file), or it is a deliberate format change, in which case bump
    // Schema.Current and add a NEW fixture beside this one — the old fixture and its pinned hash
    // stay exactly as they are.
    //
    // README.md IS THE ONE EXCEPTION, and only because it carries no format: it is prose ABOUT the
    // archives, so editing it cannot launder a format change. Its hash has moved twice: on
    // 2026-09-04, when the app-wide switch to US spelling reached "colour" in three of its lines,
    // and on 2026-09-25, when it was rewritten to describe the v2 fixtures (provenance, the new
    // depth map, the one-time exception to "don't regenerate"). That is the only reason this entry
    // may ever be re-pinned, and it is not a precedent for the three archives above it — theirs
    // move only by being replaced, never by being rewritten.
    //
    // VaporPets.cbk, cat.rcp and aura.igt moved a SECOND time on 2026-09-25, and this is not a
    // second exception either: the author's own decision to drop v1 (Rise step 4), spelled out in
    // CLAUDE.md and tests/fixtures/README.md, replaced the v1 files outright with v2 ones — same
    // structure, plus one depth map on aura/glow — rather than adding a fourth fixture beside them,
    // because nothing after this point can read the old ones to compare against. From here on the
    // ordinary rule (replace, never rewrite) applies again, against the v2 hashes below.
    //
    // roll-baseline-chest-demo.txt is a different kind of frozen fixture — TicketFreezeTests's own
    // baseline of the demo book's roll, captured before Nfty.Ticket existed, rather than an archive
    // FixtureArchiveTests reads for format compatibility. It belongs in this set for the same
    // reason: nothing but convention would otherwise stop a failing roll-comparison test from being
    // "fixed" by regenerating the very thing it is meant to catch a change in. It did NOT move on
    // 2026-09-25 — the demo's content is identical at schema 2, only its schema moved. It DID move
    // on 2026-10-26, and only because the BOOK it rolls changed, never the roll: the demo was
    // redrawn with more variants per layer (the author's request, so cooked chests vary visibly),
    // which is a different collection by definition. The roll algorithm it guards is untouched;
    // the baseline was recaptured from the new demo by the test's own first-run path, and the
    // archive fixtures above did not move.
    private static readonly IReadOnlyDictionary<string, string> ExpectedHashes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["VaporPets.cbk"] = "389266f23b16b24953b228afdbaccbf152ba540cdfd0a812b74dd3897b100459",
            ["cat.rcp"] = "a5f6fc479c45d144e58fd7b8f4a46b65ae943508ee1c9c601d8ebf0ea7a84cad",
            ["aura.igt"] = "3e4561142f7250e0206303d9b470b9d9d2ebe049bee9c85abe7664b9c76f870e",
            ["README.md"] = "d95bb2bc48d7eb4f12413185c9b8f2df288ed64919172de8792798bc99df3c76",
            ["roll-baseline-chest-demo.txt"] =
                "1041e63fdfad4e2379ca927f2b2198b84a7a47acfa264a20411564aa8619e334",
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
