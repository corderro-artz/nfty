using System.Globalization;
using System.Text;
using Nfty.Core.Formats;
using Nfty.Core.Generation;

namespace Nfty.Core.Stats;

/// <summary>
/// The <c>stats</c> report as data plus a plain-text rendering: the odds a CookBook's weights imply,
/// and the size of the unique-DNA space they admit.
///
/// The CLI printed this straight to the console, so the GUI could only have re-derived it. Both now
/// call this, which also means the text a user copies out of the GUI is byte-identical to what the
/// command prints — the same report, not a similar one.
/// </summary>
public static class CollectionReport
{
    /// <summary>Renders the report. Every number is formatted with
    /// <see cref="CultureInfo.InvariantCulture"/>: this is a report a user copies, pastes into an
    /// issue and compares against someone else's, so a decimal comma on one machine and a point on
    /// another would make two identical collections look different.</summary>
    public static string Render(LoadedCookBook book)
    {
        var report = RarityCalculator.Compute(book);
        var sb = new StringBuilder();

        sb.AppendLine("Recipes:");
        foreach (var r in report.Recipes)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {r.RecipeName,-16} {r.Percent,6:0.00}%"));

        sb.AppendLine("Traits (overall):");
        foreach (var t in report.Traits)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {t.RecipeName,-12} {t.IngredientName,-14} {t.VariantName,-14} {t.OverallPercent,6:0.00}%"));

        AppendOptionalLayers(sb, book);
        sb.AppendLine(UniqueDnaLine(book));
        return sb.ToString();
    }

    /// <summary>
    /// The layers that may be left out, and how often. Printed only when a book uses them, so a
    /// book that does not renders byte-identically to before the feature existed — these reports
    /// get copied between machines and compared, and a new empty heading would make two identical
    /// collections look different.
    /// </summary>
    /// <remarks>
    /// It earns its place beside the trait table rather than duplicating it. The trait percentages
    /// above already fold absence in, so a chase item reads correctly there — but they cannot say
    /// WHY it is rare, and "this variant is one of two on a layer that shows up 10% of the time" is
    /// a different fact from "this variant has a low weight". This section is the second one.
    /// </remarks>
    private static void AppendOptionalLayers(StringBuilder sb, LoadedCookBook book)
    {
        var rows = new List<(string Recipe, string Layer, double Percent)>();
        foreach (var r in book.Recipes)
        {
            var ingById = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
            foreach (var i in r.Ingredients) ingById[i.Manifest.Id] = i;

            // In layerOrder, like every other per-layer listing in this product, so the same layers
            // are not shuffled between two reports one command apart.
            foreach (var id in r.Manifest.LayerOrder)
            {
                double pct = r.Manifest.AbsentPercentOf(id);
                if (pct <= 0) continue;
                string name = ingById.TryGetValue(id, out var ing) ? ing.Manifest.Name : id;
                rows.Add((r.Manifest.Name, name, pct));
            }
        }

        if (rows.Count == 0) return;

        sb.AppendLine("Optional layers:");
        foreach (var (recipe, layer, pct) in rows)
            sb.AppendLine(pct >= 100
                ? string.Create(CultureInfo.InvariantCulture,
                    $"  {recipe,-12} {layer,-14} never appears")
                : string.Create(CultureInfo.InvariantCulture,
                    $"  {recipe,-12} {layer,-14} absent {pct,6:0.00}%  present {100 - pct,6:0.00}%"));
    }

    /// <summary>The largest number of unique-DNA assets this CookBook can produce — the figure
    /// generate reports only on failure, surfaced so a run can be sized before it starts.
    ///
    /// <para>Three outcomes, not two. An exact count prints the number. A count that saturated its
    /// enumeration cap prints "more than N". And a book whose space is <em>undefined</em> — because
    /// it is invalid in a way that makes the question meaningless, such as a Dynamic layer with no
    /// colorization block — says so, rather than claiming "more than 0", which reads like a real
    /// lower bound.</para>
    ///
    /// <para><see cref="UniqueSpace.Count"/> reports that third case as <c>Total == 0</c> with
    /// <c>IsExact == false</c> and is documented never to throw; the catch below is kept as
    /// belt-and-braces, not as the mechanism. It used to be the mechanism, which meant the
    /// no-throw contract was asserted in one file and quietly worked around in this one.</para></summary>
    /// <param name="book">The CookBook to size.</param>
    /// <returns>A single line, always — this never throws and never omits the line.</returns>
    public static string UniqueDnaLine(LoadedCookBook book)
    {
        try
        {
            var space = UniqueSpace.Count(book);
            if (!space.IsCountable)
                return "Unique DNA space: cannot be counted (the CookBook has problems; run validate)";
            return space.IsExact
                ? $"Unique DNA space: {space.Total}"
                : $"Unique DNA space: more than {space.Total}";
        }
        catch (Exception ex)
        {
            return $"Unique DNA space: cannot be counted ({ex.Message})";
        }
    }
}
