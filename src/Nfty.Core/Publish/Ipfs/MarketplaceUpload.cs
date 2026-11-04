namespace Nfty.Core.Publish.Ipfs;

/// <summary>Which step of an upload is running.</summary>
public enum UploadStage
{
    /// <summary>Asking the service whether it can be reached.</summary>
    Checking,

    /// <summary>Exporting the art (and models) to a temporary folder.</summary>
    ExportingArt,

    /// <summary>Uploading <c>images/</c>.</summary>
    PinningImages,

    /// <summary>Uploading <c>models/</c>.</summary>
    PinningModels,

    /// <summary>Writing the metadata with the art's real addresses.</summary>
    ExportingMetadata,

    /// <summary>Uploading <c>metadata/</c> (and <c>collection.json</c>).</summary>
    PinningMetadata,

    /// <summary>Writing the export to the chosen folder.</summary>
    WritingExport,

    /// <summary>Finished.</summary>
    Done,
}

/// <summary>How far an upload has got.</summary>
/// <param name="Stage">Which step.</param>
/// <param name="Fraction">The whole run, 0..1.</param>
/// <param name="Phase">What is happening, as a person would say it.</param>
public sealed record UploadProgress(UploadStage Stage, double Fraction, string Phase);

/// <summary>What an upload pinned, and the addresses a contract needs.</summary>
/// <param name="Provider">Who pinned it.</param>
/// <param name="ImageCid">The CID of the <c>images/</c> folder's contents.</param>
/// <param name="ModelCid">The CID of <c>models/</c>, or null when no models were uploaded.</param>
/// <param name="MetadataCid">The CID of <c>metadata/</c> (with <c>collection.json</c> beside the token files when written).</param>
/// <param name="BaseUri">The contract's <c>baseURI</c>: <c>ipfs://&lt;metadata CID&gt;/</c>.</param>
/// <param name="ContractUri">The contract's <c>contractURI</c>, or null when no <c>collection.json</c> was written.</param>
/// <param name="Local">The export written to the chosen folder, carrying the same addresses.</param>
/// <param name="Warnings">What about the result is worth stopping for, in Core's words.</param>
public sealed record UploadResult(
    string Provider,
    string ImageCid,
    string? ModelCid,
    string MetadataCid,
    string BaseUri,
    string? ContractUri,
    ExportResult Local,
    IReadOnlyList<string> Warnings);

