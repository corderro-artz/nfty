namespace Nfty.Core.Editing;

/// <summary>
/// Whether a paint command may write partial alpha. Locked does <em>not</em> mean "no alpha":
/// erasing is how a sprite gets its shape and layers have to composite over one another, so a fully
/// opaque rectangle per layer would make stacking meaningless. It means <em>binary</em> alpha —
/// every painted pixel comes out fully opaque or fully erased, nothing between.
/// </summary>
/// <remarks>
/// <see cref="Locked"/> is the zero value on purpose: it is what <see langword="default"/> yields,
/// so a command constructed without saying anything about opacity gets the safe mode. This is an
/// editor setting and nothing else — no manifest field, no schema change, no archive affected.
/// </remarks>
public enum OpacityLock
{
    /// <summary>Binary alpha. Every pixel a command writes is snapped to fully opaque (255) or fully
    /// erased (0); the pixels it does not touch, and everything <c>Undo</c> restores, are left
    /// exactly as they were.</summary>
    Locked,

    /// <summary>Partial alpha is written through as the command computed it. Semi-transparent pixels
    /// do not voxelise cleanly, which is why this is never the default.</summary>
    Unlocked,
}
