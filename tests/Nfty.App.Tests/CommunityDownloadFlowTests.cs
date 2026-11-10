using System.Security.Cryptography;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Core.Output;
using Nfty.Core.Publish;
using Nfty.Core.Publish.Community;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>An HTTP handler that answers from a script and counts what it was asked.</summary>
internal sealed class ScriptedHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _answer;
    private int _requests;

    public ScriptedHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) => _answer = answer;

    public int Requests => Volatile.Read(ref _requests);

    public HttpClient Client() => new(this) { Timeout = Timeout.InfiniteTimeSpan };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _requests);
        return _answer(request, ct);
    }

    public static ScriptedHttp Serving(byte[] bytes) => new((_, _) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
}

/// <summary>A body that gives a few bytes, says it has started, then waits until it is canceled.</summary>
internal sealed class StallingBody : Stream
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _first = true;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_first)
        {
            _first = false;
            buffer.Span[..10].Fill(1);
            return 10;
        }
        Started.TrySetResult();
        await Task.Delay(Timeout.Infinite, ct);
        return 0;
    }
}

/// <summary>Downloading from the Community page: fetch, verify, open, and the ways it ends otherwise.</summary>
public class CommunityDownloadFlowTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("nfty-dl-flow-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string Fresh() => _dir.CreateSubdirectory(Guid.NewGuid().ToString("N")).FullName;

    private byte[] RealSet()
    {
        var ids = Enumerable.Range(0, 8).Select(v => $"v{v}").ToList();
        var ing = new LoadedIngredient
        {
            Manifest = new IngredientManifest("l0", "Layer 0", LayerKind.Custom, null,
                ids.Select(v => new Variant(v, v.ToUpperInvariant(), 1)).ToList()),
            VariantImages = ids.Select((v, i) => (v, i)).ToDictionary(t => t.v,
                t => new Image<Rgba32>(4, 4, new Rgba32((byte)(t.i * 20), 0, 3, 255))),
        };
        using var book = new LoadedCookBook
        {
            Manifest = new CookBookManifest("cb", "Chest Demo", new Dimensions(4, 4),
                new Collection("Chest Demo", "d", "CHST"), new Dictionary<string, double> { ["chest"] = 100 }),
            Recipes =
            [
                new LoadedRecipe
                {
                    Manifest = new RecipeManifest("chest", "Chest", ["l0"], Array.Empty<IncompatibilityRule>()),
                    Ingredients = [ing],
                },
            ],
        };
        string dir = Path.Combine(Fresh(), "ChestDemo");
        using (var set = Generator.Generate(book, new GenerateOptions(2, "launch")))
            SetWriter.Write(set, dir, pack: false);
        return File.ReadAllBytes(SetExporter.Export(dir, Fresh(), new ExportOptions(), null, null).Path);
    }

    private static CommunityEntry EntryFor(byte[] bytes, long? size = null, string? sha = null) =>
        CommunityFixtures.Entry("chest", "Chest", assets: 2) with
        {
            Canvas = new CommunityCanvas(4, 4),
            Set = new CommunityFile("https://cdn.example/files/chest.set", size ?? bytes.Length,
                sha ?? Convert.ToHexStringLower(SHA256.HashData(bytes))),
        };

    private sealed record Page(CommunityViewModel Vm, FakeCommunityCache Cache, List<string> Opened, FakeDialogs Dialogs);

    private async Task<Page> Open(CommunityEntry entry, ScriptedHttp http)
    {
        var cache = new FakeCommunityCache
        {
            Next = CommunityFixtures.Listing([entry]),
            SetsDir = Path.Combine(Fresh(), "sets"),
        };
        var opened = new List<string>();
        var dialogs = new FakeDialogs();
        var vm = new CommunityViewModel(cache, new CommunitySeen(Fresh()), new FakeGitHubAccount(), new FakeNav(), dialogs,
            new StatusService(), p => { opened.Add(p); return Task.CompletedTask; }, http: http.Client());
        vm.PageSize = 8;
        await vm.PendingRefresh;
        vm.SelectCommand.Execute(vm.Tiles[0]);
        return new Page(vm, cache, opened, dialogs);
    }

    [Fact]
    public async Task Download_opens_the_set_browser()
    {
        var bytes = RealSet();
        var entry = EntryFor(bytes);
        var p = await Open(entry, ScriptedHttp.Serving(bytes));
        using var vm = p.Vm;

        await vm.DownloadCommand.ExecuteAsync(null);

        // The verified file, named by its hash, is what the open path is handed.
        var expected = Path.Combine(p.Cache.SetsDir, entry.Set.Sha256 + ".set");
        Assert.Equal([expected], p.Opened);
        Assert.Equal(bytes, File.ReadAllBytes(expected));
        Assert.False(vm.IsDownloading);
        Assert.Equal("", vm.DownloadNote);
        Assert.Null(p.Dialogs.Active);
        Assert.True(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_mismatch_shows_the_sentence_in_the_rail()
    {
        var bytes = RealSet();
        var wrong = (byte[])bytes.Clone();
        wrong[^1] ^= 0xFF;                                   // same size, different bytes
        var p = await Open(EntryFor(bytes), ScriptedHttp.Serving(wrong));
        using var vm = p.Vm;

        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(ShareRules.HashMismatch, vm.DownloadNote);
        Assert.Empty(p.Opened);
        Assert.Null(p.Dialogs.Active);                       // never a dialog
        Assert.Empty(Directory.GetFiles(p.Cache.SetsDir));   // nothing half-written stays
        Assert.False(vm.IsDownloading);
        Assert.True(vm.DownloadCommand.CanExecute(null));    // and it can be tried again
    }

    [Fact]
    public async Task Download_can_be_stopped_and_leaves_nothing()
    {
        var body = new StallingBody();
        var bytes = RealSet();
        var http = new ScriptedHttp((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StreamContent(body) }));
        var p = await Open(EntryFor(bytes), http);
        using var vm = p.Vm;

        var running = vm.DownloadCommand.ExecuteAsync(null);
        await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.IsDownloading);
        Assert.True(vm.ShowsDownloadProgress);
        Assert.False(vm.DownloadCommand.CanExecute(null));   // one at a time
        Assert.True(vm.StopDownloadCommand.CanExecute(null));

        vm.StopDownloadCommand.Execute(null);
        await running;

        Assert.False(vm.IsDownloading);
        Assert.Empty(p.Opened);
        Assert.Equal("", vm.DownloadNote);                   // a stop is not a failure
        Assert.Empty(Directory.GetFiles(p.Cache.SetsDir));
        Assert.True(vm.DownloadCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_second_download_of_the_same_set_reuses_the_cache()
    {
        var bytes = RealSet();
        var http = ScriptedHttp.Serving(bytes);
        var p = await Open(EntryFor(bytes), http);
        using var vm = p.Vm;

        await vm.DownloadCommand.ExecuteAsync(null);
        await vm.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(1, http.Requests);                      // the second never asked the network
        Assert.Equal(2, p.Opened.Count);
        Assert.Equal(p.Opened[0], p.Opened[1]);
    }

    [Fact]
    public async Task A_failure_belongs_to_the_collection_it_happened_on()
    {
        var bytes = RealSet();
        var wrong = (byte[])bytes.Clone();
        wrong[^1] ^= 0xFF;
        var a = EntryFor(bytes);
        var b = CommunityFixtures.Entry("other", "Other", assets: 2);
        var cache = new FakeCommunityCache
        {
            Next = CommunityFixtures.Listing([a, b]),
            SetsDir = Path.Combine(Fresh(), "sets"),
        };
        using var vm = new CommunityViewModel(cache, new CommunitySeen(Fresh()), new FakeGitHubAccount(), new FakeNav(),
            new FakeDialogs(), new StatusService(), _ => Task.CompletedTask, http: ScriptedHttp.Serving(wrong).Client());
        vm.PageSize = 8;
        await vm.PendingRefresh;
        vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Entry.Id == a.Id));
        await vm.DownloadCommand.ExecuteAsync(null);
        Assert.Equal(ShareRules.HashMismatch, vm.DownloadNote);

        vm.SelectCommand.Execute(vm.Tiles.Single(t => t.Entry.Id == b.Id));

        Assert.Equal("", vm.DownloadNote);
    }
}
