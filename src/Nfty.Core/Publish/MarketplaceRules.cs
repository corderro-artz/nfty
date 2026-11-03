using System.Globalization;
using System.Text.RegularExpressions;

namespace Nfty.Core.Publish;

/// <summary>How an export names the files that go to a marketplace.</summary>
public enum TokenNaming
{
    /// <summary>The Set's own names: <c>0001.json</c>, <c>0001.png</c>. Byte-for-byte what nfty has always exported.</summary>
    Padded,

    /// <summary>
    /// What an ERC-721 contract's <c>tokenURI = baseURI + tokenId</c> expects: the token id alone,
    /// unpadded — <c>1</c> for metadata (no extension), <c>1.png</c> for art, <c>1.glb</c> for a model.
    /// </summary>
    TokenId,
}

/// <summary>
/// The one mapping between <see cref="TokenNaming"/> and the words the CLI, the export dialog and
/// the manual spell it with, so none of them can name a choice two ways.
/// </summary>
public static class TokenNamingTokens
{
    /// <summary>The words, in the order a picker lists them.</summary>
    public static IReadOnlyList<string> Words { get; } = ["padded", "token-id"];

    /// <summary>The word for a naming.</summary>
    /// <param name="naming">Which naming.</param>
    /// <returns><c>padded</c> or <c>token-id</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="naming"/> is not defined.</exception>
    public static string Spell(TokenNaming naming) => naming switch
    {
        TokenNaming.Padded => "padded",
        TokenNaming.TokenId => "token-id",
        _ => throw new ArgumentOutOfRangeException(nameof(naming)),
    };

    /// <summary>Reads a word back. Never guesses: anything but a listed word is refused.</summary>
    /// <param name="word">The text.</param>
    /// <param name="naming">The naming, when the word is one.</param>
    /// <returns>Whether <paramref name="word"/> named a naming.</returns>
    public static bool TryParse(string? word, out TokenNaming naming)
    {
        switch (word)
        {
            case "padded": naming = TokenNaming.Padded; return true;
            case "token-id": naming = TokenNaming.TokenId; return true;
            default: naming = default; return false;
        }
    }
}

/// <summary>
/// The rules for the marketplace fields of <see cref="ExportOptions"/>, stated once.
/// </summary>
/// <remarks>
/// Every method returns the sentence to show (or null when the value is fine) instead of throwing, so
/// the export dialog can put the reason beside the field as the user types, the CLI can refuse with
/// it, and <see cref="SetExporter"/> can refuse the same value with the same words. Nothing here
/// guesses: a base URI missing its trailing slash is an error, not a slash added for the author.
/// </remarks>
public static class MarketplaceRules
{
    private static readonly string[] UriSchemes = ["ipfs", "https", "http", "ar"];

    private static readonly Regex Address = new("^0x[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);

    /// <summary>Why a base URI is unusable, or null when it is fine (or empty, meaning "none").</summary>
    /// <param name="value">The text.</param>
    /// <param name="what">What the address is, for the message (for example <c>Image base URI</c>).</param>
    /// <returns>The reason, naming the rule.</returns>
    public static string? BaseUriProblem(string? value, string what = "Base URI")
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!IsAbsolute(value, UriSchemes))
            return $"{what} '{value}' is not valid: it must be an absolute URI with the scheme ipfs, https, http or ar "
                + "(for example ipfs://<CID>/).";
        if (!value.EndsWith('/'))
            return $"{what} '{value}' must end with '/', because each file name is appended to it as it stands.";
        return null;
    }

    /// <summary>Why a collection image link is unusable, or null.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The reason, naming the rule.</returns>
    public static string? ImageUriProblem(string? value) =>
        string.IsNullOrEmpty(value) || IsAbsolute(value, UriSchemes)
            ? null
            : $"Collection image '{value}' is not valid: it must be an absolute URI with the scheme ipfs, https, http or ar.";

    /// <summary>Why a web address is unusable, or null.</summary>
    /// <param name="value">The text.</param>
    /// <param name="what">What the address is, for the message.</param>
    /// <returns>The reason, naming the rule.</returns>
    public static string? WebUrlProblem(string? value, string what) =>
        string.IsNullOrEmpty(value) || IsAbsolute(value, ["https", "http"])
            ? null
            : $"{what} '{value}' is not valid: it must be an absolute http or https address.";

    /// <summary>Why a royalty percentage is unusable, or null.</summary>
    /// <param name="percent">The percentage, or null for none.</param>
    /// <returns>The reason, naming the rule.</returns>
    public static string? RoyaltyProblem(double? percent)
    {
        if (percent is not { } p) return null;
        if (double.IsNaN(p) || p < 0 || p > 100)
            return "Royalty must be a percentage from 0 to 100.";
        double bp = p * 100;
        if (Math.Abs(bp - Math.Round(bp)) > 1e-6)
            return "Royalty can have at most 2 decimals (it becomes whole basis points, 0.01% each).";
        return null;
    }

    /// <summary>The royalty as OpenSea's whole basis points: 2.5% is 250.</summary>
    /// <param name="percent">A percentage already accepted by <see cref="RoyaltyProblem"/>.</param>
    /// <returns>Basis points.</returns>
    public static int BasisPoints(double percent) => (int)Math.Round(percent * 100, MidpointRounding.AwayFromZero);

    /// <summary>Reads a royalty typed as text, always with <c>.</c> as the decimal point.</summary>
    /// <param name="text">The text; empty means none.</param>
    /// <param name="percent">The value, or null for none.</param>
    /// <returns>Null when it parsed (or was empty); otherwise the reason it did not.</returns>
    public static string? TryParseRoyalty(string? text, out double? percent)
    {
        percent = null;
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            return $"Royalty '{text.Trim()}' is not a number.";
        if (RoyaltyProblem(v) is { } problem) return problem;
        percent = v;
        return null;
    }

    /// <summary>Why a fee recipient is unusable, or null.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The reason, naming the rule.</returns>
    public static string? FeeRecipientProblem(string? value) =>
        string.IsNullOrEmpty(value) || Address.IsMatch(value)
            ? null
            : $"Fee recipient '{value}' is not an address: it must be 0x followed by 40 hexadecimal digits.";

    /// <summary>Why a token start is unusable, or null.</summary>
    /// <param name="start">The first token id.</param>
    /// <returns>The reason.</returns>
    public static string? TokenStartProblem(int start) =>
        start is 0 or 1 ? null : "The first token id must be 0 or 1.";

    private static bool IsAbsolute(string value, string[] schemes) =>
        !value.Any(char.IsWhiteSpace)
        && value.Contains("://", StringComparison.Ordinal)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && schemes.Contains(uri.Scheme, StringComparer.Ordinal)
        && uri.Host.Length > 0;
}
