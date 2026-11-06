using System.IO.Compression;
using System.Text.Json;
using Nfty.Core.Diagnostics;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Nfty.Core.Output;

/// <summary>How far a set write has got. <see cref="Fraction"/> suits a progress bar.</summary>
/// <param name="Completed">Assets written so far.</param>
/// <param name="Total">Assets the write will produce.</param>
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
    /// <param name="Models">What <c>set.json</c> recorded about the Set's 3D models, or null when it
    /// recorded none (cooked without models, or unreadable). Optional so it is additive.</param>
    public record ExistingSet(IReadOnlyList<string> Dnas, int NextNumber, string? CookbookSha256 = null,
        SetModels? Models = null);

    // An item already on disk (from a previous batch) that this write is not overwriting.
    private record ExistingItem(string NftyPath, int SetNumber, string Recipe,
        IReadOnlyList<MetadataAttribute> Attributes, NftyMetadata Nfty);

    private record Layout(string OutDir, string ImagesDir, string MetaDir, string NftyDir, string ModelsDir);

    /// <summary>Writes a Set.</summary>
    /// <param name="set">The generated collection.</param>
    /// <param name="outDir">Destination folder; created if missing.</param>
    /// <param name="pack">Also zip the folder into a <c>.set</c> archive inside it, beside the images.</param>
    /// <param name="models">Turns each asset into 3D model files under <c>models/</c>; null writes none.
    /// A Set is modeled whole or not at all, so extending one needs the same options.</param>
    /// <param name="progress">Receives one report per asset, one at a time, in increasing order.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="parallelism">How wide to write; null means <see cref="Parallelism.Default"/>. The files are
    /// byte-identical at any degree.</param>
    /// <exception cref="InvalidOperationException">The Set already on disk was modeled differently, or a
    /// modeler named a file outside <c>models/</c>.</exception>
    public static void Write(GeneratedSet set, string outDir, bool pack, IAssetModeler? models = null,
        IProgress<WriteProgress>? progress = null, CancellationToken cancellationToken = default,
        Parallelism? parallelism = null)
    {
        // A failed or canceled run takes back the files it added (WriteRollback), so a stop never
        // leaves a folder that looks like a Set without a set.json.
        var rollback = WriteRollback.Capture(outDir);
        try { WriteCore(set, outDir, pack, models, progress, cancellationToken, parallelism, rollback); }
        catch { rollback.Undo(); throw; }
    }

    private static void WriteCore(GeneratedSet set, string outDir, bool pack, IAssetModeler? models,
        IProgress<WriteProgress>? progress, CancellationToken cancellationToken, Parallelism? parallelism,
        WriteRollback rollback)
    {
        parallelism ??= Parallelism.Default;
        // Read BEFORE anything can overwrite set.json — see RecordedAt.
        var onDisk = RecordedAt(outDir);
        string? recorded = onDisk.Sha;
        var recordedModels = CheckModels(outDir, models, onDisk.Models);
        // Every file NAME is validated for every asset before the first file is written, so a bad
        // name on asset 300 refuses the run as cleanly as a mismatched option does. Names are asked
        // of the modeler without building any model, so this costs no memory.
        ValidateModelNames(models, set);
        var skipped = models is null ? null : new bool[set.Assets.Count];
        rollback.BeginMutation();
        var layout = Prepare(outDir, models is not null);
        int shape = ShapeFor(onDisk, outDir);
        // An interrupted write must leave no index, not a stale one; it is rewritten below.
        SetIndex.Delete(outDir);
        var existing = LoadExisting(layout, set);
        var rarity = new Rarity(existing, set, shape);
        var indexSlots = new SetIndexEntry[set.Assets.Count];
        var slots = SlotsOf(set);

        // One asset's three files depend on that asset and on `rarity`, which is finished being
        // built above and is only read from here on. Nothing is shared and nothing accumulates, so
        // the files that land on disk are byte-identical whatever order they are written in — only
        // the order changes, and no reader can see it. PNG encoding is the cost here, and it is
        // CPU-bound, so this is where the time goes.
        ParallelWork.ForEachReporting(set.Assets, parallelism, cancellationToken, asset =>
        {
            var (imagePath, metaPath, nftyPath) = PathsFor(layout, asset.SetNumber);
            asset.Image.Save(imagePath, new PngEncoder());
            File.WriteAllText(metaPath, Serialize(BuildOpenSea(set, asset, shape)));
            var nftyText = Serialize(BuildNfty(set, asset, rarity, shape));
            File.WriteAllText(nftyPath, nftyText);
            indexSlots[slots[asset.SetNumber]] = IndexEntryOf(nftyPath, asset.SetNumber, asset.Dna, nftyText);

            // Built, written and dropped one at a time: memory is one model per worker, never the set.
            var files = ModelFor(models, asset);
            if (skipped is not null && files is null) skipped[slots[asset.SetNumber]] = true;
            ClearStaleModels(layout, asset, files, models);
            if (files is not null)
                foreach (var file in files) File.WriteAllBytes(Path.Combine(layout.ModelsDir, file.FileName), file.Bytes);
        }, progress is null ? null : done => progress.Report(new WriteProgress(done, set.Assets.Count)));

        // Extend's regrade: same shape, same independence — one file per existing item.
        var existingEntries = new SetIndexEntry[existing.Count];
        ParallelWork.For(existing.Count, parallelism, cancellationToken, i =>
        {
            var item = existing[i];
            var text = Serialize(Regraded(item, rarity, shape));
            File.WriteAllText(item.NftyPath, text);
            existingEntries[i] = IndexEntryOf(item.NftyPath, item.SetNumber, item.Nfty.Dna, text);
        });

        File.WriteAllText(SetJsonPath(outDir),
            Serialize(BuildSetManifest(set, existing, rarity, recorded, onDisk.Unique,
                MergedModels(models, recordedModels, set, skipped), shape)));

        SetIndex.Write(outDir, [.. indexSlots, .. existingEntries]);

        if (pack) Pack(outDir);
    }

    /// <summary>
    /// Writes the set with genuinely async I/O, reporting one <see cref="WriteProgress"/> per
    /// asset. Behaves exactly as <see cref="Write"/> — same files, same bytes.
    /// </summary>
    /// <param name="set">The generated collection.</param>
    /// <param name="outDir">Destination folder; created if missing.</param>
    /// <param name="pack">Also zip the folder into a <c>.set</c> archive inside it.</param>
    /// <param name="progress">Receives one report per asset, one at a time, in increasing order.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <param name="models">As for <see cref="Write"/>; last so every existing call site is unchanged.</param>
    /// <param name="parallelism">As for <see cref="Write"/>.</param>
    public static async Task WriteAsync(
        GeneratedSet set,
        string outDir,
        bool pack,
        IProgress<WriteProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IAssetModeler? models = null,
        Parallelism? parallelism = null)
    {
        var rollback = WriteRollback.Capture(outDir);
        try
        {
            await WriteCoreAsync(set, outDir, pack, progress, cancellationToken, models, parallelism, rollback).ConfigureAwait(false);
        }
        catch { rollback.Undo(); throw; }
    }

    private static async Task WriteCoreAsync(GeneratedSet set, string outDir, bool pack,
        IProgress<WriteProgress>? progress, CancellationToken cancellationToken, IAssetModeler? models,
        Parallelism? parallelism, WriteRollback rollback)
    {
        parallelism ??= Parallelism.Default;
        // Read BEFORE anything can overwrite set.json — see RecordedAt.
        var onDisk = RecordedAt(outDir);
        string? recorded = onDisk.Sha;
        var recordedModels = CheckModels(outDir, models, onDisk.Models);
        // As in Write: every name validated before anything is written.
        ValidateModelNames(models, set);
        var skipped = models is null ? null : new bool[set.Assets.Count];
        rollback.BeginMutation();
        var layout = Prepare(outDir, models is not null);
        int shape = ShapeFor(onDisk, outDir);
        SetIndex.Delete(outDir);
        var existing = await LoadExistingAsync(layout, set, cancellationToken).ConfigureAwait(false);
        var rarity = new Rarity(existing, set, shape);
        var indexSlots = new SetIndexEntry[set.Assets.Count];
        var slots = SlotsOf(set);

        var progressGate = new Lock();
        int done = 0;
        await ParallelWork.ForEachAsync(set.Assets, parallelism, cancellationToken, async (asset, ct) =>
        {
            var (imagePath, metaPath, nftyPath) = PathsFor(layout, asset.SetNumber);
            await asset.Image.SaveAsync(imagePath, new PngEncoder(), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(metaPath, Serialize(BuildOpenSea(set, asset, shape)), ct).ConfigureAwait(false);
            var nftyText = Serialize(BuildNfty(set, asset, rarity, shape));
            await File.WriteAllTextAsync(nftyPath, nftyText, ct).ConfigureAwait(false);
            indexSlots[slots[asset.SetNumber]] = IndexEntryOf(nftyPath, asset.SetNumber, asset.Dna, nftyText);

            // The modeler is synchronous CPU work; it is not faked async. One model at a time per worker.
            var files = ModelFor(models, asset);
            if (skipped is not null && files is null) skipped[slots[asset.SetNumber]] = true;
            ClearStaleModels(layout, asset, files, models);
            if (files is not null)
                foreach (var file in files)
                    await File.WriteAllBytesAsync(Path.Combine(layout.ModelsDir, file.FileName), file.Bytes, ct).ConfigureAwait(false);

            // Serialized for the same reason as the generator's: a caller's handler is written
            // assuming one call at a time, in order.
            if (progress is not null)
                lock (progressGate)
                    progress.Report(new WriteProgress(++done, set.Assets.Count));
        }).ConfigureAwait(false);

        var existingEntries = new SetIndexEntry[existing.Count];
        await ParallelWork.ForEachAsync(Enumerable.Range(0, existing.Count).ToList(), parallelism, cancellationToken,
            async (i, ct) =>
            {
                var item = existing[i];
                var text = Serialize(Regraded(item, rarity, shape));
                await File.WriteAllTextAsync(item.NftyPath, text, ct).ConfigureAwait(false);
                existingEntries[i] = IndexEntryOf(item.NftyPath, item.SetNumber, item.Nfty.Dna, text);
            }).ConfigureAwait(false);

        await File.WriteAllTextAsync(SetJsonPath(outDir),
            Serialize(BuildSetManifest(set, existing, rarity, recorded, onDisk.Unique,
                MergedModels(models, recordedModels, set, skipped), shape)),
            cancellationToken).ConfigureAwait(false);

        await SetIndex.WriteAsync(outDir, [.. indexSlots, .. existingEntries], cancellationToken).ConfigureAwait(false);

        // ZipFile has no async API; keep the UI thread free rather than pretend.
        if (pack) await Task.Run(() => Pack(outDir), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads what an existing Set already holds, for extend.</summary>
    /// <param name="outDir">The Set folder.</param>
    /// <param name="parallelism">How wide the per-file parsing may run; null means <see cref="Parallelism.Default"/>. The result never depends on it.</param>
    /// <param name="needDnas">False when the caller will not dedup against DNAs (an <c>--unlimited</c> extend, or a
    /// read for the models block): <see cref="ExistingSet.Dnas"/> is then empty when the index answers, and
    /// <see cref="ExistingSet.NextNumber"/> is right either way. The walk, used when the index cannot be trusted,
    /// still returns DNAs: it opens every file regardless.</param>
    /// <returns>Its DNAs and the next free number. An empty Set yields no DNAs and number 1.</returns>
    /// <remarks>Answered from <c>set-index.txt</c> when it matches <c>nfty/*.json</c> exactly (see
    /// <c>SetIndex</c>), else by opening every item file. Identical result either way.</remarks>
    /// <exception cref="CorruptSetException">An item file is unreadable or missing its fields.</exception>
    public static ExistingSet ReadExisting(string outDir, Parallelism? parallelism = null, bool needDnas = true)
    {
        var onDisk = RecordedAt(outDir);
        string? sha = onDisk.Sha;
        var recordedModels = onDisk.Models;

        var nftyDir = Path.Combine(outDir, SetLayout.NftyDir);
        if (!Directory.Exists(nftyDir)) return new ExistingSet(Array.Empty<string>(), 1, sha, recordedModels);

        using (Perf.Measure("extend.readExisting.index"))
            if (SetIndex.TryRead(outDir, nftyDir, needDnas) is { } indexed)
                return FromIndex(indexed, sha, recordedModels);

        // Parsed in parallel into slots; the maximum is order-free. The listing is sorted ordinally,
        // as SetReader's is, so when several files are damaged the one named is the same on every
        // file system - enumeration order is the file system's, not nfty's.
        using var walk = Perf.Measure("extend.readExisting.walk");
        var files = ListItemFiles(nftyDir);
        var parsed = new (string Dna, int Number)[files.Count];
        ParallelWork.For(files.Count, parallelism ?? Parallelism.Default, CancellationToken.None,
            i => parsed[i] = ReadDnaAndNumber(File.ReadAllText(files[i]), files[i]));
        return Assemble(parsed, sha, recordedModels);
    }

    /// <summary>Reads what an existing Set already holds, for extend.</summary>
    /// <param name="outDir">The Set folder.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <param name="parallelism">How wide the per-file parsing may run; null means <see cref="Parallelism.Default"/>.</param>
    /// <param name="needDnas">As for <see cref="ReadExisting"/>.</param>
    /// <returns>Its DNAs and the next free number.</returns>
    /// <exception cref="CorruptSetException">An item file is unreadable or missing its fields.</exception>
    public static async Task<ExistingSet> ReadExistingAsync(
        string outDir, CancellationToken cancellationToken = default, Parallelism? parallelism = null,
        bool needDnas = true)
    {
        var onDisk = await RecordedAtAsync(outDir, cancellationToken).ConfigureAwait(false);
        string? sha = onDisk.Sha;
        var recordedModels = onDisk.Models;

        var nftyDir = Path.Combine(outDir, SetLayout.NftyDir);
        if (!Directory.Exists(nftyDir)) return new ExistingSet(Array.Empty<string>(), 1, sha, recordedModels);

        using (Perf.Measure("extend.readExisting.index"))
            if (await SetIndex.TryReadAsync(outDir, nftyDir, needDnas, cancellationToken).ConfigureAwait(false) is { } indexed)
                return FromIndex(indexed, sha, recordedModels);

        using var walk = Perf.Measure("extend.readExisting.walk");
        var files = ListItemFiles(nftyDir);
        var parsed = new (string Dna, int Number)[files.Count];
        await ParallelWork.ForEachAsync(Enumerable.Range(0, files.Count).ToList(),
            parallelism ?? Parallelism.Default, cancellationToken,
            async (i, ct) =>
            {
                var text = await File.ReadAllTextAsync(files[i], ct).ConfigureAwait(false);
                parsed[i] = ReadDnaAndNumber(text, files[i]);
            }).ConfigureAwait(false);
        return Assemble(parsed, sha, recordedModels);
    }

    /// <summary>An <see cref="ExistingSet"/> from a verified index: the same fold <see cref="Assemble"/> does.</summary>
    private static ExistingSet FromIndex(List<SetIndexEntry> entries, string? sha, SetModels? models)
    {
        int max = 0;
        var dnas = new List<string>(entries.Count);
        foreach (var e in entries)
        {
            if (e.Dna.Length > 0) dnas.Add(e.Dna);
            max = Math.Max(max, e.Number);
        }
        return new ExistingSet(dnas, max + 1, sha, models);
    }

    /// <summary>The index line for an item file just written: its size is the UTF-8 length of the text written.</summary>
    private static SetIndexEntry IndexEntryOf(string nftyPath, int number, string dna, string text) =>
        new(Path.GetFileName(nftyPath), System.Text.Encoding.UTF8.GetByteCount(text), number, dna);

    /// <summary>Every <c>nfty/*.json</c>, in ordinal order of path.</summary>
    private static List<string> ListItemFiles(string nftyDir)
    {
        var files = Directory.EnumerateFiles(nftyDir, "*.json").ToList();
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>Folds the per-file slots into an <see cref="ExistingSet"/>: DNAs in slot order, the
    /// next number one past the largest.</summary>
    private static ExistingSet Assemble((string Dna, int Number)[] parsed, string? sha, SetModels? models)
    {
        var dnas = new List<string>(parsed.Length);
        int maxNumber = 0;
        foreach (var (dna, number) in parsed)
        {
            dnas.Add(dna);
            maxNumber = Math.Max(maxNumber, number);
        }
        return new ExistingSet(dnas, maxNumber + 1, sha, models);
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
    private static (string Dna, int SetNumber) ReadDnaAndNumber(string json, string path) =>
        SetItemFile.ReadIdentity(path, json);

    /// <summary>Where a Set folder keeps its manifest.</summary>
    private static string SetJsonPath(string outDir) => SetLayout.ManifestPath(outDir);

    /// <summary>What <c>set.json</c> in a folder already records, read once.</summary>
    /// <param name="Sha">The source archive's hash, or null when it did not record one.</param>
    /// <param name="Models">The recorded 3D-models block, or null.</param>
    /// <param name="Unique">What it recorded about its own uniqueness, or null when it never said.</param>
    /// <param name="Shape">The recorded <c>metadataShape</c>, or null when absent (the legacy shape).</param>
    /// <param name="HasManifest">Whether a readable <c>set.json</c> existed at all.</param>
    private sealed record Recorded(string? Sha, SetModels? Models, bool? Unique, int? Shape = null, bool HasManifest = false);

    private static readonly Recorded NothingRecorded = new(null, null, null);

    /// <summary>
    /// What <c>set.json</c> in this folder already records, or nothing when there is no readable
    /// one. One read and one parse for every field.
    ///
    /// <para><b>Best effort, on purpose.</b> These fields exist so extend can <i>warn</i> that the
    /// book it was handed is not the one that cooked the Set - see <see cref="SetProvenance"/> - and
    /// a warning must never be able to fail the operation it decorates. Extend's real inputs are
    /// the per-item files, which are read strictly and do raise <see cref="CorruptSetException"/>; a
    /// missing or malformed <c>set.json</c> only costs the warning, so it reads as "cannot tell"
    /// rather than as an error.</para>
    ///
    /// <para>Read at the very top of a write, because the write itself overwrites the file it reads.
    /// That ordering is the whole point: without it an extend erased the record it was about to be
    /// judged against.</para>
    /// </summary>
    private static Recorded RecordedAt(string outDir)
    {
        string setJson = SetJsonPath(outDir);
        return File.Exists(setJson) ? ParseRecorded(File.ReadAllText(setJson)) : NothingRecorded;
    }

    /// <summary>The awaiting twin of <see cref="RecordedAt"/>; same rule, same best-effort parse.</summary>
    private static async Task<Recorded> RecordedAtAsync(string outDir, CancellationToken ct)
    {
        string setJson = SetJsonPath(outDir);
        return File.Exists(setJson)
            ? ParseRecorded(await File.ReadAllTextAsync(setJson, ct).ConfigureAwait(false))
            : NothingRecorded;
    }

    private static Recorded ParseRecorded(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<SetManifest>(json, Json.Options);
            // An export's files are named for a marketplace, and its metadata links were rewritten.
            // Adding assets to it would write padded names beside token ids, so it is refused whole.
            if (m?.Files is not null)
                throw new InvalidOperationException(
                    "This Set is an export: its files were renamed or relinked for upload, so assets cannot "
                    + "be added to it without mixing names. Extend the source Set, then export again.");
            return m is null ? NothingRecorded : new Recorded(m.CookbookSha256, m.Models, m.UniqueDna, m.MetadataShape, true);
        }
        catch (JsonException)
        {
            return NothingRecorded;
        }
    }

    private static Layout Prepare(string outDir, bool withModels = false)
    {
        var layout = new Layout(outDir,
            Path.Combine(outDir, SetLayout.ImagesDir),
            Path.Combine(outDir, SetLayout.MetadataDir),
            Path.Combine(outDir, SetLayout.NftyDir),
            Path.Combine(outDir, SetLayout.ModelsDir));

        if (withModels) Directory.CreateDirectory(layout.ModelsDir);
        Directory.CreateDirectory(layout.ImagesDir);
        Directory.CreateDirectory(layout.MetaDir);
        Directory.CreateDirectory(layout.NftyDir);
        return layout;
    }

    private static (string Image, string Meta, string Nfty) PathsFor(Layout layout, int setNumber)
    {
        string stem = SetLayout.Stem(setNumber);
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
            var nfty = SetItemFile.ReadNfty(nftyFile, File.ReadAllText(nftyFile));
            if (newNumbers.Contains(nfty.SetNumber)) continue;

            var open = ReadSibling(layout, nftyFile);
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
            var nfty = SetItemFile.ReadNfty(nftyFile, await File.ReadAllTextAsync(nftyFile, ct).ConfigureAwait(false));
            if (newNumbers.Contains(nfty.SetNumber)) continue;

            var open = await ReadSiblingAsync(layout, nftyFile, ct).ConfigureAwait(false);
            existing.Add(ToExistingItem(nftyFile, nfty, open));
        }
        return existing;
    }

    private static HashSet<int> NewNumbers(GeneratedSet set) =>
        set.Assets.Select(a => a.SetNumber).ToHashSet();

    private static ExistingItem ToExistingItem(string nftyFile, NftyMetadata nfty, OpenSeaMetadata open) =>
        new(nftyFile, nfty.SetNumber, nfty.Recipe, open.Attributes, nfty);

    private static OpenSeaMetadata ReadSibling(Layout layout, string nftyFile)
    {
        string path = SiblingOf(layout, nftyFile);
        return SetItemFile.ReadOpenSea(path, File.ReadAllText(path));
    }

    private static async Task<OpenSeaMetadata> ReadSiblingAsync(Layout layout, string nftyFile, CancellationToken ct)
    {
        string path = SiblingOf(layout, nftyFile);
        return SetItemFile.ReadOpenSea(path, await File.ReadAllTextAsync(path, ct).ConfigureAwait(false));
    }

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

    /// <summary>
    /// The value an absent layer is counted and reported under. One constant, because the rarity
    /// tally and the per-item table must agree on the string or a layer's shares stop summing.
    /// </summary>
    private const string AbsentValue = "(none)";

    /// <summary>
    /// The shape this write produces. A Set already on disk keeps ITS shape (absent means the
    /// legacy one), so extend never mixes shapes and never rewrites a published file's attributes;
    /// a Set that is not on disk yet gets the current one.
    /// </summary>
    private static int ShapeFor(Recorded onDisk, string outDir) =>
        onDisk.HasManifest ? onDisk.Shape ?? MetadataTraits.LegacyShape
        : HasItems(outDir) ? MetadataTraits.LegacyShape
        : MetadataTraits.CurrentShape;

    private static OpenSeaMetadata BuildOpenSea(GeneratedSet set, GeneratedAsset asset, int shape)
    {
        var attributes = new List<MetadataAttribute> { new(TypeTrait, asset.RecipeName) };
        attributes.AddRange(MetadataTraits.Attributes(asset, shape));

        return new OpenSeaMetadata(
            Name: $"{set.CollectionName} #{asset.SetNumber}",
            Description: set.Description,
            Image: $"images/{SetLayout.Stem(asset.SetNumber)}.png",
            Attributes: attributes);
    }

    private static NftyMetadata BuildNfty(GeneratedSet set, GeneratedAsset asset, Rarity rarity, int shape)
    {
        var table = new List<RarityAttribute> { rarity.For(TypeTrait, asset.RecipeName) };
        table.AddRange(MetadataTraits.Attributes(asset, shape).Select(t => rarity.For(t.Trait_type, t.Value)));

        // An absent layer earns a rarity row of its own. "How unusual is it to have no hat" is a
        // real question about a collection, and it is only answerable if not-having is counted like
        // having. It goes in the RICH file only: the OpenSea attributes above are built from
        // asset.Traits, which an absent layer never enters, so the standards-pure file stays free
        // of a "None" trait — which is what the standard means by not having something.
        // Shape 1 only: from shape 2 the absence is an ordinary attribute above, spelled once.
        if (shape < MetadataTraits.CurrentShape)
            table.AddRange(asset.AbsentLayers.Select(a => rarity.For(a.IngredientName, AbsentValue)));

        return new NftyMetadata(
            SetNumber: asset.SetNumber,
            Recipe: asset.RecipeId,
            Dna: asset.Dna,
            Seed: set.Seed,
            Rarity: table,
            Layers: asset.ColorRolls.Select(ToLayerColor).ToList(),
            AbsentLayers: asset.AbsentLayers.Count == 0
                ? null
                : asset.AbsentLayers.Select(a => a.IngredientName).ToList());
    }

    /// <summary>An existing item with its rarity restated against the enlarged collection.</summary>
    private static NftyMetadata Regraded(ExistingItem item, Rarity rarity, int shape) =>
        item.Nfty with { Rarity = ExistingAttributes(item, shape).Select(a => rarity.For(a.Trait_type, a.Value)).ToList() };

    private static IEnumerable<MetadataAttribute> ExistingAttributes(ExistingItem item, int shape)
    {
        foreach (var attr in item.Attributes) yield return attr;
        if (shape >= MetadataTraits.CurrentShape || item.Nfty.AbsentLayers is not { } absent) yield break;
        foreach (string name in absent.Distinct(StringComparer.Ordinal))
            if (!item.Attributes.Any(a => a.Trait_type == name))
                yield return new MetadataAttribute(name, AbsentValue);
    }

    /// <summary>Set number to its slot in the per-asset <c>skipped</c> array; read-only once built.</summary>
    private static Dictionary<int, int> SlotsOf(GeneratedSet set)
    {
        var slots = new Dictionary<int, int>(set.Assets.Count);
        for (int i = 0; i < set.Assets.Count; i++) slots[set.Assets[i].SetNumber] = i;
        return slots;
    }

    /// <summary>
    /// Asks the modeler for the file names it will use for every asset and refuses any that is not a
    /// bare file name, before anything is written. Asking for names builds no model.
    /// </summary>
    private static void ValidateModelNames(IAssetModeler? models, GeneratedSet set)
    {
        if (models is null) return;
        foreach (var asset in set.Assets)
            foreach (string name in models.FileNames(SetLayout.Stem(asset.SetNumber)))
                CheckModelFileName(name);
    }

    private static void CheckModelFileName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || Path.GetFileName(name) != name
            || name.Contains('/') || name.Contains('\\') || Path.IsPathRooted(name))
            throw new InvalidOperationException(
                $"A model file name must be a bare file name inside '{SetLayout.ModelsDir}/', "
                + $"but the modeler named '{name}'.");
    }

    /// <summary>
    /// Builds one asset's model, or null when it cannot be modeled (a skip). A returned name the
    /// modeler did not declare up front is refused, so the up-front check cannot be sidestepped.
    /// </summary>
    private static IReadOnlyList<SetFile>? ModelFor(IAssetModeler? models, GeneratedAsset asset)
    {
        if (models is null) return null;
        string baseName = SetLayout.Stem(asset.SetNumber);
        var files = models.Model(asset, baseName);
        if (files is null) return null;

        var declared = models.FileNames(baseName);
        foreach (var file in files)
        {
            CheckModelFileName(file.FileName);
            if (!declared.Contains(file.FileName, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"The modeler wrote '{file.FileName}' for asset {baseName} without declaring it in FileNames.");
        }
        return files;
    }

    /// <summary>
    /// An asset re-made as a skip (extend over a number that used to have a model) must not leave
    /// its old model behind: set.json would say "no model" beside a file that claims otherwise.
    /// </summary>
    private static void ClearStaleModels(Layout layout, GeneratedAsset asset, IReadOnlyList<SetFile>? files, IAssetModeler? models)
    {
        if (models is null || files is not null || !Directory.Exists(layout.ModelsDir)) return;
        // Only the files this modeler would have written for the number - never a wildcard, since
        // the folder may hold a user's own 0007.blend.
        foreach (string name in models.FileNames(SetLayout.Stem(asset.SetNumber)))
        {
            string path = Path.Combine(layout.ModelsDir, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>
    /// The refusals, before anything is written. A Set is modeled whole or not at all: its
    /// <c>models</c> block describes every asset, so extending with different (or no) options would
    /// make that sentence false for some of them.
    /// </summary>
    /// <returns>What the Set on disk recorded, or null.</returns>
    private static SetModels? CheckModels(string outDir, IAssetModeler? models, SetModels? recorded)
    {
        if (recorded is not null && models is null)
            throw new InvalidOperationException(
                $"'{outDir}' was cooked with 3D models ({Describe(recorded)}); extending it needs the same options.");

        if (recorded is null && models is not null && HasItems(outDir))
            throw new InvalidOperationException(
                $"'{outDir}' was cooked without 3D models; a Set is modeled whole or not at all "
                + $"(asked for {Describe(models.Options)}).");

        if (recorded is not null && models is not null
            && (recorded.Format != models.Options.Format || recorded.Pivot != models.Options.Pivot || recorded.VoxelSize != models.Options.VoxelSize))
            throw new InvalidOperationException(
                $"'{outDir}' was cooked with 3D models ({Describe(recorded)}) but this run asks for "
                + $"({Describe(models.Options)}); extending it needs the same options.");

        return recorded;
    }

    private static string Describe(SetModels m) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{m.Format}, {m.Pivot}, voxel {m.VoxelSize}");

    private static bool HasItems(string outDir)
    {
        string nfty = Path.Combine(outDir, SetLayout.NftyDir);
        return Directory.Exists(nfty) && Directory.EnumerateFiles(nfty, "*.json").Any();
    }

    /// <summary>The block for <c>set.json</c>: this run's options, skipped = recorded (minus any
    /// number this run re-made) union this run's, ascending, null when empty.</summary>
    private static SetModels? MergedModels(
        IAssetModeler? models, SetModels? recorded, GeneratedSet set, bool[]? skipped)
    {
        if (models is null || skipped is null) return null;
        var mine = new HashSet<int>(set.Assets.Select(a => a.SetNumber));
        var all = new SortedSet<int>((recorded?.Skipped ?? []).Where(n => !mine.Contains(n)));
        for (int i = 0; i < skipped.Length; i++)
            if (skipped[i]) all.Add(set.Assets[i].SetNumber);
        return models.Options with { Skipped = all.Count == 0 ? null : all.ToList() };
    }

    /// <summary>The <c>set.json</c> for the enlarged collection. <paramref name="recordedSha256"/> is
    /// what this Set already recorded as its origin, from <see cref="RecordedAt"/>.</summary>
    private static SetManifest BuildSetManifest(
        GeneratedSet set, IReadOnlyList<ExistingItem> existing, Rarity rarity, string? recordedSha256,
        bool? recordedUnique = null, SetModels? models = null, int shape = MetadataTraits.CurrentShape)
    {
        var distribution = existing.Select(e => e.Recipe)
            .Concat(set.Assets.Select(a => a.RecipeId))
            .GroupBy(id => id, StringComparer.Ordinal)
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
        // uniqueDna is a claim about the WHOLE collection, so an extend can only ever weaken it:
        // adding unlimited rolls to a unique Set makes the result not-unique, and no later unique
        // extend can undo that. False sticks; null (an older Set that never recorded it) stays
        // unknown rather than being upgraded to a claim nobody made.
        bool? unique = recordedUnique is false ? false
            : recordedUnique is null && existing.Count > 0 ? null
            : set.UniqueDna;

        return new SetManifest(set.CollectionName, rarity.Total, set.Seed,
            recordedSha256 ?? set.CookbookSha256, GeneratorVersion, distribution, rarity.Table(),
            unique, models, set.Symbol,
            shape >= MetadataTraits.CurrentShape ? shape : null);
    }

    /// <summary>
    /// Zips the Set into a <c>.set</c> archive <em>inside</em> the output folder, beside the
    /// <c>images</c> folder it packs.
    /// </summary>
    /// <remarks>
    /// <para><b>It used to land one level UP</b> &#8212; <c>Path.TrimEndingDirectorySeparator(outDir) +
    /// ".set"</c>, a sibling of the folder. That put a file the user did not name in a folder the
    /// user did not choose: cook into <c>Documents/chests</c> and the archive appeared in
    /// <c>Documents</c>. Everything else a cook writes goes in the chosen folder, and the one output
    /// that left it was the one output most likely to be handed to somebody else.</para>
    ///
    /// <para><b>It packs what a Set IS, not what the folder HOLDS</b>, and that distinction is the
    /// whole of <see cref="SetLayout"/>. This enumerated every file under the output folder and
    /// merely excluded a top-level <c>.set</c> &#8212; which is unbounded in the wrong direction. The
    /// output folder belongs to the user: cook into a Kitchen, whose entire purpose is to hold loose
    /// <c>.cbk</c>, <c>.rcp</c> and <c>.igt</c> parts, and the archive swallowed the author's
    /// complete source, verbatim, inside the file they were about to hand a buyer. Nothing in the
    /// product said so, because the folder on disk still looked right and the reader still found
    /// <c>set.json</c> at the top.</para>
    ///
    /// <para>The old exclusion is now unreachable rather than merely redundant, so it is gone: a
    /// <c>.set</c> sits at the top level of the folder, and the top level contributes exactly one
    /// file to the include list &#8212; <c>set.json</c>, by name. The case it was written for
    /// (cooking twice into one folder, which is what extend does) is still covered, and still
    /// tested.</para>
    ///
    /// <para>Entries are added in <see cref="StringComparer.Ordinal"/> order rather than in whatever
    /// order the filesystem enumerates, so the same Set packs to the same archive on any machine
    /// &#8212; the rule every other sort that reaches an output file already follows.</para>
    /// </remarks>
    private static void Pack(string outDir)
    {
        string dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outDir));
        string archivePath = SetLayout.PackedPath(dir);

        // Taken BEFORE the archive is created, so it cannot contain itself mid-write.
        var entries = SetLayout.FilesIn(dir);

        string staging = Path.Combine(dir, ".nfty-pack-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var zip = ZipFile.Open(staging, ZipArchiveMode.Create))
                foreach (var file in entries)
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(dir, file).Replace(Path.DirectorySeparatorChar, '/'));
            File.Move(staging, archivePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
                try { File.Delete(staging); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json.Options);

    private static LayerColor ToLayerColor(ColorRoll c) => new(
        Layer: c.LayerId,
        Kind: c.Kind.ToString().ToLowerInvariant(),
        Model: c.Model?.ToString().ToLowerInvariant(),
        H: c.H is double h ? Math.Round(h, 1) : null,
        S: c.S is double s ? Math.Round(s, 3) : null,
        ExactH: c.H,
        ExactS: c.S);

    /// <summary>
    /// Observed trait frequencies across the whole collection — the items already on disk plus
    /// the batch being written. Rarity is collection-wide, so it cannot be derived from one run.
    /// </summary>
    private sealed class Rarity
    {
        private readonly Dictionary<(string TraitType, string Value), int> counts = new();
        private readonly double denominator;

        public int Total { get; }

        public Rarity(IReadOnlyList<ExistingItem> existing, GeneratedSet set, int shape)
        {
            foreach (var item in existing)
                foreach (var attr in ExistingAttributes(item, shape)) Bump(attr.Trait_type, attr.Value);

            foreach (var asset in set.Assets)
            {
                Bump(TypeTrait, asset.RecipeName);
                // The same list the metadata file publishes, so the table and the file cannot differ:
                // color traits and (from shape 2) None are counted exactly as they are written.
                foreach (var t in MetadataTraits.Attributes(asset, shape)) Bump(t.Trait_type, t.Value);
                // Shape 1 counts the absence under its old spelling, alongside the traits, so the
                // shares of one layer still sum to 100: every asset contributes exactly one outcome
                // per layer, and "not there" is one of them.
                if (shape < MetadataTraits.CurrentShape)
                    foreach (var a in asset.AbsentLayers) Bump(a.IngredientName, AbsentValue);
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
