using System.Globalization;
using Nfty.Core.Generation;

namespace Nfty.Core.Tests;

/// <summary>
/// How a DNA-space figure is written down. One formatter, because the CLI's <c>stats</c> line and
/// the CookBook card each used to carry their own copy of the decision and worded it differently.
/// </summary>
public class SpaceTextTests
{
    // ---- the display question: a tile is not a report -------------------------------------------

    [Theory]
    [InlineData(0, "0")]
    [InlineData(90, "90")]
    [InlineData(615_600, "615,600")]
    [InlineData(2_560_000, "2,560,000")]
    [InlineData(999_999_999, "999,999,999")]
    public void Below_a_billion_a_tile_shows_every_digit(long value, string expected)
    {
        // The whole design of the compact form: degrade ONLY when you must. Essentially every real
        // book lands here, so essentially every book shows its true figure on the card.
        Assert.Equal(expected, SpaceText.Compact(value));
        Assert.Equal(expected, SpaceText.Exact(value));
    }

    [Theory]
    [InlineData(1_000_000_000L, "1.00 billion")]
    [InlineData(2_822_400_000L, "2.82 billion")]
    [InlineData(4_500_000_000_000L, "4.50 trillion")]
    [InlineData(7_000_000_000_000_000L, "7.00 quadrillion")]
    [InlineData(long.MaxValue, "9.22 quintillion")]
    public void Above_a_billion_a_tile_names_the_magnitude(long value, string expected)
    {
        // A named magnitude rather than scientific notation. "9.22e18" is precise and is the wrong
        // register for a card an artist reads while sizing a collection; "1.2e7 versus 9.8e6" is a
        // comparison nobody makes at a glance.
        Assert.Equal(expected, SpaceText.Compact(value));
    }

    [Fact]
    public void The_mantissa_is_always_two_places_so_a_column_of_them_lines_up()
    {
        // These are read as a COLUMN - one row per recipe, stacked - and "0.##" drops a trailing
        // zero, so a book of 17.744 trillion over one of 12.900 printed "17.74 trillion" above
        // "12.9 trillion" and the decimal points did not line up. Caught in the running app, not
        // here: every assertion above passes on either format.
        string[] column =
        [
            SpaceText.Compact(17_744_000_000_000L),
            SpaceText.Compact(12_900_000_000_000L),
            SpaceText.Compact(1_000_000_000_000L),
        ];

        Assert.Equal(["17.74 trillion", "12.90 trillion", "1.00 trillion"], column);

        // Stated as the property rather than as three strings: every mantissa carries a point and
        // exactly two digits after it, whatever the value rounds to.
        foreach (string s in column)
        {
            string mantissa = s.Split(' ')[0];
            Assert.Equal(2, mantissa.Length - mantissa.IndexOf('.', StringComparison.Ordinal) - 1);
        }
    }

    [Fact]
    public void The_exact_digits_survive_whatever_the_tile_shows()
    {
        // A number the product refuses to print in full is a number the author cannot check, and
        // this is the figure they tune quantize steps against. The tile rounds; nothing else does.
        Assert.Equal("9,223,372,036,854,775,807", SpaceText.Exact(long.MaxValue));
        Assert.Equal("2,822,400,000", SpaceText.Exact(2_822_400_000L));
    }

    [Fact]
    public void Both_forms_are_invariant()
    {
        // Reports get copied into issues and diffed against a colleague's run. A machine set to
        // de-DE would otherwise render 615,600 as "615.600" and make two identical books look
        // different - the same rule every ordinal sort in this codebase follows.
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("615,600", SpaceText.Exact(615_600));
            Assert.Equal("2.82 billion", SpaceText.Compact(2_822_400_000L));
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    // ---- the certainty question: four sentences, and two of them are opposites -------------------

    [Theory]
    [InlineData(SpaceCertainty.Exact, "615,600")]
    [InlineData(SpaceCertainty.AtLeast, "more than 615,600")]
    [InlineData(SpaceCertainty.AtMost, "at most 615,600")]
    public void Each_certainty_says_something_different_and_true(SpaceCertainty c, string expected)
    {
        Assert.Equal(expected, SpaceText.Describe(615_600, c));
    }

    [Fact]
    public void An_uncountable_space_prints_no_figure_at_all()
    {
        // Total is 0 in this case, so wording it like the others would put "more than 0" on the
        // card - which reads like a measurement rather than a shrug.
        Assert.Equal(SpaceText.Unknown, SpaceText.Describe(0, SpaceCertainty.Unknown));
        Assert.DoesNotContain("0", SpaceText.Describe(0, SpaceCertainty.Unknown), StringComparison.Ordinal);
    }

    [Fact]
    public void A_bound_is_worded_the_same_way_whether_or_not_the_number_was_shortened()
    {
        // The direction belongs to the certainty and the digits belong to the surface; the two are
        // independent, and a compact bound that dropped its "at most" would be the original bug in a
        // narrower window.
        Assert.Equal("at most 2.82 billion",
            SpaceText.Describe(2_822_400_000L, SpaceCertainty.AtMost, compact: true));
        Assert.Equal("at most 2,822,400,000",
            SpaceText.Describe(2_822_400_000L, SpaceCertainty.AtMost));
    }
}
