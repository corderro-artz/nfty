namespace Nfty.Core.Editing;

/// <summary>An editable variant: identity + weight + its grayscale value-map.</summary>
public sealed class VariantDraft
{
    /// <summary>Stable identifier, unique within the draft.</summary>
    public string Id { get; }
    /// <summary>Display name.</summary>
    public string Name { get; set; }
    /// <summary>Roll weight; zero shelves the variant.</summary>
    public double Weight { get; set; }
    /// <summary>The editable pixels.</summary>
    public ValueMap Map { get; }

    /// <summary>Creates a variant being edited.</summary>
    /// <param name="id">Stable identifier.</param>
    /// <param name="name">Display name.</param>
    /// <param name="weight">Roll weight.</param>
    /// <param name="map">Its pixels.</param>
    public VariantDraft(string id, string name, double weight, ValueMap map)
    {
        Id = id;
        Name = name;
        Weight = weight;
        Map = map;
    }
}
