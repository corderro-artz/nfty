using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The help view. Code-behind is the search box's keys: they have to act on key events that
/// a text box has already handled, which no <c>KeyBinding</c> sees.</summary>
public partial class HelpView : UserControl
{
    /// <summary>Loads the view.</summary>
    public HelpView()
    {
        InitializeComponent();
        // TUNNEL + handledEventsToo, narrowly. A TextBox marks Escape and Enter handled before the
        // bubble phase, so a KeyBinding (or a bubble handler) never sees them while the search has
        // focus. Escape is also bound on the WINDOW to close the dialog: handling it here, first, is
        // what makes the first Esc clear a query and only the next one close the sheet.
        AddHandler(KeyDownEvent, OnKeyTunnel, RoutingStrategies.Tunnel, handledEventsToo: true);
        // The dialog layer's ContentControl never takes focus itself, so without this a key pressed
        // with the sheet open is delivered to the page behind it - "/" would be typed there. The sheet
        // takes focus (not the search box: Esc must stay free to close it) so its keys route here.
        Focusable = true;
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => { if (!IsKeyboardFocusWithin) Focus(); }, DispatcherPriority.Loaded);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>The search box. Found by name: this view loads its own XAML, so the generated name
    /// fields are never assigned.</summary>
    internal TextBox Search => this.FindControl<TextBox>("SearchBox")!;

    private void OnKeyTunnel(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HelpViewModel vm) return;
        var inSearch = e.Source is Visual v && Search.IsVisualAncestorOf(v) || ReferenceEquals(e.Source, Search);
        switch (e.Key)
        {
            case Key.Escape:
                // Esc is stated here, not as a KeyBinding: a binding on this view still ran after this
                // handler had handled the key, so the first Esc cleared the query AND closed the sheet.
                if (vm.IsSearching) { vm.ClearSearchCommand.Execute(null); Search.Focus(); }
                else vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter when inSearch && vm.IsSearching:
                vm.OpenFirstCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F when e.KeyModifiers == KeyModifiers.Control:
                Search.Focus();
                Search.SelectAll();
                e.Handled = true;
                break;
            case Key.OemQuestion or Key.Divide when e.KeyModifiers == KeyModifiers.None && !inSearch:
                // "/" is the search key everywhere except inside a text box, where it is a character.
                Search.Focus();
                e.Handled = true;
                break;
        }
    }
}
