using System.Collections.Concurrent;
using System.Globalization;
using SixLabors.ImageSharp;

namespace Nfty.Core;

/// <summary>
/// How wide independent per-item work may run. Output never depends on it: every parallel step
/// writes to its own slot, so one thread and many give byte-identical results.
/// </summary>
/// <remarks>
/// Equality is by <see cref="Requested"/> alone. The degree actually used is
/// <see cref="Effective"/>, which the library clamps to the machine's logical processor count.
/// </remarks>
/// <param name="Requested">The degree asked for; 0 means every logical processor. Negative is refused.</param>
public sealed record Parallelism(int Requested)
{
    private static readonly ConcurrentDictionary<int, Configuration> ImageConfigurations = new();

    private static readonly AsyncLocal<Func<int>?> Override = new();

    /// <summary>
    /// Test seam: replaces <see cref="Environment.ProcessorCount"/> for the calling async flow only,
    /// so tests running side by side never see each other's value.
    /// </summary>
    internal static Func<int>? ProcessorOverride
    {
        get => Override.Value;
        set => Override.Value = value;
    }

    /// <summary>The machine's logical processor count (affinity- and container-aware).</summary>
    public static int LogicalProcessors => ProcessorOverride?.Invoke() ?? Environment.ProcessorCount;

    /// <summary>Two thirds of the logical processors, floored, and at least one.</summary>
    public static int DefaultDegree => Math.Max(1, LogicalProcessors * 2 / 3);

    /// <summary>The default degree.</summary>
    public static Parallelism Default => new(DefaultDegree);

    /// <summary>One thread, on a plain loop with no scheduler involved.</summary>
    public static Parallelism Sequential { get; } = new(1);

    /// <summary>Every logical processor.</summary>
    public static Parallelism All { get; } = new(0);

    /// <summary>The environment variable that overrides the saved or default count for one run.</summary>
    public const string EnvironmentVariable = "NFTY_THREADS";

    /// <summary>
    /// Reads a thread count as people spell it: a whole number, or <c>max</c>; <c>0</c> and
    /// <c>max</c> both mean every logical processor. The one rule the CLI's <c>--threads</c>, the
    /// <c>NFTY_THREADS</c> variable and the app's Settings card all share. Nothing is clamped
    /// here: a count above the machine's is a valid request, and <see cref="Effective"/> brings it down.
    /// </summary>
    /// <param name="text">The text, with surrounding whitespace ignored.</param>
    /// <param name="parallelism">The count when this returns true; <see cref="Default"/> otherwise.</param>
    /// <returns>False for empty text, a sign, a fraction or anything else that is not a whole number or <c>max</c>.</returns>
    public static bool TryParse(string? text, out Parallelism parallelism)
    {
        parallelism = Default;
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return false;
        if (text.Equals("max", StringComparison.OrdinalIgnoreCase)) { parallelism = All; return true; }
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return false;
        parallelism = n == 0 ? All : new Parallelism(n);
        return true;
    }

    /// <summary>The degree requested; 0 means all.</summary>
    public int Requested { get; } = Requested >= 0
        ? Requested
        : throw new ArgumentOutOfRangeException(nameof(Requested), Requested, "A degree of parallelism cannot be negative.");

    /// <summary>The degree that actually runs: clamped to between 1 and the logical processor count.</summary>
    public int Effective
    {
        get
        {
            var lp = Math.Max(1, LogicalProcessors);
            return Requested == 0 ? lp : Math.Clamp(Requested, 1, lp);
        }
    }

    /// <summary>True when work runs on the calling thread.</summary>
    public bool IsSequential => Effective == 1;

    /// <summary>
    /// A capped ImageSharp configuration: single-threaded inside a parallel body (no nested
    /// fan-out), <see cref="Effective"/>-wide when the outer loop is sequential.
    /// <see cref="Configuration.Default"/> is never mutated.
    /// </summary>
    internal Configuration ImageConfiguration(bool insideParallelBody)
    {
        var degree = insideParallelBody ? 1 : Effective;
        return ImageConfigurations.GetOrAdd(degree, d =>
        {
            var c = Configuration.Default.Clone();
            c.MaxDegreeOfParallelism = d;
            return c;
        });
    }
}
