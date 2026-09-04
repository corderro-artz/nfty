using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Cook dialog names both of its fields.
/// </summary>
/// <remarks>
/// The seed box was labeled by <c>PlaceholderText</c> alone, and the ViewModel pre-fills a random
/// seed the moment the dialog opens -- so the only thing that named the field appeared exactly when
/// the field was empty, which it never was. Every ViewModel test passed: the seed round-tripped
/// fine, it just met the user as an unexplained box of hex. Found by opening the real dialog and
/// looking at it.
/// </remarks>
public class CookDialogLabelTests
{
    private sealed class NoPicker : IFilePickerService
    {
        public Task<string?> OpenFileAsync(string t, params string[] e) => Task.FromResult<string?>(null);
        public Task<string?> SaveFileAsync(string t, string e) => Task.FromResult<string?>(null);
        public Task<string?> PickFolderAsync(string t) => Task.FromResult<string?>(null);
    }
    private sealed class NoReveal : IFolderRevealer { public void Reveal(string p) { } }

    [AvaloniaFact]
    public void Both_cook_fields_are_labeled_by_something_a_filled_field_still_shows()
    {
        var vm = new CookDialogViewModel(ExplorerViewModelTests.TwoRecipeBook(),
            new NoPicker(), new NoReveal(), new FakeDialogs());
        var view = new Views.CookDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The premise: the dialog opens with a seed already in the box, so a placeholder is
            // never on screen. Without this the test would pass on an empty-seed dialog.
            Assert.False(string.IsNullOrWhiteSpace(vm.Seed));

            var labels = view.GetVisualDescendants().OfType<TextBlock>()
                .Select(t => t.Text).ToList();
            Assert.Contains("Count", labels);
            Assert.Contains("Seed", labels);
        }
        finally { window.Close(); }
    }
}
