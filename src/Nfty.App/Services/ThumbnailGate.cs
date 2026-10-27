using Nfty.Core;

namespace Nfty.App.Services;

/// <summary>
/// The one limit on how many thumbnails the Set browser decodes at once.
/// </summary>
/// <remarks>
/// <para>A screenful of tiles used to start a thread-pool decode per tile, all at once, which at 1000px
/// is forty 7 ms decodes competing with everything else the machine is doing - including the cook the
/// reader may have asked for a thread count for. The same number that bounds a cook now bounds this, so
/// "use 4 threads" means the app, not four plus however many tiles are on screen.</para>
/// <para>The in-flight counter is kept apart from the semaphore on purpose: it is what a test reads to
/// prove the limit holds, and a counter that lived inside the semaphore's critical section would read
/// zero, and pass, if the semaphore were removed.</para>
/// </remarks>
public sealed class ThumbnailGate
{
    private readonly SemaphoreSlim _slots;
    private int _inFlight;
    private int _peak;

    /// <summary>Creates a gate of <paramref name="degree"/> slots.</summary>
    /// <param name="degree">How many decodes may run at once; at least one.</param>
    public ThumbnailGate(int degree)
    {
        var n = Math.Max(1, degree);
        _slots = new SemaphoreSlim(n, n);
    }

    /// <summary>A gate sized by a thread setting, read now.</summary>
    /// <param name="parallelism">The setting's current value.</param>
    /// <returns>A new gate of that many slots.</returns>
    public static ThumbnailGate For(Parallelism parallelism)
    {
        ArgumentNullException.ThrowIfNull(parallelism);
        return new ThumbnailGate(parallelism.Effective);
    }

    /// <summary>Waits for a slot.</summary>
    /// <returns>Completes when the caller may decode.</returns>
    internal Task WaitAsync() => _slots.WaitAsync();

    /// <summary>Gives the slot back.</summary>
    internal void Release() => _slots.Release();

    /// <summary>Called by a decode as it starts, to count it.</summary>
    internal void Enter()
    {
        var now = Interlocked.Increment(ref _inFlight);
        int peak;
        while (now > (peak = Volatile.Read(ref _peak)))
            if (Interlocked.CompareExchange(ref _peak, now, peak) == peak) break;
        Probe?.Invoke();
    }

    /// <summary>Called by a decode as it ends.</summary>
    internal void Exit() => Interlocked.Decrement(ref _inFlight);

    /// <summary>The most decodes ever running at once. For tests.</summary>
    internal int Peak => Volatile.Read(ref _peak);

    /// <summary>Runs inside a decode, so a test can hold it open long enough for others to overlap.</summary>
    internal Action? Probe { get; set; }
}
