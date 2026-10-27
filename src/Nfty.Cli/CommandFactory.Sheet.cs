using System.CommandLine;
using System.Globalization;
using Nfty.Core.Publish;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Nfty.Cli;

public static partial class CommandFactory
{
    /// <summary>
    /// The <c>sheet</c> group: commands on a spritesheet PNG, which carries its own grid inside the
    /// file.
    /// </summary>
    /// <returns>The group.</returns>
    private static Command SheetGroup()
    {
        var group = new Command("sheet", "Work with a spritesheet: read or set the grid it carries.");
        group.Subcommands.Add(SheetGridCommand());
        return group;
    }

    private static Command SheetGridCommand()
    {
        var png = new Argument<string>("png") { Description = "The spritesheet PNG." };
        var cols = new Option<int>("--cols") { Description = "Columns. Given with --rows." };
        var rows = new Option<int>("--rows") { Description = "Rows. Given with --cols." };
        var cell = new Option<string?>("--cell")
        {
            Description = "Cell size as WxH, e.g. 64x64. Instead of --cols and --rows.",
        };
        var clear = new Option<bool>("--clear") { Description = "Take the grid out of the file." };

        var cmd = new Command("grid",
            "Show or set the grid a spritesheet carries. With no options, prints the embedded grid "
            + "(or none) and the grid detected from the pixels. With --cols and --rows, or --cell, "
            + "writes one into the file, keeping every pixel. --clear removes it.")
        { png, cols, rows, cell, clear };

        cmd.Validators.Add(result =>
        {
            bool colsRows = result.IsGiven(cols) || result.IsGiven(rows);
            if (result.IsGiven(cols) != result.IsGiven(rows))
                result.AddError("--cols and --rows describe the grid together; pass both.");
            if (colsRows && result.IsGiven(cell))
                result.AddError("--cell and --cols/--rows are two ways to say one grid; pass one.");
            if (result.GetValue(clear) && (colsRows || result.IsGiven(cell)))
                result.AddError("--clear removes the grid; it cannot be combined with --cols, --rows or --cell.");
        });

        cmd.SetAction(parse =>
        {
            string file = parse.GetValue(png)!;
            if (!File.Exists(file)) throw new FileNotFoundException($"No spritesheet at '{file}'.", file);

            bool setCols = parse.IsGiven(cols);
            bool setCell = parse.GetValue(cell) is { Length: > 0 };

            if (parse.GetValue(clear))
            {
                bool had = SheetGrid.Read(file) is not null;
                SheetGrid.RemoveFrom(file);
                Console.WriteLine(had ? $"Removed the grid from {file}." : $"{file} carries no grid.");
                return 0;
            }

            if (setCols || setCell)
            {
                var info = Image.Identify(file);
                SheetGrid? grid;
                string? problem;
                if (setCell)
                {
                    var (cw, ch) = ParseCell(parse.GetValue(cell)!);
                    problem = SheetGrid.TryFromCellSize(cw, ch, info.Width, info.Height, out grid);
                }
                else
                {
                    problem = SheetGrid.TryFromColumnsRows(
                        parse.GetValue(cols), parse.GetValue(rows), info.Width, info.Height, out grid);
                }
                if (problem is not null) throw new InvalidOperationException(problem);

                SheetGrid.WriteInto(file, grid!);
                Console.WriteLine($"Wrote grid {grid!.ToChunkText()} into {file}.");
                return 0;
            }

            Console.WriteLine("embedded: " + (SheetGrid.Read(file)?.ToChunkText() ?? "none"));
            using var image = Image.Load<Rgba32>(file);
            Console.WriteLine("detected: " + (SheetGrid.Detect(image)?.ToChunkText() ?? "none"));
            return 0;
        });
        return cmd;
    }

    private static (int Width, int Height) ParseCell(string text)
    {
        string[] parts = text.Split('x', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int w)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int h))
            return (w, h);
        throw new InvalidOperationException($"--cell must be WIDTHxHEIGHT, like 64x64; got '{text}'.");
    }
}
