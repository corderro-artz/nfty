using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nfty.App.Views;

/// <summary>The report dialog view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class ReportDialogView : UserControl
{
    /// <summary>Loads the view.</summary>
    public ReportDialogView() => AvaloniaXamlLoader.Load(this);
}
