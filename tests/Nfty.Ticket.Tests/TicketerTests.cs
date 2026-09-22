using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Ticket.Tests;

public class TicketerTests
{
    /// <summary>An RNG that counts what it is asked for, so draw ORDER can be asserted and not
    /// merely hoped for. The sequence repeats, which is fine: the count is what is under test.</summary>
    private sealed class CountingRng(params double[] values) : IRng
    {
        private int _i;
        public int Draws { get; private set; }
        public double NextDouble()
        {
            Draws++;
            return values[_i++ % values.Length];
        }
    }

    private static Colorization Range() =>
        new(ColorModel.Hsv, 10, 10, [new ColorEntry(1, new ColorRange(0, 360, 0, 100), null)]);

    private static Colorization Fixed() =>
        new(ColorModel.Hsv, 10, 10, [new ColorEntry(1, null, "hex:ff0000")]);

    private static MenuBook Book(params MenuLayer[] layers) =>
        new(1, null, new Dimensions(2, 2), "C", "SYM",
            [new MenuRecipe("cat", "Cat", 1, layers.Select(l => l.Id).ToList(), layers, [],
                new Dictionary<string, double>())],
            new MenuArt(MenuArtMode.Loose, 0, 0, []));

    private static MenuLayer Layer(string id, LayerKind kind, Colorization? c) =>
        new(id, id, kind, c, [new Variant("a", "A", 1), new Variant("b", "B", 1)]);

    [Fact]
    public void A_custom_layer_costs_one_draw_for_its_variant_and_no_colour()
    {
        var rng = new CountingRng(0.5);
        Ticketer.TryRoll(Ticketer.Prepare(Book(Layer("body", LayerKind.Custom, null))), rng);
        Assert.Equal(2, rng.Draws);   // 1 recipe + 1 variant
    }

    [Fact]
    public void A_static_layer_consumes_no_rng_for_its_colour()
    {
        var rng = new CountingRng(0.5);
        Ticketer.TryRoll(Ticketer.Prepare(Book(Layer("bg", LayerKind.Static, Fixed()))), rng);
        Assert.Equal(2, rng.Draws);   // 1 recipe + 1 variant, and NOTHING for the fixed colour
    }

    [Fact]
    public void A_dynamic_range_layer_costs_three_draws_for_its_colour()
    {
        var rng = new CountingRng(0.5);
        Ticketer.TryRoll(Ticketer.Prepare(Book(Layer("bg", LayerKind.Dynamic, Range()))), rng);
        Assert.Equal(5, rng.Draws);   // 1 recipe + 1 variant + 1 entry + 1 hue + 1 saturation
    }

    /// <summary>Falsifiable in the direction the previous test cannot see: a fixed color entry must
    /// cost exactly ONE draw (to pick the entry) and zero more, so a regression that adds a second
    /// draw for "picking the fixed colour itself" — as opposed to picking the entry — shows up here
    /// even though the overall count for a single-entry Dynamic colorization would coincidentally
    /// still look plausible if only the total were checked.</summary>
    [Fact]
    public void A_dynamic_fixed_entry_costs_one_draw_for_its_colour()
    {
        var rng = new CountingRng(0.5);
        Ticketer.TryRoll(Ticketer.Prepare(Book(Layer("bg", LayerKind.Dynamic, Fixed()))), rng);
        Assert.Equal(3, rng.Draws);   // 1 recipe + 1 variant + 1 entry, no hue/saturation
    }

    [Fact]
    public void A_dynamic_layer_records_its_colour_and_a_custom_layer_does_not()
    {
        var ticket = Ticketer.TryRoll(Ticketer.Prepare(
            Book(Layer("bg", LayerKind.Dynamic, Range()), Layer("body", LayerKind.Custom, null))),
            new CountingRng(0.5));

        Assert.NotNull(ticket);
        Assert.NotNull(ticket!.Layers[0].Hue);
        Assert.Null(ticket.Layers[1].Hue);
    }

    [Fact]
    public void Layers_come_back_in_layer_order()
    {
        var ticket = Ticketer.TryRoll(Ticketer.Prepare(
            Book(Layer("zzz", LayerKind.Custom, null), Layer("aaa", LayerKind.Custom, null))),
            new CountingRng(0.5));

        // layerOrder, NOT ordinal: "zzz" is declared first and paints first. Dna.Compute sorts its
        // own input ordinally, which is a different question from what order the layers are drawn in.
        Assert.Equal(["zzz", "aaa"], ticket!.Layers.Select(l => l.IngredientId));
    }

    [Fact]
    public void A_selection_the_rules_reject_returns_null_for_the_caller_to_reroll()
    {
        var layers = new[] { Layer("bg", LayerKind.Custom, null), Layer("hat", LayerKind.Custom, null) };
        var book = new MenuBook(1, null, new Dimensions(2, 2), "C", "SYM",
            [new MenuRecipe("cat", "Cat", 1, ["bg", "hat"], layers,
                // Every combination is excluded: bg=a forbids hat=a and hat=b, and so does bg=b.
                [
                    new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "a"),
                        [new RuleTarget("hat", "a"), new RuleTarget("hat", "b")]),
                    new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "b"),
                        [new RuleTarget("hat", "a"), new RuleTarget("hat", "b")]),
                ],
                new Dictionary<string, double>())],
            new MenuArt(MenuArtMode.Loose, 0, 0, []));

        Assert.Null(Ticketer.TryRoll(Ticketer.Prepare(book), new CountingRng(0.5)));
    }

    [Fact]
    public void A_layer_shelved_at_a_hundred_percent_is_not_rolled_at_all()
    {
        var layers = new[] { Layer("bg", LayerKind.Custom, null), Layer("hat", LayerKind.Custom, null) };
        var book = new MenuBook(1, null, new Dimensions(2, 2), "C", "SYM",
            [new MenuRecipe("cat", "Cat", 1, ["bg", "hat"], layers, [],
                new Dictionary<string, double> { ["hat"] = 100 })],
            new MenuArt(MenuArtMode.Loose, 0, 0, []));

        var rng = new CountingRng(0.5);
        var ticket = Ticketer.TryRoll(Ticketer.Prepare(book), rng);

        Assert.Equal(2, rng.Draws);                              // recipe + bg; hat costs NOTHING
        Assert.Single(ticket!.Layers);
        Assert.Equal("bg", ticket.Layers[0].IngredientId);
    }

    /// <summary>Pins the Menu-is-authoritative rule for <see cref="TicketLayer.Kind"/> and
    /// <see cref="TicketLayer.Model"/>: both sit outside the DNA hash, so nothing about the roll
    /// forces them to be set at all. If <c>TryRoll</c> ever left them at their default instead of
    /// copying them from the <see cref="MenuLayer"/> being rolled, this is the test that would
    /// catch it — asserting only that a ticket exists would not.</summary>
    [Fact]
    public void A_ticket_layer_carries_its_kind_and_colour_model_from_the_menu()
    {
        var ticket = Ticketer.TryRoll(Ticketer.Prepare(
            Book(Layer("bg", LayerKind.Static, Fixed()), Layer("body", LayerKind.Custom, null))),
            new CountingRng(0.5));

        Assert.NotNull(ticket);
        Assert.Equal(LayerKind.Static, ticket!.Layers[0].Kind);
        Assert.Equal(ColorModel.Hsv, ticket.Layers[0].Model);
        Assert.Equal(LayerKind.Custom, ticket.Layers[1].Kind);
        Assert.Null(ticket.Layers[1].Model);
    }
}
