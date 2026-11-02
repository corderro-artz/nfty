// The full engine from your own program: cook, render a ticket, rise to 3D.
//
//   dotnet run --project samples/Nfty.Embedding.Sample -- mybook.cbk mybook.mnu ./out
//
// Make the Menu first:  nfty menu mybook.cbk --out mybook.mnu

// --8<-- [start:usings]
using Nfty.Core.Formats;       // CookBookArchive, LoadedCookBook
using Nfty.Core.Generation;    // Generator, GenerateOptions, GeneratedSet
using Nfty.Core.Publish;       // MenuRenderer
using Nfty.Model;              // Riser, ModelWriter, StackDepth, RiseOptions
using Nfty.Ticket;             // MenuArchive, AssetTicket
using SixLabors.ImageSharp;    // SaveAsPng - arrives through Nfty.Core
// --8<-- [end:usings]

if (args.Length < 3)
{
    Console.Error.WriteLine("usage: Nfty.Embedding.Sample <book.cbk> <menu.mnu> <outdir>");
    return 2;
}

string outDir = args[2];
Directory.CreateDirectory(outDir);

// --8<-- [start:cook]
// Reading decodes every variant PNG, so the book owns images: dispose it.
using LoadedCookBook book = CookBookArchive.Read(args[0]);

// Generate validates the book itself and throws on any problem, so there is nothing to pre-check.
using GeneratedSet set = Generator.Generate(book, new GenerateOptions(Count: 3, Seed: "embed-demo"));

foreach (GeneratedAsset asset in set.Assets)                // the set owns the asset images too
{
    asset.Image.SaveAsPng(Path.Combine(outDir, $"{asset.SetNumber:0000}.png"));
    Console.WriteLine($"#{asset.SetNumber} {asset.RecipeName} {asset.Dna[..12]}");
}
// --8<-- [end:cook]

// --8<-- [start:render]
// A ticket rolled elsewhere (a server, nfty serve) renders to the identical pixels. Note
// MenuRenderer needs Nfty.Core; the Nfty.Ticket sample draws the same ticket without it.
using var menu = MenuArchive.Open(args[1]);
AssetTicket ticket = Ticketer.TryRoll(Ticketer.Prepare(menu.Book),
                                      new SplitMix64Rng(7))
                     ?? throw new InvalidOperationException("The rules rejected the roll; roll again.");
using (var image = MenuRenderer.Render(menu, ticket))       // you own the returned image
    image.SaveAsPng(Path.Combine(outDir, "ticket.png"));
// --8<-- [end:render]

// --8<-- [start:rise]
// Rise the first cooked asset into a 3D model, the way a Set written with --models does.
GeneratedAsset first = set.Assets[0];
var options = new RiseOptions { Format = ModelFormat.Glb, Pivot = Pivot.BottomCenter, VoxelSize = 1f };

using StackRise? rise = StackDepth.ForAsset(book.Recipe(first.RecipeId), first, "asset #1");
Mesh? mesh = Riser.TryBuild(first.Image, rise?.Depth, options, "asset #1", rise?.Faces);

if (mesh is null)
    Console.WriteLine("Nothing fully opaque: no model.");   // not an error; a batch skips and names it
else
    foreach (ModelFile file in ModelWriter.Write(mesh, first.Image, options, "0001", rise?.Faces))
    {
        File.WriteAllBytes(Path.Combine(outDir, file.FileName), file.Bytes);
        Console.WriteLine($"model {file.FileName} {file.Bytes.Length:N0} bytes");
    }
// --8<-- [end:rise]

return 0;
