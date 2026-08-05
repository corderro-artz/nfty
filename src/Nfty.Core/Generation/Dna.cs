using System.Security.Cryptography;
using System.Text;

namespace Nfty.Core.Generation;

public readonly record struct LayerSelection(
    string IngredientId, string VariantId, double? Hue, double? Sat, int HueQuantize, int SatQuantize);

public static class Dna
{
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
