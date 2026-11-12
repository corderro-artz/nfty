using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core.Demo;
using Nfty.Core.Formats;
using Xunit;

namespace Nfty.App.Tests;

public partial class VisualCapture
{
    /// <summary>Exercise real compact tables and the settings card with their scrollbar expanded.
    /// The content must end before the bar, and hovering must leave its geometry unchanged.</summary>
    [AvaloniaFact]
    public void Compact_scrollbars_leave_the_content_readable()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var demoPath = DemoCookBook.WriteTo(dir.FullName);
            foreach (var theme in Themes)
            {
                var nav = new FakeNav();
                var dialogs = new FakeDialogs();
                using var session = new CookBookSession();
                session.Open(CookBookArchive.Read(demoPath), demoPath);
                var book = session.Current!;
                var status = new StatusService();
                using var explorer = new ExplorerViewModel(book, nav, dialogs, new ImageBridge(),
                    ExplorerViewModelTests.EditorFactory(nav), ExplorerViewModelTests.CookFactory(dialogs),
                    session, new FilePickerService(), ExplorerViewModelTests.LooseEditorFactory(nav, session, dialogs), status,
                    riseFactory: source => new RiseViewModel(source, new ImageBridge(), nav, dialogs, new FilePickerService(), status));
                var shell = new ShellViewModel(nav, dialogs, new ThemeService(), status);
                nav.To(explorer);
                explorer.ToggleLockCommand.Execute(null);
                var view = new Views.ShellChromeView { DataContext = shell };
                var window = new Window { Content = view, RequestedThemeVariant = theme,
                    Width = ShellViewModel.MinWindowWidth, Height = ShellViewModel.MinWindowHeight };
                window.Show();
                try
                {
                    var recipe = explorer.Root.Children[0];
                    recipe.IsExpanded = true;
                    foreach (var node in new[] { recipe, recipe.Children.First(n => n.Id == "glow") })
                    {
                        explorer.SelectNodeCommand.Execute(node);
                        Dispatcher.UIThread.RunJobs();
                        var detail = view.GetVisualDescendants().OfType<Control>().First(c =>
                            c is Views.RecipeDetailView or Views.IngredientDetailView);
                        var scroll = detail.GetVisualDescendants().OfType<ScrollViewer>().First();
                        CheckCompactScrollbar(window, scroll, $"compact-{node.Id}-{KeyOf(theme)}");
                        var delete = detail.GetVisualDescendants().OfType<Button>()
                            .Single(b => b.Content is string text && text == (detail is Views.IngredientDetailView ? "Delete ingredient" : "Delete recipe"));
                        var riseCommand = detail.DataContext is IngredientDetailViewModel ingredient
                            ? ingredient.RiseCommand : ((RecipeDetailViewModel)detail.DataContext!).RiseCommand;
                        var rise = detail.GetVisualDescendants().OfType<Button>().Single(b => b.Command == riseCommand);
                        Assert.True(rise.IsEffectivelyEnabled);
                        Assert.True(delete.IsEffectivelyEnabled);
                        var deleteBounds = delete.Bounds;
                        explorer.ToggleLockCommand.Execute(null);
                        Dispatcher.UIThread.RunJobs();
                        Assert.False(delete.IsEffectivelyEnabled);
                        Assert.True(rise.IsEffectivelyEnabled); // Opening Rise is allowed while locked.
                        Assert.Equal(deleteBounds, delete.Bounds);
                        if (Dir is not null)
                            window.CaptureRenderedFrame()!.Save(Path.Combine(Dir, $"compact-{node.Id}-{KeyOf(theme)}-locked.png"), PngBitmapEncoderOptions.Default);
                        explorer.ToggleLockCommand.Execute(null);
                        Dispatcher.UIThread.RunJobs();
                        Assert.True(delete.IsEffectivelyEnabled);
                        Assert.Equal(deleteBounds, delete.Bounds);
                        var deleteCentre = delete.TranslatePoint(new Point(delete.Bounds.Width / 2, delete.Bounds.Height / 2), window)!.Value;
                        window.MouseDown(deleteCentre, MouseButton.Left);
                        window.MouseUp(deleteCentre, MouseButton.Left);
                        Dispatcher.UIThread.RunJobs();
                        Assert.IsType<ConfirmDialogViewModel>(dialogs.Active);
                        dialogs.Close(null);
                        Dispatcher.UIThread.RunJobs();
                        if (detail is Views.IngredientDetailView)
                        {
                            foreach (var target in new[] { "name", "pencil" })
                            {
                                var title = view.GetVisualDescendants().OfType<Button>()
                                    .Single(b => b.Classes.Contains("titlelink"));
                                var control = target == "name" ? (Control)title : title.GetVisualDescendants()
                                    .OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Classes.Contains("pen"));
                                var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                                window.MouseDown(centre, MouseButton.Left);
                                window.MouseUp(centre, MouseButton.Left);
                                Dispatcher.UIThread.RunJobs();
                                Assert.IsType<IngredientEditorViewModel>(nav.Current).Dispose();
                                nav.To(explorer);
                                Dispatcher.UIThread.RunJobs();
                            }
                        }
                    }
                }
                finally { window.Close(); }

