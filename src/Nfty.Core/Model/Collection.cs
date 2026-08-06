namespace Nfty.Core.Model;

/// <summary>The collection's marketplace-facing identity, copied into every asset's metadata.</summary>
/// <param name="Name">Collection name, e.g. "VaporPets".</param>
/// <param name="Description">Prose shown alongside it.</param>
/// <param name="Symbol">Short ticker-style code, e.g. "VPET".</param>
public record Collection(string Name, string Description, string Symbol);
