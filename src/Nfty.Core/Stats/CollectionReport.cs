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

        sb.AppendLine(UniqueDnaLine(book));
        return sb.ToString();
    }

    /// <summary>The largest number of unique-DNA assets this CookBook can produce — the figure
    /// generate reports only on failure, surfaced so a run can be sized before it starts.
    ///
    /// Counting can throw on a book Validator would reject (a Dynamic layer with no colorization
    /// block dereferences null inside UniqueSpace), and a report that cannot be produced for a broken
    /// book is useless precisely when it is most wanted — so that case degrades to a line saying so
    /// rather than taking the whole report down.</summary>
    public static string UniqueDnaLine(LoadedCookBook book)
    {
        try
        {
            var space = UniqueSpace.Count(book);
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
