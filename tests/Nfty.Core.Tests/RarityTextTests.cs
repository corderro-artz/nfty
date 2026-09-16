using System.Globalization;
using Nfty.Core.Stats;

namespace Nfty.Core.Tests;

/// <summary>
/// How a share is written down. Three surfaces carried their own copy of this and two of them were
/// culture-sensitive; these pin the one wording they now share.
/// </summary>
public class RarityTextTests
{
    [Theory]
    [InlineData(50, "50%")]
    [InlineData(4.17, "4.17%")]
    [InlineData(4.166666, "4.17%")]
    [InlineData(0.01, "0.01%")]
    [InlineData(100, "100%")]
    public void An_ordinary_share_prints_its_figure(double percent, string expected) =>
        Assert.Equal(expected, RarityText.Percent(percent));

    [Theory]
    [InlineData(0.009)]
    [InlineData(0.0008)]
    [InlineData(1e-9)]
    public void A_SHARE_TOO_SMALL_TO_PRINT_IS_NOT_ROUNDED_TO_NOTHING(double percent)
    {
        // THE CASE THIS EXISTS FOR. Rounding to two places turns every trait under a hundredth of a
        // percent into "0%" - the same string an ABSENT trait gets - so a one-in-a-million chase
        // item and a trait no asset carries read identically. That matters more the bigger a
        // collection is, which is exactly when somebody is looking.
        Assert.Equal("<0.01%", RarityText.Percent(percent));
        Assert.NotEqual(RarityText.Percent(0), RarityText.Percent(percent));
    }

    [Fact]
    public void A_zero_share_is_zero_and_not_a_floor() =>
        // The floor says "smaller than we will print". A trait no asset carries is not small, it is
        // absent, and the two must not look alike in either direction.
        Assert.Equal("0%", RarityText.Percent(0));

    [Fact]
    public void A_share_that_is_not_a_number_is_not_a_small_number() =>
        Assert.All(new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity },
            v => Assert.Equal(RarityText.None, RarityText.Percent(v)));

    [Theory]
    [InlineData(4.17, "1 in 24")]
    [InlineData(2.08, "1 in 48")]
    [InlineData(50, "1 in 2")]
    public void Odds_are_derived_from_the_percentage_and_rounded_to_whole_assets(
        double percent, string expected) =>
        // You cannot own a fraction of an asset, so "1 in 23.98" would be arithmetic showing through.
        Assert.Equal(expected, RarityText.Odds(percent));

    [Fact]
    public void ODDS_ARE_THOUSANDS_SEPARATED_LIKE_EVERY_OTHER_FIGURE()
    {
        // Where this came from: the Set browser's rarity column formatted "0" and printed
        // "1 in 100000", while the combined-chance line an inch above it went through
        // SelectionOdds.Describe, formatted "N0" and printed "1 in 100,000". The same unit, on the
        // same rail, in two shapes - visible only once a collection is big enough to reach five
        // digits, which is precisely when the number is worth reading.
        Assert.Equal("1 in 100,000", RarityText.Odds(0.001));
        Assert.Equal("1 in 1,000,000", RarityText.Odds(0.0001));
    }

    [Fact]
    public void A_zero_share_has_no_odds_at_all() =>
        // A trait no asset carries is not "1 in infinity", and infinity is not an answer anyone
        // wants on a card.
        Assert.Equal(RarityText.None, RarityText.Odds(0));

    [Fact]
    public void Odds_past_a_trillion_say_so_rather_than_printing_digits_they_do_not_have()
    {
        // 100.0/percent stays finite for any positive finite input but runs past what a double
        // represents exactly, and "N0" on a value past 2^53 prints digits that are an artefact of
        // the binary representation rather than of the share.
        Assert.Equal("1 in over a trillion", RarityText.Odds(1e-13));
        Assert.Equal("1 in over a trillion", RarityText.Odds(double.Epsilon));
    }

    [Fact]
    public void BOTH_FORMS_ARE_INVARIANT()
    {
        // The reason this class exists. Two of the three surfaces interpolated the double bare -
        // `$"{pct}%"` and Avalonia's StringFormat both take the CURRENT culture - so a machine set
        // to de-DE printed "4,17%" on a screen whose own comment promised every figure it shows is
        // invariant. These are read off screenshots and compared between machines.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("4.17%", RarityText.Percent(4.17));
            Assert.Equal("<0.01%", RarityText.Percent(0.001));
            Assert.Equal("1 in 100,000", RarityText.Odds(0.001));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
