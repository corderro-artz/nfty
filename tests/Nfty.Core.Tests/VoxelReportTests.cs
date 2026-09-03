using Nfty.Core.Formats;
using Nfty.Core.Model;
using Nfty.Core.Stats;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// Voxel readiness: which artwork a voxel converter can and cannot resolve.
/// </summary>
/// <remarks>
/// Partial alpha is legal — the editor's opacity lock is a default an author may deliberately turn
/// off — so this is a REPORT and never a <see cref="Validator"/> problem. Everything here is about
/// counting honestly and saying so in text that reads the same on every machine.
/// </remarks>
public class VoxelReportTests
{
    private static Image<Rgba32> Solid(int w, int h, byte alpha) =>
        new(w, h, new Rgba32(10, 20, 30, alpha));

    private static LoadedIngredient Ing(string name, params (string id, string vname, byte alpha)[] variants)
    {
        var images = new Dictionary<string, Image<Rgba32>>(StringComparer.Ordinal);
        foreach (var (id, _, alpha) in variants) images[id] = Solid(4, 4, alpha);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(name.ToLowerInvariant(), name, LayerKind.Custom, null,
                variants.Select(v => new Variant(v.id, v.vname, 1)).ToList()),
            VariantImages = images,
        };
    }

    [Fact]
    public void Fully_opaque_and_fully_erased_both_voxelise_cleanly()
    {
        using var ing = Ing("Solid", ("a", "Opaque", 255), ("b", "Erased", 0));

        var rows = VoxelReport.Scan(ing);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.IsClean));
        Assert.All(rows, r => Assert.Equal(0, r.Partial));
    }

    [Fact]
    public void Any_alpha_between_the_two_is_counted()
    {
        using var ing = Ing("Soft", ("a", "Half", 128), ("b", "Barely", 1), ("c", "Nearly", 254));

        var rows = VoxelReport.Scan(ing);

        Assert.All(rows, r => Assert.False(r.IsClean));
        Assert.All(rows, r => Assert.Equal(16, r.Partial));      // every pixel of a 4x4
        Assert.All(rows, r => Assert.Equal(100, r.PartialPercent));
    }

    [Fact]
    public void The_count_is_per_pixel_not_per_image()
    {
        using var img = Solid(4, 4, 255);
        img[0, 0] = new Rgba32(1, 2, 3, 128);
        img[1, 0] = new Rgba32(1, 2, 3, 200);

        Assert.Equal(2, VoxelReport.CountPartial(img));
    }

    [Fact]
    public void A_recipe_is_scanned_in_paint_order_and_a_dangling_layer_is_skipped()
    {
        using var top = Ing("Top", ("t", "T", 255));
        using var bottom = Ing("Bottom", ("b", "B", 128));
        var recipe = new LoadedRecipe
        {
            // layerOrder names a layer the ingredient list does not have: a report is exactly what
            // someone runs on a broken archive, so it must survive one.
            Manifest = new RecipeManifest("r", "R", new[] { "bottom", "ghost", "top" },
                Array.Empty<IncompatibilityRule>()),
            Ingredients = new[] { top, bottom },
        };

        var rows = VoxelReport.Scan(recipe);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Bottom", rows[0].IngredientName);   // paint order, not list order
        Assert.Equal("Top", rows[1].IngredientName);
    }

    [Fact]
    public void A_variant_with_no_image_is_skipped_rather_than_throwing()
    {
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("i", "I", LayerKind.Custom, null,
                new[] { new Variant("present", "Present", 1), new Variant("absent", "Absent", 1) }),
            VariantImages = new Dictionary<string, Image<Rgba32>> { ["present"] = Solid(2, 2, 255) },
        };
        using (ing)
        {
            var row = Assert.Single(VoxelReport.Scan(ing));
            Assert.Equal("present", row.VariantId);
        }
    }

    [Fact]
    public void A_clean_book_says_so_and_offers_no_advice_it_does_not_need_to()
    {
        using var ing = Ing("Solid", ("a", "A", 255));

        var text = VoxelReport.Render("Book", VoxelReport.Scan(ing));

        Assert.Contains("Voxel readiness: Book", text, StringComparison.Ordinal);
        Assert.Contains("All 1 variants voxelise cleanly.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("opacity lock", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dirty_book_names_the_variant_the_counts_and_what_to_do()
    {
        using var ing = Ing("Aura", ("glow", "Glow", 255), ("soft", "Soft", 90));

        var text = VoxelReport.Render("Book", VoxelReport.Scan(ing));

        Assert.Contains("Aura / Soft [soft]", text, StringComparison.Ordinal);
        Assert.Contains("16 of 16 pixels partly transparent (100%)", text, StringComparison.Ordinal);
        Assert.Contains("1 of 2 variants carries partial alpha.", text, StringComparison.Ordinal);
        Assert.Contains("opacity lock", text, StringComparison.Ordinal);
    }

    /// <summary>Counting more than one keeps the plural — the singular case is the one that reads
    /// wrong, and it is the common one.</summary>
    [Fact]
    public void Two_dirty_variants_read_as_carry()
    {
        using var ing = Ing("Aura", ("a", "A", 90), ("b", "B", 90));

        Assert.Contains("2 of 2 variants carry partial alpha.",
            VoxelReport.Render("Book", VoxelReport.Scan(ing)), StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_scan_is_stated_rather_than_rendered_as_an_empty_table()
    {
        var text = VoxelReport.Render("Book", Array.Empty<VoxelVariant>());

        Assert.Contains("(no variant images to scan)", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The report gets pasted into issues and diffed across machines, so its numbers are
    /// invariant-formatted — never the current culture's separators.
    /// </summary>
    /// <remarks>
    /// The numbers here are chosen so the two cultures actually DISAGREE: a thousands separator and
    /// a fractional percent. A 16-pixel image at 100% formats identically under de-DE and invariant,
    /// so an assertion built on one proves nothing (it survived a mutation probe that swapped the
    /// formatter to CurrentCulture).
    /// </remarks>
    [Fact]
    public void The_numbers_are_culture_invariant()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            // 64x64 = 4,096 pixels, 1,000 of them partial => 24.41%.
            using var img = Solid(64, 64, 255);
            for (int i = 0; i < 1000; i++) img[i % 64, i / 64] = new Rgba32(1, 2, 3, 128);
            var rows = new[] { new VoxelVariant("Aura", "Soft", "soft", 64 * 64, VoxelReport.CountPartial(img)) };

            var text = VoxelReport.Render("Book", rows);

            Assert.Contains("1,000 of 4,096 pixels", text, StringComparison.Ordinal);   // not "1.000 of 4.096"
            Assert.Contains("(24.41%)", text, StringComparison.Ordinal);                // not "(24,41%)"
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }
}
