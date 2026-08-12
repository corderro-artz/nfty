using Nfty.Core.Model;

namespace Nfty.App.Models;

/// <summary>
/// The single letter a layer kind is drawn as — <c>D</c>, <c>S</c>, <c>C</c>.
///
/// <para>One owner, because the table had been written out three times: the Explorer tree, the
/// reference panel's rows and its pinned row each carried their own copy of the same switch. A fourth
/// <see cref="LayerKind"/> would have had to be found in all of them, and a stale one shows the wrong
/// letter in a place no test is looking.</para>
/// </summary>
public static class LayerKindMark
{
    /// <summary>The mark for a kind.</summary>
    /// <param name="kind">The layer kind.</param>
    /// <returns>A one-letter mark.</returns>
    public static string For(LayerKind kind) => kind switch
    {
        LayerKind.Dynamic => "D",
        LayerKind.Static => "S",
        LayerKind.Custom => "C",
        // Unreachable while LayerKind has three members, and deliberately not a throw: a mark is
        // decoration, and a new kind should show up as an unfamiliar glyph rather than take down
        // whichever pane happened to draw it first.
        _ => "?",
    };

    /// <summary>The mark for a kind that may be absent — a tree node that is not an ingredient.</summary>
    /// <param name="kind">The layer kind, or null.</param>
    /// <returns>A one-letter mark, or null when there is no kind.</returns>
    public static string? For(LayerKind? kind) => kind is { } k ? For(k) : null;
}
