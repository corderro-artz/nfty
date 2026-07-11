using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Core.Output;

public static class SetWriter
{
    public const string GeneratorVersion = "nfty/1.0";
    private const string TypeTrait = "Type";

    public record ExistingSet(IReadOnlyList<string> Dnas, int NextNumber);

    public static void Write(GeneratedSet set, string outDir, bool pack)
    {
        Directory.CreateDirectory(Path.Combine(outDir, "images"));
        Directory.CreateDirectory(Path.Combine(outDir, "metadata"));

        // Observed frequencies across the produced set (Type + every layer trait).
        var counts = new Dictionary<(string, string), int>();
        void Bump(string traitType, string value) =>
            counts[(traitType, value)] = counts.GetValueOrDefault((traitType, value)) + 1;
        foreach (var a in set.Assets)
        {
            Bump(TypeTrait, a.RecipeName);
            foreach (var t in a.Traits) Bump(t.IngredientName, t.VariantName);
        }

        double n = Math.Max(1, set.Assets.Count);
        RarityAttribute Rar(string traitType, string value) =>
            new(traitType, value, Math.Round(counts.GetValueOrDefault((traitType, value)) / n * 100, 2));

        foreach (var a in set.Assets)
        {
            string stem = a.SetNumber.ToString("D4");
            a.Image.Save(Path.Combine(outDir, "images", $"{stem}.png"), new PngEncoder());

            var attributes = new List<MetadataAttribute> { new(TypeTrait, a.RecipeName) };
            attributes.AddRange(a.Traits.Select(t => new MetadataAttribute(t.IngredientName, t.VariantName)));

            var rarity = new List<RarityAttribute> { Rar(TypeTrait, a.RecipeName) };
            rarity.AddRange(a.Traits.Select(t => Rar(t.IngredientName, t.VariantName)));

            var meta = new ItemMetadata(
                Name: $"{set.CollectionName} #{a.SetNumber}",
                Description: set.Description,
                Image: $"images/{stem}.png",
                Attributes: attributes,
                SetNumber: a.SetNumber,
                Recipe: a.RecipeId,
                Dna: a.Dna,
                Seed: set.Seed,
                Rarity: rarity,
                ColorRolls: a.ColorRolls
                    .Select(c => new ColorRollDto(c.LayerId, c.Model.ToString().ToLowerInvariant(),
                        Math.Round(c.H, 1), Math.Round(c.S, 3)))
                    .ToList());

            File.WriteAllText(Path.Combine(outDir, "metadata", $"{stem}.json"),
                JsonSerializer.Serialize(meta, Json.Options));
        }

        var distribution = set.Assets
            .GroupBy(a => a.RecipeId)
            .Select(g => new RecipeCount(g.Key, g.Count(), Math.Round(g.Count() / n * 100, 2)))
            .OrderBy(d => d.Recipe).ToList();

        var rarityTable = counts.Keys
            .Select(k => Rar(k.Item1, k.Item2))
            .OrderBy(r => r.Trait_type).ThenBy(r => r.Value).ToList();

        var setManifest = new SetManifest(set.CollectionName, set.Assets.Count, set.Seed,
            GeneratorVersion, distribution, rarityTable);
        File.WriteAllText(Path.Combine(outDir, "set.json"),
            JsonSerializer.Serialize(setManifest, Json.Options));

        if (pack)
        {
            string archivePath = outDir + ".set";
            if (File.Exists(archivePath)) File.Delete(archivePath);
            ZipFile.CreateFromDirectory(outDir, archivePath);
        }
    }

    public static ExistingSet ReadExisting(string outDir)
    {
        var metaDir = Path.Combine(outDir, "metadata");
        if (!Directory.Exists(metaDir))
            return new ExistingSet(Array.Empty<string>(), 1);

        var dnas = new List<string>();
        int maxNumber = 0;
        foreach (var file in Directory.EnumerateFiles(metaDir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            dnas.Add(doc.RootElement.GetProperty("dna").GetString()!);
            maxNumber = Math.Max(maxNumber, doc.RootElement.GetProperty("setNumber").GetInt32());
        }
        return new ExistingSet(dnas, maxNumber + 1);
    }
}
