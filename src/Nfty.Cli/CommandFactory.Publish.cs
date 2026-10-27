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

        var cmd = new Command("export",
            "Publish a cooked Set: choose what goes with it, and in what shape. A cook writes your "
            + "working copy and holds everything; an export is addressed to somebody — a "
            + "marketplace, a buyer, a collaborator, a reviewer — and each of those wants a "
            + "different subset.")
        { path, outDir, preset, images, noImages, openSea, noOpenSea, nfty, noNfty, models, noModels,
          sheet, sheetCols, sheetRows, watermark, watermarkCorner, book, folder, pack, seal, note, key };

        // Dependent options are REJECTED without what they depend on, never ignored: a --sheet-cols
        // typed without --sheet would silently make no sheet. Counts below one cannot name a grid.
        cmd.Validators.Add(result =>
        {
            bool Given(Option option) => result.GetResult(option) is { Implicit: false };
            if (!result.GetValue(sheet))
                foreach (var (option, name) in new (Option, string)[] { (sheetCols, "--sheet-cols"), (sheetRows, "--sheet-rows") })
                    if (Given(option)) result.AddError($"{name} sizes the spritesheet; it needs --sheet.");
            foreach (var (option, name) in new (Option<int>, string)[] { (sheetCols, "--sheet-cols"), (sheetRows, "--sheet-rows") })
                if (Given(option) && result.GetValue(option) < 1)
                    result.AddError($"{name} must be at least 1.");
            if (!result.GetValue(watermark) && Given(watermarkCorner))
                result.AddError("--watermark-corner places the number; it needs --watermark.");
            if (result.GetValue(models) && result.GetValue(noModels))
                result.AddError("--models and --no-models ask for different things.");
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
                    SpriteSheetColumns = parse.GetResult(sheetCols) is { Implicit: false } ? parse.GetValue(sheetCols) : null,
                    SpriteSheetRows = parse.GetResult(sheetRows) is { Implicit: false } ? parse.GetValue(sheetRows) : null,
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

            string? bookPath = parse.GetValue(book);
            if (bookPath is { Length: > 0 }) options = options with { IncludeCookBook = true };
            else if (options.IncludeCookBook) bookPath = null;   // Core reports what is missing

            // What the Set itself says, asked once: whether it has models to include, and which book
            // made it. Planned with the default options, so nothing here depends on the flags.
            if (parse.GetValue(models) || bookPath is { Length: > 0 })
            {
                var probe = SetExporter.Plan(parse.GetValue(path)!, new ExportOptions());
                if (parse.GetValue(models) && !probe.SetHasModels)
                    throw new InvalidOperationException("This Set has no 3D models; it was cooked without --models.");

                // A warning, never a refusal - the rule `extend` keeps about the same comparison.
                if (bookPath is { Length: > 0 }
                    && SetProvenance.Warning(probe.RecordedCookBookSha256, CookBookArchive.HashOf(bookPath)) is { } warning)
                    Console.Error.WriteLine(warning);
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
    private static Option<string?> KeyOption() => new("--key")
    {
        Description = $"Where the passphrase comes from: {Passphrase.Sources}. The source is NAMED "
            + "rather than guessed, the way a color spec is - they are not interchangeable, and "
            + "picking one silently would be picking your threat model for you. An environment "
            + "variable is inherited by every child process and readable from /proc on Linux; a "
            + "file can be mode 600; stdin leaves nothing anywhere. There is deliberately no "
            + "--passphrase: an argument is visible to every process on the machine, lands in your "
            + "shell history, and is captured verbatim by CI logs. Omit it and nfty asks at the "
            + "terminal, echoing nothing.",
    };

    private static StampCorner CornerOf(string slug) => slug switch
    {
        "top-left" => StampCorner.TopLeft,
        "top-right" => StampCorner.TopRight,
        "bottom-left" => StampCorner.BottomLeft,
        "bottom-right" => StampCorner.BottomRight,
        _ => throw new ArgumentOutOfRangeException(nameof(slug), slug, "Unknown corner."),
    };

    private static string Slug(ExportPreset preset) => preset.ToString().ToLowerInvariant();

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
    private static void PrintExport(ExportResult result, ExportOptions options)
    {
        Console.WriteLine($"{result.Plan.Collection} → {result.Path}");
        foreach (var part in result.Plan.Parts()) Console.WriteLine("  " + part);
        Console.WriteLine($"  {result.Plan.SizeText()}");

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
