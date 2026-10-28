namespace Nfty.App.Viewer;

/// <summary>One label/value line in the viewer's facts panel.</summary>
/// <param name="Label">What the fact is.</param>
/// <param name="Value">Its value, already worded for display.</param>
public sealed record ViewerFact(string Label, string Value);
