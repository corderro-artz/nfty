using System.Globalization;
using System.Text;
using Nfty.Core.Output;

namespace Nfty.Core.Stats;

/// <summary>
/// The <c>inspect</c> report for a cooked Set: what it holds, what made it, and the rarity it
/// actually came out with.
/// </summary>
/// <remarks>
/// <para><b>This is the OBSERVED collection, and that is what separates it from
/// <see cref="CollectionReport"/>.</b> <c>stats</c> answers "what odds do these weights imply",
/// computed from a CookBook that has not been cooked. This answers "what did the run produce" — the
/// counts and percentages recorded in <c>set.json</c> when the assets were written. Same order —
/// recipes, then traits — so a run can be read against the prediction it was supposed to match.</para>
///
/// <para><b>The two tables are comparable, not identical, and the differences are the data's, not a
/// choice made here.</b> <c>stats</c> splits its traits by recipe; a Set's rarity table is
/// collection-wide, because that is what rarity IS — one number per trait value across the whole
/// mint — so a layer shared by two recipes is two rows there and one row here. And the recipe table
/// prints IDS, because <c>set.json</c> records ids where <c>stats</c> prints display names (see
/// <see cref="RecipeCount.Recipe"/>), which is why the heading below says so rather than leaving the
/// reader to notice.</para>
///
/// <para><b>It reads the manifest and nothing else.</b> A <see cref="LoadedSet"/> carries image
/// PATHS rather than decoded images, precisely so listing a Set does not pull the whole collection
/// into memory, and this report keeps that promise — a 10,000-asset Set costs the same to inspect as
/// a two-asset one.</para>
///
/// <para>Rendered in Core beside its three siblings for the reason they all are: a front-end that
/// re-derived something similar would drift from this the first time either changed.</para>
/// </remarks>
public static class SetReport
{
    /// <summary>
    /// Renders the report. Every number is formatted with
    /// <see cref="CultureInfo.InvariantCulture"/>, like every other report here: this text is
    /// copied, pasted into an issue and diffed against a colleague's run, so a decimal comma on one
    /// machine would make two identical collections look different.
    /// </summary>
    /// <param name="set">The Set to describe — a folder or an unpacked <c>.set</c>.</param>
    /// <returns>The report text, newline-terminated per line.</returns>
    public static string Render(LoadedSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        var m = set.Manifest;
        var sb = new StringBuilder();

        sb.AppendLine($"Set: {m.Name}");
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"  Assets: {m.Count}"));
        sb.AppendLine($"  Seed: {m.Seed}");

        // Three states, not two. A Set written before the field existed cannot say whether its run
        // required unique DNA, and claiming "no" would be inventing a fact - the same distinction
        // SetManifest.UniqueDna carries. It matters here because the seed does not reproduce a run
        // on its own: the two modes consume different numbers of RNG draws.
        sb.AppendLine("  Unique DNA: " + m.UniqueDna switch
        {
            true => "required - every asset is distinct",
            false => "not required - assets may repeat",
            null => "not recorded (written by an earlier build)",
        });

        // What ties this Set back to the exact archive that produced it. Null is a real state - an
        // in-memory book never had a file - so it is reported rather than omitted, because "no
        // hash" and "a hash you cannot see" are different things to a reader checking provenance.
        sb.AppendLine($"  CookBook SHA-256: {m.CookbookSha256 ?? "not recorded"}");
        sb.AppendLine($"  Generator: {m.GeneratorVersion}");

        if (m.Distribution.Count > 0)
        {
            // "by id", stated: the manifest records ids, and a column of lowercase run-together
            // words under a heading that read "Recipes" invites the reader to think the collection
            // was named badly rather than that they are looking at identifiers.
            sb.AppendLine("Recipes (by id):");
            foreach (var r in m.Distribution)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {r.Recipe,-16} {r.Count,6} {r.Percent,7:0.00}%"));
        }

        if (m.Rarity.Count > 0)
        {
            // In the manifest's own order, which groups by trait. NOT sorted rarest-first, however
            // much that reads like the more useful view: `stats` groups its traits the same way, and
            // a reader comparing predicted odds against observed ones needs to find the same trait
            // in the same place. Someone who wants the chase item can sort these lines themselves;
            // someone comparing two reports cannot un-sort them.
            sb.AppendLine("Traits (observed):");
            foreach (var t in m.Rarity)
                sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"  {t.Trait_type,-14} {t.Value,-14} {t.RarityPct,6:0.00}%"));
        }

        return sb.ToString();
    }
}
