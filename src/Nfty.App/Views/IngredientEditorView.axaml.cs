using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

public partial class IngredientEditorView : UserControl
{
    public IngredientEditorView()
    {
        InitializeComponent();
        // Real pointer -> pixel-path wiring lands in Task 3 (canvas interaction slice).
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