                var settings = new BookSettingsDialogViewModel(dialogs, book, null, () => true,
                    _ => Task.FromResult(new BookSettingsOutcome(false, [])));
                foreach (var height in new[] { PageHeight, PageHeight - 40 })
                {
                    var card = new Views.BookSettingsDialogView { DataContext = settings };
                    var modalWindow = new Window { Content = card, RequestedThemeVariant = theme,
                        Width = PageWidth, Height = height };
                    modalWindow.Show();
                    try
                    {
                        Dispatcher.UIThread.RunJobs();
                        CheckCompactScrollbar(modalWindow, card.GetVisualDescendants().OfType<ScrollViewer>().First(),
                            $"compact-book-settings-{height}-{KeyOf(theme)}");
                    }
                    finally { modalWindow.Close(); }
                }
            }
        }
        finally { dir.Delete(recursive: true); }
    }

    private static void CheckCompactScrollbar(Window window, ScrollViewer scroll, string file)
    {
        var bar = scroll.GetVisualDescendants().OfType<ScrollBar>()
            .First(b => ReferenceEquals(b.TemplatedParent, scroll) && b.Orientation == Avalonia.Layout.Orientation.Vertical);
        var presenter = scroll.GetVisualDescendants().OfType<ScrollContentPresenter>()
            .First(p => ReferenceEquals(p.TemplatedParent, scroll));
        if (!bar.IsVisible)
        {
            if (Dir is not null)
                window.CaptureRenderedFrame()!.Save(Path.Combine(Dir, file + "-fits.png"), PngBitmapEncoderOptions.Default);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height);
            return;
        }
        var before = presenter.Bounds;
        bar.HideDelay = TimeSpan.Zero;
        bar.ShowDelay = TimeSpan.Zero;
        window.MouseMove(new Point(-1, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.False(bar.IsExpanded, $"{file}: the resting frame already has an expanded scrollbar");
        using var resting = window.CaptureRenderedFrame()!;
        var point = bar.TranslatePoint(new Point(bar.Bounds.Width / 2, bar.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        Dispatcher.UIThread.RunJobs();
        using var expanded = window.CaptureRenderedFrame()!;
        if (Dir is not null)
        {
            resting.Save(Path.Combine(Dir, file + "-resting.png"), PngBitmapEncoderOptions.Default);
            expanded.Save(Path.Combine(Dir, file + "-expanded.png"), PngBitmapEncoderOptions.Default);
        }
        Assert.True(bar.IsExpanded, $"{file}: the pointer did not expand the scrollbar (visible {bar.IsVisible}, bounds {bar.Bounds}, point {point})");
        Assert.Equal(before, presenter.Bounds);
        Assert.True(bar.Bounds.Width <= 10, $"{file}: expanded scrollbar is {bar.Bounds.Width}px wide");
        var contentRight = presenter.TranslatePoint(new Point(presenter.Bounds.Width, 0), window)!.Value.X;
        var barLeft = bar.TranslatePoint(default, window)!.Value.X;
        Assert.True(contentRight <= barLeft + 0.1, $"{file}: content ends at {contentRight}, bar starts at {barLeft}");

        // Pixel proof: the content's ink stays exactly the same while the scrollbar expands beside it.
        using var restingBytes = new MemoryStream();
        using var expandedBytes = new MemoryStream();
        resting.Save(restingBytes, PngBitmapEncoderOptions.Default);
        expanded.Save(expandedBytes, PngBitmapEncoderOptions.Default);
        using var a = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(restingBytes.ToArray());
        using var b = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(expandedBytes.ToArray());
        var topLeft = presenter.TranslatePoint(default, window)!.Value;
        var bottomRight = presenter.TranslatePoint(new Point(presenter.Bounds.Width, presenter.Bounds.Height), window)!.Value;
        for (int y = (int)Math.Ceiling(topLeft.Y); y < (int)Math.Floor(bottomRight.Y); y++)
        for (int x = (int)Math.Ceiling(topLeft.X); x < (int)Math.Floor(bottomRight.X); x++)
            Assert.True(a[x, y] == b[x, y], $"{file}: content pixel ({x}, {y}) changed on scrollbar hover");
    }
}
