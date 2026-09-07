using System.CommandLine;
using Nfty.Core.Publish;

namespace Nfty.Cli.Tests;

/// <summary>
/// That the <c>export</c> surface parses. What it actually writes is covered in
/// <c>Nfty.Core.Tests.SetExporterTests</c>, per the split this repo keeps: this project proves the
/// command line exists and accepts what the manual says it does.
/// </summary>
public class ExportCommandTests
{
    private static ParseResult Parse(string line) => CommandFactory.Build().Parse(line);

    [Fact]
    public void Export_is_a_subcommand()
    {
        Assert.Contains("export", CommandFactory.Build().Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void Out_is_required_because_an_export_has_to_land_somewhere()
    {
        Assert.NotEmpty(Parse("export ./collection").Errors);
        Assert.Empty(Parse("export ./collection --out ./dist").Errors);
        Assert.Empty(Parse("export ./collection -o ./dist").Errors);
    }

    [Theory]
    [InlineData("marketplace")]
    [InlineData("assetpack")]
    [InlineData("fullproject")]
    [InlineData("sealedcritique")]
    public void Every_preset_the_engine_defines_is_nameable_on_the_command_line(string slug)
    {
        // The slugs are derived from ExportPreset rather than listed in the command, so a preset
        // added to the engine is accepted here for free. This theory is the other half of that: it
        // fails if a slug ever stops matching, which is what a user would type.
        Assert.Empty(Parse($"export ./c --out ./d --preset {slug}").Errors);
    }

    [Fact]
    public void The_theory_above_names_every_preset_there_is()
    {
        // Without this, adding a fifth preset would leave it silently untested - the theory would go
        // on passing about the four it happens to list.
        Assert.Equal(4, Enum.GetValues<ExportPreset>().Length);
    }

    [Fact]
    public void An_unknown_preset_is_a_parse_error_rather_than_a_silent_default()
    {
        var result = Parse("export ./c --out ./d --preset everything");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Both_halves_of_every_content_switch_exist()
    {
        // A preset has to be turnable OFF as well as on: with only --nfty there would be no way to
        // drop the nfty metadata from the asset-pack preset short of rebuilding the whole command
        // line out of individual flags.
        foreach (var flag in new[] { "images", "opensea", "nfty" })
        {
            Assert.Empty(Parse($"export ./c --out ./d --{flag}").Errors);
            Assert.Empty(Parse($"export ./c --out ./d --no-{flag}").Errors);
        }
    }

    [Fact]
    public void Shape_book_seal_and_note_all_parse()
    {
        Assert.Empty(Parse("export ./c --out ./d --folder").Errors);
        Assert.Empty(Parse("export ./c --out ./d --pack").Errors);
        Assert.Empty(Parse("export ./c --out ./d --book ./Book.cbk").Errors);
        Assert.Empty(Parse("export ./c --out ./d --seal --note \"for review\"").Errors);
    }

    [Fact]
    public void There_is_no_passphrase_option_on_any_command()
    {
        // Deliberate, and worth a test rather than a comment: an argument on a command line is
        // visible to every process on the machine while it runs, lands in shell history, and is
        // captured verbatim by CI logs. Offering the flag would mean most people used it, because
        // the convenient path is the one that gets taken. --key-env is the way in.
        Assert.NotEmpty(Parse("export ./c --out ./d --seal --passphrase hunter2hunter2").Errors);
        Assert.NotEmpty(Parse("inspect ./c.tin --passphrase hunter2hunter2").Errors);

        Assert.Empty(Parse("export ./c --out ./d --seal --key-env NFTY_KEY").Errors);
        Assert.Empty(Parse("inspect ./c.tin --key-env NFTY_KEY").Errors);
    }

    [Fact]
    public void Inspect_takes_a_key_so_a_sealed_export_can_be_opened()
    {
        Assert.Empty(Parse("inspect ./c.tin --key").Errors);
    }
}
