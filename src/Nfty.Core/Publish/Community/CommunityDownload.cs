using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Nfty.Core.Publish.Community;

/// <summary>
/// Fetches a listed Set into a cache folder and proves it is what the gallery lists before anything
/// opens it. The download is unauthenticated: the link is a public release download that may redirect
/// to a CDN, so no token is ever attached.
/// </summary>
public static partial class CommunityDownload
{
    /// <summary>The sentence for an entry whose link is not an https address.</summary>
    public static string LinkNotOpened => ShareRules.LinkNotOpened;

    /// <summary>How long the body may go without a byte before the download gives up.</summary>
    internal static readonly TimeSpan DefaultStall = TimeSpan.FromSeconds(60);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex LowerHex64();

    /// <summary>Downloads <paramref name="entry"/>'s Set into <paramref name="cacheDir"/>, verified.</summary>
    /// <param name="http">A client with no credentials on it.</param>
    /// <param name="entry">The listed entry; its Set file's size and hash are what the bytes must match.</param>
    /// <param name="cacheDir">The folder holding downloaded Sets; created when missing.</param>
    /// <param name="progress">Receives the fraction of the listed size received, 0 to 1.</param>
    /// <param name="ct">Stops the download.</param>
    /// <returns>The path <c>&lt;cacheDir&gt;/&lt;sha256&gt;.set</c>; a cached copy that still hashes right is returned without a request.</returns>
    /// <exception cref="CommunityException">The download failed or did not match the listing; the message is a <see cref="ShareRules"/> sentence.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was canceled.</exception>
    public static Task<string> FetchAsync(HttpClient http, CommunityEntry entry, string cacheDir,
        IProgress<double>? progress, CancellationToken ct) =>
        FetchAsync(http, entry, cacheDir, progress, DefaultStall, ct);

    internal static async Task<string> FetchAsync(HttpClient http, CommunityEntry entry, string cacheDir,
        IProgress<double>? progress, TimeSpan stall, CancellationToken ct)
    {
        var file = entry.Set;
        // The hash comes from the network and becomes a file name: nothing but 64 lowercase hex may pass.
        if (file.Sha256 is null || !LowerHex64().IsMatch(file.Sha256) || file.Size < 0)
            throw new CommunityException(ShareRules.HashMismatch);
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new CommunityException(LinkNotOpened);

        try { Directory.CreateDirectory(cacheDir); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new CommunityException(ShareRules.UploadSourceFailed); }

        string final = Path.Combine(cacheDir, file.Sha256 + ".set");
        string part = Path.Combine(cacheDir, file.Sha256 + ".part");

        if (File.Exists(final))
        {
            if (await HashOfAsync(final, ct).ConfigureAwait(false) == file.Sha256) return final;
            TryDelete(final);
        }

        bool moved = false;
        try
        {
            await Download(http, uri, file, part, progress, stall, ct).ConfigureAwait(false);

            var facts = await ShareCheck.CheckAsync(part, ct).ConfigureAwait(false);
            if (facts.Problems.Count > 0) throw new CommunityException(facts.Problems[0]);
            if (facts.Assets != entry.Assets) throw new CommunityException(ShareRules.CountMismatch);
            if (facts.Width != entry.Canvas.Width || facts.Height != entry.Canvas.Height)
                throw new CommunityException(ShareRules.CanvasMismatch);

            try
            {
                File.Move(part, final, overwrite: false);
                moved = true;
            }
            catch (IOException)
            {
                // Another download of the same Set may have landed first; keep it only if intact.
                if (!File.Exists(final) || await HashOfAsync(final, ct).ConfigureAwait(false) != file.Sha256) throw;
            }
            return final;
        }
        finally
        {
            if (!moved) TryDelete(part);
        }
    }

    private static async Task Download(HttpClient http, Uri uri, CommunityFile file, string part,
        IProgress<double>? progress, TimeSpan stall, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            timer.CancelAfter(stall);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timer.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new CommunityException(ShareRules.Unreachable);
            // A length it admits to is enough to refuse early; one it hides is caught while counting.
            if (response.Content.Headers.ContentLength is { } declared && declared > file.Size)
                throw new CommunityException(ShareRules.HashMismatch);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            var body = await response.Content.ReadAsStreamAsync(timer.Token).ConfigureAwait(false);
            await using (body.ConfigureAwait(false))
            {
                FileStream output;
                try { output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { throw new CommunityException(ShareRules.SaveFailed); }
                await using var _ = output.ConfigureAwait(false);
                var buffer = new byte[81920];
                int n;
                while (true)
                {
                    timer.CancelAfter(stall);
                    n = await body.ReadAsync(buffer, timer.Token).ConfigureAwait(false);
                    if (n <= 0) break;
                    total += n;
                    if (total > file.Size) throw new CommunityException(ShareRules.HashMismatch);
                    hash.AppendData(buffer, 0, n);
                    try { await output.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false); }
                    catch (IOException) { throw new CommunityException(ShareRules.SaveFailed); }
                    progress?.Report(file.Size == 0 ? 1 : Math.Min(1.0, (double)total / file.Size));
                }
            }
            if (total != file.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != file.Sha256)
                throw new CommunityException(ShareRules.HashMismatch);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new CommunityException(ShareRules.TimedOut); }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            throw new CommunityException(ShareRules.Unreachable);
        }
    }

    private static async Task<string> HashOfAsync(string path, CancellationToken ct)
    {
        try
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await using var _ = fs.ConfigureAwait(false);
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
