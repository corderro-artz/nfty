using System.Globalization;
using System.Text;
using Nfty.Core.Formats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Stats;

/// <summary>What a single variant's artwork does about transparency.</summary>
/// <param name="IngredientName">The layer the variant belongs to.</param>
/// <param name="VariantName">The variant's display name.</param>
/// <param name="VariantId">Its id — what <c>--variant</c> expects.</param>
/// <param name="Pixels">Total pixels in the image.</param>
/// <param name="Partial">Pixels whose alpha is neither fully opaque nor fully erased.</param>
public readonly record struct VoxelVariant(
    string IngredientName, string VariantName, string VariantId, int Pixels, int Partial)
{
    /// <summary>Whether this variant voxelises cleanly — every pixel wholly there or wholly absent.</summary>
    public bool IsClean => Partial == 0;

    /// <summary>The share of the image that is only partly there, 0-100.</summary>
    public double PartialPercent => Pixels == 0 ? 0 : 100.0 * Partial / Pixels;
}

/// <summary>
/// Reports whether an archive's artwork can be turned into a voxel model, and where it cannot.
/// </summary>
/// <remarks>
/// <para>Partial alpha is <b>allowed</b>: the editor's opacity lock is a default an author may turn
/// off deliberately, so this is a report and not a <see cref="Validator"/> problem. A semi-transparent
/// pixel is simply not answerable as a voxel — a converter has to drop it or make it solid, and
/// either way the result is not what was drawn. This says which pixels those are before someone
/// finds out downstream.</para>
///
/// <para>Rendered here rather than in a front-end for the reason every report in this namespace is:
/// the CLI and the GUI must show the same text, and two copies is how that stops being true. The
/// format is invariant — these get pasted into issues and diffed across machines.</para>
///
/// <para>It costs a full scan of every variant image, which is why nothing runs it implicitly.</para>
/// </remarks>
public static class VoxelReport
{
    /// <summary>Scans one ingredient's variants.</summary>
    /// <param name="ing">The layer to scan.</param>
    /// <returns>One row per variant, in manifest order.</returns>
    public static IReadOnlyList<VoxelVariant> Scan(LoadedIngredient ing)
    {
        var rows = new List<VoxelVariant>();
        foreach (var v in ing.Manifest.Variants)
        {
            if (!ing.VariantImages.TryGetValue(v.Id, out var img)) continue;
            rows.Add(new VoxelVariant(ing.Manifest.Name, v.Name, v.Id,
                img.Width * img.Height, CountPartial(img)));
        }
        return rows;
    }

    /// <summary>Scans every variant of every layer in a recipe, in paint order.</summary>
    /// <param name="recipe">The recipe to scan.</param>
    /// <returns>One row per variant.</returns>
    public static IReadOnlyList<VoxelVariant> Scan(LoadedRecipe recipe)
    {
        // Resolve tolerantly and walk in layerOrder, matching what inspect prints: a report run on a
        // broken archive is exactly when someone reaches for it.
        var byId = new Dictionary<string, LoadedIngredient>(StringComparer.Ordinal);
        foreach (var i in recipe.Ingredients) byId[i.Manifest.Id] = i;

        var rows = new List<VoxelVariant>();
        foreach (var id in recipe.Manifest.LayerOrder)
            if (byId.TryGetValue(id, out var ing))
                rows.AddRange(Scan(ing));
        return rows;
    }

    /// <summary>Scans every variant in a cookbook.</summary>
    /// <param name="book">The book to scan.</param>
    /// <returns>One row per variant.</returns>
    public static IReadOnlyList<VoxelVariant> Scan(LoadedCookBook book)
    {
        var rows = new List<VoxelVariant>();
        foreach (var r in book.Recipes) rows.AddRange(Scan(r));
        return rows;
    }

    /// <summary>Counts pixels that are neither fully opaque nor fully erased.</summary>
    /// <param name="img">The image to scan.</param>
    /// <returns>How many pixels carry partial alpha.</returns>
    public static int CountPartial(Image<Rgba32> img)
    {
        int partial = 0;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    byte a = row[x].A;
                    if (a != 0 && a != 255) partial++;
                }
            }
        });
        return partial;
    }

    /// <summary>Renders the report a human reads.</summary>
    /// <param name="title">What was scanned, for the heading.</param>
    /// <param name="rows">The scanned variants.</param>
    /// <returns>The report text, ending in a newline.</returns>
    public static string Render(string title, IReadOnlyList<VoxelVariant> rows)
    {
        var sb = new StringBuilder();
        sb.Append("Voxel readiness: ").Append(title).AppendLine();

        if (rows.Count == 0)
        {
            sb.AppendLine("  (no variant images to scan)");
            return sb.ToString();
        }

        // Pad to the widest label so the counts line up in a terminal, and compute it from the rows
        // rather than guessing a column: a report that wraps is a report nobody reads.
        int width = rows.Max(r => Label(r).Length);
        foreach (var r in rows)
        {
            sb.Append("  ").Append(r.IsClean ? "ok   " : "ALPHA")
              .Append(' ').Append(Label(r).PadRight(width)).Append("  ");
            sb.AppendLine(r.IsClean
                ? "no partial alpha"
                : string.Format(CultureInfo.InvariantCulture,
                    "{0:N0} of {1:N0} pixels partly transparent ({2:0.##}%)",
                    r.Partial, r.Pixels, r.PartialPercent));
        }

        int dirty = rows.Count(r => !r.IsClean);
        sb.AppendLine();
        sb.AppendLine(dirty == 0
            ? string.Format(CultureInfo.InvariantCulture,
                "All {0:N0} variants voxelise cleanly.", rows.Count)
            : string.Format(CultureInfo.InvariantCulture,
                "{0:N0} of {1:N0} variants {2} partial alpha.", dirty, rows.Count,
                dirty == 1 ? "carries" : "carry"));

        if (dirty > 0)
            sb.AppendLine("Semi-transparent pixels have no voxel answer: a converter must either drop "
                + "them or make them solid. Paint with the opacity lock on to keep every pixel wholly "
                + "there or wholly absent.");
        return sb.ToString();
    }

    private static string Label(VoxelVariant r) => $"{r.IngredientName} / {r.VariantName} [{r.VariantId}]";
}
