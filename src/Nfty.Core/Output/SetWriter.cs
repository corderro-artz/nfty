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
    /// <summary>Completion as 0..1, suitable for a progress bar.</summary>
    public double Fraction => Total <= 0 ? 0 : (double)Completed / Total;
}

/// <summary>Writes a generated collection to disk: the images, both metadata files per asset, and
/// the Set manifest — and reads an existing Set back so <c>extend</c> can add to it.</summary>
public static class SetWriter
{
    /// <summary>The engine stamp written into every Set manifest.</summary>
    public const string GeneratorVersion = "nfty/1.0";

    /// <summary>
    /// The pseudo trait-type under which an asset's Recipe is published, alongside its real
    /// ingredient traits. It shares a namespace with ingredient <em>names</em> in both the OpenSea
    /// attributes and the rarity table, so an Ingredient actually named "Type" collides with it and
    /// double-counts — which shipped as a <c>rarityPct</c> above 100 and a duplicated row.
    ///
    /// <para>Public because <see cref="Formats.Validator"/> reserves the name on this constant's
    /// authority rather than repeating the literal. Rejecting the book is the fix rather than
    /// disambiguating the key, because the two are genuinely indistinguishable once written: on
    /// extend, the recipe trait is read back from <c>metadata/NNNN.json</c> as the plain string
    /// "Type", with nothing left to tell it apart from an ingredient of that name.</para>
    /// </summary>
    public const string TypeTrait = "Type";

    /// <summary>What a Set already on disk contributes to an extend run.</summary>
    /// <param name="Dnas">Every DNA already minted, so new rolls can avoid them.</param>
    /// <param name="NextNumber">The number the next asset should take.</param>
    /// <param name="CookbookSha256">What <c>set.json</c> recorded as the source archive's hash, so an
    /// extend can tell whether the CookBook it was handed is the one that cooked this Set — see
    /// <see cref="SetProvenance"/>. <b>Null means "cannot tell"</b>, never "they differ": the Set may
    /// have been cooked from an in-memory book, or from a build before the field existed, and its
    /// <c>set.json</c> may be absent or unreadable. Optional so it is additive at every existing call
    /// site.</param>
    public record ExistingSet(IReadOnlyList<string> Dnas, int NextNumber, string? CookbookSha256 = null);

    // An item already on disk (from a previous batch) that this write is not overwriting.
    private record ExistingItem(string NftyPath, int SetNumber, string Recipe,
        IReadOnlyList<MetadataAttribute> Attributes, NftyMetadata Nfty);

    private record Layout(string OutDir, string ImagesDir, string MetaDir, string NftyDir);

    /// <summary>Writes a Set.</summary>
    /// <param name="set">The generated collection.</param>
    /// <param name="outDir">Destination folder; created if missing.</param>
    /// <param name="pack">Also zip the folder into a sibling <c>.set</c>.</param>
    public static void Write(GeneratedSet set, string outDir, bool pack)
    {
        // Read BEFORE anything can overwrite set.json — see RecordedShaAt.
        string? recorded = RecordedShaAt(outDir);
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

        File.WriteAllText(SetJsonPath(outDir),
            Serialize(BuildSetManifest(set, existing, rarity, recorded)));

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
        // Read BEFORE anything can overwrite set.json — see RecordedShaAt.
        string? recorded = RecordedShaAt(outDir);
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

        await File.WriteAllTextAsync(SetJsonPath(outDir),
            Serialize(BuildSetManifest(set, existing, rarity, recorded)), cancellationToken);

        // ZipFile has no async API; keep the UI thread free rather than pretend.
        if (pack) await Task.Run(() => Pack(outDir), cancellationToken);
    }

    /// <summary>Reads what an existing Set already holds, for extend.</summary>
    /// <param name="outDir">The Set folder.</param>
    /// <returns>Its DNAs and the next free number. An empty Set yields no DNAs and number 1.</returns>
    /// <exception cref="CorruptSetException">An item file is unreadable or missing its fields.</exception>
    public static ExistingSet ReadExisting(string outDir)
    {
        string? sha = RecordedShaAt(outDir);

        var nftyDir = Path.Combine(outDir, "nfty");
        if (!Directory.Exists(nftyDir)) return new ExistingSet(Array.Empty<string>(), 1, sha);

        var dnas = new List<string>();
        int maxNumber = 0;
        foreach (var file in Directory.EnumerateFiles(nftyDir, "*.json"))
        {
            var (dna, number) = ReadDnaAndNumber(File.ReadAllText(file), file);
            dnas.Add(dna);
            maxNumber = Math.Max(maxNumber, number);
        }
        return new ExistingSet(dnas, maxNumber + 1, sha);
    }

