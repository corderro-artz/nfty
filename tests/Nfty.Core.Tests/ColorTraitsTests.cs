using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Demo;
using Nfty.Core.Generation;
using Nfty.Core.Imaging;
using Nfty.Core.Model;
using Nfty.Core.Output;

namespace Nfty.Core.Tests;

/// <summary>
/// Rolled colors and absent optional layers are PUBLISHED: color traits and <c>None</c>, in a
/// metadata shape the Set records so <c>extend</c> never mixes shapes.
/// </summary>
public class ColorTraitsTests
{
    // ------------------------------------------------------------------ the name table

    [Theory]
    [InlineData(0, 1, "Red")]
    [InlineData(14.999, 1, "Red")]
    [InlineData(15, 1, "Orange")]          // boundaries are half-open: the sector starts AT 15
    [InlineData(44.999, 1, "Orange")]
    [InlineData(45, 1, "Yellow")]
    [InlineData(75, 1, "Lime")]
    [InlineData(105, 1, "Green")]
    [InlineData(135, 1, "Mint")]
    [InlineData(165, 1, "Cyan")]
    [InlineData(195, 1, "Azure")]
    [InlineData(225, 1, "Blue")]
    [InlineData(255, 1, "Violet")]
    [InlineData(285, 1, "Purple")]
    [InlineData(315, 1, "Magenta")]
    [InlineData(344.999, 1, "Magenta")]
    [InlineData(345, 1, "Red")]            // the wrap: Red is [345, 360) and [0, 15)
    [InlineData(359.999, 1, "Red")]
    [InlineData(360, 1, "Red")]
    [InlineData(375, 1, "Orange")]
    [InlineData(-16, 1, "Magenta")]
    [InlineData(-15, 1, "Red")]
    public void Hue_sectors_are_half_open_and_wrap(double hue, double saturation, string expected) =>
        Assert.Equal(expected, ColorName.Of(hue, saturation));

    [Fact]
    public void Every_sector_is_named_at_its_center()
    {
        for (int i = 0; i < 12; i++)
            Assert.Equal(ColorName.Hues[i], ColorName.Of(30.0 * i, 1));
        Assert.Equal(12, ColorName.Hues.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 0.0, "Gray")]
    [InlineData(200, 0.1199, "Gray")]
    [InlineData(200, 0.12, "Pale Azure")]       // 0.12 is the first Pale
    [InlineData(200, 0.3499, "Pale Azure")]
    [InlineData(200, 0.35, "Azure")]            // 0.35 is the first full color
    [InlineData(200, 1.0, "Azure")]
    public void Saturation_makes_gray_then_pale_then_full(double hue, double saturation, string expected) =>
        Assert.Equal(expected, ColorName.Of(hue, saturation));

    [Fact]
    public void Gray_never_takes_a_hue_or_a_prefix_and_odd_input_is_gray()
    {
        Assert.Equal("Gray", ColorName.Of(0, 0.05));
        Assert.Equal("Gray", ColorName.Of(180, 0.05));
        Assert.Equal("Gray", ColorName.Of(double.NaN, 1));
        Assert.Equal("Gray", ColorName.Of(10, double.PositiveInfinity));
    }

    [Fact]
    public void Naming_is_deterministic_and_total_over_the_whole_circle()
    {
        var allowed = ColorName.Hues.Concat(ColorName.Hues.Select(h => "Pale " + h)).Append("Gray").ToHashSet();
        for (double h = -720; h <= 720; h += 0.25)
            foreach (double s in new[] { 0.0, 0.2, 0.9 })
            {
                string a = ColorName.Of(h, s);
                Assert.Equal(a, ColorName.Of(h, s));
                Assert.Contains(a, allowed);
            }
    }

    [Fact]
    public void The_trait_type_is_the_layer_name_and_Color()
    {
        Assert.Equal("Body Color", ColorName.TraitTypeOf("Body"));
    }

    // ------------------------------------------------------------------ a cooked demo

    private static string CookDemo(int count, out LoadedCookBook book)
    {
        string root = Directory.CreateTempSubdirectory("nfty-color-").FullName;
        book = CookBookArchive.Read(DemoCookBook.WriteTo(root));
        string dir = Path.Combine(root, "set");
        using var set = Generator.Generate(book, new GenerateOptions(count, "color-traits"));
        SetWriter.Write(set, dir, pack: false);
        return dir;
    }

