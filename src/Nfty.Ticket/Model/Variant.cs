namespace Nfty.Core.Model;

/// <summary>One image inside an Ingredient — the smallest unit a roll can land on. Not a file of its
/// own: variants live inside the <c>.igt</c> that owns them.</summary>
/// <param name="Id">Stable identifier, unique within its Ingredient. Becomes the variant's PNG entry
/// name inside the archive, and is what the DNA records.</param>
/// <param name="Name">Display name, shown in reports and the GUI.</param>
/// <param name="Weight">Relative roll weight. Zero shelves the variant — it stays in the archive and
/// is never rolled — and negative is rejected by <c>Validator</c>.</param>
public record Variant(string Id, string Name, double Weight);
