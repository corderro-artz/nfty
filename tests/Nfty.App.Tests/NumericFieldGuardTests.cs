using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using Nfty.App.ViewModels;
using Nfty.App.Views;
using Xunit;

namespace Nfty.App.Tests;

/// <summary>
/// A number field never shows a .NET exception. Clearing New CookBook's target supply printed
/// "System.InvalidCastException: Could not convert '(null)' (null) to System.Int32" under it, and
/// erasing a saturation endpoint did the same in the editor rail.
/// </summary>
public partial class NumericFieldGuardTests
{
    private sealed partial class Holder : ObservableObject
    {
        [ObservableProperty] private double _amount = 40;
        [ObservableProperty] private int _count = 7;
    }

    private static (Window Window, TextBox Box, NumericUpDown Nud, Button Elsewhere, Holder Vm) Show()
    {
        var vm = new Holder();
        var box = new TextBox();
        box.Bind(TextBox.TextProperty, new Binding(nameof(Holder.Amount)) { Mode = BindingMode.TwoWay });
        var nud = new NumericUpDown { Minimum = 1 };
        nud.Bind(NumericUpDown.ValueProperty, new Binding(nameof(Holder.Count)) { Mode = BindingMode.TwoWay });
        var elsewhere = new Button { Content = "x" };
        var window = new Window { DataContext = vm, Content = new StackPanel { Children = { box, nud, elsewhere } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, box, nud, elsewhere, vm);
    }

    private static bool AnyErrorText(Control field) =>
        field.GetVisualDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("System.", StringComparison.Ordinal));

    [AvaloniaFact]
    public void An_emptied_text_field_shows_no_exception_and_gets_its_value_back_when_left()
    {
        var (window, box, _, elsewhere, vm) = Show();
        box.Focus();
        box.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.False(AnyErrorText(box));
        Assert.Equal(40, vm.Amount);

        elsewhere.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("40", box.Text);
        Assert.False(DataValidationErrors.GetHasErrors(box));
        window.Close();
    }

    [AvaloniaFact]
    public void A_cleared_number_box_bound_to_an_int_shows_no_exception_and_restores()
    {
        var (window, _, nud, elsewhere, vm) = Show();
        var inner = nud.GetVisualDescendants().OfType<TextBox>().Single();
        inner.Focus();
        nud.Value = null;
        Dispatcher.UIThread.RunJobs();
        Assert.False(AnyErrorText(nud));
        Assert.Equal(7, vm.Count);

        elsewhere.Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(7m, nud.Value);
        Assert.False(DataValidationErrors.GetHasErrors(nud));
        window.Close();
    }

    [AvaloniaFact]
    public void Clearing_new_cookbook_target_supply_means_not_decided()
    {
        var vm = new NewCookBookViewModel(new FakeDialogs());
        var view = new NewCookBookView { DataContext = vm };
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        vm.TargetSupply = 500;
        Dispatcher.UIThread.RunJobs();
        var nud = view.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Increment == 100);

        vm.TargetSupply = 1_000_000_000;
        Assert.Equal(999_999_999, vm.TargetSupplyOrNull);

        nud.Value = null;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(vm.TargetSupplyOrNull);
        Assert.False(DataValidationErrors.GetHasErrors(nud));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(150, 100)]
    [InlineData(-5, 0)]
    [InlineData(double.NaN, 0)]
    public void A_saturation_typed_off_the_axis_lands_on_it(double typed, double expected)
    {
        var vm = new NewIngredientViewModel(new FakeDialogs());
        vm.SatMax = typed;
        vm.SatMin = typed;
        Assert.InRange(vm.SatMax, 0, 100);
        Assert.Equal(expected, vm.SatMin);
    }
}
