using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Reactive;

namespace Nfty.App.Views;

/// <summary>The shared preview-light button and flyout, hosted in the Rise and viewer footers.</summary>
public sealed partial class PreviewLightingView : UserControl
{
    /// <summary>Loads the light controls and gives their separate flyout tree the same preview VM.</summary>
    public PreviewLightingView()
    {
        AvaloniaXamlLoader.Load(this);
        var controls = this.FindControl<StackPanel>("LightControls")!;
        this.GetObservable(DataContextProperty).Subscribe(new AnonymousObserver<object?>(dc => controls.DataContext = dc));
    }
}