    /// <summary>Reads what an existing Set already holds, for extend.</summary>
    /// <param name="outDir">The Set folder.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Its DNAs and the next free number.</returns>
    /// <exception cref="CorruptSetException">An item file is unreadable or missing its fields.</exception>
    public static async Task<ExistingSet> ReadExistingAsync(
        string outDir, CancellationToken cancellationToken = default)
    {
        string setJson = SetJsonPath(outDir);
        string? sha = File.Exists(setJson)
            ? ParseRecordedCookbookSha(await File.ReadAllTextAsync(setJson, cancellationToken))
            : null;   // the awaiting twin of RecordedShaAt; same rule, same best-effort parse

        var nftyDir = Path.Combine(outDir, "nfty");
        if (!Directory.Exists(nftyDir)) return new ExistingSet(Array.Empty<string>(), 1, sha);

        var dnas = new List<string>();
        int maxNumber = 0;
        foreach (var file in Directory.EnumerateFiles(nftyDir, "*.json"))
        {
            var (dna, number) = ReadDnaAndNumber(await File.ReadAllTextAsync(file, cancellationToken), file);
            dnas.Add(dna);
            maxNumber = Math.Max(maxNumber, number);
        }
        return new ExistingSet(dnas, maxNumber + 1, sha);
    }

