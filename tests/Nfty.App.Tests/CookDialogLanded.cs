using Avalonia.Threading;
using Nfty.App.ViewModels;

namespace Nfty.App.Tests;

/// <summary>Test helper: the cook card builds its partial-alpha answer off the constructing thread, so
/// anything that draws or measures the card waits for it first - exactly as the app's first frame
/// is followed by the answer arriving.</summary>
internal static class CookDialogLanded
{
    /// <summary>Pumps the dispatcher until the card's scan has landed.</summary>
    /// <param name="vm">The card.</param>
    /// <returns><paramref name="vm"/>, landed.</returns>
    public static CookDialogViewModel Landed(this CookDialogViewModel vm)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!vm.ScanLanded && sw.ElapsedMilliseconds < 10000)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        return vm;
    }
}
