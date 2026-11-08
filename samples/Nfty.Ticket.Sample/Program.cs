// Roll and plate an asset from a Menu using Nfty.Ticket alone.
//
//   dotnet run --project samples/Nfty.Ticket.Sample -- mybook.mnu [seed]
//
// Make the Menu first:  nfty menu mybook.cbk --out mybook.mnu

// --8<-- [start:usings]
using Nfty.Core.Generation;   // SplitMix64Rng, SeedHash (they live in the Ticket assembly)
using Nfty.Core.Imaging;      // ColorConvert, ColorSpec, RgbColor
using Nfty.Core.Model;        // LayerKind
using Nfty.Ticket;            // MenuArchive, Ticketer, AssetTicket, Plating
// --8<-- [end:usings]

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: Nfty.Ticket.Sample <menu.mnu> [seed]");
    return 2;
}

string seed = args.Length > 1 ? args[1] : "player-42";

// --8<-- [start:open]
using var menu = MenuArchive.Open(args[0]);                 // reads the manifest; decodes no image
PreparedMenu prepared = Ticketer.Prepare(menu.Book);        // once; reuse it for every roll
// --8<-- [end:open]

// --8<-- [start:roll]
var rng = new SplitMix64Rng(SeedHash.ToUlong(seed));        // same seed, same drops, any machine
AssetTicket? ticket = null;
for (int attempt = 0; attempt < 1000 && ticket is null; attempt++)
    ticket = Ticketer.TryRoll(prepared, rng);               // null = a rule rejected this roll
if (ticket is null)
    throw new InvalidOperationException("No legal ticket after 1,000 attempts.");

string wire = ticket.ToWire();                              // store this, or send it
// --8<-- [end:roll]

Console.WriteLine($"seed    {seed}");
Console.WriteLine($"recipe  {ticket.RecipeId}");
Console.WriteLine($"dna     {ticket.Dna}");
Console.WriteLine($"wire    {wire}");

// --8<-- [start:plate]
AssetTicket back = AssetTicket.Parse(wire);                 // recomputes and checks the DNA

foreach (Draw draw in Plating.Plate(menu, back))            // bottom-first: paint in this order
{
    byte[] png = menu.ArtBytes(draw.ImageKey);              // atlas mode: the whole atlas PNG
    MenuCell cell = draw.Source;                            // crop this rectangle out of it

    if (draw.Kind == LayerKind.Custom)
    {
        // Full-color art: composite the cropped cell as it is.
        Console.WriteLine(FormattableString.Invariant($"  custom  {cell.Key} at ({cell.X},{cell.Y}) {cell.Width}x{cell.Height}"));
    }
    else
    {
        // Gray supplies value for HSV or lightness for HSL; keep the source pixel's alpha.
        RgbColor mid = draw.Model == ColorModel.Hsv
            ? ColorConvert.HsvToRgb(draw.Hue, draw.Sat, 128 / 255.0)
            : ColorConvert.HslToRgb(draw.Hue, draw.Sat, 128 / 255.0);
        Console.WriteLine(FormattableString.Invariant(
            $"  tinted  {cell.Key} hue {draw.Hue:0.#} sat {draw.Sat:0.##} -> mid-gray becomes {ColorSpec.Format(mid)}"));
    }
    _ = png; // a real renderer decodes this with its own PNG library and crops `cell`
}
// --8<-- [end:plate]

return 0;
