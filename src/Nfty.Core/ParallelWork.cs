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
/// <para>And exceptions come out as themselves. <see cref="Parallel"/> wraps everything in an
/// <see cref="AggregateException"/>; this engine's callers — and <c>ErrorReport</c> — expect the
/// engine's own exception types and their messages, so the first inner exception is rethrown with
/// its stack intact.</para>
/// </remarks>
internal static class ParallelWork
{
    /// <summary>Runs <paramref name="body"/> for each index, in parallel.</summary>
    /// <param name="count">How many indices, from zero.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one index. Must not touch shared mutable state.</param>
    public static void For(int count, CancellationToken cancellationToken, Action<int> body)
    {
        if (count <= 0) return;
        // One item is the common case for a smoke test and a cold start; the loop's setup costs
        // more than the work.
        if (count == 1) { body(0); return; }

        try
        {
            Parallel.For(0, count, new ParallelOptions { CancellationToken = cancellationToken }, body);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
            throw;   // unreachable; satisfies the compiler
        }
    }

    /// <summary>Runs <paramref name="body"/> for each item, in parallel.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one item. Must not touch shared mutable state.</param>
    public static void ForEach<T>(IReadOnlyList<T> items, CancellationToken cancellationToken,
        Action<T> body) =>
        For(items.Count, cancellationToken, i => body(items[i]));

    /// <summary>Runs an asynchronous <paramref name="body"/> for each item, in parallel.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="cancellationToken">Cancels the loop.</param>
    /// <param name="body">The work for one item. Must not touch shared mutable state.</param>
    /// <returns>A task that completes when every item has been processed.</returns>
    public static async Task ForEachAsync<T>(IReadOnlyList<T> items,
        CancellationToken cancellationToken, Func<T, CancellationToken, ValueTask> body)
    {
        if (items.Count == 0) return;
        if (items.Count == 1) { await body(items[0], cancellationToken); return; }

        try
        {
            await Parallel.ForEachAsync(items, cancellationToken, body);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Capture(ex.InnerExceptions[0]).Throw();
        }
    }
}
