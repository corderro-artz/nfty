namespace Nfty.Core.Editing;

/// <summary>An editable variant: identity + weight + its grayscale value-map.</summary>
public sealed class VariantDraft
{
    public string Id { get; }
    public string Name { get; set; }
    public double Weight { get; set; }
    public ValueMap Map { get; }

    public VariantDraft(string id, string name, double weight, ValueMap map)
    {
        Id = id;
        Name = name;
        Weight = weight;
        Map = map;
    }
}
