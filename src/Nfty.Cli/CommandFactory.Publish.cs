using System.CommandLine;
using System.Globalization;
using Nfty.Core.Formats;
using Nfty.Core.Imaging;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Stats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>
    /// The `export` command: publish a cooked Set, choosing what goes with it.
    /// </summary>
    /// <returns>The command.</returns>
    public static Command Export()
    {
        var path = new Argument<string>("set")
        {
            Description = "The Set to export: the folder `generate --out` wrote, or a .set archive.",
        };
        var outDir = new Option<string>("--out", "-o")
        {
            Description = "Folder to write the export into. The export is named after the "
                + "COLLECTION, not the folder you cooked into - a working folder is called whatever "
                + "you typed into a picker, and a Set opened from a .set is read out of a temporary "
                + "one.",
            Required = true,
        };
        var preset = new Option<string?>("--preset")
        {
            Description = "Start from a named preset: "
                + string.Join("; ", Enum.GetValues<ExportPreset>()
                    .Select(p => $"{Slug(p)} — {ExportOptions.SummaryOf(p)}"))
                + " Every flag below overrides whichever preset was chosen.",
        };
        preset.AcceptOnlyFromAmong(Enum.GetValues<ExportPreset>().Select(Slug).ToArray());

        var images = new Option<bool>("--images") { Description = "Include the generated art." };
        var noImages = new Option<bool>("--no-images") { Description = "Leave the generated art out." };
        var openSea = new Option<bool>("--opensea") { Description = "Include the standard ERC-721 metadata." };
        var noOpenSea = new Option<bool>("--no-opensea") { Description = "Leave the standard metadata out." };
        var nfty = new Option<bool>("--nfty")
        {
            Description = "Include the nfty metadata: per-asset DNA, the seed, and the color rolled "
                + "on every layer.",
        };
        var noNfty = new Option<bool>("--no-nfty")
        {
            Description = "Leave the nfty metadata out. This is the file that records HOW each asset "
                + "came out the way it did.",
        };
        var models = new Option<bool>("--models")
        {
            Description = "Include the 3D models. Refused on a Set that was cooked without --models.",
        };
        var noModels = new Option<bool>("--no-models") { Description = "Leave the 3D models out." };
        var sheet = new Option<bool>("--sheet")
        {
            Description = "Also stitch every asset into one spritesheet.png, in the Set's own numbering, "
                + "carrying its grid inside the file.",
        };
        var sheetCols = new Option<int>("--sheet-cols")
        {
            Description = "Columns of the spritesheet. Needs --sheet. Rows follow unless --sheet-rows says.",
        };
        var sheetRows = new Option<int>("--sheet-rows")
        {
            Description = "Rows of the spritesheet. Needs --sheet.",
        };
        var watermark = new Option<bool>("--watermark")
        {
            Description = "Stamp each exported image with its set number (a debug mark). The cooked "
                + "Set is never touched, and models never carry it.",
        };
        var watermarkCorner = new Option<string?>("--watermark-corner")
        {
            Description = "Which corner the number goes in: top-left, top-right, bottom-left or "
                + "bottom-right (the default). Needs --watermark.",
        };
        watermarkCorner.AcceptOnlyFromAmong("top-left", "top-right", "bottom-left", "bottom-right");
        var book = new Option<string?>("--book")
        {
            Description = "Ship this .cbk alongside the Set. Whoever opens the export can then "
                + "regenerate the collection and cook a different one from the same art. A Set "
                + "records only its book's hash, never the book, so the path has to be given here.",
        };
        var folder = new Option<bool>("--folder") { Description = "Write a folder rather than one file." };
        var pack = new Option<bool>("--pack") { Description = "Write one archive. The default." };
        var seal = new Option<bool>("--seal")
        {
            Description = "Encrypt the export under a passphrase and mark it view-only. Real against "
                + "anyone WITHOUT the passphrase; against someone you gave it to, nfty declines to "
                + "export and nothing more. A lock on a door, not a wall.",
        };
        var note = new Option<string?>("--note")
        {
            Description = "A line for the recipient. On a sealed export it travels in the clear, so "
                + "they can read it without the passphrase.",
        };
        var key = KeyOption();

        // The marketplace options. All export-only: the cooked Set on disk never changes.
        var artOnly = new Option<bool>("--art-only")
        {
            Description = "Step one of a marketplace drop: export only what has to be uploaded first - "
                + "the images, and the models when the Set has them - with no metadata, collection.json "
                + "or set.json. Upload images/ (and models/), then export again with --image-base-uri.",
        };
        var imageBaseUri = new Option<string?>("--image-base-uri")
        {
            Description = "Where you uploaded the images/ folder. Each image link becomes this followed by "
                + "the file name: ipfs://<CID>/ gives ipfs://<CID>/1.png (an uploaded folder's address is "
                + "its contents, so there is no images/ in the link). Must be absolute (ipfs, https, "
                + "http or ar) and end with '/'; it is never repaired for you. Without it the links "
                + "stay relative and nfty warns that OpenSea cannot resolve them.",
        };
        var modelBaseUri = new Option<string?>("--model-base-uri")
        {
            Description = "Where you uploaded the models/ folder. Each animation_url becomes this followed "
                + "by the file name (ipfs://<CID>/1.glb). Written only for glb models that are exported. "
                + "Same rules as --image-base-uri.",
        };
        var tokenNames = new Option<string?>("--token-names")
        {
            Description = "How the exported files are named: padded (0001.json, 0001.png - the "
                + "default, what nfty has always written) or token-id (metadata/1 with no extension "
                + "and images/1.png, which is what a contract's baseURI + tokenId resolves). Also "
                + "renames the #n in each item's name and every link. nfty/ files keep the Set's names.",
        };
        tokenNames.AcceptOnlyFromAmong(TokenNamingTokens.Words.ToArray());
        var startId = new Option<int>("--start-id")
        {
            Description = "The token id of the Set's first asset: 1 (the default) or 0, as your "
                + "contract counts. Id = set number - 1 + this.",
        };
        var externalUrl = new Option<string?>("--external-url")
        {
            Description = "Give each item an external_url: this address followed by its token id.",
        };
        var collection = new Option<bool>("--collection")
        {
            Description = "Write collection.json, the contract-level metadata (OpenSea's contractURI).",
        };
        var noCollection = new Option<bool>("--no-collection") { Description = "Do not write collection.json." };
        var externalLink = new Option<string?>("--external-link")
        {
            Description = "collection.json's external_link: your collection's website. Needs collection.json.",
        };
        var royalty = new Option<string?>("--royalty")
        {
            Description = "Creator royalty as a percentage from 0 to 100 with at most 2 decimals "
                + "(2.5 becomes seller_fee_basis_points 250). Needs --fee-recipient and collection.json.",
        };
        var feeRecipient = new Option<string?>("--fee-recipient")
        {
            Description = "Who the royalty is paid to: 0x followed by 40 hexadecimal digits. Needs collection.json.",
        };
        var collectionImage = new Option<string?>("--collection-image")
        {
            Description = "collection.json's image. Defaults to the first token's image when --image-base-uri "
                + "is set and the art is included. Needs collection.json.",
        };
        var setManifest = new Option<bool>("--set-manifest")
        {
            Description = "Include set.json (the seed, the source book's hash, the rarity table).",
        };
        var noSetManifest = new Option<bool>("--no-set-manifest")
        {
            Description = "Leave set.json out, so the seed and book hash are not published. Such an "
                + "export is for upload: nfty cannot reopen it as a Set, and --seal refuses it.",
        };

        var upload = new Option<bool>("--upload")
        {
            Description = "Upload as well as export, in one step: pin images/ (and models/), write the "
                + "metadata with those addresses, pin metadata/ with collection.json beside it, then write "
                + "the export here with the same addresses. To the local IPFS node by default; --pinata for "
                + "Pinata. Prints the CIDs, the contract's baseURI and its contractURI.",
        };
        var pin = PinningOptions.Create();

        var cmd = new Command("export",
            "Publish a cooked Set: choose what goes with it, and in what shape. A cook writes your "
            + "working copy and holds everything; an export is addressed to somebody — a "
            + "marketplace, a buyer, a collaborator, a reviewer — and each of those wants a "
            + "different subset.")
        { path, outDir, preset, images, noImages, openSea, noOpenSea, nfty, noNfty, models, noModels,
          sheet, sheetCols, sheetRows, watermark, watermarkCorner, book, folder, pack, seal, note, key,
          artOnly, imageBaseUri, modelBaseUri, tokenNames, startId, externalUrl, collection, noCollection, externalLink, royalty,
          feeRecipient, collectionImage, setManifest, noSetManifest, upload };
        pin.AddTo(cmd);

        // Dependent options are REJECTED without what they depend on, never ignored: a --sheet-cols
        // typed without --sheet would silently make no sheet. Counts below one cannot name a grid.
        cmd.Validators.Add(result =>
        {
            // Conversion can be lazy. Force each typed value through the guarded boundary before
            // dependent checks read it; the parser keeps its original conversion diagnostic.
            foreach (var option in cmd.Options.OfType<Option<bool>>())
                if (!result.TryGetValue(option, out _)) return;
            foreach (var option in cmd.Options.OfType<Option<int>>())
                if (!result.TryGetValue(option, out _)) return;
            foreach (var option in cmd.Options.OfType<Option<string?>>())
                if (!result.TryGetValue(option, out _)) return;
            result.RejectUnless(result.GetValue(sheet), "sizes the spritesheet; it needs --sheet.", sheetCols, sheetRows);
            foreach (var (option, name) in new (Option<int>, string)[] { (sheetCols, "--sheet-cols"), (sheetRows, "--sheet-rows") })
                if (result.IsGiven(option) && result.GetValue(option) < 1)
                    result.AddError($"{name} must be at least 1.");
            if (!result.GetValue(watermark) && result.IsGiven(watermarkCorner))
                result.AddError("--watermark-corner places the number; it needs --watermark.");
            if (result.GetValue(artOnly))
                foreach (var (given, name) in new (bool, string)[]
                {
                    (result.GetValue(openSea), "--open-sea"), (result.GetValue(nfty), "--nfty"),
                    (result.GetValue(collection), "--collection"), (result.GetValue(setManifest), "--set-manifest"),
                    (result.GetValue(noImages), "--no-images"), (result.GetValue(noModels), "--no-models"),
                    (result.IsGiven(imageBaseUri), "--image-base-uri"), (result.IsGiven(modelBaseUri), "--model-base-uri"),
                })
                    if (given) result.AddError($"--art-only exports the art and nothing else; {name} asks for the opposite.");
            pin.Validate(result, result.GetValue(upload));
            if (result.GetValue(upload))
                foreach (var (given, name) in new (bool, string)[]
                {
                    (result.GetValue(artOnly), "--art-only"), (result.GetValue(seal), "--seal"),
                    (result.IsGiven(imageBaseUri), "--image-base-uri"), (result.IsGiven(modelBaseUri), "--model-base-uri"),
                })
                    if (given) result.AddError($"--upload uploads the art and writes its address itself; {name} cannot go with it.");
            if (result.GetValue(models) && result.GetValue(noModels))
                result.AddError("--models and --no-models ask for different things.");
            if (result.GetValue(images) && result.GetValue(noImages))
                result.AddError("--images and --no-images ask for different things.");
            if (result.GetValue(openSea) && result.GetValue(noOpenSea))
                result.AddError("--opensea and --no-opensea ask for different things.");
            if (result.GetValue(nfty) && result.GetValue(noNfty))
                result.AddError("--nfty and --no-nfty ask for different things.");
            if (result.GetValue(collection) && result.GetValue(noCollection))
                result.AddError("--collection and --no-collection ask for different things.");
            if (result.GetValue(setManifest) && result.GetValue(noSetManifest))
                result.AddError("--set-manifest and --no-set-manifest ask for different things.");
            if (result.IsGiven(startId) && MarketplaceRules.TokenStartProblem(result.GetValue(startId)) is { } startProblem)
                result.AddError("--start-id: " + startProblem);
            // Each value is refused with the sentence the export dialog shows beside the field.
            foreach (var problem in new[]
            {
                MarketplaceRules.BaseUriProblem(result.GetValue(imageBaseUri), "--image-base-uri"),
                MarketplaceRules.BaseUriProblem(result.GetValue(modelBaseUri), "--model-base-uri"),
                MarketplaceRules.WebUrlProblem(result.GetValue(externalUrl), "--external-url"),
                MarketplaceRules.WebUrlProblem(result.GetValue(externalLink), "--external-link"),
                MarketplaceRules.ImageUriProblem(result.GetValue(collectionImage)),
                MarketplaceRules.FeeRecipientProblem(result.GetValue(feeRecipient)),
                MarketplaceRules.TryParseRoyalty(result.GetValue(royalty), out _),
            })
                if (problem is not null) result.AddError(problem);
        });

        cmd.SetAction(parse =>
        {
            if (parse.GetValue(folder) && parse.GetValue(pack))
                throw new InvalidOperationException("--folder and --pack ask for different things.");

            var options = parse.GetValue(preset) is { } chosen
                ? ExportOptions.For(Parse(chosen))
                : new ExportOptions();

            // Each flag overrides the preset it started from. Both halves of every pair exist so a
            // preset can be turned OFF as well as on: with only `--nfty`, there would be no way to
            // drop the nfty metadata from the asset-pack preset without rebuilding the whole
            // command line out of individual flags.
            if (parse.GetValue(images)) options = options with { Images = true };
            if (parse.GetValue(noImages)) options = options with { Images = false };
            if (parse.GetValue(openSea)) options = options with { OpenSeaMetadata = true };
            if (parse.GetValue(noOpenSea)) options = options with { OpenSeaMetadata = false };
            if (parse.GetValue(nfty)) options = options with { NftyMetadata = true };
            if (parse.GetValue(noNfty)) options = options with { NftyMetadata = false };
            if (parse.GetValue(models)) options = options with { Models = true };
            if (parse.GetValue(noModels)) options = options with { Models = false };
            if (parse.GetValue(sheet))
                options = options with
                {
                    SpriteSheet = true,
                    SpriteSheetColumns = parse.IsGiven(sheetCols) ? parse.GetValue(sheetCols) : null,
                    SpriteSheetRows = parse.IsGiven(sheetRows) ? parse.GetValue(sheetRows) : null,
                };
            if (parse.GetValue(watermark))
                options = options with
                {
                    NumberWatermark = true,
                    WatermarkCorner = parse.GetValue(watermarkCorner) is { } corner ? CornerOf(corner) : StampCorner.BottomRight,
                };
            if (parse.GetValue(folder)) options = options with { Shape = ExportShape.Folder };
            if (parse.GetValue(pack)) options = options with { Shape = ExportShape.Archive };
            if (parse.GetValue(seal)) options = options with { Sealed = true };
            if (parse.GetValue(note) is { Length: > 0 } text) options = options with { Note = text };
            if (parse.IsGiven(key) && !options.Sealed)
                throw new InvalidOperationException("--key supplies a sealing passphrase; it needs --seal or a sealed preset.");

            if (parse.GetValue(collection)) options = options with { IncludeCollectionMetadata = true };
            if (parse.GetValue(noCollection)) options = options with { IncludeCollectionMetadata = false };
            if (parse.GetValue(setManifest)) options = options with { IncludeSetManifest = true };
            if (parse.GetValue(noSetManifest)) options = options with { IncludeSetManifest = false };
            // Both callers ask only about the source, using identical default options. Keep the
            // probe lazy so unrelated options retain their existing error and warning timing.
            ExportPlan? defaultPlan = null;
            ExportPlan DefaultPlan() => defaultPlan ??= SetExporter.Plan(parse.GetValue(path)!, new ExportOptions());
            if (parse.GetValue(artOnly))
            {
                // The models go with the images when the Set has any: both are uploaded before the
                // metadata can name them. Asked of the Set itself, so --art-only never needs --models.
                bool hasModels = DefaultPlan().SetHasModels;
                options = options with
                {
                    Images = true,
                    Models = hasModels,
                    OpenSeaMetadata = false,
                    NftyMetadata = false,
                    IncludeCollectionMetadata = false,
                    IncludeSetManifest = false,
                };
            }

            // Marketplace values are REJECTED where nothing would read them, never ignored: a
            // --image-base-uri typed against an export with no metadata would quietly do nothing.
            void Needs(bool meaningful, string why, params Option[] given)
            {
                foreach (var option in given)
                    if (parse.IsGiven(option) && !meaningful)
                        throw new InvalidOperationException($"{option.Name} {why}");
            }
            Needs(options.OpenSeaMetadata || options.IncludeCollectionMetadata,
                "is written into the standard metadata and collection.json, and this export has neither.", imageBaseUri);
            Needs(options.OpenSeaMetadata && options.Models,
                "is written into the metadata's animation_url, and this export has no metadata or no models.", modelBaseUri);
            Needs(options.OpenSeaMetadata, "is written into the standard metadata, and this export has none.",
                externalUrl);
            Needs(options.OpenSeaMetadata || options.Images || options.Models,
                "names the metadata, art and models, and this export has none of them.", tokenNames, startId);
            Needs(options.IncludeCollectionMetadata, "is written into collection.json; it needs --collection.",
                externalLink, royalty, feeRecipient, collectionImage);

            MarketplaceRules.TryParseRoyalty(parse.GetValue(royalty), out double? royaltyPercent);
            options = options with
            {
                ImageBaseUri = parse.GetValue(imageBaseUri) is { Length: > 0 } uri ? uri : options.ImageBaseUri,
                ModelBaseUri = parse.GetValue(modelBaseUri) is { Length: > 0 } muri ? muri : options.ModelBaseUri,
                TokenNaming = parse.GetValue(tokenNames) is { } naming
                    && TokenNamingTokens.TryParse(naming, out var chosenNaming) ? chosenNaming : options.TokenNaming,
                TokenStart = parse.IsGiven(startId) ? parse.GetValue(startId) : options.TokenStart,
                ExternalUrlBase = parse.GetValue(externalUrl) is { Length: > 0 } ext ? ext : options.ExternalUrlBase,
                ExternalLink = parse.GetValue(externalLink) is { Length: > 0 } link ? link : options.ExternalLink,
                RoyaltyPercent = royaltyPercent ?? options.RoyaltyPercent,
                FeeRecipient = parse.GetValue(feeRecipient) is { Length: > 0 } fee ? fee : options.FeeRecipient,
                CollectionImage = parse.GetValue(collectionImage) is { Length: > 0 } img ? img : options.CollectionImage,
            };

            string? bookPath = parse.GetValue(book);
            if (bookPath is { Length: > 0 }) options = options with { IncludeCookBook = true };
            else if (options.IncludeCookBook) bookPath = null;   // Core reports what is missing

            // What the Set itself says, asked once: whether it has models to include, and which book
            // made it. Planned with the default options, so nothing here depends on the flags.
            if (parse.GetValue(models) || parse.IsGiven(modelBaseUri) || bookPath is { Length: > 0 })
            {
                var probe = DefaultPlan();
                if ((parse.GetValue(models) || parse.IsGiven(modelBaseUri)) && !probe.SetHasModels)
                    throw new InvalidOperationException("This Set has no 3D models; it was cooked without --models.");

                // A warning, never a refusal - the rule `extend` keeps about the same comparison.
                if (bookPath is { Length: > 0 }
                    && SetProvenance.Warning(probe.RecordedCookBookSha256, CookBookArchive.HashOf(bookPath)) is { } warning)
                    Console.Error.WriteLine(warning);
            }

            if (parse.GetValue(upload))
            {
                RunUpload(parse, pin, parse.GetValue(path)!, parse.GetValue(outDir)!, options, bookPath);
                return 0;
            }

            string? passphrase = options.Sealed
                ? Passphrase.Read(parse.GetValue(key), "Passphrase to seal with", confirm: true)
                : null;

            var result = SetExporter.Export(parse.GetValue(path)!, parse.GetValue(outDir)!,
                options, bookPath, passphrase, parallelism: ResolveThreads(parse));

            PrintExport(result, options);
            return 0;
        });
        return cmd;
    }

    /// <summary>The option both <c>export</c> and <c>inspect</c> take a passphrase through.
    /// One declaration, so the two cannot describe it differently.</summary>
    /// <returns>The option that names the passphrase source.</returns>
    private static Option<string?> KeyOption() => new("--key")
    {
        Description = $"Where the passphrase comes from: {Passphrase.Sources}. The source is NAMED "
            + "rather than guessed, the way a color spec is - they are not interchangeable, and "
            + "picking one silently would be picking your threat model for you. An environment "
            + "variable is normally inherited by child processes and can be readable from /proc on Linux; a "
            + "file can be mode 600; stdin leaves nothing anywhere. There is deliberately no "
            + "--passphrase: an argument can be visible to processes with sufficient permissions, land in "
            + "shell history, and enter CI logs. Omit it and nfty asks at the "
            + "terminal, echoing nothing.",
    };

    /// <summary>Maps a validated corner token to the image-stamp corner.</summary>
    /// <param name="slug">One of the four accepted corner tokens.</param>
    /// <returns>The matching corner.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The token is unknown.</exception>
    private static StampCorner CornerOf(string slug) => slug switch
    {
        "top-left" => StampCorner.TopLeft,
        "top-right" => StampCorner.TopRight,
        "bottom-left" => StampCorner.BottomLeft,
        "bottom-right" => StampCorner.BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(slug), slug, "Unknown corner."),
    };

    /// <summary>Names a preset using its invariant command-line token.</summary>
    /// <param name="preset">The preset.</param>
    /// <returns>The lowercase token.</returns>
    private static string Slug(ExportPreset preset) => preset.ToString().ToLowerInvariant();

    /// <summary>Resolves a preset token already accepted by the parser.</summary>
    /// <param name="slug">The validated token.</param>
    /// <returns>The named preset.</returns>
    private static ExportPreset Parse(string slug) =>
        Enum.GetValues<ExportPreset>().First(p => Slug(p) == slug);

    /// <summary>
    /// What was written, as a list of what is inside it.
    /// </summary>
    /// <remarks>
    /// Named rather than counted, because the whole point of this command is that the author knows
    /// what leaves the machine. "Exported 1,502 files" is exactly the sentence that let a packed Set
    /// carry an author's source book for three releases without anyone noticing.
    /// </remarks>
    /// <param name="result">The completed export and its final contents.</param>
    /// <param name="options">The options, used to explain sealing.</param>
    private static void PrintExport(ExportResult result, ExportOptions options)
    {
        Console.WriteLine($"{result.Plan.Collection} → {result.Path}");
        foreach (var part in result.Plan.Parts()) Console.WriteLine("  " + part);
        Console.WriteLine($"  {result.Plan.SizeText()}");

        // On stderr so a script reading the list above is not handed prose, and a person still sees it.
        foreach (var warning in result.Plan.Warnings) Console.Error.WriteLine("warning: " + warning);

        if (result.Plan.CarriesCookBook)
            Console.WriteLine(
                "  note: this export carries the source CookBook. Anyone who opens it can "
                + "regenerate the collection and cook a different one from your art.");

        if (options.Sealed)
            Console.WriteLine(
                "  note: sealed. Without the passphrase this file cannot be read. With it, nfty "
                + "declines to export the assets - which is a refusal in this program, not a law "
                + "of arithmetic. Send the passphrase by a different route than the file.");
    }

    /// <summary>
    /// Prints a sealed export: its header always, and the Set inside it when a passphrase opens it.
    /// </summary>
    /// <remarks>The cleartext header contains unverified claims until authenticated opening
    /// succeeds. With no key source this prints those claims only; opening owns and disposes the
    /// decrypted Set after rendering its report.</remarks>
    /// <param name="file">The <c>.tin</c>.</param>
    /// <param name="keySpec">The source named by <c>--key</c>, or null when none was given.</param>
    private static void PrintSealed(string file, string? keySpec)
    {
        var header = Seal.Peek(file);
        Console.WriteLine($"Sealed export: {header.Collection}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Assets: {header.Count}"));
        if (header.Note is { Length: > 0 } note) Console.WriteLine($"  Note: {note}");
        Console.WriteLine("  Export allowed: " + (header.Policy.AllowExport ? "yes" : "no"));
        Console.WriteLine("  Edit allowed: " + (header.Policy.AllowEdit ? "yes" : "no"));

        if (keySpec is null)
        {
            // The header exists so this sentence can be printed. A recipient holding a file they
            // cannot open needs to be told what it is, or the only thing the format communicates is
            // that something went wrong.
            Console.WriteLine();
            Console.WriteLine($"This export is encrypted. Pass --key ({Passphrase.Sources}) to "
                + "read what is inside it.");
            return;
        }

        using var set = SealedSetReader.Open(file, Passphrase.Read(keySpec, "Passphrase"));
        Console.WriteLine();
        Console.Write(SetReport.Render(set));
    }
}
