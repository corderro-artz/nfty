using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nfty.Core.Formats;
using Nfty.Core.Output;

namespace Nfty.Core.Publish;

/// <summary>
/// The names an export gives its files, as a <see cref="SetFiles"/>: the SAME mapping the readers use
/// (<c>SetReader</c>, the models lookup, the viewer), so a file's name, its link, the <c>#n</c> in its
/// <c>name</c> and the way nfty finds it again cannot disagree.
/// </summary>
internal static class ExportNaming
{
    /// <summary>The naming these options give the exported files.</summary>
    internal static SetFiles Of(ExportOptions o) => new(TokenNamingTokens.Spell(o.TokenNaming), o.TokenStart);

    /// <summary>Whether these options leave every name exactly as the Set wrote it.</summary>
    internal static bool IsDefault(ExportOptions o) => Of(o).IsDefault;

    /// <summary>The token id of the asset with this set number.</summary>
    internal static int Id(int number, ExportOptions o) => Of(o).Id(number);

    /// <summary>The name an asset's files share, without extension.</summary>
    internal static string Stem(int number, ExportOptions o) => Of(o).Stem(number);

    internal static string ImageFile(int number, ExportOptions o) => Of(o).ImageFile(number);

    /// <summary>A metadata file's name: the bare token id when a contract's <c>baseURI + tokenId</c> must resolve it.</summary>
    internal static string MetadataFile(int number, ExportOptions o) => Of(o).MetadataFile(number);
}

/// <summary>One item's metadata as exported: the standard fields plus the optional links.</summary>
internal sealed record ExportedItem(
    string Name,
    string Description,
    string Image,
    [property: JsonPropertyName("external_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ExternalUrl,
    [property: JsonPropertyName("animation_url"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? AnimationUrl,
    IReadOnlyList<MetadataAttribute> Attributes);

/// <summary>OpenSea's contract-level metadata, as <c>collection.json</c>.</summary>
internal sealed record CollectionFile(
    string Name,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Image,
    [property: JsonPropertyName("external_link"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ExternalLink,
    [property: JsonPropertyName("seller_fee_basis_points"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? SellerFeeBasisPoints,
    [property: JsonPropertyName("fee_recipient"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FeeRecipient,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Symbol);

/// <summary>Renders the files an export makes rather than copies.</summary>
internal static class MarketplaceFiles
{
    /// <summary>The name of the contract-level file.</summary>
    internal const string CollectionName = "collection.json";

    /// <summary>
    /// One item's metadata, rewritten for upload.
    /// </summary>
    /// <param name="sourcePath">The Set's <c>metadata/NNNN.json</c>.</param>
    /// <param name="number">Its set number.</param>
    /// <param name="options">The export request.</param>
    /// <param name="fallbackName">The collection's name, for an item whose own name carries no <c>#</c>.</param>
    /// <param name="animationFile">The model's file name (for example <c>1.glb</c>), or null.</param>
    internal static byte[] RenderItem(string sourcePath, int number, ExportOptions options,
        string fallbackName, string? animationFile)
    {
        var item = SetItemFile.ReadOpenSea(sourcePath, File.ReadAllText(sourcePath));
        string id = Id(number, options);

        int hash = item.Name.LastIndexOf(" #", StringComparison.Ordinal);
        string prefix = hash > 0 ? item.Name[..hash] : fallbackName;

        string imageFile = ExportNaming.ImageFile(number, options);
        // Always the link the export's own naming gives: for a Set's own names that IS what the writer
        // put there, and for a re-export of an export it replaces the previous run's links. With a base
        // URI the link is the FILE NAME under it (an uploaded images/ folder is the CID's root).
        string image = options.ImageBaseUri is { } b ? b + imageFile : SetLayout.ImagesDir + "/" + imageFile;

        var exported = new ExportedItem(
            Name: $"{prefix} #{id}",
            Description: item.Description,
            Image: image,
            ExternalUrl: options.ExternalUrlBase is { Length: > 0 } ext ? ext + id : null,
            AnimationUrl: animationFile is not null && options.ModelBaseUri is { } baseUri ? baseUri + animationFile : null,
            Attributes: item.Attributes);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(exported, Json.Options));
    }

    private static string Id(int number, ExportOptions o) =>
        ExportNaming.Id(number, o).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>collection.json</c>.
    /// </summary>
    /// <param name="manifest">The Set's record, for the name and symbol.</param>
    /// <param name="description">The collection's description, read from the Set's metadata.</param>
    /// <param name="image">The collection image, or null to leave it out.</param>
    /// <param name="options">The export request.</param>
    internal static byte[] RenderCollection(SetManifest manifest, string description, string? image, ExportOptions options)
    {
        var file = new CollectionFile(
            Name: manifest.Name,
            Description: description,
            Image: image,
            ExternalLink: options.ExternalLink is { Length: > 0 } link ? link : null,
            SellerFeeBasisPoints: options.RoyaltyPercent is { } pct ? MarketplaceRules.BasisPoints(pct) : null,
            FeeRecipient: options.FeeRecipient is { Length: > 0 } fee ? fee : null,
            Symbol: manifest.Symbol is { Length: > 0 } sym ? sym : null);
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(file, Json.Options));
    }
}
