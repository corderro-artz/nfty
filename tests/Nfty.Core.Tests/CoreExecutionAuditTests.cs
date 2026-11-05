using System.Globalization;
using Nfty.Core.Diagnostics;
using Xunit;

namespace Nfty.Core.Tests;

/// <summary>Profiler and parallel failure boundaries discovered during the Core audit.</summary>
public class CoreExecutionAuditTests
{
    [Fact]
    public void One_profiler_snapshot_formats_identically_across_locales()
    {
        var original = CultureInfo.CurrentCulture;
        Perf.Enable();
        try
        {
            using (Perf.Measure("locale")) GC.KeepAlive(new byte[20000]);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var expected = Perf.Report();
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal(expected, Perf.Report());
        }
        finally { CultureInfo.CurrentCulture = original; Perf.Disable(); Perf.Reset(); }
    }

    [Fact]
    public void A_thread_transfer_does_not_report_unrelated_allocation_counters()
    {
        Perf.Enable();
        try
        {
            GC.KeepAlive(new byte[1000000]);
            var scope = Perf.Measure("transfer");
            var other = new Thread(scope.Dispose);
            other.Start(); other.Join();
            var entry = Assert.Single(Perf.Snapshot());
            Assert.True(entry.AllocatedBytes >= 0);
            Assert.False(entry.AllocationAvailable);
            Assert.Contains("n/a", Perf.Report());
        }
        finally { Perf.Disable(); Perf.Reset(); }
    }

    [Fact]
    public void Same_thread_allocations_remain_available()
    {
        Perf.Enable();
        try
        {
            using (Perf.Measure("local")) GC.KeepAlive(new byte[20000]);
            var entry = Assert.Single(Perf.Snapshot());
            Assert.True(entry.AllocationAvailable);
            Assert.True(entry.AllocatedBytes >= 20000);
            Assert.DoesNotContain("n/a", Perf.Report());
        }
        finally { Perf.Disable(); Perf.Reset(); }
    }

    [Fact]
    public void A_callback_cancellation_exception_keeps_its_type_at_every_thread_count()
    {
        foreach (var degree in new[] { 1, 2, 4 })
        {
            var expected = new OperationCanceledException("callback, not operation");
            var actual = Assert.Throws<OperationCanceledException>(() =>
                ParallelWork.For(64, new Parallelism(degree), CancellationToken.None,
                    i => { if (i == 0) throw expected; }));
            Assert.Same(expected, actual);
        }
    }

    [Fact]
    public void A_lower_callback_cancellation_is_not_suppressed_by_a_higher_fault()
    {
        foreach (var degree in new[] { 1, 2, 4 })
            Assert.Throws<OperationCanceledException>(() =>
                ParallelWork.For(64, new Parallelism(degree), CancellationToken.None, i =>
                {
                    if (i == 33) throw new InvalidOperationException("higher");
                    if (i < 30) Thread.Sleep(1);
                    if (i == 30) throw new OperationCanceledException("lower");
                }));
    }

    [Fact]
    public async Task The_async_loop_also_preserves_lower_callback_cancellation()
    {
        foreach (var degree in new[] { 1, 2, 4 })
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                ParallelWork.ForEachAsync(Enumerable.Range(0, 64).ToList(), new Parallelism(degree),
                    CancellationToken.None, async (i, _) =>
                    {
                        if (i == 33) throw new InvalidOperationException("higher");
                        if (i < 30) await Task.Delay(1);
                        if (i == 30) throw new OperationCanceledException("lower");
                    }));
    }
}
