using System.CommandLine;
using System.Text.Json;
using Nfty.Core.Formats;
using Nfty.Core.Publish.Community;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>Creates the <c>share</c> group: what the community gallery asks of a Set.</summary>
    /// <returns>The group command.</returns>
    private static Command ShareGroup()
    {
        var group = new Command("share", "Check a cooked Set against the community gallery's rules.");
        group.Subcommands.Add(ShareCheckCommand());
        return group;
    }

    /// <summary>Creates <c>share check</c>, the same check the app and the gallery's Action run.</summary>
    /// <returns>The check command.</returns>
    private static Command ShareCheckCommand()
    {
        var file = new Argument<string>("file")
        {
            Description = "The cooked Set (.set) to check.",
        };
        var json = new Option<bool>("--json")
        {
            Description = "Print the facts as JSON (problems, assets, canvas, fingerprint, file hash) "
                + "instead of text. The exit code is the same.",
        };
        var cmd = new Command("check",
            "Check a .set against the gallery's rules: prints each problem on its own line and exits 1 "
            + "if there is any; otherwise prints a one-line summary and exits 0.");
        cmd.Arguments.Add(file);
        cmd.Options.Add(json);
        cmd.SetAction(parse =>
        {
            var facts = ShareCheck.CheckAsync(parse.GetRequiredValue(file)).GetAwaiter().GetResult();
            if (parse.GetValue(json))
                Console.WriteLine(JsonSerializer.Serialize(facts, Json.Options));
            else if (facts.Problems.Count > 0)
                foreach (string problem in facts.Problems) Console.WriteLine(problem);
            else
                Console.WriteLine($"ok: {facts.Assets} assets · {facts.Width} x {facts.Height}"
                    + (facts.Models ? " · models" : "") + (facts.IncludesCookBook ? " · book" : ""));
            return facts.Problems.Count > 0 ? 1 : 0;
        });
        return cmd;
    }
}
