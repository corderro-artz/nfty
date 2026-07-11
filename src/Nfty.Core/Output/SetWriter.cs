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

        // Existing metadata not being overwritten by this batch, so rarity/count can
        // be recomputed over the whole collection rather than just this batch.
        var newNumbers = new HashSet<int>(set.Assets.Select(a => a.SetNumber));
        var existing = new List<(string Path, ItemMetadata Item)>();
        var metaDir = Path.Combine(outDir, "metadata");
        if (Directory.Exists(metaDir))
        {
            foreach (var file in Directory.EnumerateFiles(metaDir, "*.json"))
            {
                var item = JsonSerializer.Deserialize<ItemMetadata>(File.ReadAllText(file), Json.Options)!;
                if (!newNumbers.Contains(item.SetNumber))
                    existing.Add((file, item));
            }
        }

        // Observed frequencies across the whole collection (existing ∪ this batch).
        var counts = new Dictionary<(string, string), int>();
        void Bump(string traitType, string value) =>
            counts[(traitType, value)] = counts.GetValueOrDefault((traitType, value)) + 1;
        foreach (var (_, item) in existing)
            foreach (var attr in item.Attributes) Bump(attr.Trait_type, attr.Value);
        foreach (var a in set.Assets)
        {
            Bump(TypeTrait, a.RecipeName);
            foreach (var t in a.Traits) Bump(t.IngredientName, t.VariantName);
        }

        int total = existing.Count + set.Assets.Count;
        double n = Math.Max(1, total);
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

        // Rewrite existing items' rarity to reflect the whole collection.
        foreach (var (path, item) in existing)
        {
            var updated = item with
            {
                Rarity = item.Attributes.Select(a => Rar(a.Trait_type, a.Value)).ToList(),
            };
            File.WriteAllText(path, JsonSerializer.Serialize(updated, Json.Options));
        }

        var distribution = existing.Select(e => e.Item.Recipe)
            .Concat(set.Assets.Select(a => a.RecipeId))
            .GroupBy(id => id)
            .Select(g => new RecipeCount(g.Key, g.Count(), Math.Round(g.Count() / n * 100, 2)))
            .OrderBy(d => d.Recipe).ToList();

        var rarityTable = counts.Keys
            .Select(k => Rar(k.Item1, k.Item2))
            .OrderBy(r => r.Trait_type).ThenBy(r => r.Value).ToList();

        var setManifest = new SetManifest(set.CollectionName, total, set.Seed,
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
