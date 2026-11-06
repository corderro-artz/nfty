using System.CommandLine;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Ipfs;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>The options that choose where an upload goes, declared once for <c>ipfs status</c>
    /// and <c>export --upload</c> so the two cannot describe them differently.</summary>
    /// <param name="Api">The local node address option.</param>
    /// <param name="Pinata">The hosted-provider switch.</param>
    /// <param name="PinataKey">The named credential-source option.</param>
    private sealed record PinningOptions(Option<string?> Api, Option<bool> Pinata, Option<string?> PinataKey)
    {
        /// <summary>Declares a fresh set of provider options for one command.</summary>
        /// <returns>The provider options.</returns>
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
                    + "option that takes the key itself, because an argument can be visible to other "
                    + "processes with sufficient permissions, land in shell history and enter CI logs. Omit it and "
                    + "nfty asks at the terminal, echoing nothing.",
            });

        /// <summary>Attaches the provider options to a command.</summary>
        /// <param name="cmd">The command to configure.</param>
        public void AddTo(Command cmd)
        {
            cmd.Options.Add(Api);
            cmd.Options.Add(Pinata);
            cmd.Options.Add(PinataKey);
        }

        /// <summary>Rejects the combinations that would silently do nothing.</summary>
        /// <param name="result">The command being validated; conversion errors remain parser errors.</param>
        /// <param name="active">Whether this command will use a pinning service.</param>
        public void Validate(System.CommandLine.Parsing.CommandResult result, bool active = true)
        {
            if (!result.TryGetValue(Pinata, out bool usePinata)) return;
            if (!result.TryGetValue(Api, out string? api)) return;
            if (!active)
            {
                foreach (var (given, name) in new (bool, string)[]
                {
                    (result.IsGiven(Api), "--api"), (usePinata, "--pinata"),
                    (result.IsGiven(PinataKey), "--pinata-key"),
                })
                    if (given) result.AddError($"{name} chooses where an upload goes; it needs --upload.");
                return;
            }
            if (usePinata && result.IsGiven(Api))
                result.AddError("--api names the local node; --pinata uploads to Pinata instead. Choose one.");
            if (!usePinata && result.IsGiven(PinataKey))
                result.AddError("--pinata-key is the key for Pinata; it needs --pinata.");
            if (api is not null && KuboPinning.ApiProblem(api) is { } problem)
                result.AddError("--api: " + problem);
        }

        /// <summary>Builds the service the options name. The key is read here, from its named source.</summary>
        /// <param name="parse">The validated options.</param>
        /// <param name="http">Borrowed client; its action owns disposal after the operation.</param>
        /// <returns>The service, which borrows the client for every request.</returns>
        public IPinningService Build(ParseResult parse, HttpClient http)
        {
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
    /// <returns>The IPFS command group.</returns>
    private static Command IpfsGroup()
    {
        var group = new Command("ipfs", "Talk to the IPFS service an upload would use.");
        group.Subcommands.Add(IpfsStatus());
        return group;
    }

    /// <summary>Checks the chosen provider, returning exit 1 when unreachable.</summary>
    /// <returns>The status command.</returns>
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
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var service = pin.Build(parse, http);
            var status = service.StatusAsync().GetAwaiter().GetResult();
            Console.WriteLine($"{status.Provider}: {status.Summary}");
            Console.WriteLine("  " + status.Detail);
            if (status.PeerId is { Length: > 0 } peer) Console.WriteLine("  peer id: " + peer);
            return status.Reachable ? 0 : 1;
        });
        return cmd;
    }

    /// <summary>Runs <c>export --upload</c> and prints what was pinned.</summary>
    /// <param name="parse">The validated invocation.</param>
    /// <param name="pin">The chosen provider options.</param>
    /// <param name="setPath">The source Set folder or archive.</param>
    /// <param name="outDir">The local export destination.</param>
    /// <param name="options">The effective export options.</param>
    /// <param name="bookPath">The optional source book to include locally.</param>
    private static void RunUpload(ParseResult parse, PinningOptions pin, string setPath, string outDir,
        ExportOptions options, string? bookPath)
    {
        if (MarketplaceUpload.Problem(options) is { } problem) throw new InvalidOperationException(problem);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var service = pin.Build(parse, http);
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

    /// <summary>Invokes the callback synchronously on the thread that reports progress.</summary>
    /// <typeparam name="T">The progress value.</typeparam>
    /// <param name="report">The borrowed callback, without synchronization-context dispatch.</param>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        /// <inheritdoc />
        public void Report(T value) => report(value);
    }
}
