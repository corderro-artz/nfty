namespace Nfty.Core.Publish.Community;

/// <summary>What <see cref="ShareCheck.CheckAsync"/> learned about a packed Set.</summary>
/// <param name="Problems">What keeps the Set out of the gallery, in <see cref="ShareRules"/>' words; empty when it may be shared. When not empty every other member is a default.</param>
/// <param name="Assets">How many assets the Set holds.</param>
/// <param name="Width">Canvas width in pixels.</param>
/// <param name="Height">Canvas height in pixels.</param>
/// <param name="Models">Whether the Set carries 3D models.</param>
/// <param name="IncludesCookBook">Whether the Set's own CookBook rides inside it.</param>
/// <param name="CookbookSha256">The hash of the CookBook that made the Set, as <c>set.json</c> records it, or null.</param>
/// <param name="Collection">The collection's name.</param>
/// <param name="Fingerprint">Lowercase hex SHA-256 of the assets' DNA in number order: the same for the same assets whatever else was packed with them, different once the Set is extended.</param>
/// <param name="FileSha256">Lowercase hex SHA-256 of the <c>.set</c> file itself, which is what a download is checked against.</param>
public sealed record ShareFacts(
    IReadOnlyList<string> Problems,
    int Assets,
    int Width,
    int Height,
    bool Models,
    bool IncludesCookBook,
    string? CookbookSha256,
    string Collection,
    string Fingerprint,
    string FileSha256);
