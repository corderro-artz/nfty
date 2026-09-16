using System.Numerics;
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

    [Theory]
    // THE RUNG IS CHOSEN AFTER ROUNDING. Picking it first and rounding afterwards printed a
    // mantissa that had left its own rung - "1000.00 billion" - in a column whose entire purpose is
    // that the rungs line up. Every value from 999,995,000,000 up hit it, which a real book reaches.
    [InlineData(999_999_999_999L, "1.00 trillion")]
    [InlineData(999_995_000_000L, "1.00 trillion")]
    [InlineData(999_994_999_999L, "999.99 billion")]
    [InlineData(999_999_999_999_999L, "1.00 quadrillion")]
    [InlineData(999_999_999_999_999_999L, "1.00 quintillion")]
    public void A_mantissa_that_rounds_off_its_rung_is_promoted_to_the_next_one(long value, string expected) =>
        Assert.Equal(expected, SpaceText.Compact(value));

    [Fact]
    public void Past_the_last_rung_a_mantissa_that_rounds_off_it_becomes_an_exponent()
    {
        // There is no name above quintillion in this ladder, so the promotion has nowhere to go and
        // the register changes instead of printing "1000.00 quintillion".
        Assert.Equal("1.00e21", SpaceText.Compact(BigInteger.Pow(10, 21) - 1));
        Assert.Equal("1.00e21", SpaceText.Compact(BigInteger.Pow(10, 21)));
    }

    [Theory]
    // A BigInteger has no widest figure, so the ladder has to end somewhere and the exponent is
    // what is past it. "Sextillion" is not more legible than e21; it is less, because at this size
    // the exponent is the thing actually being compared.
    [InlineData(21, "2.18e21")]
    [InlineData(35, "2.18e35")]
    [InlineData(120, "2.18e120")]
    public void A_figure_past_the_named_magnitudes_is_written_as_an_exponent(int exponent, string expected)
    {
        var value = BigInteger.Parse("218", CultureInfo.InvariantCulture)
            * BigInteger.Pow(10, exponent - 2);
        Assert.Equal(expected, SpaceText.Compact(value));
    }

    [Fact]
    public void An_exponent_is_derived_from_the_digits_rather_than_by_dividing()
    {
        // Dividing needs 10^exponent as a double, which is infinity past about 1e308 - so the
        // arithmetic that exists to describe an unbounded number would have a bound of its own, one
        // rung further out. A 400-digit figure is not a realistic book; it is the probe that says
        // the method has no ceiling.
        Assert.Equal("1.00e400", SpaceText.Compact(BigInteger.Pow(10, 400)));
        Assert.Equal("9.99e401", SpaceText.Compact(BigInteger.Parse(
            "999" + new string('0', 399), CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_space_no_long_could_hold_still_prints_every_digit_in_full()
    {
        // Exact never shortens, whatever the magnitude - that is what makes the tooltip worth
        // reaching for on a surface whose tile had to round.
        var value = BigInteger.Pow(2_160_000, 3) * 2;
        Assert.Equal("20,155,392,000,000,000,000", SpaceText.Exact(value));
        Assert.True(value > long.MaxValue);
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
