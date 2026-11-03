using System.Globalization;
using System.Text;

namespace Nfty.Core.Publish;

/// <summary>
/// The text report for an upload bundle, rendered in Core so <c>inspect</c> and any other surface say
/// the identical thing. Invariant, like every other figure this product prints.
/// </summary>
public static class BundleReport
{
    /// <summary>The most values listed under one trait before the rest are counted.</summary>
    private const int ValuesShown = 8;

    /// <summary>Renders what a bundle holds: how many items, how many pictures resolve, the collection
    /// file, every trait with its values, and anything that could not be read.</summary>
    /// <param name="bundle">The bundle.</param>
    /// <returns>The report, ending in a newline.</returns>
    public static string Render(LoadedBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        sb.Append(inv, $"Upload bundle: {bundle.Name}  (not a Set: no set.json, read only)\n");
        int found = bundle.Items.Count(i => i.ImagePath is not null);
        sb.Append(inv, $"  Items: {bundle.Items.Count:N0}\n");
        sb.Append(inv, $"  Pictures found in the bundle: {found:N0} of {bundle.Items.Count:N0}\n");
        if (found < bundle.Items.Count)
            foreach (var missing in bundle.Items.Where(i => i.ImagePath is null).Take(5))
                sb.Append(inv, $"    missing: {missing.File} -> {missing.ImageField}\n");

        if (bundle.Collection is { } c)
        {
            sb.Append("  collection.json:\n");
            sb.Append(inv, $"    Name: {c.Name}\n");
            if (c.Symbol is { Length: > 0 }) sb.Append(inv, $"    Symbol: {c.Symbol}\n");
            if (c.Description.Length > 0) sb.Append(inv, $"    Description: {c.Description}\n");
            if (c.Image is { Length: > 0 }) sb.Append(inv, $"    Image: {c.Image}\n");
            if (c.ExternalLink is { Length: > 0 }) sb.Append(inv, $"    External link: {c.ExternalLink}\n");
            if (c.SellerFeeBasisPoints is { } bps)
                sb.Append(inv, $"    Royalty: {bps / 100m:0.##}% ({bps} basis points)\n");
            if (c.FeeRecipient is { Length: > 0 }) sb.Append(inv, $"    Fee recipient: {c.FeeRecipient}\n");
        }
        else sb.Append("  collection.json: none\n");

        var traits = bundle.Items
            .SelectMany(i => i.Attributes)
            .GroupBy(a => a.Trait_type, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        sb.Append(inv, $"  Traits: {traits.Count}\n");
        foreach (var t in traits)
        {
            var values = t.GroupBy(a => a.Value, StringComparer.Ordinal)
                .OrderByDescending(v => v.Count()).ThenBy(v => v.Key, StringComparer.Ordinal).ToList();
            sb.Append(inv, $"    {t.Key} ({values.Count} value{(values.Count == 1 ? "" : "s")}): ");
            sb.Append(string.Join(", ", values.Take(ValuesShown).Select(v => $"{v.Key} x{v.Count()}")));
            if (values.Count > ValuesShown) sb.Append(inv, $", and {values.Count - ValuesShown} more");
            sb.Append('\n');
        }

        if (bundle.Problems.Count > 0)
        {
            sb.Append(inv, $"  Problems: {bundle.Problems.Count}\n");
            foreach (var p in bundle.Problems) sb.Append(inv, $"    {p}\n");
        }
        return sb.ToString();
    }
}