    private static List<(string Trait, string Value)> Attributes(string dir, int number)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "metadata", SetLayout.Stem(number) + ".json")));
        return doc.RootElement.GetProperty("attributes").EnumerateArray()
            .Select(a => (a.GetProperty("trait_type").GetString()!, a.GetProperty("value").GetString()!)).ToList();
    }

    [Fact]
    public void A_cooked_demo_publishes_color_traits_and_None()
    {
        string dir = CookDemo(40, out var book);
        using var _ = book;
        var recipes = book.Recipes.ToDictionary(r => r.Manifest.Name, r => r);
        bool sawNone = false, sawColor = false, sawGlow = false;

        for (int n = 1; n <= 40; n++)
        {
            var attrs = Attributes(dir, n);
            Assert.Equal("Type", attrs[0].Trait);
            var recipe = recipes[attrs[0].Value];
            foreach (var layerId in recipe.Manifest.LayerOrder)
            {
                var ing = recipe.Ingredients.Single(i => i.Manifest.Id == layerId);
                string name = ing.Manifest.Name;
                int at = attrs.FindIndex(a => a.Trait == name);
                bool optional = recipe.Manifest.AbsentPercentOf(layerId) > 0;
                if (at < 0) Assert.Fail($"asset {n}: no attribute for layer {name}");

                if (attrs[at].Value == MetadataTraits.Absent)
                {
                    Assert.True(optional, "None is published only for an optional layer");
                    Assert.DoesNotContain(attrs, a => a.Trait == name + " Color");
                    sawNone = true;
                    continue;
                }

                if (ing.Manifest.Kind is LayerKind.Dynamic or LayerKind.Static)
                {
                    // Right after the layer's own attribute, named from the roll.
                    Assert.Equal(name + " Color", attrs[at + 1].Trait);
                    Assert.Contains(attrs[at + 1].Value, ColorName.Hues.Concat(ColorName.Hues.Select(h => "Pale " + h)).Append("Gray"));
                    sawColor = true;
                }
                else
                {
                    // Custom layers carry no color trait.
                    Assert.DoesNotContain(attrs, a => a.Trait == name + " Color");
                }
                if (name == "Glow") sawGlow = true;
            }
        }

        Assert.True(sawColor);
        Assert.True(sawNone, "the demo's glow layer is optional, so some of 40 assets leave it out");
        Assert.True(sawGlow);
    }

    [Fact]
    public void Two_assets_that_differ_only_in_hue_now_publish_different_attributes()
    {
        // One variant on one dynamic layer: every asset has the SAME variants and differs only in the
        // hue it rolled - which used to publish byte-identical attributes.
        var body = Ing("a", "Body", LayerKind.Dynamic, "x");
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(2, 2), new Collection("B", "d", "B"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("r", "R", new[] { "a" }, Array.Empty<IncompatibilityRule>()),
                    Ingredients = new[] { body },
                },
            },
        };
        string dir = Path.Combine(Directory.CreateTempSubdirectory().FullName, "set");
        using (var set = Generator.Generate(book, new GenerateOptions(30, "hues", EnforceUniqueDna: false)))
            SetWriter.Write(set, dir, pack: false);

        var colors = Enumerable.Range(1, 30)
            .Select(n => Attributes(dir, n).Single(t => t.Trait == "Body Color").Value).Distinct().ToList();
        Assert.True(colors.Count > 3, "a full-circle roll names several colors: " + string.Join(", ", colors));

        // Two assets with different color names have different attribute lists, variant for variant the same.
        var all = Enumerable.Range(1, 30).Select(n => Attributes(dir, n)).ToList();
        var first = all[0];
        var other = all.First(a => a.Single(t => t.Trait == "Body Color").Value != first.Single(t => t.Trait == "Body Color").Value);
        Assert.Equal(first.Single(t => t.Trait == "Body"), other.Single(t => t.Trait == "Body"));
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void The_set_records_its_shape_and_the_rarity_table_counts_color_traits()
    {
        string dir = CookDemo(40, out var book);
        using var _ = book;

        using var setDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "set.json")));
        Assert.Equal(MetadataTraits.CurrentShape, setDoc.RootElement.GetProperty("metadataShape").GetInt32());

        var rows = setDoc.RootElement.GetProperty("rarity").EnumerateArray()
            .Select(r => (Trait: r.GetProperty("trait_type").GetString()!, Value: r.GetProperty("value").GetString()!,
                Pct: r.GetProperty("rarityPct").GetDouble())).ToList();
        Assert.Contains(rows, r => r.Trait.EndsWith(" Color", StringComparison.Ordinal));
        Assert.Contains(rows, r => r.Trait == "Glow" && r.Value == MetadataTraits.Absent);

        // Every trait whose layer is present on every asset sums to 100; Glow (optional) includes
        // None in its own sum, so it does too.
        var byTrait = rows.GroupBy(r => r.Trait);
        foreach (var g in byTrait.Where(g => g.Key is "Glow" or "Type"))
            Assert.Equal(100, g.Sum(r => r.Pct), 1);

        // The rich rows agree with the table.
        using var rich = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "nfty", "0001.json")));
        Assert.Contains(rich.RootElement.GetProperty("rarity").EnumerateArray(),
            r => r.GetProperty("trait_type").GetString()!.EndsWith(" Color", StringComparison.Ordinal));
        Assert.DoesNotContain("(none)", File.ReadAllText(Path.Combine(dir, "nfty", "0001.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_names_come_from_the_unrounded_roll()
    {
        string dir = CookDemo(10, out var book);
        using var _ = book;
        using var rich = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "nfty", "0001.json")));
        var attrs = Attributes(dir, 1);

        foreach (var layer in rich.RootElement.GetProperty("layers").EnumerateArray())
        {
            if (layer.GetProperty("kind").GetString() == "custom") continue;
            double h = layer.GetProperty("exactH").GetDouble(), s = layer.GetProperty("exactS").GetDouble();
            string name = book.Recipes.SelectMany(r => r.Ingredients)
                .First(i => i.Manifest.Id == layer.GetProperty("layer").GetString()).Manifest.Name;
            Assert.Equal(ColorName.Of(h, s), attrs.Single(a => a.Trait == name + " Color").Value);
        }
    }

    // ------------------------------------------------------------------ stability

    [Fact]
    public void Extending_a_legacy_set_keeps_the_legacy_shape_and_never_rewrites_published_attributes()
    {
        string root = Directory.CreateTempSubdirectory("nfty-legacy-").FullName;
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(root));
        string dir = Path.Combine(root, "set");
        using (var first = Generator.Generate(book, new GenerateOptions(10, "first")))
            SetWriter.Write(first, dir, pack: false);

        // Make it a Set cooked before the shape existed: the field absent, and its files in the old
        // shape (rewritten here by hand, which is what the older build wrote).
        string setJson = Path.Combine(dir, "set.json");
        File.WriteAllText(setJson, System.Text.RegularExpressions.Regex.Replace(
            File.ReadAllText(setJson), @",\s*""metadataShape"": 2", ""));
        Assert.DoesNotContain("metadataShape", File.ReadAllText(setJson));
        string before = File.ReadAllText(Path.Combine(dir, "metadata", "0001.json"));

        var e = SetWriter.ReadExisting(dir);
        using (var more = Generator.Generate(book, new GenerateOptions(5, "second"), e.Dnas, e.NextNumber))
            SetWriter.Write(more, dir, pack: false);

        // The already-published file is byte-for-byte unchanged...
        Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "metadata", "0001.json")));
        // ...the new ones are in the recorded (legacy) shape: no color traits, no None...
        for (int n = 11; n <= 15; n++)
        {
            var attrs = Attributes(dir, n);
            Assert.DoesNotContain(attrs, a => a.Trait.EndsWith(" Color", StringComparison.Ordinal));
            Assert.DoesNotContain(attrs, a => a.Value == MetadataTraits.Absent);
        }
        // ...and the Set still records no shape.
        Assert.DoesNotContain("metadataShape", File.ReadAllText(setJson));
    }

    [Fact]
    public void Extending_a_current_set_stays_in_the_current_shape()
    {
        string root = Directory.CreateTempSubdirectory("nfty-current-").FullName;
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(root));
        string dir = Path.Combine(root, "set");
        using (var first = Generator.Generate(book, new GenerateOptions(10, "first")))
            SetWriter.Write(first, dir, pack: false);

        var e = SetWriter.ReadExisting(dir);
        using (var more = Generator.Generate(book, new GenerateOptions(5, "second"), e.Dnas, e.NextNumber))
            SetWriter.Write(more, dir, pack: false);

        Assert.Contains(Attributes(dir, 12), a => a.Trait.EndsWith(" Color", StringComparison.Ordinal));
        using var setDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "set.json")));
        Assert.Equal(MetadataTraits.CurrentShape, setDoc.RootElement.GetProperty("metadataShape").GetInt32());
        Assert.Equal(15, setDoc.RootElement.GetProperty("count").GetInt32());
    }

    [Fact]
    public void Color_names_never_enter_the_dna()
    {
        // The same seed cooks the same DNA whatever the metadata shape: DNA is hashed from ids and
        // quantized colors, and the name is only a view of the color.
        using var book = CookBookArchive.Read(DemoCookBook.WriteTo(Directory.CreateTempSubdirectory().FullName));
        using var a = Generator.Generate(book, new GenerateOptions(20, "dna"));
        using var b = Generator.Generate(book, new GenerateOptions(20, "dna"));
        Assert.Equal(a.Assets.Select(x => x.Dna), b.Assets.Select(x => x.Dna));
    }

    // ------------------------------------------------------------------ the Validator

    private static LoadedIngredient Ing(string id, string name, LayerKind kind, params string[] variants)
    {
        var colorization = kind == LayerKind.Custom ? null
            : kind == LayerKind.Static
                ? new Colorization(ColorModel.Hsv, 10, 10, [new ColorEntry(1, null, "hex:ff0000")])
                : new Colorization(ColorModel.Hsv, 10, 10, [new ColorEntry(1, new ColorRange(0, 360, 50, 100), null)]);
        return new LoadedIngredient
        {
            Manifest = new IngredientManifest(id, name, kind, colorization,
                variants.Select(v => new Variant(v, v, 1)).ToList()),
            VariantImages = variants.ToDictionary(v => v,
                _ => new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2,
                    kind == LayerKind.Custom ? new(9, 9, 9, 255) : new(100, 100, 100, 255))),
        };
    }

    private static List<string> Problems(LoadedIngredient[] ings, Dictionary<string, double>? absent = null)
    {
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "B", new Dimensions(2, 2), new Collection("B", "d", "B"),
                new Dictionary<string, double> { ["r"] = 1 }),
            Recipes = new[]
            {
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("r", "R", ings.Select(i => i.Manifest.Id).ToArray(),
                        Array.Empty<IncompatibilityRule>(), AbsentPercent: absent),
                    Ingredients = ings,
                },
            },
        };
        return Validator.Validate(book).ToList();
    }

    [Fact]
    public void A_layer_named_like_a_colorized_layers_color_trait_is_refused()
    {
        var problems = Problems([Ing("a", "Body", LayerKind.Dynamic, "x"), Ing("b", "Body Color", LayerKind.Custom, "y")]);
        Assert.Contains(problems, p => p.Contains("'Body Color'", StringComparison.Ordinal) && p.Contains("merge", StringComparison.Ordinal));

        // A Static layer's color is a trait too; a Custom layer has none, so no clash there.
        Assert.Contains(Problems([Ing("a", "Body", LayerKind.Static, "x"), Ing("b", "Body Color", LayerKind.Custom, "y")]),
            p => p.Contains("'Body Color'", StringComparison.Ordinal));
        Assert.DoesNotContain(Problems([Ing("a", "Body", LayerKind.Custom, "x"), Ing("b", "Body Color", LayerKind.Custom, "y")]),
            p => p.Contains("'Body Color'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_variant_named_None_on_an_optional_layer_is_refused_but_fine_elsewhere()
    {
        var optional = Problems([Ing("a", "Bg", LayerKind.Custom, "x"), Ing("h", "Hat", LayerKind.Custom, "None", "crown")],
            new Dictionary<string, double> { ["h"] = 30 });
        Assert.Contains(optional, p => p.Contains("'None'", StringComparison.Ordinal) && p.Contains("optional", StringComparison.Ordinal));

        // The same variant on a layer that can never be absent is unambiguous.
        Assert.DoesNotContain(Problems([Ing("a", "Bg", LayerKind.Custom, "x"), Ing("h", "Hat", LayerKind.Custom, "None", "crown")]),
            p => p.Contains("'None'", StringComparison.Ordinal));
    }
}
