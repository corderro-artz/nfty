using Nfty.Core.Imaging;

namespace Nfty.Core.Tests;

public class ColorSpecTests
{
    [Fact]
    public void Parses_hex() =>
        Assert.Equal(new RgbColor(214, 36, 159), ColorSpec.Parse("hex:d6249f"));

    [Fact]
    public void Parses_rgb() =>
        Assert.Equal(new RgbColor(214, 36, 159), ColorSpec.Parse("rgb:214,36,159"));

    [Fact]
    public void Parses_hsv_to_expected_rgb() =>
        Assert.Equal(new RgbColor(255, 0, 0), ColorSpec.Parse("hsv:0,100,100"));

    [Fact]
    public void Missing_prefix_throws() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("d6249f"));

    [Fact]
    public void Unknown_prefix_throws() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("cmyk:1,2,3,4"));

    // --- hex parsing (finding 3) ---

    [Fact]
    public void Invalid_hex_digits_throw_a_hand_written_message_not_the_framework_one()
    {
        var ex = Assert.Throws<FormatException>(() => ColorSpec.Parse("hex:zzzzzz"));

        Assert.Contains("zzzzzz", ex.Message, StringComparison.Ordinal);
        // .NET's own byte.Parse wording, which every sibling validation message is hand-written
        // to avoid leaking to a user.
        Assert.DoesNotContain("was not in a correct format", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Eight_digit_hex_with_alpha_is_rejected_not_silently_truncated() =>
        Assert.Throws<FormatException>(() => ColorSpec.Parse("hex:d6249fff"));

    [Fact]
    public void Eight_digit_hex_rejection_explains_that_alpha_is_unused()
    {
        var ex = Assert.Throws<FormatException>(() => ColorSpec.Parse("hex:d6249fff"));

        Assert.Contains("alpha", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