/// <summary>
/// The whole marketplace upload in one call: art first, then metadata that points at it.
/// </summary>
/// <remarks>
/// <para><b>The order is forced by the links.</b> The metadata carries the art's address, so the art
/// is uploaded first, its CID becomes <see cref="ExportOptions.ImageBaseUri"/> (and the models'
/// <see cref="ExportOptions.ModelBaseUri"/>), and only then is the metadata written and uploaded.
/// This is the two-step "Art only" path the export dialog describes, done for the author.</para>
/// <para><b>One export path, three calls.</b> Every file is written by <see cref="SetExporter"/>:
/// the art to a temporary folder, the metadata to another, and finally the whole export to the
/// author's folder with the real addresses - so the local copy of every pinned folder is
/// byte-for-byte what was pinned.</para>
/// <para><b><c>collection.json</c> is pinned beside the token files</b>, so
/// <c>contractURI = ipfs://&lt;metadata CID&gt;/collection.json</c>; a contract's
/// <c>baseURI + tokenId</c> never asks for that name. In the local export it stays at the root,
/// where every export puts it.</para>
/// <para><b>Refused up front</b> when the service cannot be reached, before anything is written.</para>
/// </remarks>
public static class MarketplaceUpload
{
    /// <summary>Why these options cannot be uploaded, or null when they can. One sentence, shared by
    /// the CLI and the export dialog.</summary>
    /// <param name="options">What the export would contain.</param>
    /// <returns>The sentence, or null.</returns>
    public static string? Problem(ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Sealed)
            return "A sealed export cannot be uploaded: it is made to be opened in nfty, not published.";
        if (!options.Images)
            return "An upload needs the art: turn on Generated art.";
        if (!options.OpenSeaMetadata)
            return "An upload needs the marketplace metadata: it is what the contract points at.";
        if (options.NumberWatermark)
            return "Numbered art is a debug copy, not a mintable one: turn the debug mark off to upload.";
        return null;
    }

    /// <summary>Uploads a Set's art and metadata and writes the export with the real addresses.</summary>
    /// <param name="setPath">The Set: a folder or a <c>.set</c>.</param>
    /// <param name="outDir">Where the final export is written.</param>
    /// <param name="options">What to export; the base URIs are replaced by the uploaded ones.</param>
    /// <param name="pinning">Where to upload.</param>
    /// <param name="cookBookPath">The <c>.cbk</c> to ship in the local export, when the options include it.</param>
    /// <param name="progress">How far the run has got.</param>
    /// <param name="cancellationToken">Stops the run. Folders a service already pinned stay pinned.</param>
    /// <param name="parallelism">Passed to every export.</param>
    /// <returns>The CIDs, the contract addresses and the local export.</returns>
    /// <exception cref="ArgumentException">The options cannot be uploaded (<see cref="Problem"/>).</exception>
    /// <exception cref="PinningException">The service cannot be reached, or refused an upload.</exception>
    public static async Task<UploadResult> RunAsync(string setPath, string outDir, ExportOptions options,
        IPinningService pinning, string? cookBookPath = null, IProgress<UploadProgress>? progress = null,
        CancellationToken cancellationToken = default, Parallelism? parallelism = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outDir);
        ArgumentNullException.ThrowIfNull(pinning);
        if (Problem(options) is { } problem) throw new ArgumentException(problem, nameof(options));

        void Say(UploadStage stage, double fraction, string phase) =>
            progress?.Report(new UploadProgress(stage, Math.Clamp(fraction, 0, 1), phase));

        Say(UploadStage.Checking, 0, $"Checking {pinning.Provider}...");
        var status = await pinning.StatusAsync(cancellationToken).ConfigureAwait(false);
        if (!status.Reachable) throw new PinningException(status.Detail);

        var plan = SetExporter.Plan(setPath, options with { Shape = ExportShape.Folder }, cookBookPath);
        bool models = options.Models && plan.SetHasModels;
        string label = plan.OutputName;

        string temp = Directory.CreateTempSubdirectory("nfty-upload-").FullName;
        // Every CID pinned so far. A stopped or failed upload unpins them, so a half drop does not
        // stay on the service (or on the author's Pinata bill) with no export naming it.
        var pinned = new List<string>();
        try
        {
            // 1. The art, alone - named exactly as the final export will name it.
            Say(UploadStage.ExportingArt, 0.02, "Exporting the art...");
            var art = options with
            {
                Images = true,
                Models = models,
                OpenSeaMetadata = false,
                NftyMetadata = false,
                IncludeCollectionMetadata = false,
                IncludeSetManifest = false,
                IncludeCookBook = false,
                SpriteSheet = false,
                Shape = ExportShape.Folder,
                ImageBaseUri = null,
                ModelBaseUri = null,
            };
            var artResult = await SetExporter.ExportAsync(setPath, Path.Combine(temp, "art"), art,
                cancellationToken: cancellationToken, parallelism: parallelism).ConfigureAwait(false);

            // 2. Pin the art.
            string imageCid = await PinAsync(pinning, Path.Combine(artResult.Path, "images"), label + "-images",
                UploadStage.PinningImages, 0.05, models ? 0.45 : 0.75, "Uploading images", progress, cancellationToken)
                .ConfigureAwait(false);
            pinned.Add(imageCid);
            string? modelCid = null;
            string modelDir = Path.Combine(artResult.Path, "models");
            if (models && Directory.Exists(modelDir) && Directory.EnumerateFiles(modelDir).Any())
                modelCid = await PinAsync(pinning, modelDir, label + "-models",
                    UploadStage.PinningModels, 0.45, 0.75, "Uploading models", progress, cancellationToken)
                    .ConfigureAwait(false);
            if (modelCid is not null) pinned.Add(modelCid);

            // 3. The metadata, now that it can name the art.
            Say(UploadStage.ExportingMetadata, 0.76, "Writing the metadata...");
            var final = options with
            {
                ImageBaseUri = $"ipfs://{imageCid}/",
                ModelBaseUri = modelCid is null ? null : $"ipfs://{modelCid}/",
            };
            var metaResult = await SetExporter.ExportAsync(setPath, Path.Combine(temp, "meta"),
                final with
                {
                    // The art and models stay as the final export has them, though only metadata/ is
                    // pinned from here: collection.json takes its image from the first exported
                    // image, and animation_url is written only for models that ship - dropping
                    // either would write different files than the export the author keeps.
                    Images = true,
                    Models = models,
                    NftyMetadata = false,
                    IncludeSetManifest = false,
                    IncludeCookBook = false,
                    SpriteSheet = false,
                    Shape = ExportShape.Folder,
                },
                cancellationToken: cancellationToken, parallelism: parallelism).ConfigureAwait(false);

            string stage = Path.Combine(temp, "pin-metadata");
            CopyFolder(Path.Combine(metaResult.Path, "metadata"), stage);
            string collection = Path.Combine(metaResult.Path, MarketplaceFiles.CollectionName);
            bool hasCollection = File.Exists(collection);
            if (hasCollection) File.Copy(collection, Path.Combine(stage, MarketplaceFiles.CollectionName));

            // 4. Pin the metadata.
            string metadataCid = await PinAsync(pinning, stage, label + "-metadata",
                UploadStage.PinningMetadata, 0.78, 0.92, "Uploading metadata", progress, cancellationToken)
                .ConfigureAwait(false);
            pinned.Add(metadataCid);

            // 5. The author's own copy, carrying the same addresses.
            Say(UploadStage.WritingExport, 0.93, "Writing the export...");
            var local = await SetExporter.ExportAsync(setPath, outDir, final, cookBookPath,
                cancellationToken: cancellationToken, parallelism: parallelism).ConfigureAwait(false);

            var warnings = new List<string>();
            if (options.TokenNaming == TokenNaming.Padded)
                warnings.Add("The files keep padded names (0001.json): a contract's baseURI + tokenId "
                    + "will not find them. Choose token-id names for a contract drop.");
            if (!hasCollection)
                warnings.Add("No collection.json was written, so there is no contractURI.");

            Say(UploadStage.Done, 1, "Done");
            return new UploadResult(pinning.Provider, imageCid, modelCid, metadataCid,
                $"ipfs://{metadataCid}/",
                hasCollection ? $"ipfs://{metadataCid}/{MarketplaceFiles.CollectionName}" : null,
                local, warnings);
        }
        catch
        {
            await UnpinAsync(pinning, pinned).ConfigureAwait(false);
            throw;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Takes back what a stopped or failed upload pinned, ignoring the caller's (already
    /// canceled) token but bounded so a dead service cannot hold the error up for long.</summary>
    private static async Task UnpinAsync(IPinningService pinning, List<string> pinned)
    {
        if (pinned.Count == 0) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var cid in pinned)
        {
            try { await pinning.RemoveAsync(cid, timeout.Token).ConfigureAwait(false); }
            catch { /* best effort: the upload's own failure is what the caller reports */ }
        }
    }

    private static async Task<string> PinAsync(IPinningService pinning, string folder, string label,
        UploadStage stage, double from, double to, string phase, IProgress<UploadProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new UploadProgress(stage, from, phase + "..."));
        var bytes = progress is null ? null : new SyncProgress<PinProgress>(p =>
            progress.Report(new UploadProgress(stage, from + (to - from) * p.Fraction,
                $"{phase}... {SizeText.Of(p.BytesSent)} of {SizeText.Of(p.BytesTotal)}")));
        return await pinning.AddFolderAsync(folder, label, bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    /// <summary>Reports on the caller's thread, in order: <see cref="Progress{T}"/> would post.</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