    /// <summary>
    /// Pulls the two fields extend needs out of one <c>nfty/NNNN.json</c>. Everything here is
    /// reported as <see cref="CorruptSetException"/> rather than allowed to escape as the
    /// framework's own type: <c>GetProperty</c> throws a bare <c>KeyNotFoundException</c> whose
    /// message is "The given key was not present in the dictionary", and <c>ErrorReport</c> prints
    /// exactly that to a user trying to extend their collection. <c>SiblingOf</c>, in this same
    /// file, already did it properly.
    /// </summary>
    /// <param name="json">The file's contents.</param>
    /// <param name="path">The file it came from, for the message.</param>
    /// <returns>The DNA and set number recorded for that asset.</returns>
    /// <exception cref="CorruptSetException">The file is not readable as an nfty metadata record.</exception>
    private static (string Dna, int SetNumber) ReadDnaAndNumber(string json, string path)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            throw new CorruptSetException(path,
                $"Set item '{path}' is not valid JSON, so extend cannot tell which assets already "
                + $"exist: {ex.Message}");
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("dna", out var dna) || dna.GetString() is not { } dnaText)
                throw new CorruptSetException(path,
                    $"Set item '{path}' has no 'dna' field, so extend cannot tell which assets "
                    + "already exist.");
            if (!doc.RootElement.TryGetProperty("setNumber", out var number)
                || !number.TryGetInt32(out int setNumber))
                throw new CorruptSetException(path,
                    $"Set item '{path}' has no usable 'setNumber' field, so extend cannot tell "
                    + "where to continue numbering.");

            return (dnaText, setNumber);
        }
    }

    /// <summary>Where a Set folder keeps its manifest.</summary>
    private static string SetJsonPath(string outDir) => Path.Combine(outDir, "set.json");

    /// <summary>
    /// The source-archive hash a Set recorded when it was cooked, or null when it did not record one.
    ///
    /// <para><b>Best effort, on purpose.</b> This field exists so extend can <i>warn</i> that the book
    /// it was handed is not the one that cooked the Set — see <see cref="SetProvenance"/> — and a
    /// warning must never be able to fail the operation it decorates. Extend's real inputs are the
    /// per-item files, which are read strictly and do raise <see cref="CorruptSetException"/>; a
    /// missing or malformed <c>set.json</c> only costs the warning, so it reads as "cannot tell"
    /// rather than as an error.</para>
    /// </summary>
    /// <param name="json">The contents of <c>set.json</c>.</param>
    /// <returns>The recorded hash, or null when it is absent or unreadable.</returns>
    private static string? ParseRecordedCookbookSha(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SetManifest>(json, Json.Options)?.CookbookSha256;
        }
        catch (JsonException)
        {
            return null;
        }
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
        var newNumbers = NewNumbers(set);
        var existing = new List<ExistingItem>();

        foreach (var nftyFile in Directory.EnumerateFiles(layout.NftyDir, "*.json"))
        {
            var nfty = Deserialize<NftyMetadata>(File.ReadAllText(nftyFile));
            if (newNumbers.Contains(nfty.SetNumber)) continue;

            var open = Deserialize<OpenSeaMetadata>(File.ReadAllText(SiblingOf(layout, nftyFile)));
            existing.Add(ToExistingItem(nftyFile, nfty, open));
        }
        return existing;
    }

    private static async Task<List<ExistingItem>> LoadExistingAsync(
        Layout layout, GeneratedSet set, CancellationToken ct)
    {
        var newNumbers = NewNumbers(set);
        var existing = new List<ExistingItem>();

        foreach (var nftyFile in Directory.EnumerateFiles(layout.NftyDir, "*.json"))
        {
            var nfty = Deserialize<NftyMetadata>(await File.ReadAllTextAsync(nftyFile, ct));
            if (newNumbers.Contains(nfty.SetNumber)) continue;

            var open = Deserialize<OpenSeaMetadata>(
                await File.ReadAllTextAsync(SiblingOf(layout, nftyFile), ct));
            existing.Add(ToExistingItem(nftyFile, nfty, open));
        }
        return existing;
    }

    private static HashSet<int> NewNumbers(GeneratedSet set) =>
        set.Assets.Select(a => a.SetNumber).ToHashSet();

    private static ExistingItem ToExistingItem(string nftyFile, NftyMetadata nfty, OpenSeaMetadata open) =>
        new(nftyFile, nfty.SetNumber, nfty.Recipe, open.Attributes, nfty);

    /// <summary>
    /// The standards-pure metadata/NNNN.json paired with a rich nfty/NNNN.json. A missing sibling
    /// means the Set is corrupt, which is a domain fact — not the raw FileNotFoundException the
    /// JSON read would otherwise throw from somewhere deep inside extend.
    /// </summary>
    private static string SiblingOf(Layout layout, string nftyFile)
    {
        var openFile = Path.Combine(layout.MetaDir, Path.GetFileName(nftyFile));
        if (File.Exists(openFile)) return openFile;
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

    /// <summary>
    /// What <c>set.json</c> in this folder already records as its source archive, or null when there
    /// is no readable one — the same best-effort parse <c>ReadExisting</c> does.
    ///
    /// <para>Read at the very top of a write, because the write itself overwrites the file it reads.
    /// That ordering is the whole point: without it an extend erased the record it was about to be
    /// judged against.</para>
    /// </summary>
    private static string? RecordedShaAt(string outDir)
    {
        string setJson = SetJsonPath(outDir);
        return File.Exists(setJson) ? ParseRecordedCookbookSha(File.ReadAllText(setJson)) : null;
    }

    /// <summary>The <c>set.json</c> for the enlarged collection. <paramref name="recordedSha256"/> is
    /// what this Set already recorded as its origin, from <see cref="RecordedShaAt"/>.</summary>
    private static SetManifest BuildSetManifest(
        GeneratedSet set, IReadOnlyList<ExistingItem> existing, Rarity rarity, string? recordedSha256)
    {
        var distribution = existing.Select(e => e.Recipe)
            .Concat(set.Assets.Select(a => a.RecipeId))
            .GroupBy(id => id)
            .Select(g => new RecipeCount(g.Key, g.Count(), rarity.Percent(g.Count())))
            // Ordinal: the default comparer sorts by CURRENT CULTURE, so the same book and seed
            // would emit different set.json bytes on an en-US box than a sv-SE one (spec 5.5
            // promises byte-identical output).
            .OrderBy(d => d.Recipe, StringComparer.Ordinal).ToList();

        // The origin wins, and is never overwritten. cookbookSha256 means "the archive this Set was
        // cooked from", and an extend does not re-cook a Set — it adds to one. Stamping the book in
        // hand made the field describe the last EDIT instead of the origin, which made the mismatch
        // warning self-defeating: extending a Set from book A with book B warned once, wrote B over
        // A, then fell silent for every further extend with B — and started warning about A, the
        // book that had actually minted most of the collection. The check inverted.
        //
        // Null falls through, because null means "no origin was ever recorded" (cooked from an
        // in-memory book, or by a build predating the field) — an absence to fill, not a value to
        // preserve.
        return new SetManifest(set.CollectionName, rarity.Total, set.Seed,
            recordedSha256 ?? set.CookbookSha256, GeneratorVersion, distribution, rarity.Table());
    }

    private static void Pack(string outDir)
    {
        // TrimEndingDirectorySeparator, not bare concatenation: for an outDir given with a trailing
        // separator ("out\"), "out\" + ".set" is "out\.set" — a file INSIDE the directory being
        // zipped, rather than the sibling archive the caller asked for.
        string archivePath = Path.TrimEndingDirectorySeparator(outDir) + ".set";
        if (File.Exists(archivePath)) File.Delete(archivePath);
        ZipFile.CreateFromDirectory(outDir, archivePath);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json.Options);

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json.Options)!;

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
