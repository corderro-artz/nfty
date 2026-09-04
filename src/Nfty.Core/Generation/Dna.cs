using System.Security.Cryptography;
using System.Text;

namespace Nfty.Core.Generation;

/// <summary>What one layer contributed to an asset, in the form the DNA hashes.</summary>
/// <param name="IngredientId">The layer.</param>
/// <param name="VariantId">The variant rolled.</param>
/// <param name="Hue">The rolled hue, or null for a Custom layer.</param>
/// <param name="Sat">The rolled saturation as a 0..1 fraction, or null for a Custom layer.</param>
/// <param name="HueQuantize">Hue bucket width used when hashing.</param>
/// <param name="SatQuantize">Saturation bucket width used when hashing.</param>
public readonly record struct LayerSelection(
    string IngredientId, string VariantId, double? Hue, double? Sat, int HueQuantize, int SatQuantize);

/// <summary>
/// An asset's identity: SHA-256 over the recipe id, each layer's variant id, and — for colorized
/// layers — the QUANTIZED color. Quantizing is what makes uniqueness decidable at all; without it
/// two assets a pixel apart in hue would count as different.
/// </summary>
public static class Dna
{
    /// <summary>Computes an asset's DNA.</summary>
    /// <param name="recipeId">The recipe rolled.</param>
    /// <param name="selections">One entry per layer, in any order — they are sorted ordinally here,
    /// which is why the same selection hashes the same on every machine.</param>
    /// <returns>Lowercase hex SHA-256.</returns>
    public static string Compute(string recipeId, IReadOnlyList<LayerSelection> selections)
    {
        var sb = new StringBuilder();
        sb.Append("recipe=").Append(recipeId).Append('|');
        foreach (var s in selections.OrderBy(x => x.IngredientId, StringComparer.Ordinal))
        {
            sb.Append(s.IngredientId).Append('=').Append(s.VariantId);
            if (s.Hue is double h && s.Sat is double sat)
            {
                long hb = ColorBuckets.Hue(h, s.HueQuantize);
                long sbk = ColorBuckets.Sat(sat, s.SatQuantize);
                sb.Append('@').Append(hb).Append(',').Append(sbk);
            }
            sb.Append('|');
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
