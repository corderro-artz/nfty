using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// V-11: the format cards on the Rise EXPORT tab never ellipsize. "One binary file. The default; what
/// engines and viewers expect." was cut to "...what engines and view..." at the rail's real width.
/// </summary>
public class RiseExportLayoutTests
{
    [AvaloniaFact]
    public void Format_descriptions_are_never_ellipsized()
    {
        var (vm, owner) = RisePages.DemoSetVm();
        vm.ShowExportTabCommand.Execute(null);
        using var page = RisePages.Show(vm, RisePages.MinArea.Width, RisePages.MinArea.Height, owner: owner);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var descriptions = page.View.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("pd") && t.IsEffectivelyVisible).ToList();
        Assert.Equal(3, descriptions.Count);
        Assert.All(descriptions, t =>
        {
            Assert.Equal(Avalonia.Media.TextWrapping.Wrap, t.TextWrapping);   // the headless layout cannot report trimming, so the mechanism is pinned beside the ink
            // INK, not the box: a TextBlock arranged to its parent trims without complaint, so the
            // sentence is laid out at the box's own width and asked how many lines it really needs.
            var ft = new FormattedText(t.Text!, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(t.FontFamily), t.FontSize, Brushes.Black) { MaxTextWidth = t.Bounds.Width, LineHeight = t.LineHeight };
            Assert.True(t.Bounds.Height + 0.5 >= ft.Height, $"the box is {t.Bounds.Height:F0}px, the sentence needs {ft.Height:F0}");
            double lines = ft.Height / t.LineHeight;
            Assert.True(lines <= 2.2, $"\"{t.Text}\" needs {lines:F1} lines at {t.Bounds.Width:F0}px - it is trimmed");
        });
        // Reserved: the three cards are one height whatever their sentences need.
        Assert.True(descriptions.Max(t => t.Bounds.Height) - descriptions.Min(t => t.Bounds.Height) < 0.5);
    }
}
