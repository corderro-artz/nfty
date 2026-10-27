using System.Diagnostics;
using System.Threading;
using Avalonia.Threading;

namespace Nfty.App.Tests;

/// <summary>
/// The one sanctioned way for a SYNCHRONOUS test helper to wait on an async command from the UI
/// thread: pump the dispatcher until the task is done, and fail by name rather than hang.
/// </summary>
/// <remarks>
/// <c>command.ExecuteAsync(null).GetAwaiter().GetResult()</c> on the UI thread is a deadlock the
/// moment the command awaits anything that really goes async: the view model's own <c>await</c>
/// captures the UI context, so its continuation is posted to the very thread that is blocked
/// waiting for it. It passed for as long as <c>MenuWriter.WriteAsync</c> happened to fault
/// synchronously on a refused atlas; once the encode moved onto the thread pool it froze the
/// test host (MenuExportDialogTests, DarkModeContrastTests, VisualCapture all build that card).
/// <c>UiThreadWaitTests.No_App_test_blocks_the_UI_thread_on_a_task</c> keeps the pattern out.
/// </remarks>
internal static class UiThreadWait
{
    /// <summary>Pumps the dispatcher until <paramref name="task"/> completes, then rethrows its
    /// failure, if any.</summary>
    /// <param name="task">The task to wait for, typically a command's <c>ExecuteAsync</c>.</param>
    /// <param name="timeoutMs">How long before the wait is reported as a hang.</param>
    /// <exception cref="TimeoutException">The task did not complete in time.</exception>
    public static void For(Task task, int timeoutMs = 30_000)
    {
        var sw = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException($"The task did not complete within {timeoutMs} ms while the UI thread pumped.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();
    }
}
