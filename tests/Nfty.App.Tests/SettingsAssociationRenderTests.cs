using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using Point = Avalonia.Point;

namespace Nfty.App.Tests;

public class SettingsAssociationRenderTests
{
    private const string Exe = @"C:\nfty\nfty.exe";
    private static Image<Rgba32> Frame(Window window)
    {
        using var bitmap = window.CaptureRenderedFrame()!;
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SixLabors.ImageSharp.Image.Load<Rgba32>(stream);
    }
    private static void Save(Image<Rgba32> frame, string name)
    {
        if (Environment.GetEnvironmentVariable("NFTY_CAPTURE_DIR") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        frame.SaveAsPng(Path.Combine(directory, name + ".png"));
    }
    private static Rect In(Control control, Visual root) => new(control.TranslatePoint(default, root)!.Value, control.Bounds.Size);
    private static async Task Settle(Window window)
    {
        for (int i = 0; i < 18; i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_and_default_states_keep_the_card_and_actions_fixed_and_every_type_readable(bool dark)
    {
        var hive = new InMemoryRegistryHive();
        var defaults = new Dictionary<string, AssociationDefault>();
        var registrar = new FileAssociationRegistrar(hive, AssociationLayout.Windows, Exe, launch: _ => { },
            defaultFor: ext => defaults.GetValueOrDefault(ext, AssociationDefault.Unknown));
        var vm = new SettingsDialogViewModel(new DialogService(),
            new ParallelismSettingService(StateStore.InMemory(), () => null), associations: registrar) { Tab = SettingsTab.System };
        var view = new Views.SettingsDialogView { DataContext = vm };
        var window = new Window { Content = view, Width = 1067, Height = 526,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            var footer = view.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("modal-foot"));
            var actions = view.GetVisualDescendants().OfType<Button>().Where(b => b.Name is "ApplyAssociations" or "RemoveAssociations").ToArray();
            var bounds = new[] { In(card, window), In(footer, window) }.Concat(actions.Select(b => In(b, window))).ToArray();
            foreach (var action in actions)
                foreach (var label in action.GetVisualDescendants().OfType<TextBlock>())
                {
                    Assert.True(In(action, window).Contains(In(label, window)));
                    Assert.True(label.Bounds.Height >= label.DesiredSize.Height);
                }
            var boxes = view.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Content?.ToString()?.StartsWith('.') == true).ToArray();
            Assert.Equal(13, boxes.Length);
            foreach (var box in boxes)
            {
                Assert.True(bounds[0].Contains(In(box, window)));
                Assert.True(box.Bounds.Width >= box.DesiredSize.Width);
            }
            using var baseline = Frame(window);
            Save(baseline, $"settings-system-empty-{dark}");
            vm.AllTypesSelected = true;
            vm.ApplyAssociationsCommand.Execute(null);
            defaults[".cbk"] = new(true, "nfty.CookBook");
            defaults[".png"] = new(true, "Other.Picture");
            defaults[".json"] = new(true, null);
            vm.RefreshAssociationsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(bounds, new[] { In(card, window), In(footer, window) }.Concat(actions.Select(b => In(b, window))).ToArray());
            using var registered = Frame(window);
            Save(registered, $"settings-system-registered-{dark}");
            Assert.Contains("1 default", vm.AssociationSummary, StringComparison.Ordinal);
            Assert.Contains(vm.AssociationTypes, t => t.Extension == ".cbk" && t.Status == "app · default");
            Assert.Contains(vm.AssociationTypes, t => t.Extension == ".png" && t.Status == "viewer · registered");
            hive.SetString(@"Software\Classes\nfty.CookBook\shell\open\command", "", "Foreign command");
            vm.RefreshAssociationsCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(bounds, new[] { In(card, window), In(footer, window) }.Concat(actions.Select(b => In(b, window))).ToArray());
            using var repair = Frame(window);
            Save(repair, $"settings-system-repair-{dark}");
            // Changes stay in the reserved body; the card's top and footer pixels are identical.
            int top = (int)Math.Floor(bounds[0].Y);
            int bodyTop = (int)Math.Ceiling(boxes.Min(b => In(b, window).Y)) - 35;
            int footerTop = (int)Math.Ceiling(bounds[1].Y);
            for (int y = top; y < (int)bounds[0].Bottom; y++)
                if (y < bodyTop || y >= footerTop)
                    for (int x = (int)Math.Ceiling(bounds[0].X); x < (int)bounds[0].Right; x++)
                        Assert.Equal(baseline[x,y], repair[x,y]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_footer_icons_animate_hover_return_and_selection_without_moving(bool dark)
    {
        var dialogs = new DialogService();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService(),
            settingsCard: () => new SettingsDialogViewModel(dialogs,
                new ParallelismSettingService(StateStore.InMemory(), () => null)));
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1280, Height = 720,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            foreach (string tooltip in new[] { "Settings", "Help" })
            {
                var button = view.GetVisualDescendants().OfType<Button>().Single(b => ToolTip.GetTip(b)?.ToString() == tooltip);
                var icon = button.GetVisualDescendants().OfType<Panel>().Single(p => p.Classes.Contains("footer-icon"));
                var bounds = In(button, window);
                window.MouseMove(new Point(0, 0));
                await Settle(window);
                var initial = icon.RenderTransform!.Value;
                using var before = Frame(window);
                Save(before, $"footer-{tooltip}-rest-{dark}");
                window.MouseMove(bounds.Center);
                await Settle(window);
                Assert.NotEqual(initial, icon.RenderTransform!.Value);
                Assert.NotEmpty(icon.Transitions!);
                Assert.Equal(bounds, In(button, window));
                using var hover = Frame(window);
                Save(hover, $"footer-{tooltip}-hover-{dark}");
                Assert.True(ChangedIn(before, hover, bounds) > 4);
                window.MouseMove(new Point(0, 0));
                await Settle(window);
                Assert.Equal(initial, icon.RenderTransform!.Value);
                using var returned = Frame(window);
                Save(returned, $"footer-{tooltip}-return-{dark}");
                Assert.Equal(0, ChangedIn(before, returned, bounds));
                Assert.True(button.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                await Settle(window);
                Assert.Contains("on", button.Classes);
                Assert.False(button.IsEffectivelyEnabled);
                Assert.Equal(bounds, In(button, window));
                using var selected = Frame(window);
                Save(selected, $"footer-{tooltip}-selected-{dark}");
                dialogs.Close(null);
                await Settle(window);
                Assert.DoesNotContain("on", button.Classes);
                Assert.Equal(initial, icon.RenderTransform!.Value);
                Assert.Equal(bounds, In(button, window));
            }
        }
        finally { window.Close(); }
    }

    private static int ChangedIn(Image<Rgba32> a, Image<Rgba32> b, Rect bounds)
    {
        int count = 0;
        for (int y = (int)Math.Ceiling(bounds.Y); y < (int)bounds.Bottom; y++)
            for (int x = (int)Math.Ceiling(bounds.X); x < (int)bounds.Right; x++)
                if (a[x,y] != b[x,y]) count++;
        return count;
    }
}
