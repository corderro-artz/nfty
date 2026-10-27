using SixLabors.ImageSharp;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>
/// The thread cap is real: clamped, honored by the loop, and deterministic about failures.
/// </summary>
public class ParallelismTests
{
    private static T WithProcessors<T>(int logical, Func<T> act)
    {
        Parallelism.ProcessorOverride = () => logical;
        try { return act(); }
        finally { Parallelism.ProcessorOverride = null; }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(8, 5)]
    [InlineData(12, 8)]
    [InlineData(16, 10)]
    [InlineData(32, 21)]
    public void Default_is_two_thirds_of_the_logical_processors_floored_with_a_minimum_of_one(int lp, int expected) =>
        Assert.Equal(expected, WithProcessors(lp, () => Parallelism.DefaultDegree));

    [Fact]
    public void Effective_is_clamped_to_the_logical_processor_count()
    {
        Assert.Equal(4, WithProcessors(4, () => new Parallelism(64).Effective));
        Assert.Equal(4, WithProcessors(4, () => new Parallelism(0).Effective));
        Assert.Equal(2, WithProcessors(4, () => new Parallelism(2).Effective));
    }

    [Fact]
    public void A_negative_degree_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new Parallelism(-1));

    [Fact]
    public void Parallelism_is_equal_by_requested_degree_alone()
    {
        Assert.Equal(new Parallelism(3), new Parallelism(3));
        Assert.NotEqual(new Parallelism(3), new Parallelism(4));
        // Building a configuration caches by degree and must not disturb equality.
        _ = new Parallelism(3).ImageConfiguration(true);
        Assert.Equal(new Parallelism(3), new Parallelism(3));
    }

    [Fact]
    public void The_inner_image_configuration_is_single_threaded_inside_a_parallel_body()
    {
        var before = Configuration.Default.MaxDegreeOfParallelism;
        var p = new Parallelism(3);
        Assert.Equal(1, p.ImageConfiguration(insideParallelBody: true).MaxDegreeOfParallelism);
        Assert.Equal(p.Effective, p.ImageConfiguration(insideParallelBody: false).MaxDegreeOfParallelism);
        Assert.Equal(before, Configuration.Default.MaxDegreeOfParallelism);
        Assert.NotSame(Configuration.Default, p.ImageConfiguration(true));
    }

    [Fact]
    public void The_sequential_path_runs_on_the_calling_thread()
    {
        var caller = Environment.CurrentManagedThreadId;
        var seen = new int[50];
        ParallelWork.For(50, Parallelism.Sequential, CancellationToken.None,
            i => seen[i] = Environment.CurrentManagedThreadId);
        Assert.All(seen, t => Assert.Equal(caller, t));
    }

    [Fact]
    public void The_loop_never_runs_wider_than_effective()
    {
        var p = new Parallelism(2);
        // The cap only means something where there are at least two processors to cap.
        Assert.SkipWhen(Parallelism.LogicalProcessors < 2, "single-processor machine");
        int running = 0, peak = 0;
        ParallelWork.For(64, p, CancellationToken.None, _ =>
        {
            var now = Interlocked.Increment(ref running);
            int seenPeak;
            while ((seenPeak = Volatile.Read(ref peak)) < now &&
                   Interlocked.CompareExchange(ref peak, now, seenPeak) != seenPeak) { }
            Thread.Sleep(5);   // hold the slot so any extra worker overlaps
            Interlocked.Decrement(ref running);
        });
        Assert.InRange(peak, 1, p.Effective);
        // Not vacuous: with a wider cap the same body really does overlap.
        peak = 0;
        ParallelWork.For(64, new Parallelism(0), CancellationToken.None, _ =>
        {
            var now = Interlocked.Increment(ref running);
            int seenPeak;
            while ((seenPeak = Volatile.Read(ref peak)) < now &&
                   Interlocked.CompareExchange(ref peak, now, seenPeak) != seenPeak) { }
            Thread.Sleep(5);
            Interlocked.Decrement(ref running);
        });
        Assert.True(peak > 2 || Parallelism.LogicalProcessors <= 2, $"wide run peaked at {peak}");
    }

    [Fact]
    public void The_lowest_failing_index_is_the_exception_reported()
    {
        var items = Enumerable.Range(0, 64).ToList();
        for (var run = 0; run < 20; run++)
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                ParallelWork.ForEach(items, Parallelism.All, CancellationToken.None, i =>
                {
                    // The later failure throws first, so completion order cannot pick the answer.
                    if (i == 40) throw new InvalidOperationException("forty");
                    if (i == 5) { Thread.Sleep(20); throw new InvalidOperationException("five"); }
                }));
            Assert.Equal("five", ex.Message);
        }
    }

    [Fact]
    public async Task The_lowest_failing_index_is_reported_by_the_async_loop_too()
    {
        var items = Enumerable.Range(0, 64).ToList();
        for (var run = 0; run < 10; run++)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ParallelWork.ForEachAsync(items, Parallelism.All, CancellationToken.None, async (i, _) =>
                {
                    if (i == 40) throw new InvalidOperationException("forty");
                    if (i == 5) { await Task.Delay(20); throw new InvalidOperationException("five"); }
                }));
            Assert.Equal("five", ex.Message);
        }
    }

    [Fact]
    public void Cancellation_surfaces_as_cancellation()
    {
        using var cts = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ParallelWork.For(100, Parallelism.All, cts.Token, i => { if (i == 3) cts.Cancel(); Thread.Sleep(1); }));
    }

    [Fact]
    public void Progress_is_reported_in_increasing_order_one_call_at_a_time()
    {
        var items = Enumerable.Range(0, 200).ToList();
        var seen = new List<int>();
        int inside = 0, overlapped = 0;
        ParallelWork.ForEachReporting(items, Parallelism.All, CancellationToken.None, _ => { }, done =>
        {
            if (Interlocked.Increment(ref inside) > 1) Interlocked.Increment(ref overlapped);
            seen.Add(done);   // a plain list: only safe if calls really are serialized
            Thread.SpinWait(200);
            Interlocked.Decrement(ref inside);
        });
        Assert.Equal(0, overlapped);
        Assert.Equal(Enumerable.Range(1, 200), seen);
    }
}
