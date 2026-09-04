namespace Nfty.Core.Editing;

/// <summary>An editable variant: identity + weight + its pixels, in one or both of the two forms a
/// variant can be authored in.</summary>
/// <remarks>
/// A variant carries a grayscale <see cref="Map"/> always, and a full-color <see cref="Color"/> once
/// it has been painted or imported in color mode. Which one is authoritative is decided by the
/// owning <see cref="IngredientDraft.Kind"/> and never by which happens to be non-null: a Custom
/// ingredient exports its <see cref="Color"/>, everything else exports its <see cref="Map"/>.
///
/// <para>Both are kept rather than one replacing the other because that is precisely what the
/// non-destructive save needs. Painting a Dynamic layer in color leaves its value-map untouched —
/// so the original ingredient stays byte-identical on disk — while the color map becomes a new
/// Custom ingredient beside it.</para>
/// </remarks>
public sealed class VariantDraft
{
    /// <summary>Stable identifier, unique within the draft.</summary>
    public string Id { get; }
    /// <summary>Display name.</summary>
    public string Name { get; set; }
    /// <summary>Roll weight; zero shelves the variant.</summary>
    public double Weight { get; set; }
    /// <summary>The editable grayscale pixels — what a Dynamic or Static layer is authored in.</summary>
    public ValueMap Map { get; }
    /// <summary>The editable full-color pixels, or null while this variant has only ever been a
    /// value-map. Set when color mode is entered, and the only thing a Custom layer exports.</summary>
    public ColorMap? Color { get; set; }

    /// <summary>Creates a variant being edited.</summary>
    /// <param name="id">Stable identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="weight">Roll weight.</param>
    /// <param name="map">Its grayscale pixels.</param>
    /// <param name="color">Its full-color pixels, or null for a variant not authored in color.</param>
    public VariantDraft(string id, string name, double weight, ValueMap map, ColorMap? color = null)
    {
        Id = id;
        Name = name;
        Weight = weight;
        Map = map;
        Color = color;
    }

    /// <summary>The variant's color pixels, widening its value-map on first use so entering color
    /// mode shows the drawing that is already there rather than a blank canvas. Idempotent: once a
    /// color map exists it is returned untouched, so re-entering color mode never discards paint.</summary>
    /// <returns>This variant's color map.</returns>
    public ColorMap EnsureColor() => Color ??= ColorMap.FromValueMap(Map);
}
