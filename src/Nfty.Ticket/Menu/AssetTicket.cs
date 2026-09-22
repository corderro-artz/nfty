using System.Globalization;
using Nfty.Core.Generation;
using Nfty.Core.Model;
using CoreDna = Nfty.Core.Generation.Dna;

namespace Nfty.Ticket;

/// <summary>What one layer contributed to a rolled asset.</summary>
/// <param name="IngredientId">The layer.</param>
/// <param name="VariantId">The variant rolled.</param>
/// <param name="Hue">The rolled hue in degrees, or null for a Custom layer.</param>
/// <param name="Sat">The rolled saturation as a 0..1 fraction, or null for a Custom layer.</param>
/// <param name="Model">The color space, or null for a Custom layer.</param>
/// <param name="Kind">Dynamic, Static or Custom.</param>
/// <param name="HueQuantize">The layer's hue bucket width, carried so the DNA can be recomputed
/// from the ticket alone.</param>
/// <param name="SatQuantize">The layer's saturation bucket width, likewise.</param>
public readonly record struct TicketLayer(
    string IngredientId, string VariantId, double? Hue, double? Sat,
    ColorModel? Model, LayerKind Kind, int HueQuantize, int SatQuantize);

/// <summary>
/// One roll, complete, costing no pixels: what a server stores and hands a client.
/// </summary>
/// <param name="Dna">The identity this roll produces.</param>
/// <param name="RecipeId">The recipe rolled.</param>
/// <param name="Layers">The layers that are PRESENT, in <c>layerOrder</c>. An absent layer is
/// omitted entirely.</param>
public sealed record AssetTicket(string Dna, string RecipeId, IReadOnlyList<TicketLayer> Layers)
{
    private const string Version = "v1";

    /// <summary>The DNA a set of layer selections produces.</summary>
    /// <param name="recipeId">The recipe rolled.</param>
    /// <param name="layers">The present layers.</param>
    /// <returns>Lowercase hex SHA-256, identical to what <see cref="CoreDna.Compute"/> produces for
    /// the same selection — it IS that function; there is no second definition.</returns>
    public static string ComputeDna(string recipeId, IReadOnlyList<TicketLayer> layers) =>
        CoreDna.Compute(recipeId, layers
            .Select(l => new LayerSelection(
                l.IngredientId, l.VariantId, l.Hue, l.Sat, l.HueQuantize, l.SatQuantize))
            .ToList());

    /// <summary>
    /// The ticket as one line: what goes in a database column or across a network.
    /// </summary>
    /// <returns>
    /// <c>v1|recipe|ing:var[@hue,sat,model,hq,sq]|...|dna</c>. Every figure is INVARIANT — a comma
    /// decimal separator under a European locale would collide with the field separator itself.
    /// </returns>
    public string ToWire()
    {
        var parts = new List<string>(Layers.Count + 3) { Version, RecipeId };
        foreach (var l in Layers)
        {
            string head = $"{l.IngredientId}:{l.VariantId}";
            // ENUMS GO OUT BY NAME, NEVER BY ORDINAL. An (int) cast would pin the DECLARATION
            // ORDER of LayerKind and ColorModel into the wire format, so reordering either enum
            // would silently change what every stored ticket means - a Custom layer reading back as
            // Dynamic, with no error and the wrong pixels. Names are also what Json.Options already
            // does for every enum this product persists (JsonStringEnumConverter).
            parts.Add(l.Hue is double h && l.Sat is double s && l.Model is ColorModel m
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{head}@{h:R},{s:R},{m},{l.HueQuantize},{l.SatQuantize},{l.Kind}")
                : string.Create(CultureInfo.InvariantCulture, $"{head}@,,,,,{l.Kind}"));
        }
        parts.Add(Dna);
        return string.Join("|", parts);
    }

    /// <summary>Reads a ticket back from <see cref="ToWire"/>.</summary>
    /// <param name="wire">The wire form.</param>
    /// <returns>The ticket.</returns>
    /// <exception cref="FormatException">The wire is malformed, is a version this build does not
    /// read, or carries a DNA its own contents do not produce.</exception>
    /// <remarks>
    /// <b>The DNA is recomputed and compared, never trusted.</b> A ticket altered in transit fails
    /// to parse rather than rendering an asset nobody rolled.
    /// </remarks>
    public static AssetTicket Parse(string wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        var parts = wire.Split('|');
        if (parts.Length < 3) throw new FormatException("A ticket needs a version, a recipe and a DNA.");
        if (parts[0] != Version)
            throw new FormatException($"Ticket version '{parts[0]}' is not one this build reads; expected {Version}.");

        string recipeId = parts[1];
        string dna = parts[^1];
        var layers = new List<TicketLayer>(parts.Length - 3);

        for (int i = 2; i < parts.Length - 1; i++)
        {
            var (head, tail) = Split(parts[i], '@');
            var (ingId, varId) = Split(head, ':');
            var f = tail.Split(',');
            if (f.Length != 6) throw new FormatException($"Layer '{parts[i]}' has {f.Length} fields; expected 6.");

            layers.Add(new TicketLayer(
                ingId, varId,
                f[0].Length == 0 ? null : Num(f[0]),
                f[1].Length == 0 ? null : Num(f[1]),
                f[2].Length == 0 ? null : Enum.Parse<ColorModel>(f[2]),
                Enum.Parse<LayerKind>(f[5]),
                f[3].Length == 0 ? 0 : Int(f[3]),
                f[4].Length == 0 ? 0 : Int(f[4])));
        }

        string recomputed = ComputeDna(recipeId, layers);
        if (!string.Equals(recomputed, dna, StringComparison.Ordinal))
            throw new FormatException(
                "This ticket's DNA is not the DNA its contents produce; it has been altered in transit.");

        return new AssetTicket(dna, recipeId, layers);
    }

    private static (string, string) Split(string s, char at)
    {
        int i = s.IndexOf(at);
        if (i < 0) throw new FormatException($"Expected '{at}' in '{s}'.");
        return (s[..i], s[(i + 1)..]);
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
