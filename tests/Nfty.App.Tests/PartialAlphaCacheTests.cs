using System.Linq;
using Avalonia.Headless.XUnit;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>The Explorer tree's 2D-only mark must not rescan every pixel on every rebuild, or each
/// reorder keystroke pays a full scan of the book.</summary>
public class PartialAlphaCacheTests
{
    [AvaloniaFact]
    public void A_rebuild_that_changes_no_pixels_scans_nothing()
    {
        var (session, path) = RiseFixtures.OnDisk(RiseFixtures.ChestBook());
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        ExplorerViewModel Build() => new(session.Current!, nav, dialogs, new ImageBridge(),
            ExplorerViewModelTests.EditorFactory(nav, session, dialogs),
            ExplorerViewModelTests.CookFactory(dialogs), session, new FilePickerService(),
            ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), new StatusService());

        long start = PartialAlphaCache.Scans;
        using var first = Build();
        long afterFirst = PartialAlphaCache.Scans;
        using var second = Build();     // the same decoded images, a fresh tree
        using var third = Build();

        Assert.True(afterFirst > start, "the first build had to look at the pixels");
        Assert.Equal(afterFirst, PartialAlphaCache.Scans);
        Assert.Equal(first.Root.Children.Select(r => r.Children.Select(i => i.IsTwoDOnly).ToList()),
            second.Root.Children.Select(r => r.Children.Select(i => i.IsTwoDOnly).ToList()));
        session.Dispose();
        try { System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(path)!, recursive: true); } catch (System.IO.IOException) { }
    }
}
