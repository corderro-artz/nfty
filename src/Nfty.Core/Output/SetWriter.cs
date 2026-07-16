using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Nfty.Core.Output;

/// <summary>How far a set write has got. <see cref="Fraction"/> suits a progress bar.</summary>
public readonly record struct WriteProgress(int Completed, int Total)
{
    public double Fraction => Total <= 0 ? 0 : (double)Completed / Total;
}

public static class SetWriter
{
    public const string GeneratorVersion = "nfty/1.0";
    private const string TypeTrait = "Type";

    public record ExistingSet(IReadOnlyList<string> Dnas, int NextNumber);

    // An item already on disk (from a previous batch) that this write is not overwriting.
    private record ExistingItem(string NftyPath, int SetNumber, string Recipe,
        IReadOnlyList<MetadataAttribute> Attributes, NftyMetadata Nfty);

    private record Layout(string OutDir, string ImagesDir, string MetaDir, string NftyDir);

    public static void Write(GeneratedSet set, string outDir, bool pack)
    {
        var layout = Prepare(outDir);
        var existing = LoadExisting(layout, set);
        var rarity = new Rarity(existing, set);

        foreach (var asset in set.Assets)
        {
            var (imagePath, metaPath, nftyPath) = PathsFor(layout, asset.SetNumber);
            asset.Image.Save(imagePath, new PngEncoder());
            File.WriteAllText(metaPath, Serialize(BuildOpenSea(set, asset)));
            File.WriteAllText(nftyPath, Serialize(BuildNfty(set, asset, rarity)));
        }

        foreach (var item in existing)
            File.WriteAllText(item.NftyPath, Serialize(Regraded(item, rarity)));

        File.WriteAllText(Path.Combine(outDir, "set.json"),
            Serialize(BuildSetManifest(set, existing, rarity)));

        if (pack) Pack(outDir);
    }

    /// <summary>
    /// Writes the set with genuinely async I/O, reporting one <see cref="WriteProgress"/> per
    /// asset. Behaves exactly as <see cref="Write"/> — same files, same bytes.
    /// </summary>
    public static async Task WriteAsync(
        GeneratedSet set,
        string outDir,
        bool pack,
        IProgress<WriteProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var layout = Prepare(outDir);
        var existing = await LoadExistingAsync(layout, set, cancellationToken);
        var rarity = new Rarity(existing, set);

        int done = 0;
        foreach (var asset in set.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (imagePath, metaPath, nftyPath) = PathsFor(layout, asset.SetNumber);
            await asset.Image.SaveAsync(imagePath, new PngEncoder(), cancellationToken);
            await File.WriteAllTextAsync(metaPath, Serialize(BuildOpenSea(set, asset)), cancellationToken);
            await File.WriteAllTextAsync(nftyPath, Serialize(BuildNfty(set, asset, rarity)), cancellationToken);

            progress?.Report(new WriteProgress(++done, set.Assets.Count));
        }

        foreach (var item in existing)
            await File.WriteAllTextAsync(item.NftyPath, Serialize(Regraded(item, rarity)), cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(outDir, "set.json"),
            Serialize(BuildSetManifest(set, existing, rarity)), cancellationToken);

        // ZipFile has no async API; keep the UI thread free rather than pretend.
        if (pack) await Task.Run(() => Pack(outDir), cancellationToken);
    }

    public static ExistingSet ReadExisting(string outDir)
    {
        var nftyDir = Path.Combine(outDir, "nfty");
        if (!Directory.Exists(nftyDir)) return new ExistingSet(Array.Empty<string>(), 1);

        var dnas = new List<string>();
        int maxNumber = 0;
        foreach (var file in Directory.EnumerateFiles(nftyDir, "*.json"))
        {
            var (dna, number) = ReadDnaAndNumber(File.ReadAllText(file));
            dnas.Add(dna);
            maxNumber = Math.Max(maxNumber, number);
        }
        return new ExistingSet(dnas, maxNumber + 1);
    }

    public static async Task<ExistingSet> ReadExistingAsync(
        string outDir, CancellationToken cancellationToken = default)
    {
        var nftyDir = Path.Combine(outDir, "nfty");
        if (!Directory.Exists(nftyDir)) return new ExistingSet(Array.Empty<string>(), 1);

        var dnas = new List<string>();
        int maxNumber = 0;
        foreach (var file in Directory.EnumerateFiles(nftyDir, "*.json"))
        {
            var (dna, number) = ReadDnaAndNumber(await File.ReadAllTextAsync(file, cancellationToken));
            dnas.Add(dna);
            maxNumber = Math.Max(maxNumber, number);
        }
        return new ExistingSet(dnas, maxNumber + 1);
    }

