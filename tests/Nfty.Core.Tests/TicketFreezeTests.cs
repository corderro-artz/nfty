using System.Security.Cryptography;
using System.Text;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Nfty.Core.Generation;
using SixLabors.ImageSharp.Formats.Png;

namespace Nfty.Core.Tests;

/// <summary>
/// The roll, recorded before it was extracted into Nfty.Ticket.
///
/// This is the guard that moving the roll out of Generator changed NO OUTPUT THAT HAS EVER BEEN
/// PRODUCED. It only works because it was captured first; a baseline written after a refactor
/// records the refactor's opinion of itself.
///
/// DO NOT REGENERATE THIS FIXTURE TO MAKE A FAILURE GO AWAY. A deliberate change to roll order
/// means a NEW baseline beside this one and a note saying why, exactly as FixtureFreezeTests says
/// about the archives it guards.
///
/// Recaptured once, 2026-10-26, because the demo BOOK changed (more variants per layer), not the
/// roll: a baseline of a book that no longer ships guards nothing. See FixtureFreezeTests.
/// </summary>
public class TicketFreezeTests
{
    private const string Seed = "nfty-ticket-baseline-0001";
    private const int Count = 200;

    private static string FixturePath() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "roll-baseline-chest-demo.txt");

    /// <summary>The demo book's first 200 DNAs plus asset 0001's PNG hash, as one text block.</summary>
    private static string CaptureBaseline()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string cbk = DemoCookBook.WriteTo(dir);
            using var book = CookBookArchive.Read(cbk);
            using var set = Generator.Generate(book, new GenerateOptions(Count: Count, Seed: Seed));

            var sb = new StringBuilder();
            foreach (var asset in set.Assets)
                sb.Append(asset.Dna).Append('\n');

            using var ms = new MemoryStream();
            set.Assets[0].Image.Save(ms, new PngEncoder());
            sb.Append("image0001=")
              .Append(Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant())
              .Append('\n');
            return sb.ToString();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void The_demo_book_rolls_exactly_what_it_has_always_rolled()
    {
        string path = FixturePath();
        string actual = CaptureBaseline();

        if (!File.Exists(path))
        {
            // First run only: record the baseline into the SOURCE fixtures folder, not the copy in
            // bin, so it can be committed. Fail loudly so nobody mistakes a capture for a pass.
            string source = Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures",
                "roll-baseline-chest-demo.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(source))!);
            File.WriteAllText(Path.GetFullPath(source), actual);
            Assert.Fail(
                $"Baseline captured to {Path.GetFullPath(source)}. Commit it, then re-run. "
                + "This failure is expected exactly once.");
        }

        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n"), actual.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_baseline_has_the_shape_it_claims()
    {
        // Guards the guard: a truncated or empty fixture would make the test above pass vacuously.
        var lines = File.ReadAllLines(FixturePath());
        Assert.Equal(Count + 1, lines.Length);
        Assert.All(lines.Take(Count), l => Assert.Equal(64, l.Length));
        Assert.StartsWith("image0001=", lines[^1], StringComparison.Ordinal);
    }
}
