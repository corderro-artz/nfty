using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Generation;
using Nfty.Core.Output;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// Every modal fits inside the smallest window the app allows, and Escape closes it.
/// </summary>
/// <remarks>
/// <para>The window minimum is set by the largest MODAL, not by the pages. A page can scroll or
/// reflow; a modal is a fixed card that either fits or is cut off, and a quick-reference sheet with
/// its footer sliced away is worse than no sheet. So the requirement is <em>derived</em> here from
/// the controls themselves — if a modal grows, this fails and names the number it now needs, rather
/// than the app quietly shipping a clipped one.</para>
/// </remarks>
public class ModalFitTests
{
    /// <summary>How much of the window the chrome takes before a modal gets any: titlebar, status
    /// bar and the frame's shadow gutter.</summary>
    private const double Chrome = ShellViewModel.ChromeReserve;

    private static Size Measure(Control view)
    {
        // Measured against infinity so the control reports what it WANTS, not what a host allowed.
        var window = new Window { Content = view, Width = 1600, Height = 1200 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            view.Measure(Size.Infinity);
            return view.DesiredSize;
        }
        finally { window.Close(); }
    }

    /// <summary>Both axes in ONE assertion, so a failure reports the whole requirement.</summary>
    /// <remarks>Two separate <c>Assert.True</c> calls stop at the first, which meant a width problem
    /// hid the height entirely and there was no way to read what the minimum actually had to be.
    /// </remarks>
    private static void Fits(string what, double needW, double needH)
    {
        var okW = ShellViewModel.MinWindowWidth >= needW;
        var okH = ShellViewModel.MinWindowHeight >= needH;
        Assert.True(okW && okH,
            $"{what} needs {needW:0} x {needH:0}; the minimum window is " +
            $"{ShellViewModel.MinWindowWidth:0} x {ShellViewModel.MinWindowHeight:0}");
    }

    private static LoadedSet CookedSet(out string dir)
    {
        dir = Directory.CreateTempSubdirectory().FullName;
        using var set = Generator.Generate(CoreTestBook.Tiny(), new GenerateOptions(2, "seed1"));
        SetWriter.Write(set, dir, pack: false);
        return SetReader.Read(dir);
    }

    [AvaloniaFact]
    public void The_quick_reference_sheet_fits_the_smallest_window()
    {
        var sheet = Measure(new Views.HelpView { DataContext = new HelpViewModel(new FakeDialogs()) });

        var needW = sheet.Width * ShellViewModel.BaseScale + 24;        // gutter only, no side chrome
        var needH = sheet.Height * ShellViewModel.BaseScale + Chrome;

        Fits("The quick-reference sheet", needW, needH);
    }

    [AvaloniaFact]
    public void The_asset_inspector_fits_the_smallest_window()
    {
        var loaded = CookedSet(out var dir);
        using var browser = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(browser.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());
            var size = Measure(new Views.SetInspectView { DataContext = ins });

            var needW = size.Width * ShellViewModel.BaseScale + 24;
            var needH = size.Height * ShellViewModel.BaseScale + Chrome;

            Fits("The asset inspector", needW, needH);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// A modal's footer band rounds its own bottom corners.
    /// </summary>
    /// <remarks>
    /// <c>ClipToBounds</c> clips to the layout RECTANGLE, not to the corner radius, so a band with
    /// its own background paints a square corner over the card's arc and the border reads as cut off
    /// at both bottom corners. Seen in a rendered frame; invisible in the markup, where the card
    /// plainly has a radius.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData("sh-f")]
    [InlineData("ins-f")]
    [InlineData("rail-foot")]
    public void A_modal_footer_band_rounds_its_own_bottom_corners(string cls)
    {
        var loaded = CookedSet(out var dir);
        using var browser = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(browser.Items, 0, new FilePickerService(),
                new DialogService(), new StatusService());
            browser.SelectedItem = browser.Items[0];

            Control view = cls switch
            {
                "sh-f" => new Views.HelpView { DataContext = new HelpViewModel(new FakeDialogs()) },
                "ins-f" => new Views.SetInspectView { DataContext = ins },
                _ => new Views.SetBrowserView { DataContext = browser },
            };
            var window = new Window { Content = view, Width = 1400, Height = 1000 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            try
            {
                var band = view.GetVisualDescendants().OfType<Border>()
                    .First(b => b.Classes.Contains(cls));
                Assert.True(band.CornerRadius.BottomLeft > 0 && band.CornerRadius.BottomRight > 0,
                    $".{cls} has square bottom corners, so it cuts the card's arc off");
                Assert.Equal(0, band.CornerRadius.TopLeft);
                Assert.Equal(0, band.CornerRadius.TopRight);
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    /// <summary>
    /// Escape closes an open modal, and does nothing when none is open.
    /// </summary>
    /// <remarks>
    /// Both modals declared their own Escape <c>KeyBinding</c> and neither worked: the dialog layer
    /// hosts its content in a ContentControl that never takes focus, and a UserControl KeyBinding
    /// only fires for the focused element. The command lives on the shell instead — and it has to
    /// refuse when nothing is open, or Escape would stop reaching the editor's selection marquee.
    /// </remarks>
    [AvaloniaFact]
    public void Escape_closes_an_open_modal_and_is_inert_without_one()
    {
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new StubThemeService(), new StatusService());

        Assert.False(shell.CloseDialogCommand.CanExecute(null));   // nothing open: Escape falls through

        var loaded = CookedSet(out var dir);
        using var browser = new SetBrowserViewModel(loaded);
        try
        {
            using var ins = new SetInspectViewModel(browser.Items, 0, new FilePickerService(),
                dialogs, new StatusService());
            _ = dialogs.ShowAsync<object>(ins);

            Assert.True(shell.CloseDialogCommand.CanExecute(null));
            shell.CloseDialogCommand.Execute(null);

            Assert.Null(dialogs.Active);
            Assert.False(shell.CloseDialogCommand.CanExecute(null));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private sealed class StubThemeService : IThemeService
    {
        public bool IsDark { get; private set; }
        public void Toggle() => IsDark = !IsDark;
    }
}