    private static (string Dna, int SetNumber) ReadDnaAndNumber(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return (doc.RootElement.GetProperty("dna").GetString()!,
                doc.RootElement.GetProperty("setNumber").GetInt32());
    }

    private static Layout Prepare(string outDir)
    {
        var layout = new Layout(outDir,
            Path.Combine(outDir, "images"),
            Path.Combine(outDir, "metadata"),
            Path.Combine(outDir, "nfty"));

        Directory.CreateDirectory(layout.ImagesDir);
        Directory.CreateDirectory(layout.MetaDir);
        Directory.CreateDirectory(layout.NftyDir);
        return layout;
    }

    private static (string Image, string Meta, string Nfty) PathsFor(Layout layout, int setNumber)
    {
        string stem = setNumber.ToString("D4");
        return (Path.Combine(layout.ImagesDir, $"{stem}.png"),
                Path.Combine(layout.MetaDir, $"{stem}.json"),
                Path.Combine(layout.NftyDir, $"{stem}.json"));
    }

    /// <summary>
    /// Items from earlier batches that this write is not overwriting. They still count toward
    /// rarity, which is a property of the whole collection rather than of one run.
    /// </summary>
    private static List<ExistingItem> LoadExisting(Layout layout, GeneratedSet set)
    {
        var newNumbers = set.Assets.Select(a => a.SetNumber).ToHashSet();
        var existing = new List<ExistingItem>();

        foreach (var nftyFile in Directory.EnumerateFiles(layout.NftyDir, "*.json"))
        {
            var item = ReadExistingItem(layout, nftyFile, File.ReadAllText(nftyFile),
                path => File.ReadAllText(path), newNumbers);
            if (item is not null) existing.Add(item);
        }
        return existing;
    }

    private static async Task<List<ExistingItem>> LoadExistingAsync(
        Layout layout, GeneratedSet set, CancellationToken ct)
    {
        var newNumbers = set.Assets.Select(a => a.SetNumber).ToHashSet();
        var existing = new List<ExistingItem>();

        foreach (var nftyFile in Directory.EnumerateFiles(layout.NftyDir, "*.json"))
        {
            string nftyJson = await File.ReadAllTextAsync(nftyFile, ct);
            var nfty = JsonSerializer.Deserialize<NftyMetadata>(nftyJson, Json.Options)!;
            if (newNumbers.Contains(nfty.SetNumber)) continue;

            var openFile = Path.Combine(layout.MetaDir, Path.GetFileName(nftyFile));
            RequireSibling(openFile, nftyFile);
            var open = JsonSerializer.Deserialize<OpenSeaMetadata>(
                await File.ReadAllTextAsync(openFile, ct), Json.Options)!;
            existing.Add(new ExistingItem(nftyFile, nfty.SetNumber, nfty.Recipe, open.Attributes, nfty));
        }
        return existing;
    }

    private static ExistingItem? ReadExistingItem(
        Layout layout, string nftyFile, string nftyJson, Func<string, string> readText, HashSet<int> newNumbers)
    {
        var nfty = JsonSerializer.Deserialize<NftyMetadata>(nftyJson, Json.Options)!;
        if (newNumbers.Contains(nfty.SetNumber)) return null;

        var openFile = Path.Combine(layout.MetaDir, Path.GetFileName(nftyFile));
        RequireSibling(openFile, nftyFile);
        var open = JsonSerializer.Deserialize<OpenSeaMetadata>(readText(openFile), Json.Options)!;
        return new ExistingItem(nftyFile, nfty.SetNumber, nfty.Recipe, open.Attributes, nfty);
    }

    /// <summary>
    /// A Set pairs every rich nfty/NNNN.json with a standards-pure metadata/NNNN.json. A missing
    /// sibling means the Set is corrupt, which is a domain fact — not the raw FileNotFoundException
    /// the JSON read would otherwise throw from somewhere deep inside extend.
    /// </summary>
    private static void RequireSibling(string openFile, string nftyFile)
    {
        if (File.Exists(openFile)) return;
        throw new CorruptSetException(openFile,
            $"Set is missing '{openFile}', the OpenSea metadata paired with '{nftyFile}'. "
            + "Every nfty/NNNN.json needs its metadata/NNNN.json sibling to extend this set.");
    }

