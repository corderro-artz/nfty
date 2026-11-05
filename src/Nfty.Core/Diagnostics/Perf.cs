using System.Diagnostics;
using System.Globalization;

namespace Nfty.Core.Diagnostics;

/// <summary>
/// One measured scope's running totals.
/// </summary>
/// <param name="Name">What was measured.</param>
/// <param name="Calls">How many times it ran.</param>
/// <param name="TotalMs">Wall-clock time across every call.</param>
/// <param name="MaxMs">The slowest single call — the number a user actually feels.</param>
/// <param name="AllocatedBytes">Summed thread-local allocation deltas from scopes that start and
/// end on the same thread. Check <see cref="AllocationAvailable"/> before interpreting the total.</param>
public readonly record struct PerfEntry(string Name, long Calls, double TotalMs, double MaxMs, long AllocatedBytes)
{
    /// <summary>Mean time per call.</summary>
    public double MeanMs => Calls == 0 ? 0 : TotalMs / Calls;

    /// <summary>Whether every measured call started and ended on the same thread, allowing its
    /// thread-local allocation delta to be included. False makes the allocation column unavailable.</summary>
    /// <remarks>A delta includes allocations by that thread during the interval, not allocations on
    /// other threads. It does not attribute all allocations of an asynchronous operation.</remarks>
    public bool AllocationAvailable { get; init; } = true;
}

/// <summary>
/// A tiny always-available profiler: named scopes, wall time, and managed allocation.
///
/// <para>Off by default and free when off — <see cref="Measure"/> returns a struct whose Dispose does
/// nothing, so an unenabled scope costs one branch and no allocation. Turn it on with the
/// <c>NFTY_PERF=1</c> environment variable, or from code with <see cref="Enable"/>, which is what
/// the benchmark tests do.</para>
///
/// <para>Deliberately not BenchmarkDotNet: this measures whole user-facing operations inside the
/// real app (opening a Set, realizing a row of thumbnails) rather than micro-benchmarking a method
/// in isolation, and it has to be able to run in a GUI process. It is a stopwatch and a dictionary,
/// which is the right size for the question.</para>
/// </summary>
public static class Perf
{
    private sealed class Bucket
    {
        public long Calls;
        public long Ticks;
        public long MaxTicks;
        public long Bytes;
        public bool AllocationAvailable = true;
    }

    private static readonly Dictionary<string, Bucket> Buckets = new(StringComparer.Ordinal);
    private static readonly Lock Gate = new();

    /// <summary>Whether measurement is running. Read on every scope, so it is a plain field.</summary>
    public static bool Enabled { get; private set; } =
        Environment.GetEnvironmentVariable("NFTY_PERF") == "1";

    /// <summary>Turns measurement on and clears whatever was collected before.</summary>
    public static void Enable()
    {
        lock (Gate) { Buckets.Clear(); Enabled = true; }
    }

    /// <summary>Turns measurement off. Collected totals are kept so a report can still be read.</summary>
    public static void Disable() => Enabled = false;

    /// <summary>Discards every collected total.</summary>
    public static void Reset()
    {
        lock (Gate) Buckets.Clear();
    }

    /// <summary>
    /// Measures the enclosing block.
    /// </summary>
    /// <param name="name">The scope's name; totals accumulate per name.</param>
    /// <returns>A scope to dispose at the end of the block. Disposing a disabled scope does nothing.</returns>
    /// <remarks>Wall time spans thread changes. Allocation deltas are thread-local and available only
    /// when the scope starts and ends on the same thread; they do not measure worker-thread allocations.</remarks>
    /// <example><code>using var _ = Perf.Measure("SetReader.Read");</code></example>
    public static Scope Measure(string name) =>
        Enabled ? new Scope(name, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

    private static void Add(string name, long ticks, long? bytes)
    {
        lock (Gate)
        {
            if (!Buckets.TryGetValue(name, out var b)) Buckets[name] = b = new Bucket();
            b.Calls++;
            b.Ticks += ticks;
            if (ticks > b.MaxTicks) b.MaxTicks = ticks;
            if (bytes is { } measured) b.Bytes += measured;
            else b.AllocationAvailable = false;
        }
    }

    /// <summary>Everything measured so far, slowest total first.</summary>
    /// <returns>One entry per scope name.</returns>
    public static IReadOnlyList<PerfEntry> Snapshot()
    {
        lock (Gate)
        {
            var perTick = 1000.0 / Stopwatch.Frequency;
            return Buckets
                .Select(kv => new PerfEntry(kv.Key, kv.Value.Calls, kv.Value.Ticks * perTick,
                    kv.Value.MaxTicks * perTick, kv.Value.Bytes)
                    { AllocationAvailable = kv.Value.AllocationAvailable })
                .OrderByDescending(e => e.TotalMs)
                .ToList();
        }
    }

    /// <summary>
    /// The snapshot as a fixed-width table.
    /// </summary>
    /// <returns>One line per scope, plus a header. Empty string when nothing was measured.</returns>
    /// <remarks>Numbers use invariant formatting, as <c>Stats/</c>'s reports do. An allocation total
    /// is <c>n/a</c> if any of its calls ended on a different thread; times are still reported.</remarks>
    public static string Report()
    {
        var rows = Snapshot();
        if (rows.Count == 0) return "";

        var w = Math.Max(4, rows.Max(r => r.Name.Length));
        var sb = new System.Text.StringBuilder();
        // Scopes that run inside parallel bodies overlap, so their times add up to more than the
        // wall clock elapsed.
        sb.Append("ms (summed across threads)\n");
        sb.Append("SCOPE".PadRight(w))
          .Append("   CALLS      TOTAL ms       MEAN ms        MAX ms      ALLOC KB\n");
        foreach (var r in rows)
            sb.Append(r.Name.PadRight(w))
              .Append(r.Calls.ToString(CultureInfo.InvariantCulture).PadLeft(8))
              .Append(r.TotalMs.ToString("N2", CultureInfo.InvariantCulture).PadLeft(13))
              .Append(r.MeanMs.ToString("N3", CultureInfo.InvariantCulture).PadLeft(14))
              .Append(r.MaxMs.ToString("N2", CultureInfo.InvariantCulture).PadLeft(14))
              .Append((r.AllocationAvailable
                  ? (r.AllocatedBytes / 1024.0).ToString("N0", CultureInfo.InvariantCulture)
                  : "n/a").PadLeft(14))
              .Append('\n');
        return sb.ToString();
    }

    /// <summary>One measured block. A default instance is the disabled case and does nothing.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        private readonly long _bytes;
        private readonly int _threadId;

        internal Scope(string name, long start, long bytes)
        {
            _name = name;
            _start = start;
            _bytes = bytes;
            _threadId = Environment.CurrentManagedThreadId;
        }

        /// <summary>Closes the scope, folds its wall time into the totals, and includes the thread-local
        /// allocation delta only when closing on the creating thread.</summary>
        public void Dispose()
        {
            if (_name is null) return;
            Add(_name, Stopwatch.GetTimestamp() - _start,
                Environment.CurrentManagedThreadId == _threadId
                    ? GC.GetAllocatedBytesForCurrentThread() - _bytes
                    : null);
        }
    }
}
