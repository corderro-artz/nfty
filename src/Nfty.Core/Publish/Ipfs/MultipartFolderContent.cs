using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Nfty.Core.Publish.Ipfs;

/// <summary>
/// A <c>multipart/form-data</c> body built from a folder, written one file at a time.
/// </summary>
/// <remarks>
/// <para><b>Streamed, never buffered.</b> <see cref="MultipartFormDataContent"/> wants an open
/// stream per part up front, which for a ten-thousand-asset collection is ten thousand open file
/// handles; this opens each file only while its bytes are being written.</para>
/// <para><b>The length is computed, not chunked.</b> Every part's header and every file's size is
/// known before the first byte goes out, so the request carries a <c>Content-Length</c> and no
/// service has to accept chunked uploads.</para>
/// </remarks>
internal sealed class MultipartFolderContent : HttpContent
{
    private sealed record Part(byte[] Header, string? FilePath, byte[]? Body, long Length);

    private readonly List<Part> _parts = new();
    private readonly string _boundary;
    private IProgress<PinProgress>? _progress;

    /// <summary>Creates an empty body under a fresh boundary.</summary>
    public MultipartFolderContent()
    {
        _boundary = "nfty-" + Guid.NewGuid().ToString("N");
        Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={_boundary}");
    }

    /// <summary>Where bytes-sent is reported while the body is written.</summary>
    public IProgress<PinProgress>? Progress { set => _progress = value; }

    /// <summary>Every file under <paramref name="folder"/>, relative, '/'-separated, in ordinal order.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The relative paths.</returns>
    /// <exception cref="ArgumentException">The folder is missing or holds no file.</exception>
    public static IReadOnlyList<string> FilesIn(string folder)
    {
        if (!Directory.Exists(folder))
            throw new ArgumentException($"'{folder}' is not a folder.", nameof(folder));
        var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (files.Count == 0)
            throw new ArgumentException($"'{folder}' holds no file to upload.", nameof(folder));
        return files;
    }

    /// <summary>Adds one file part.</summary>
    /// <param name="field">The form field name.</param>
    /// <param name="fileName">The file name the part carries, already escaped as the service wants.</param>
    /// <param name="path">The file on disk.</param>
    public void AddFile(string field, string fileName, string path) =>
        _parts.Add(new Part(Header(field, fileName, "application/octet-stream"), path, null,
            new FileInfo(path).Length));

    /// <summary>Adds a directory entry, the way Kubo's multipart reader expects one.</summary>
    /// <param name="field">The form field name.</param>
    /// <param name="fileName">The directory's path, already escaped.</param>
    public void AddDirectory(string field, string fileName) =>
        _parts.Add(new Part(Header(field, fileName, "application/x-directory"), null, [], 0));

    /// <summary>Adds a plain text field.</summary>
    /// <param name="field">The form field name.</param>
    /// <param name="text">Its value.</param>
    public void AddText(string field, string text)
    {
        var header = Encoding.UTF8.GetBytes(
            $"--{_boundary}\r\nContent-Disposition: form-data; name=\"{field}\"\r\n\r\n");
        var body = Encoding.UTF8.GetBytes(text);
        _parts.Add(new Part(header, null, body, body.Length));
    }

    private byte[] Header(string field, string fileName, string type) =>
        Encoding.UTF8.GetBytes(
            $"--{_boundary}\r\nContent-Disposition: form-data; name=\"{field}\"; filename=\"{fileName.Replace("\"", "%22", StringComparison.Ordinal)}\"\r\n"
            + $"Content-Type: {type}\r\n\r\n");

    private byte[] Closing => Encoding.UTF8.GetBytes($"--{_boundary}--\r\n");

    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    /// <inheritdoc />
    protected override bool TryComputeLength(out long length)
    {
        length = Closing.Length;
        foreach (var p in _parts) length += p.Header.Length + p.Length + Crlf.Length;
        return true;
    }

    /// <inheritdoc />
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    /// <inheritdoc />
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
        CancellationToken cancellationToken)
    {
        TryComputeLength(out long total);
        long sent = 0;
        var buffer = new byte[81920];
        foreach (var part in _parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await stream.WriteAsync(part.Header, cancellationToken).ConfigureAwait(false);
            sent += part.Header.Length;
            if (part.FilePath is { } path)
            {
                var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    buffer.Length, useAsync: true);
                await using var _ = file.ConfigureAwait(false);
                int read;
                while ((read = await file.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    sent += read;
                    _progress?.Report(new PinProgress(sent, total));
                }
            }
            else if (part.Body is { Length: > 0 } body)
            {
                await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
                sent += body.Length;
            }
            await stream.WriteAsync(Crlf, cancellationToken).ConfigureAwait(false);
            sent += Crlf.Length;
        }
        await stream.WriteAsync(Closing, cancellationToken).ConfigureAwait(false);
        _progress?.Report(new PinProgress(total, total));
    }
}
