using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Nfty.Core;

/// <summary>
/// The one place this engine runs work in parallel, so the rules for doing it safely live together
/// rather than being restated at each call site.
/// </summary>
/// <remarks>
/// <para><b>Only independent, non-aggregating work goes through here.</b> Every body must be a pure
/// function of its own item: no shared accumulator, no running total, no appending to a shared list,
/// no ordering derived from completion. That restriction is what keeps output reproducible across
/// machines — a result that never depends on how many threads ran it cannot vary with core count or
/// scheduling. Anything that sums (rarity, the DNA-space count) stays sequential, because
/// floating-point addition is not associative and a different order is a different number.</para>
///
/// <para>Results are written to a caller-owned slot per item, never collected from the loop, so the
/// output order is the input order on any machine.</para>
///
/// <para>Exceptions come out as themselves, and deterministically: <see cref="Parallel"/> wraps
/// everything in an <see cref="AggregateException"/>, but this engine's callers — and
/// <c>ErrorReport</c> — expect the engine's own types and messages. When several items fail, the
/// one with the LOWEST index is rethrown with its stack intact, so the message never varies with
/// thread count. A failure therefore does not stop the loop outright — only indices above the lowest
/// failure so far are skipped; every index below it still runs, since it could fail too and the
/// sequential loop would have reported it. Cancellation still wins when the token fired.</para>
///
/// <para>A sequential <see cref="Parallelism"/> (or a single item) is a plain <c>for</c> loop with no
/// scheduler involved, which makes it the oracle the parallel path is tested against.</para>
/// </remarks>
internal static class ParallelWork
{
    /// <summary>Runs <paramref name="body"/> for each index, in parallel.</summary>
    /// <param name="count">How many indices, from zero.</param>
    /// <param name="p">The degree of parallelism.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one index. Must not touch shared mutable state.</param>
    public static void For(int count, Parallelism p, CancellationToken cancellationToken, Action<int> body)
    {
        if (count <= 0) return;
        if (count == 1 || p.IsSequential)
        {
            for (var i = 0; i < count; i++) { cancellationToken.ThrowIfCancellationRequested(); body(i); }
            return;
        }

        var failures = new ConcurrentBag<(int Index, Exception Error)>();
        var lowest = int.MaxValue;
        try
        {
            Parallel.For(0, count,
                new ParallelOptions { MaxDegreeOfParallelism = p.Effective, CancellationToken = cancellationToken },
                i =>
                {
                    // A failure does NOT stop the loop: an index below it that no worker has reached
                    // yet could fail too, and the sequential loop would have reported THAT one. Only
                    // indices above the lowest failure so far are skipped — they can never be it.
                    if (i > Volatile.Read(ref lowest)) return;
                    try { body(i); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add((i, ex));
                        int seen;
                        while (i < (seen = Volatile.Read(ref lowest)) &&
                               Interlocked.CompareExchange(ref lowest, i, seen) != seen) { }
                    }
                });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && !failures.IsEmpty)
        {
            RethrowLowest(failures);
        }
        if (!failures.IsEmpty) RethrowLowest(failures);
    }

    /// <summary>Runs <paramref name="body"/> for each item, in parallel.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="p">The degree of parallelism.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one item. Must not touch shared mutable state.</param>
    public static void ForEach<T>(IReadOnlyList<T> items, Parallelism p, CancellationToken cancellationToken,
        Action<T> body) =>
        For(items.Count, p, cancellationToken, i => body(items[i]));

    /// <summary>
    /// Like <see cref="ForEach{T}"/>, and calls <paramref name="reportDone"/> after each item with the
    /// number finished so far: one call at a time, strictly increasing.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="p">The degree of parallelism.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one item.</param>
    /// <param name="reportDone">Receives the finished count; null reports nothing.</param>
    public static void ForEachReporting<T>(IReadOnlyList<T> items, Parallelism p,
        CancellationToken cancellationToken, Action<T> body, Action<int>? reportDone)
    {
        var gate = new Lock();
        var done = 0;
        For(items.Count, p, cancellationToken, i =>
        {
            body(items[i]);
            if (reportDone is null) return;
            // Counting and reporting together: two threads must not take 1 and 2 and report them
            // in the other order.
            lock (gate) reportDone(++done);
        });
    }

    /// <summary>Runs an asynchronous <paramref name="body"/> for each item, in parallel.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="p">The degree of parallelism.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one item. Must not touch shared mutable state.</param>
    /// <returns>A task that completes when every item has been processed.</returns>
    public static async Task ForEachAsync<T>(IReadOnlyList<T> items, Parallelism p,
        CancellationToken cancellationToken, Func<T, CancellationToken, ValueTask> body)
    {
        if (items.Count == 0) return;
        if (items.Count == 1 || p.IsSequential)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await body(item, cancellationToken);
            }
            return;
        }

        var failures = new ConcurrentBag<(int Index, Exception Error)>();
        var lowest = int.MaxValue;
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, items.Count),
                new ParallelOptions { MaxDegreeOfParallelism = p.Effective, CancellationToken = cancellationToken },
                async (i, ct) =>
                {
                    // As in For: a failure skips only higher indices, so the lowest one still runs.
                    if (i > Volatile.Read(ref lowest)) return;
                    try { await body(items[i], ct); }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failures.Add((i, ex));
                        int seen;
                        while (i < (seen = Volatile.Read(ref lowest)) &&
                               Interlocked.CompareExchange(ref lowest, i, seen) != seen) { }
                    }
                });
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && !failures.IsEmpty)
        {
            RethrowLowest(failures);
        }
        if (!failures.IsEmpty) RethrowLowest(failures);
    }

    private static void RethrowLowest(ConcurrentBag<(int Index, Exception Error)> failures)
    {
        var lowest = failures.OrderBy(f => f.Index).First();
        ExceptionDispatchInfo.Capture(lowest.Error).Throw();
    }
}
