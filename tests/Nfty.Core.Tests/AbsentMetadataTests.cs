using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Tests;

/// <summary>
/// What a finished Set records about a layer that was left out.
///
/// <para>Absence is PUBLISHED, from metadata shape 2 on: an optional layer the roll left out appears
/// in the OpenSea attributes as the value <c>None</c>, spelled by one constant that the rarity table
/// and the rich nfty file share, so a buyer can filter for "no hat". The rich file also records the
/// layer in <c>absentLayers</c>. Shape 1 (older Sets) keeps its old behavior, tested in
/// <c>ColorTraitsTests</c>.</para>
/// </summary>
public class AbsentMetadataTests
{
    private static LoadedIngredient Ing(string id, string name, params string[] vs) => new()
    {
        Manifest = new IngredientManifest(id, name, LayerKind.Custom, null,
            vs.Select(v => new Variant(v, v, 1)).ToList()),
        VariantImages = vs.ToDictionary(v => v, _ => new Image<Rgba32>(2, 2, new Rgba32(9, 9, 9, 255))),
    };

    private static LoadedCookBook Book(double hatAbsent) => new()
    {
        Manifest = new CookBookManifest("cb", "VaporPets", new Dimensions(2, 2),
            new Collection("VaporPets", "d", "VP"), new Dictionary<string, double> { ["cat"] = 1 }),
        Recipes = new[]
        {
            new LoadedRecipe
            {
                Manifest = new RecipeManifest("cat", "Cat", new[] { "bg", "hat" },
                    Array.Empty<IncompatibilityRule>(),
                    AbsentPercent: hatAbsent > 0
                        ? new Dictionary<string, double> { ["hat"] = hatAbsent }
                        : null),
                Ingredients = new[] { Ing("bg", "Background", "day"), Ing("hat", "Hat", "crown") },
            },
        },
    };

    private static (LoadedSet Set, string Dir) Cook(double hatAbsent, int count = 4)
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        using var book = Book(hatAbsent);
        using var generated = Generator.Generate(book,
            new GenerateOptions(count, "seed1", EnforceUniqueDna: false));
        SetWriter.Write(generated, dir, pack: false);
        return (SetReader.Read(dir), dir);
    }

    [Fact]
    public void A_layer_that_never_appears_is_recorded_as_absent_and_published_as_None()
    {
        var (set, dir) = Cook(hatAbsent: 100);
        try
        {
            Assert.All(set.Items, i =>
            {
                Assert.NotNull(i.AbsentLayers);
                Assert.Equal("Hat", Assert.Single(i.AbsentLayers!));
            });

            // The OpenSea half states it: "Hat": "None", spelled by the one shared constant.
            string json = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var hat = doc.RootElement.GetProperty("attributes").EnumerateArray()
                .Single(a => a.GetProperty("trait_type").GetString() == "Hat");
            Assert.Equal(MetadataTraits.Absent, hat.GetProperty("value").GetString());
            Assert.Contains("Background", json, StringComparison.Ordinal);
        }
        finally { set.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_recipe_without_optional_layers_gains_no_field_at_all()
    {
        var (set, dir) = Cook(hatAbsent: 0);
        try
        {
            // NULL, not an empty list — that is the claim that matters, and it is what makes a Set
            // written before optional layers existed read back unchanged.
            Assert.All(set.Items, i => Assert.Null(i.AbsentLayers));

            // The key is still WRITTEN, as `"absentLayers": null`. Json.Options sets no
            // DefaultIgnoreCondition, so every optional field this build writes appears with a null
            // — targetSupply and palette already do. Pinned so the convention is a decision rather
            // than an accident: older builds ignore unknown properties either way, and changing it
            // would rewrite the shape of every manifest this product emits.
            Assert.Contains("\"absentLayers\": null",
                File.ReadAllText(Path.Combine(dir, "nfty", "0001.json")), StringComparison.Ordinal);
        }
        finally { set.Dispose(); Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Absence is counted like any other outcome, so one layer's shares still sum to 100. Without
    /// that, a rarity table over an optional layer would quietly add up to less than the collection.
    /// </summary>
    [Fact]
    public void An_absent_layer_earns_a_rarity_row_and_the_layers_shares_still_sum_to_a_hundred()
    {
        var (set, dir) = Cook(hatAbsent: 50, count: 40);
        try
        {
            var hatRows = set.Items
                .SelectMany(i => i.Rarity)
                .Where(r => r.Trait_type == "Hat")
                .GroupBy(r => r.Value)
                .ToDictionary(g => g.Key, g => g.First().RarityPct);

            Assert.Contains(MetadataTraits.Absent, hatRows.Keys);
            Assert.DoesNotContain("(none)", hatRows.Keys);
            Assert.Contains("crown", hatRows.Keys);
            Assert.Equal(100, hatRows.Values.Sum(), 1);
        }
        finally { set.Dispose(); Directory.Delete(dir, recursive: true); }
    }
}
