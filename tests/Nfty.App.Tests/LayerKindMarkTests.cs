using Nfty.App.Models;
using Nfty.Core.Model;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The one-letter kind mark, which three surfaces draw: the Explorer tree, the reference panel's rows,
/// and its pinned row. Each used to carry its own copy of the switch, so a fourth
/// <see cref="LayerKind"/> would have had to be found in all of them — and a stale copy shows the
/// wrong letter somewhere nothing is looking.
/// </summary>
public class LayerKindMarkTests
{
    [Theory]
    [InlineData(LayerKind.Dynamic, "D")]
    [InlineData(LayerKind.Static, "S")]
    [InlineData(LayerKind.Custom, "C")]
    public void Every_kind_has_its_letter(LayerKind kind, string expected) =>
        Assert.Equal(expected, LayerKindMark.For(kind));

    /// <summary>Absent is a real state — a tree node that is not an ingredient, and a Kitchen file
    /// whose archive has not been opened yet — and it reads as no mark rather than a wrong one.</summary>
    [Fact]
    public void No_kind_means_no_mark() => Assert.Null(LayerKindMark.For((LayerKind?)null));

    /// <summary>
    /// Every declared kind is covered, without this file having to be edited when a fourth appears.
    /// The point is the <b>coupling</b>: a new kind added to Core would otherwise reach three drawing
    /// surfaces with no test able to see it.
    /// </summary>
    [Fact]
    public void No_declared_kind_falls_through_to_the_unknown_glyph()
    {
        foreach (LayerKind kind in Enum.GetValues<LayerKind>())
            Assert.NotEqual("?", LayerKindMark.For(kind));
    }
}
