using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

/// <summary>
/// An RNG fed a fixed sequence, counting what it hands out. <see cref="IRng"/> exists for exactly
/// this: a seed proves two runs agree, a script proves <i>which</i> draw went where.
///
/// <para>Overrunning throws rather than returning a default, which is what makes the <b>budget</b>
/// assertable. What a roll costs is itself a contract here — a colour roll takes three draws
/// (weighted entry, hue, saturation), a Static layer takes none, and a caller-named variant takes
/// none — so a change in consumption surfaces as a failure rather than as a quietly different
/// colour.</para>
///
/// <para>Shared rather than per-file, unlike the fixture builders around it. CLAUDE.md's carve-out is
/// for builders "shaped for what that file tests"; this is a behaviour stub with no per-file shaping
/// at all, and it had already been written out twice, byte-identically, under two names.</para>
/// </summary>
/// <param name="values">The draws to hand out, in order.</param>
internal sealed class ScriptedRng(params double[] values) : IRng
{
    private int _next;

    /// <summary>How many draws have been taken — the number the budget assertions read.</summary>
    public int Calls => _next;

    /// <summary>The next scripted draw.</summary>
    /// <returns>The value at the current position.</returns>
    /// <exception cref="InvalidOperationException">The roll wanted more draws than were scripted,
    /// which means what it costs has changed.</exception>
    public double NextDouble() => _next < values.Length
        ? values[_next++]
        : throw new InvalidOperationException(
            $"The roll took more than the {values.Length} scripted draws.");
}
