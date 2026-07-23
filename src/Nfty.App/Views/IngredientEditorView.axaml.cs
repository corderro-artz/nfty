using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

public partial class IngredientEditorView : UserControl
{
    public IngredientEditorView()
    {
        InitializeComponent();
        var canvas = this.FindControl<Border>("CanvasHost")!;
        canvas.PointerPressed += (_, _) =>
        {
            if (DataContext is IngredientEditorViewModel vm)
                vm.ApplyStrokeCommand.Execute(null);
        };
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
