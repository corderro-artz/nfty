using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// V-8: the editor footer's note never touches the Back button. "Save the layer first - Rise reads
/// the saved picture." ran flush into it, because the note was right-aligned in a star column that
/// ended exactly where the button group began.
/// </summary>
public class EditorFooterLayoutTests
{
    [AvaloniaFact]
    public void The_rise_hint_never_touches_the_back_button()
    {
        var book = ExplorerViewModelTests.TwoRecipeBook();
        var cat = book.Recipes.First(r => r.Manifest.Id == "cat");
        var nav = new FakeNav();
        var dialogs = new FakeDialogs();
        var vm = new IngredientEditorViewModel(cat.Ingredients[0], cat, book, new ImageBridge(), nav,
            new CookBookSession(), dialogs, new FilePickerService(), isEditing: () => true,
            rise: src => new RiseViewModel(src, new ImageBridge(), nav, dialogs, new FilePickerService(), new StatusService()));
        vm.ApplyToolStroke(new[] { (0, 0) });
        Assert.False(string.IsNullOrEmpty(vm.SaveNoteText), "the fixture no longer reaches the hint");

        var view = new Views.IngredientEditorView { DataContext = vm };
        var area = RisePages.MinArea;
        var window = new Window { Content = view, Width = area.Width, Height = area.Height };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var note = view.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.Classes.Contains("cshare") && t.Text == vm.SaveNoteText);
            var back = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, "Back"));
            double noteRight = note.TranslatePoint(new Point(note.Bounds.Width, 0), view)!.Value.X;
            double backLeft = back.TranslatePoint(new Point(0, 0), view)!.Value.X;
            Assert.True(backLeft - noteRight >= 8 - 0.5,
                $"the hint ends at {noteRight:F1} and Back starts at {backLeft:F1}: {backLeft - noteRight:F1}px, wanted 8");

            // INK, not the box: a TextBlock arranged to its parent clips without complaint. The whole
            // sentence must be drawn, so its full width has to fit in the column over at most two lines.
            var ft = new FormattedText(vm.SaveNoteText, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface(note.FontFamily), note.FontSize, Brushes.Black);
            Assert.True(ft.Width <= note.Bounds.Width * 2 + 1,
                $"the sentence is {ft.Width:F0}px of ink and the column {note.Bounds.Width:F0}px - it would be trimmed");
        }
        finally { window.Close(); vm.Dispose(); book.Dispose(); }
    }
}
