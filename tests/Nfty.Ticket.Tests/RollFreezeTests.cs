using Nfty.Core.Generation;
using Nfty.Core.Model;
using Nfty.Ticket;

namespace Nfty.Ticket.Tests;

/// <summary>
/// One seed, one book, frozen output. This file is ALSO compiled into Nfty.Ticket.NetStandard.Tests,
/// which runs it against the netstandard2.1 build — so the two builds are held to the same tickets,
/// wire strings and DNAs, byte for byte.
/// </summary>
public class RollFreezeTests
{
    private static MenuBook Book()
    {
        var hue = new Colorization(ColorModel.Hsv, 30, 40, [new ColorEntry(1, new ColorRange(0, 360, 20, 100), null)]);
        var fixedRed = new Colorization(ColorModel.Hsl, 10, 10, [new ColorEntry(1, null, "hex:d6249f")]);
        MenuLayer L(string id, LayerKind kind, Colorization? c) =>
            new(id, id, kind, c, [new Variant("a", "A", 3), new Variant("b", "B", 1), new Variant("c", "C", 0.5)]);
        var layers = new[] { L("bg", LayerKind.Dynamic, hue), L("body", LayerKind.Static, fixedRed), L("hat", LayerKind.Custom, null) };
        var rule = new IncompatibilityRule(RuleType.Exclude, new RuleTarget("bg", "c"), [new RuleTarget("hat", "c")]);
        return new MenuBook(2, null, new Dimensions(2, 2), "C", "S",
            [new MenuRecipe("cat", "Cat", 2, ["bg", "body", "hat"], layers, [rule],
                new Dictionary<string, double> { ["hat"] = 25 }),
             new MenuRecipe("dog", "Dog", 1, ["bg", "hat"], [layers[0], layers[2]], [],
                new Dictionary<string, double>())],
            new MenuArt(MenuArtMode.Loose, 0, 0, []));
    }

    /// <summary>Rolls eight accepted tickets and joins their wire strings — every decision and DNA.</summary>
    internal static string Rolled()
    {
        var menu = Ticketer.Prepare(Book());
        var rng = new SplitMix64Rng(SeedHash.ToUlong("freeze-seed"));
        var wires = new List<string>();
        while (wires.Count < 8)
            if (Ticketer.TryRoll(menu, rng) is { } t) wires.Add(t.ToWire());
        return string.Join("\n", wires);
    }

    [Fact]
    public void The_roll_is_frozen() => Assert.Equal(Expected, Rolled());

    // Captured from the net10.0 build (2026-11-04): Dynamic, Static, Custom, an absent hat,
    // both recipes. A change here means every seed rolls differently.
    private const string Expected =
        "v1|cat|bg:a@98.16832570768653,0.30958327307593153,Hsv,30,40,Dynamic|body:a@318.5393258426966,0.712,Hsl,10,10,Static|hat:a@,,,,,Custom|0f904fdaf6cf8c9b4a303cb05b96a1f6574f7d809550a2a6203150705bc75937"
        + "\n"
        + "v1|dog|bg:a@100.11461457880556,0.6852051352840153,Hsv,30,40,Dynamic|hat:b@,,,,,Custom|fcf0bb1e80787a00b982985b61a12ce7b5ec07866de27d54d312665f016fcd3e"
        + "\n"
        + "v1|dog|bg:a@224.37056982274413,0.21128331074800705,Hsv,30,40,Dynamic|hat:a@,,,,,Custom|9cfa5143beade1c1f07b25338e1e3739863ff9a350c50df807a90aaaffd89ed2"
        + "\n"
        + "v1|cat|bg:a@139.12843743757946,0.6531986640831299,Hsv,30,40,Dynamic|body:a@318.5393258426966,0.712,Hsl,10,10,Static|hat:a@,,,,,Custom|b82e997d415222ee971216f0885a49328a27a5094b15c62975cdf1053d670f64"
        + "\n"
        + "v1|cat|bg:a@349.3265384434492,0.4732416878397667,Hsv,30,40,Dynamic|body:a@318.5393258426966,0.712,Hsl,10,10,Static|hat:a@,,,,,Custom|d5b535038c338dce9efa0d614c41416d1d77389a41f32b066dcd8abd784534a1"
        + "\n"
        + "v1|cat|bg:b@246.71449415137678,0.8359021002743533,Hsv,30,40,Dynamic|body:a@318.5393258426966,0.712,Hsl,10,10,Static|hat:a@,,,,,Custom|7c522551148eb2dbd4c50bb5a212d3899245a363271b2107870a9d162ee5e4ae"
        + "\n"
        + "v1|cat|bg:a@171.72448428903772,0.30125303305950984,Hsv,30,40,Dynamic|body:a@318.5393258426966,0.712,Hsl,10,10,Static|hat:a@,,,,,Custom|0c6cfe1151bb63686ae29918ca846efee6941a0b0540e6ef10735a45900a7a00"
        + "\n"
        + "v1|cat|bg:a@4.024337362755754,0.4503768334163486,Hsv,30,40,Dynamic|body:a@318.5393258426966,0.712,Hsl,10,10,Static|cd619f06315391136e6f7e6e2baf0da308bc0b65e17b07110a85e09362a10b5e";
}
