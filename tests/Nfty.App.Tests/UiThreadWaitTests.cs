using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Keeps the UI-thread deadlock that froze <c>MenuExportDialogTests</c> out of the test host: no
/// App test blocks the UI thread on a task, and the one helper that waits instead pumps.
/// </summary>
public class UiThreadWaitTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "nfty.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void No_App_test_blocks_the_UI_thread_on_a_task()
    {
        // Every [AvaloniaFact] runs ON the UI thread, so a bare GetResult/Wait/.Result there is a
        // deadlock as soon as the awaited code really goes async. UiThreadWait.For is the way.
        string root = Path.Combine(RepoRoot(), "tests", "Nfty.App.Tests");
        var offenders = Directory.EnumerateFiles(root, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetFileName(f) is not ("UiThreadWait.cs" or "UiThreadWaitTests.cs"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(l => l.Text.Contains(".GetAwaiter().GetResult()", StringComparison.Ordinal)
                        || l.Text.Contains(".Wait()", StringComparison.Ordinal))
            .Select(l => $"{l.File}:{l.Line}")
            .ToList();
        Assert.True(offenders.Count == 0, "blocks the UI thread on a task: " + string.Join(", ", offenders));
    }

    [AvaloniaFact]
    public void Waiting_pumps_a_continuation_that_needs_the_UI_thread()
    {
        // The shape of a view-model command: real async work, then a continuation captured on the UI
        // thread. GetAwaiter().GetResult() here never returns.
        static async Task Command()
        {
            await Task.Run(() => Task.Delay(20));
            await Task.Yield();
        }

        UiThreadWait.For(Command(), timeoutMs: 10_000);
    }

    [AvaloniaFact]
    public void A_refused_menu_card_is_built_without_freezing_the_UI_thread()
    {
        using var book = MenuExportDialogTests.WideBook();
        var vm = MenuExportDialogTests.RefusedVm(book);
        Assert.True(vm.MessageIsProblem);
        Assert.False(vm.IsRunning);
    }
}
