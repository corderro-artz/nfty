using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Nfty.App.Controls;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>
/// The shell chrome (frame + titlebar + status bar). Head-agnostic on purpose - see the comment in
/// ShellChromeView.axaml. The properties below are the seam the desktop head wires window behavior
/// to; a UserControl has its own name scope, so the host cannot reach these by name.
/// </summary>
/// <remarks>Code-behind is limited to loading the XAML and the few interactions that genuinely need a
/// control reference; everything else is bound.</remarks>
public partial class ShellChromeView : UserControl
{
    private ShellViewModel? _observedShell;
    private CancellationTokenSource? _attentionCancellation;
    private bool _attached;

    /// <summary>The current footer cue's lifetime, for rendered interaction checks.</summary>
    internal Task PendingStatusAttention { get; private set; } = Task.CompletedTask;

    /// <summary>Loads the view.</summary>
    public ShellChromeView()
    {
        AvaloniaXamlLoader.Load(this);
        AttachedToVisualTree += (_, _) => { _attached = true; ObserveShell(DataContext as ShellViewModel); };
        DetachedFromVisualTree += (_, _) => { _attached = false; ObserveShell(null); StopAttention(); };
        DataContextChanged += (_, _) => { if (_attached) ObserveShell(DataContext as ShellViewModel); };

        // Click-the-scrim-to-dismiss is ordinary UI behavior rather than window behavior, so it
        // stays here with the scrim itself instead of being reached into from the head.
        var scrim = this.FindControl<Panel>("DialogScrim")!;
        scrim.PointerPressed += (sender, e) =>
        {
            // Only close when the click landed on the scrim itself, not bubbled up from the dialog content.
            if (e.Source == sender && DataContext is ShellViewModel shell)
                shell.CloseDialogCommand.Execute(null);
        };
    }

    private void ObserveShell(ShellViewModel? shell)
    {
        if (_observedShell is not null) _observedShell.PropertyChanged -= OnShellChanged;
        StopAttention();
        _observedShell = shell;
        if (shell is not null) shell.PropertyChanged += OnShellChanged;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(sender, _observedShell)) OnShellChanged(sender, e); });
            return;
        }
        if (e.PropertyName == nameof(ShellViewModel.StatusMessage)) StopAttention();
        if (e.PropertyName != nameof(ShellViewModel.StatusAttention)) return;
        StopAttention();
        _attentionCancellation = new CancellationTokenSource();
        PendingStatusAttention = FlashAttentionAsync(_attentionCancellation.Token);
    }

    private void StopAttention()
    {
        _attentionCancellation?.Cancel();
        _attentionCancellation?.Dispose();
        _attentionCancellation = null;
        this.FindControl<Border>("StatusAttentionWash")!.Opacity = 0;
    }

    private async Task FlashAttentionAsync(CancellationToken token)
    {
        var wash = this.FindControl<Border>("StatusAttentionWash")!;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(900),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.28d) } },
                new KeyFrame { Cue = new Cue(0.25), Setters = { new Setter(OpacityProperty, 0.28d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };
        try { await animation.RunAsync(wash, token); }
        catch (OperationCanceledException) { }
    }

    /// <summary>The shared window frame this shell is drawn in.</summary>
    public WindowFrame Frame => this.FindControl<WindowFrame>("ChromeFrame")!;

    /// <summary>The titlebar surface: drag to move, double-click to toggle maximize.</summary>
    public Control TitlebarArea => Frame.TitlebarArea;

    /// <summary>The bottom-right resize grip.</summary>
    public Control ResizeGripArea => Frame.ResizeGripArea;
}
