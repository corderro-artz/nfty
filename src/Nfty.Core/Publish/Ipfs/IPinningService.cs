namespace Nfty.Core.Publish.Ipfs;

/// <summary>
/// Whether a pinning service can be talked to right now, and what it said about itself.
/// </summary>
/// <param name="Reachable">True when the service answered and accepted the credentials (if any).</param>
/// <param name="Provider">Which service this is, as a person reads it: <c>Local IPFS</c> or <c>Pinata</c>.</param>
/// <param name="Summary">A few words for a status chip: <c>connected · kubo 0.29.0</c>, <c>not reachable</c>.</param>
/// <param name="Detail">One sentence saying what was found and, when it failed, what to do about it.</param>
/// <param name="Version">The node's version, when the service reports one.</param>
/// <param name="PeerId">The node's peer id, when the service reports one.</param>
public sealed record PinningStatus(
    bool Reachable,
    string Provider,
    string Summary,
    string Detail,
    string? Version = null,
    string? PeerId = null);

/// <summary>How far a folder upload has got.</summary>
/// <param name="BytesSent">Request bytes written so far.</param>
/// <param name="BytesTotal">Request bytes in all.</param>
public sealed record PinProgress(long BytesSent, long BytesTotal)
{
    /// <summary><see cref="BytesSent"/> over <see cref="BytesTotal"/>, between 0 and 1.</summary>
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Clamp((double)BytesSent / BytesTotal, 0, 1);
}

/// <summary>
/// A pinning service refused, failed or answered with something unusable. The message is worded for
/// the person who asked, and both front-ends show it verbatim.
/// </summary>
public sealed class PinningException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, worded for a person.</param>
    public PinningException(string message) : base(message) { }

    /// <summary>Creates the exception over the failure underneath.</summary>
    /// <param name="message">What went wrong, worded for a person.</param>
    /// <param name="inner">The failure underneath.</param>
    public PinningException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Somewhere a folder can be put on IPFS and kept there: a local Kubo node, or a pinning service.
/// </summary>
/// <remarks>
/// <para><b>A folder's address is its CONTENTS.</b> <see cref="AddFolderAsync"/> returns the CID of
/// a directory whose entries are the folder's files, so <c>ipfs://CID/1.png</c> names the folder's
/// <c>1.png</c> - the shape <see cref="ExportOptions.ImageBaseUri"/> was built around, with no
/// <c>images/</c> in the link.</para>
/// <para>No UI dependency and no package: the implementations speak HTTP through
/// <see cref="HttpClient"/>, and the files are streamed one at a time, never buffered whole.</para>
/// </remarks>
public interface IPinningService
{
    /// <summary>Which service this is, as a person reads it.</summary>
    string Provider { get; }

    /// <summary>
    /// Asks whether the service can be reached and accepts the credentials. Never throws for an
    /// unreachable service: that is an answer, reported in the result.
    /// </summary>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>What was found.</returns>
    Task<PinningStatus> StatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds and pins every file under <paramref name="folder"/>, returning the CID of a directory
    /// whose entries are those files.
    /// </summary>
    /// <param name="folder">The folder whose contents to add.</param>
    /// <param name="label">A name for the pin where the service keeps one (Pinata's dashboard).</param>
    /// <param name="progress">Bytes sent so far.</param>
    /// <param name="cancellationToken">Stops the upload. What the service already received may stay
    /// on it.</param>
    /// <returns>The root CID.</returns>
    /// <exception cref="ArgumentException">The folder is missing or holds no file.</exception>
    /// <exception cref="PinningException">The service refused, failed or answered without a CID.</exception>
    Task<string> AddFolderAsync(string folder, string label, IProgress<PinProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unpins <paramref name="cid"/>, best effort: how a stopped or failed upload takes back what it
    /// already pinned. Never throws for a pin the service no longer holds.
    /// </summary>
    /// <param name="cid">A root CID <see cref="AddFolderAsync"/> returned.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>True when the service confirmed the removal.</returns>
    Task<bool> RemoveAsync(string cid, CancellationToken cancellationToken = default);
}
