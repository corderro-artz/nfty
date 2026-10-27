using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// V-9: the import form's name-collision sentence is the NAME field's refusal, so it starts where the
/// field does and speaks in the warning ink. It sat four pixels left of the field, in ink that read as
/// body text.
/// </summary>
public class ImportImageLayoutTests
{
    [AvaloniaFact]
    public void Collision_sentence_aligns_with_the_name_field()
    {
        using var vm = VisualCapture.ImportImageForm(new FakeDialogs());
        Assert.Contains("already has a layer", vm.Problem);
        var view = new Views.ImportImageView { DataContext = vm };
        var window = new Window { Content = view, Width = RisePages.MinArea.Width, Height = RisePages.MinArea.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var line = view.FindControl<TextBlock>("ProblemLine")!;
            var name = view.FindControl<TextBox>("NameField")!;
            double lineX = line.TranslatePoint(new Point(0, 0), view)!.Value.X;
            double nameX = name.TranslatePoint(new Point(0, 0), view)!.Value.X;
            Assert.True(Math.Abs(lineX - nameX) < 0.5, $"the sentence starts at {lineX:F1}, the name field at {nameX:F1}");

            Assert.True(Application.Current!.TryGetResource("WarningBrush", window.ActualThemeVariant, out var res));
            var warn = (ISolidColorBrush)res!;
            Assert.Equal(warn.Color, ((ISolidColorBrush)line.Foreground!).Color);
        }
        finally { window.Close(); }
    }
}