    private static OpenSeaMetadata BuildOpenSea(GeneratedSet set, GeneratedAsset asset)
    {
        var attributes = new List<MetadataAttribute> { new(TypeTrait, asset.RecipeName) };
        attributes.AddRange(asset.Traits.Select(t => new MetadataAttribute(t.IngredientName, t.VariantName)));

        return new OpenSeaMetadata(
            Name: $"{set.CollectionName} #{asset.SetNumber}",
            Description: set.Description,
            Image: $"images/{asset.SetNumber:D4}.png",
            Attributes: attributes);
    }

    private static NftyMetadata BuildNfty(GeneratedSet set, GeneratedAsset asset, Rarity rarity)
    {
        var table = new List<RarityAttribute> { rarity.For(TypeTrait, asset.RecipeName) };
        table.AddRange(asset.Traits.Select(t => rarity.For(t.IngredientName, t.VariantName)));

        return new NftyMetadata(
            SetNumber: asset.SetNumber,
            Recipe: asset.RecipeId,
            Dna: asset.Dna,
            Seed: set.Seed,
            Rarity: table,
            Layers: asset.ColorRolls.Select(ToLayerColor).ToList());
    }

    /// <summary>An existing item with its rarity restated against the enlarged collection.</summary>
    private static NftyMetadata Regraded(ExistingItem item, Rarity rarity) =>
        item.Nfty with { Rarity = item.Attributes.Select(a => rarity.For(a.Trait_type, a.Value)).ToList() };

    private static SetManifest BuildSetManifest(
        GeneratedSet set, IReadOnlyList<ExistingItem> existing, Rarity rarity)
    {
        var distribution = existing.Select(e => e.Recipe)
            .Concat(set.Assets.Select(a => a.RecipeId))
            .GroupBy(id => id)
            .Select(g => new RecipeCount(g.Key, g.Count(), rarity.Percent(g.Count())))
            // Ordinal: the default comparer sorts by CURRENT CULTURE, so the same book and seed
            // would emit different set.json bytes on an en-US box than a sv-SE one (spec 5.5
            // promises byte-identical output).
            .OrderBy(d => d.Recipe, StringComparer.Ordinal).ToList();

        return new SetManifest(set.CollectionName, rarity.Total, set.Seed,
            set.CookbookSha256, GeneratorVersion, distribution, rarity.Table());
    }

    private static void Pack(string outDir)
    {
        string archivePath = outDir + ".set";
        if (File.Exists(archivePath)) File.Delete(archivePath);
        ZipFile.CreateFromDirectory(outDir, archivePath);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json.Options);

    private static LayerColor ToLayerColor(ColorRoll c) => new(
        Layer: c.LayerId,
        Kind: c.Kind.ToString().ToLowerInvariant(),
        Model: c.Model?.ToString().ToLowerInvariant(),
        H: c.H is double h ? Math.Round(h, 1) : null,
        S: c.S is double s ? Math.Round(s, 3) : null);

    /// <summary>
    /// Observed trait frequencies across the whole collection — the items already on disk plus
    /// the batch being written. Rarity is collection-wide, so it cannot be derived from one run.
    /// </summary>
    private sealed class Rarity
    {
        private readonly Dictionary<(string TraitType, string Value), int> counts = new();
        private readonly double denominator;

        public int Total { get; }

        public Rarity(IReadOnlyList<ExistingItem> existing, GeneratedSet set)
        {
            foreach (var item in existing)
                foreach (var attr in item.Attributes) Bump(attr.Trait_type, attr.Value);

            foreach (var asset in set.Assets)
            {
                Bump(TypeTrait, asset.RecipeName);
                foreach (var t in asset.Traits) Bump(t.IngredientName, t.VariantName);
            }

            Total = existing.Count + set.Assets.Count;
            denominator = Math.Max(1, Total);
        }

        public RarityAttribute For(string traitType, string value) =>
            new(traitType, value, Percent(counts.GetValueOrDefault((traitType, value))));

        public double Percent(int count) => Math.Round(count / denominator * 100, 2);

        public IReadOnlyList<RarityAttribute> Table() =>
            counts.Keys.Select(k => For(k.TraitType, k.Value))
                // Ordinal for the same reason as the recipe distribution above: culture must
                // never reach the output bytes.
                .OrderBy(r => r.Trait_type, StringComparer.Ordinal)
                .ThenBy(r => r.Value, StringComparer.Ordinal).ToList();

        private void Bump(string traitType, string value) =>
            counts[(traitType, value)] = counts.GetValueOrDefault((traitType, value)) + 1;
    }
}
