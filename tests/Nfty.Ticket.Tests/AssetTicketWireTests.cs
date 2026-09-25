using System.Globalization;
using Nfty.Core.Generation;
using Nfty.Core.Model;

namespace Nfty.Ticket.Tests;

public class AssetTicketWireTests
{
    private static TicketLayer Dynamic(string ing, string v, double h, double s) =>
        new(ing, v, h, s, ColorModel.Hsv, LayerKind.Dynamic, 10, 10);

    private static TicketLayer Custom(string ing, string v) =>
        new(ing, v, null, null, null, LayerKind.Custom, 0, 0);

    private static AssetTicket Make(params TicketLayer[] layers) =>
        new(AssetTicket.ComputeDna("cat", layers), "cat", layers);

    [Fact]
    public void A_ticket_survives_a_wire_round_trip_exactly()
    {
        var original = Make(Dynamic("bg", "a", 137.5, 0.62), Custom("body", "b"));
        var parsed = AssetTicket.Parse(original.ToWire());

        Assert.Equal(original.Dna, parsed.Dna);
        Assert.Equal(original.RecipeId, parsed.RecipeId);
        Assert.Equal(original.Layers, parsed.Layers);
    }

    [Fact]
    public void The_wire_form_is_invariant_across_locales()
    {
        // A comma decimal separator under de-DE would collide with the field separator and produce
        // a ticket that parses to a different colour, or not at all. Every figure that reaches a
        // wire or a file in this product is invariant for this reason.
        var ticket = Make(Dynamic("bg", "a", 137.5, 0.62));
        var previous = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            string german = ticket.ToWire();
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(ticket.ToWire(), german);
            Assert.Equal(ticket.Layers, AssetTicket.Parse(german).Layers);
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Fact]
    public void A_tampered_wire_is_refused_rather_than_rendered()
    {
        var ticket = Make(Dynamic("bg", "a", 137.5, 0.62));
        string tampered = ticket.ToWire().Replace("bg:a", "bg:z", StringComparison.Ordinal);
        var ex = Assert.Throws<FormatException>(() => AssetTicket.Parse(tampered));
        Assert.Contains("DNA", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_layer_is_omitted_and_not_marked()
    {
        // The same decision Dna.Compute already makes: a PRESENT layer contributes a part an absent
        // one does not, so omission is unambiguous and needs no sentinel that a variant id could
        // one day collide with.
        var withBoth = Make(Custom("bg", "a"), Custom("hat", "h"));
        var withoutHat = Make(Custom("bg", "a"));
        Assert.DoesNotContain("hat", withoutHat.ToWire(), StringComparison.Ordinal);
        Assert.NotEqual(withBoth.Dna, withoutHat.Dna);
    }

    [Fact]
    public void A_kind_only_alteration_parses_successfully_because_kind_is_outside_the_dna()
    {
        // Documents the boundary rather than pretending it does not exist: Dna.Compute hashes the
        // ingredient id, variant id and quantized hue/saturation - never Kind or Model - so a wire
        // tampered in only that field recomputes to the SAME dna and Parse accepts it. This is why
        // Parse's own docs say Kind/Model are not authoritative and consumers must read them from
        // the Menu instead: this test is the negative space of
        // A_tampered_wire_is_refused_rather_than_rendered, and would fail if Dna.Compute were ever
        // changed to fold Kind into the hash without this test being revisited.
        var ticket = Make(Dynamic("bg", "a", 137.5, 0.62));
        string tampered = ticket.ToWire().Replace(",Dynamic|", ",Static|", StringComparison.Ordinal);

        var parsed = AssetTicket.Parse(tampered);

        Assert.Equal(LayerKind.Static, parsed.Layers[0].Kind);
        Assert.Equal(ticket.Dna, parsed.Dna);
    }

    [Fact]
    public void A_wire_from_a_future_version_is_refused()
    {
        var ticket = Make(Custom("bg", "a"));
        string future = "v2|" + ticket.ToWire()[3..];
        Assert.Throws<FormatException>(() => AssetTicket.Parse(future));
    }

    // --- Falsifiability gaps the five tests above leave open ---

    [Fact]
    public void ToWire_pins_the_literal_wire_shape_for_a_dynamic_layer()
    {
        // The format IS the contract between writer, reader and any future version of the file.
        // A round-trip test alone would still pass if ToWire silently changed field order, the
        // separator, or the decimal format, so long as Parse changed to match — pin the literal
        // string so an accidental format drift is caught even when both halves still agree with
        // each other.
        var ticket = Make(Dynamic("bg", "a", 137.5, 0.62));
        string wire = ticket.ToWire();

        Assert.StartsWith("v1|cat|bg:a@137.5,0.62,Hsv,10,10,Dynamic|", wire, StringComparison.Ordinal);
        // The trailing field is the DNA: 64 lowercase hex characters, nothing else.
        string dnaField = wire.Split('|')[^1];
        Assert.Equal(64, dnaField.Length);
        Assert.Matches("^[0-9a-f]{64}$", dnaField);
    }

    [Fact]
    public void ToWire_pins_the_literal_wire_shape_for_a_custom_layer()
    {
        // A Custom layer carries no color, so every color field must be empty rather than "0" or
        // "null" — a reader splitting on ',' still needs exactly six fields to land on Kind.
        var ticket = Make(Custom("bg", "a"));
        string wire = ticket.ToWire();

        Assert.StartsWith("v1|cat|bg:a@,,,,,Custom|", wire, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputeDna_is_not_a_second_definition_of_Dna_Compute()
    {
        // AssetTicket.ComputeDna must delegate to Nfty.Core.Generation.Dna.Compute rather than
        // reimplementing it — the brief is explicit that there is exactly one definition of every
        // decision an asset is made of. Calling both independently with an equivalent selection and
        // requiring the SAME hash catches a future fork of the two functions that a round-trip test
        // (which only ever calls ComputeDna) could never see.
        var layers = new[] { Dynamic("bg", "a", 137.5, 0.62), Custom("body", "b") };
        string viaTicket = AssetTicket.ComputeDna("cat", layers);
        string viaCore = Dna.Compute("cat", layers
            .Select(l => new LayerSelection(l.IngredientId, l.VariantId, l.Hue, l.Sat, l.HueQuantize, l.SatQuantize))
            .ToList());

        Assert.Equal(viaCore, viaTicket);
    }

    [Fact]
    public void Layer_order_survives_the_round_trip_not_just_membership()
    {
        // Assert.Equal on the two IReadOnlyList<TicketLayer> in the first test already compares
        // order as well as membership, but that is easy to lose sight of — a Parse that
        // accidentally sorted layers (e.g. by ingredient id, the way Dna.Compute itself does
        // internally) would still contain the same layers and could slip past a weaker assertion.
        // Layer order is layerOrder, the paint order, and must come back exactly as written.
        var original = Make(Custom("z-top", "1"), Custom("a-bottom", "2"));
        var parsed = AssetTicket.Parse(original.ToWire());

        Assert.Equal(
            new[] { "z-top", "a-bottom" },
            parsed.Layers.Select(l => l.IngredientId));
    }

    [Fact]
    public void A_malformed_layer_field_count_is_refused()
    {
        // Parse's field-count check (f.Length != 6) has no coverage anywhere else: every other test
        // hands Parse a wire produced by ToWire, which always emits six fields. Strip one field by
        // hand so a future change that drops a field silently (rather than throwing) is caught.
        var ticket = Make(Dynamic("bg", "a", 137.5, 0.62));
        string wire = ticket.ToWire();
        string mangled = wire.Replace("137.5,0.62,Hsv,10,10,Dynamic", "137.5,0.62,Hsv,10,Dynamic", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => AssetTicket.Parse(mangled));
    }

    // --- Ids: what Validator admits, the wire must carry ---

    [Fact]
    public void An_id_containing_at_survives_the_round_trip()
    {
        // Validator admits '@' in an id - it is an ordinary file-name character, and "hat@2x" is
        // exactly how an artist names a double-size sprite. The layer field is "ing:var@figures", so
        // splitting it on the FIRST '@' cut the id in half and the figures after it no longer counted
        // six. The figures never contain '@', so the LAST one is the separator.
        var original = Make(Dynamic("hat@2x", "brim@2x", 137.5, 0.62), Custom("bg@1", "a@b@c"));
        var parsed = AssetTicket.Parse(original.ToWire());

        Assert.Equal(original.Dna, parsed.Dna);
        Assert.Equal(original.Layers, parsed.Layers);
    }

    [Theory]
    [InlineData("a|b", "v")]
    [InlineData("a:b", "v")]
    [InlineData("a", "v|w")]
    public void ToWire_refuses_an_id_the_wire_cannot_carry(string ingredientId, string variantId)
    {
        // Validator already refuses '|' and ':' in every id, so no cooked book can reach here with
        // one - but a MenuBook can be built by hand, and a ticket written with one would come back
        // from Parse as a different ticket or as nothing. Refusing it at the writer names the id;
        // letting it through names nothing until a reader somewhere fails on the stored line.
        var ticket = Make(Custom(ingredientId, variantId));
        var ex = Assert.Throws<InvalidOperationException>(() => ticket.ToWire());
        Assert.Contains(ingredientId == "a" ? variantId : ingredientId, ex.Message, StringComparison.Ordinal);
    }
}
