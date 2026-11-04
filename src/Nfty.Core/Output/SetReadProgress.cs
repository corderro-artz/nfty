namespace Nfty.Core.Output;

/// <summary>How far a Set read has got: assets parsed so far, of how many.</summary>
/// <param name="Done">Assets parsed.</param>
/// <param name="Total">Assets in the Set.</param>
public readonly record struct SetReadProgress(int Done, int Total);
