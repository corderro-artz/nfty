using System.CommandLine;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>The options that choose where an upload goes, declared once for <c>ipfs status</c>
    /// and <c>export --upload</c> so the two cannot describe them differently.</summary>
    private sealed record PinningOptions(Option<string?> Api, Option<bool> Pinata, Option<string?> PinataKey)
    {
        public static PinningOptions Create() => new(
            new Option<string?>("--api")
            {
                Description = "The local IPFS node's RPC address (IPFS Desktop or Kubo). "
                    + $"Default {KuboPinning.DefaultApi}.",
            },
            new Option<bool>("--pinata")
            {
                Description = "Upload to Pinata with your own API key (a JWT) instead of the local node.",
            },
            new Option<string?>("--pinata-key")
            {
                Description = $"Where the Pinata JWT comes from: {Passphrase.Sources}. NAMED rather "
                    + "than typed, the rule --key follows for a passphrase: there is deliberately no "
                    + "option that takes the key itself, because an argument is visible to every "
                    + "process, lands in your shell history and is captured by CI logs. Omit it and "
                    + "nfty asks at the terminal, echoing nothing.",
            });

        public void AddTo(Command cmd)
        {
            cmd.Options.Add(Api);
            cmd.Options.Add(Pinata);
            cmd.Options.Add(PinataKey);
        }

        /// <summary>Rejects the combinations that would silently do nothing.</summary>
        public void Validate(System.CommandLine.Parsing.CommandResult result, bool active = true)
        {
            if (!active)
            {
                foreach (var (given, name) in new (bool, string)[]
                {
                    (result.IsGiven(Api), "--api"), (result.GetValue(Pinata), "--pinata"),
                    (result.IsGiven(PinataKey), "--pinata-key"),
                })
                    if (given) result.AddError($"{name} chooses where an upload goes; it needs --upload.");
                return;
            }
            if (result.GetValue(Pinata) && result.IsGiven(Api))
                result.AddError("--api names the local node; --pinata uploads to Pinata instead. Choose one.");
            if (!result.GetValue(Pinata) && result.IsGiven(PinataKey))
                result.AddError("--pinata-key is the key for Pinata; it needs --pinata.");
            if (result.GetValue(Api) is { } api && KuboPinning.ApiProblem(api) is { } problem)
                result.AddError("--api: " + problem);
        }

        /// <summary>Builds the service the options name. The key is read here, from its named source.</summary>
        public IPinningService Build(ParseResult parse)
        {
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            if (parse.GetValue(Pinata))
            {
                string jwt = Passphrase.Read(parse.GetValue(PinataKey), "Pinata API key (JWT)",
                    option: "--pinata-key");
                return new PinataPinning(http, jwt);
            }
            return new KuboPinning(http, parse.GetValue(Api) ?? KuboPinning.DefaultApi);
        }
    }

    /// <summary>The <c>ipfs</c> group: <c>ipfs status</c>.</summary>
    private static Command IpfsGroup()
    {
        var group = new Command("ipfs", "Talk to the IPFS service an upload would use.");
        group.Subcommands.Add(IpfsStatus());
        return group;
    }

    private static Command IpfsStatus()
    {
        var pin = PinningOptions.Create();
        var cmd = new Command("status",
            "Say whether nfty can reach the IPFS service: the local node (IPFS Desktop or Kubo) by "
            + "default, or Pinata with --pinata. Exits 1 when it cannot.");
        pin.AddTo(cmd);
        cmd.Validators.Add(result => pin.Validate(result));
        cmd.SetAction(parse =>
        {
            var service = pin.Build(parse);
            var status = service.StatusAsync().GetAwaiter().GetResult();
            Console.WriteLine($"{status.Provider}: {status.Summary}");
            Console.WriteLine("  " + status.Detail);
            if (status.PeerId is { Length: > 0 } peer) Console.WriteLine("  peer id: " + peer);
            return status.Reachable ? 0 : 1;
        });
        return cmd;
    }

    /// <summary>Runs <c>export --upload</c> and prints what was pinned.</summary>
    private static void RunUpload(ParseResult parse, PinningOptions pin, string setPath, string outDir,
        ExportOptions options, string? bookPath)
    {
        if (MarketplaceUpload.Problem(options) is { } problem) throw new InvalidOperationException(problem);
        var service = pin.Build(parse);
        string? last = null;
        var progress = new InlineProgress<UploadProgress>(p =>
        {
            // One line per phase on stderr, so a script reading stdout gets only the result.
            string head = p.Phase.Split("...", 2)[0];
            if (head != last) Console.Error.WriteLine(head + "...");
            last = head;
        });
        var result = MarketplaceUpload.RunAsync(setPath, outDir, options, service, bookPath, progress,
            parallelism: ResolveThreads(parse)).GetAwaiter().GetResult();

        Console.WriteLine($"Uploaded to {result.Provider}");
        Console.WriteLine($"  images:      ipfs://{result.ImageCid}/");
        if (result.ModelCid is { } models) Console.WriteLine($"  models:      ipfs://{models}/");
        Console.WriteLine($"  metadata:    ipfs://{result.MetadataCid}/");
        Console.WriteLine($"  baseURI:     {result.BaseUri}");
        Console.WriteLine($"  contractURI: {result.ContractUri ?? "(none: no collection.json)"}");
        foreach (var warning in result.Warnings) Console.Error.WriteLine("warning: " + warning);
        PrintExport(result.Local, options);
    }

    /// <summary>Reports on the calling thread, in order.</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
