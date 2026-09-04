namespace Nfty.Core.Output;

/// <summary>
/// Whether the CookBook in front of you is the one a Set on disk was cooked from, and what to say
/// when it is not.
///
/// <para><b>Why it matters.</b> <c>Generator</c> walks a recipe's <c>layerOrder</c> and consumes one
/// weighted draw per layer, so moving a layer moves <i>which draw reaches it</i>. The same seed over a
/// reordered — or otherwise edited — book therefore rolls different variants and different colors:
/// not a re-render of the same collection, a different collection. Extending a Set with a book that
/// did not cook it silently interleaves two generations under one set of token ids, and nothing else
/// in the output records that it happened. <c>set.json</c>'s <c>cookbookSha256</c> is the only thread
/// tying a Set back to its source archive, so it is what this compares.</para>
///
/// <para><b>A warning, never a refusal.</b> Deliberately re-cooking an edited book is a legitimate
/// thing to want; the author is the one who knows. And the text lives here rather than in a front-end
/// for the same reason the reports in <c>Stats/</c> do — the CLI and a GUI should say the identical
/// thing, not something similar.</para>
/// </summary>
public static class SetProvenance
{
    /// <summary>
    /// The warning to show before extending, or null when there is nothing to warn about.
    /// </summary>
    /// <param name="recordedSha256">What the Set recorded at cook time —
    /// <see cref="SetManifest.CookbookSha256"/>, as <c>SetWriter.ReadExisting</c> reports it. Null
    /// when the Set predates the field, has no readable <c>set.json</c>, or was cooked from a book
    /// that never came from a file.</param>
    /// <param name="cookbookSha256">The hash of the CookBook about to be used —
    /// <c>LoadedCookBook.SourceSha256</c>. Null for an in-memory book that never came from a
    /// file.</param>
    /// <returns>The warning text, or null when the two agree or cannot be compared.</returns>
    /// <remarks>
    /// <b>A null on either side means "cannot tell", which is not "they differ".</b> Both sides are
    /// legitimately absent — an unsaved book has no file to hash, and a Set can be cooked from one —
    /// so warning on a null would fire on the ordinary case and train the author to ignore the one
    /// message that matters. Silence is the honest answer to a question that was not asked.
    /// </remarks>
    public static string? Warning(string? recordedSha256, string? cookbookSha256)
    {
        if (recordedSha256 is null || cookbookSha256 is null) return null;

        // Case-insensitive: both sides are written lowercase by ArchiveIo.HashFile, but set.json is
        // plain JSON that a person can edit, and an uppercase copy of the same hash is the same hash.
        // Ordinal-ignore-case, not the current culture's, because a hex digit must compare the same
        // under every locale.
        if (string.Equals(recordedSha256, cookbookSha256, StringComparison.OrdinalIgnoreCase))
            return null;

        return $"""
            warning: this CookBook is not the one this Set was cooked from.
              the Set records cookbookSha256 {recordedSha256}
              this CookBook hashes to        {cookbookSha256}
              Reordering a recipe's layers — or any other edit — changes which random draw reaches
              which layer, so the same seed over a changed book rolls different variants and
              different colors. Extending with it adds assets from a second generation to a
              collection minted from the first; the assets already there are not re-rolled.
              Pass the CookBook this Set was cooked from, or generate a fresh Set from this one.
            """;
    }
}
