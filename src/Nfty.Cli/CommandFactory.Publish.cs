using System.CommandLine;
using System.Globalization;
using Nfty.Core.Formats;
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
            Description = "Folder to write the export into. The export is named after the Set.",
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
        var keyEnv = KeyEnvOption();

        var cmd = new Command("export",
            "Publish a cooked Set: choose what goes with it, and in what shape. A cook writes your "
            + "working copy and holds everything; an export is addressed to somebody — a "
            + "marketplace, a buyer, a collaborator, a reviewer — and each of those wants a "
            + "different subset.")
        { path, outDir, preset, images, noImages, openSea, noOpenSea, nfty, noNfty, book, folder, pack, seal, note, keyEnv };

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
            if (parse.GetValue(folder)) options = options with { Shape = ExportShape.Folder };
            if (parse.GetValue(pack)) options = options with { Shape = ExportShape.Archive };
            if (parse.GetValue(seal)) options = options with { Sealed = true };
            if (parse.GetValue(note) is { Length: > 0 } text) options = options with { Note = text };

            string? bookPath = parse.GetValue(book);
            if (bookPath is { Length: > 0 }) options = options with { IncludeCookBook = true };
            else if (options.IncludeCookBook) bookPath = null;   // Core reports what is missing

            string? passphrase = options.Sealed
                ? Passphrase.Read(parse.GetValue(keyEnv), "Passphrase to seal with", confirm: true)
                : null;

            var result = SetExporter.Export(parse.GetValue(path)!, parse.GetValue(outDir)!,
                options, bookPath, passphrase);

            PrintExport(result, options);
            return 0;
        });
        return cmd;
    }

    /// <summary>The option both <c>export</c> and <c>inspect</c> take a passphrase through.
    /// One declaration, so the two cannot describe it differently.</summary>
    private static Option<string?> KeyEnvOption() => new("--key-env")
    {
        Description = "Read the passphrase from this environment variable instead of asking for it. "
            + "There is deliberately no --passphrase: an argument on a command line is visible to "
            + "every process on the machine and lands in your shell history.",
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
    /// <param name="envName">The variable named by <c>--key-env</c>, or null.</param>
    /// <param name="hasKey">Whether the caller asked to open it at all.</param>
    private static void PrintSealed(string file, string? envName, bool hasKey)
    {
        var header = Seal.Peek(file);
        Console.WriteLine($"Sealed export: {header.Collection}");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Assets: {header.Count}"));
        if (header.Note is { Length: > 0 } note) Console.WriteLine($"  Note: {note}");
        Console.WriteLine("  Export allowed: " + (header.Policy.AllowExport ? "yes" : "no"));
        Console.WriteLine("  Edit allowed: " + (header.Policy.AllowEdit ? "yes" : "no"));

        if (!hasKey)
        {
            // The header exists so this sentence can be printed. A recipient holding a file they
            // cannot open needs to be told what it is, or the only thing the format communicates is
            // that something went wrong.
            Console.WriteLine();
            Console.WriteLine("This export is encrypted. Pass --key to read what is inside it, or "
                + "--key-env to name an environment variable holding the passphrase.");
            return;
        }

        using var set = SealedSetReader.Open(file,
            Passphrase.Read(envName, "Passphrase"));
        Console.WriteLine();
        Console.Write(SetReport.Render(set));
    }
}
