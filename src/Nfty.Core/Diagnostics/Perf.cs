using System.Diagnostics;

namespace Nfty.Core.Diagnostics;

/// <summary>
/// One measured scope's running totals.
/// </summary>
/// <param name="Name">What was measured.</param>
/// <param name="Calls">How many times it ran.</param>
/// <param name="TotalMs">Wall-clock time across every call.</param>
/// <param name="MaxMs">The slowest single call — the number a user actually feels.</param>
/// <param name="AllocatedBytes">Managed bytes allocated inside the scope, summed.</param>
public readonly record struct PerfEntry(string Name, long Calls, double TotalMs, double MaxMs, long AllocatedBytes)
{
    /// <summary>Mean time per call.</summary>
    public double MeanMs => Calls == 0 ? 0 : TotalMs / Calls;
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
    /// <example><code>using var _ = Perf.Measure("SetReader.Read");</code></example>
    public static Scope Measure(string name) =>
        Enabled ? new Scope(name, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

    /// <summary>Records one already-timed operation, for call sites that cannot wrap a block.</summary>
    /// <param name="name">The scope's name.</param>
    /// <param name="elapsed">How long it took.</param>
    public static void Record(string name, TimeSpan elapsed)
    {
        if (!Enabled) return;
        Add(name, (long)(elapsed.TotalSeconds * Stopwatch.Frequency), 0);
    }

    private static void Add(string name, long ticks, long bytes)
    {
        lock (Gate)
        {
            if (!Buckets.TryGetValue(name, out var b)) Buckets[name] = b = new Bucket();
            b.Calls++;
            b.Ticks += ticks;
            if (ticks > b.MaxTicks) b.MaxTicks = ticks;
            b.Bytes += bytes;
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
                    kv.Value.MaxTicks * perTick, kv.Value.Bytes))
                .OrderByDescending(e => e.TotalMs)
                .ToList();
        }
    }

    /// <summary>
    /// The snapshot as a fixed-width table.
    /// </summary>
    /// <returns>One line per scope, plus a header. Empty string when nothing was measured.</returns>
    /// <remarks>Formatted the way <c>Stats/</c>'s reports are — invariant, so two runs on two
    /// machines can be diffed.</remarks>
    public static string Report()
    {
        var rows = Snapshot();
        if (rows.Count == 0) return "";

        var w = Math.Max(4, rows.Max(r => r.Name.Length));
        var sb = new System.Text.StringBuilder();
        sb.Append("SCOPE".PadRight(w))
          .Append("   CALLS      TOTAL ms       MEAN ms        MAX ms      ALLOC KB\n");
        foreach (var r in rows)
            sb.Append(r.Name.PadRight(w))
              .Append(r.Calls.ToString().PadLeft(8))
              .Append(r.TotalMs.ToString("N2").PadLeft(13))
              .Append(r.MeanMs.ToString("N3").PadLeft(14))
              .Append(r.MaxMs.ToString("N2").PadLeft(14))
              .Append((r.AllocatedBytes / 1024.0).ToString("N0").PadLeft(14))
              .Append('\n');
        return sb.ToString();
    }

    /// <summary>One measured block. A default instance is the disabled case and does nothing.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly string? _name;
        private readonly long _start;
        private readonly long _bytes;

        internal Scope(string name, long start, long bytes)
        {
            _name = name;
            _start = start;
            _bytes = bytes;
        }

        /// <summary>Closes the scope and folds its time and allocation into the totals.</summary>
        public void Dispose()
        {
            if (_name is null) return;
            Add(_name, Stopwatch.GetTimestamp() - _start,
                GC.GetAllocatedBytesForCurrentThread() - _bytes);
        }
    }
}
