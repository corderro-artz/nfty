using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.Services;
using Nfty.App.ViewModels;
using Nfty.Core;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// The Settings card and the worker-thread setting behind it.
/// </summary>
/// <remarks>
/// Machine-independent on purpose: the logical processor count is read from
/// <see cref="Parallelism"/> and the expected numbers are derived from it here, so these hold on a
/// two-core CI box and a sixty-four-thread workstation alike.
/// </remarks>
public class SettingsDialogTests
{
    private static string TempDir() => Directory.CreateTempSubdirectory().FullName;

    private static ParallelismSettingService Service(string dir, string? env = null) =>
        new(StateStore.At(dir), () => env);

    /// <summary>A count that differs from Auto's, so "saved" and "default" cannot be confused.</summary>
    private static int NotTheDefault() =>
        Parallelism.DefaultDegree == Parallelism.LogicalProcessors ? 1 : Parallelism.LogicalProcessors;

    [Fact]
    public void Auto_is_the_default_and_names_two_thirds_of_the_machine()
    {
        var lp = Parallelism.LogicalProcessors;
        var expected = Math.Max(1, lp * 2 / 3);
        var setting = Service(TempDir());
        var vm = new SettingsDialogViewModel(new FakeDialogs(), setting);

        Assert.True(setting.IsAuto);
        Assert.Equal(expected, setting.Current.Requested);
        Assert.True(vm.IsAuto);
        Assert.Equal(expected, vm.Threads);
        Assert.Equal($"Auto uses {expected} of {lp} logical processors.", vm.Note);
        Assert.Equal(lp, vm.Maximum);
    }

    [Fact]
    public void A_custom_count_persists_across_a_restart()
    {
        var dir = TempDir();
        var n = NotTheDefault();
        Service(dir).Set(n);

        var reopened = Service(dir);   // a new service over the same store: the next launch

        Assert.False(reopened.IsAuto);
        Assert.Equal(n, reopened.Saved);
        Assert.Equal(n, reopened.Current.Requested);
        Assert.Contains("\"threads\"", File.ReadAllText(Path.Combine(dir, ParallelismSettingService.FileName)));
    }

    [Fact]
    public void Choosing_Auto_again_is_remembered_too()
    {
        var dir = TempDir();
        var first = Service(dir);
        first.Set(NotTheDefault());
        first.Set(null);

        Assert.True(Service(dir).IsAuto);
    }

    [Fact]
    public void NFTY_THREADS_overrides_the_saved_setting_and_says_so()
    {
        var dir = TempDir();
        Service(dir).Set(NotTheDefault());

        var setting = Service(dir, env: "1");
        var vm = new SettingsDialogViewModel(new FakeDialogs(), setting);

        Assert.Equal(1, setting.Current.Requested);
        Assert.Equal("1", setting.EnvironmentText);
        Assert.True(vm.IsEnvLocked);
        Assert.False(vm.TrayEnabled);
        Assert.False(vm.StepperEnabled);
        Assert.Equal("Set by NFTY_THREADS=1 for this session.", vm.Note);
        Assert.Equal(1, vm.Threads);

        // The locked card cannot be talked out of it.
        vm.UseAutoCommand.Execute(null);
        Assert.Equal(1, setting.Current.Requested);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-3")]
    [InlineData("2.5")]
    [InlineData("")]
    public void An_invalid_NFTY_THREADS_is_not_an_override(string env)
    {
        var dir = TempDir();
        var n = NotTheDefault();
        Service(dir).Set(n);

        var setting = Service(dir, env);

        Assert.Null(setting.EnvironmentText);
        Assert.Equal(n, setting.Current.Requested);
    }

    [Fact]
    public void NFTY_THREADS_max_means_every_processor()
    {
        var setting = Service(TempDir(), env: "max");

        Assert.Equal("max", setting.EnvironmentText);
        Assert.Equal(Parallelism.LogicalProcessors, setting.Current.Effective);
    }

    [Fact]
    public void A_saved_count_above_this_machine_is_clamped_and_shown()
    {
        var dir = TempDir();
        var lp = Parallelism.LogicalProcessors;
        File.WriteAllText(Path.Combine(dir, ParallelismSettingService.FileName), "{ \"threads\": 99999 }");

        var setting = Service(dir);
        var vm = new SettingsDialogViewModel(new FakeDialogs(), setting);

        Assert.Equal(99999, setting.Saved);   // the file keeps what a bigger machine wrote
        Assert.Equal(lp, setting.Current.Effective);
        Assert.Equal(lp, vm.Threads);
        Assert.Equal($"Saved as 99999; this machine has {lp} logical processors, so {lp} is used.", vm.Note);

        // And a count typed past the ceiling never reaches the file above it.
        setting.Set(99999);
        Assert.Equal(lp, setting.Saved);
    }

    [Fact]
    public void A_corrupt_or_nonsense_settings_file_reads_as_auto()
    {
        foreach (var text in new[] { "not json", "{ \"threads\": 0 }", "{ \"threads\": -4 }", "[]" })
        {
            var dir = TempDir();
            File.WriteAllText(Path.Combine(dir, ParallelismSettingService.FileName), text);
            Assert.True(Service(dir).IsAuto, text);
        }
    }

    [Fact]
    public void Stepping_in_custom_saves_the_count_and_never_leaves_one_to_the_ceiling()
    {
        var setting = Service(TempDir());
        var vm = new SettingsDialogViewModel(new FakeDialogs(), setting);

        vm.UseCustomCommand.Execute(null);
        Assert.False(setting.IsAuto);

        vm.Threads = Parallelism.LogicalProcessors + 50;
        Assert.Equal(Parallelism.LogicalProcessors, setting.Saved);
        Assert.Equal(Parallelism.LogicalProcessors, vm.Threads);

        vm.Threads = -3;
        Assert.Equal(1, setting.Saved);
        Assert.Equal(1, vm.Threads);

        vm.UseAutoCommand.Execute(null);
        Assert.True(setting.IsAuto);
        Assert.Equal(Parallelism.DefaultDegree, vm.Threads);
    }

    private static (Views.SettingsDialogView View, NumericUpDown Field) Show(SettingsDialogViewModel vm,
        out Window window)
    {
        var view = new Views.SettingsDialogView { DataContext = vm };
        window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (view, view.GetVisualDescendants().OfType<NumericUpDown>().Single());
    }

    [AvaloniaFact]
    public void The_stepper_is_laid_out_in_auto_mode()
    {
        var vm = new SettingsDialogViewModel(new FakeDialogs(), Service(TempDir()));
        var (view, field) = Show(vm, out var window);
        try
        {
            var card = view.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("modal"));
            var autoField = field.Bounds;
            var autoCard = card.Bounds;
            Assert.True(autoField.Width > 0 && autoField.Height > 0, "the stepper must be laid out in Auto");
            Assert.False(field.IsEnabled);

            vm.UseCustomCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(field.IsEnabled);
            Assert.Equal(autoField, field.Bounds);   // ink changed, nothing moved
            Assert.Equal(autoCard, card.Bounds);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void The_settings_button_opens_the_card()
    {
        var dialogs = new FakeDialogs();
        var shell = new ShellViewModel(new FakeNav(), dialogs, new ThemeService(), new StatusService(),
            parallelism: Service(TempDir()));
        var view = new Views.ShellChromeView { DataContext = shell };
        var window = new Window { Content = view, Width = 1416, Height = 864 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(b => ToolTip.GetTip(b) as string == "Settings");
            var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;

            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.IsType<SettingsDialogViewModel>(dialogs.Active);
        }
        finally { window.Close(); }
    }
}
