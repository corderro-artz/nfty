using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli.Tests;

public class CliPublishingAuditTests
{
    [Theory]
    [InlineData("--image-base-uri")]
    [InlineData("--model-base-uri")]
    [InlineData("--external-url")]
    [InlineData("--external-link")]
    [InlineData("--royalty")]
    [InlineData("--fee-recipient")]
    [InlineData("--collection-image")]
    [InlineData("--api")]
    public void Missing_text_values_remain_parser_errors(string option)
    {
        string[] args = option == "--api"
            ? ["ipfs", "status", option]
            : ["export", "missing-set", "--out", "unused", option];
        var parse = CommandFactory.Build().Parse(args);
        Assert.Contains(parse.Errors, e => e.Message.Contains(option, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--sheet-cols", "abc")]
    [InlineData("--sheet-rows", "2147483648")]
    [InlineData("--start-id", "abc")]
    [InlineData("--start-id", "2147483648")]
    [InlineData("--sheet-cols", null)]
    [InlineData("--start-id", null)]
    public void Malformed_export_numbers_remain_parser_errors(string option, string? value)
    {
        var args = new List<string> { "export", "missing-set", "--out", "unused", "--sheet", option };
        if (value is not null) args.Add(value);
        var parse = CommandFactory.Build().Parse(args.ToArray());
        Assert.Contains(parse.Errors, e => e.Message.Contains(option, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("images", false)]
    [InlineData("images", true)]
    [InlineData("opensea", false)]
    [InlineData("opensea", true)]
    [InlineData("nfty", false)]
    [InlineData("nfty", true)]
    public void Opposite_content_switches_are_refused_before_export(string axis, bool reversed)
    {
        using var input = new Input();
        string positive = "--" + axis, negative = "--no-" + axis;
        var (code, _, err) = ProgramTests.Run("export", input.Set, "--out", input.Out,
            reversed ? negative : positive, reversed ? positive : negative);
        Assert.Equal(1, code);
        Assert.Contains(positive, err, StringComparison.Ordinal);
        Assert.Contains(negative, err, StringComparison.Ordinal);
        Assert.False(Directory.Exists(input.Out));
    }

    [Fact]
    public void A_key_source_cannot_be_ignored_by_an_open_export()
    {
        using var input = new Input();
        var (code, _, err) = ProgramTests.Run("export", input.Set, "--out", input.Out,
            "--key", "env:NFTY_UNUSED_AUDIT_KEY_7f2");
        Assert.Equal(1, code);
        Assert.Contains("--key", err, StringComparison.Ordinal);
        Assert.Contains("seal", err, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(input.Out));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_and_preset_sealing_still_read_the_key_and_authenticate(bool preset)
    {
        using var input = new Input();
        const string passphrase = "audit-only-passphrase-7f2";
        string key = Path.Combine(input.Root, "key.txt");
        File.WriteAllText(key, passphrase + "\r\nignored second line");
        var args = new List<string> { "export", input.Set, "--out", input.Out, "--key", "file:" + key };
        if (preset) args.AddRange(["--preset", "sealedcritique"]);
        else args.Add("--seal");
        var (code, _, err) = ProgramTests.Run(args.ToArray());
        Assert.True(code == 0, err);
        using var opened = SealedSetReader.Open(Directory.GetFiles(input.Out, "*.tin").Single(), passphrase);
        Assert.Single(opened.Items);
        Assert.Equal("audit-seed", opened.Manifest.Seed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Unknown_secret_sources_are_not_repeated_in_process_errors(bool pinata, bool verbose)
    {
        using var input = new Input();
        const string marker = "AUDIT_SYNTHETIC_SECRET_7f2";
        string option = pinata ? "--pinata-key" : "--key";
        var args = pinata
            ? new List<string> { "ipfs", "status", "--pinata", option, marker }
            : new List<string> { "export", input.Set, "--out", input.Out, "--seal", option, marker };
        if (verbose) args.Add("--verbose");
        var (code, stdout, stderr) = ProgramTests.Run(args.ToArray());
        Assert.Equal(1, code);
        Assert.DoesNotContain(marker, stdout + stderr, StringComparison.Ordinal);
        Assert.Contains(option, stderr, StringComparison.Ordinal);
        Assert.Contains("env:NAME", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(input.Out));
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Key_files_keep_first_line_spaces_and_detect_a_utf8_bom(string ending)
    {
        using var input = new Input();
        string key = Path.Combine(input.Root, "key.txt");
        File.WriteAllText(key, "  audit secret  " + ending + "ignored", new System.Text.UTF8Encoding(true));
        Assert.Equal("  audit secret  ", Passphrase.Read("file:" + key, "unused"));
    }

    private sealed class Input : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("nfty-publishing-audit-").FullName;
        public string Set => Path.Combine(Root, "set");
        public string Out => Path.Combine(Root, "out");

        public Input()
        {
            using var generated = new GeneratedSet("Audit", "d", "A", "audit-seed", [new GeneratedAsset
            {
                SetNumber = 1, Dna = "dna1", RecipeId = "cat", RecipeName = "Cat",
                Image = new Image<Rgba32>(2, 2, new Rgba32(30, 60, 90, 255)),
                Traits = [new TraitSelection("bg", "Background", "v", "Solid")],
                ColorRolls = [new ColorRoll("bg", LayerKind.Custom, null, null, null)],
            }]);
            SetWriter.Write(generated, Set, pack: false);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
