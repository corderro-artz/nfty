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

        this.FindControl<ScrollViewer>("PageScroller")!.SizeChanged += (_, _) => SizePage();

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

    /// <summary>Opens the other-windows list on its badge (Ctrl+Shift+W).</summary>
    private void OpenChildWindowMenu() =>
        this.FindControl<Button>("ChildWindowsButton")?.ContextFlyout?.ShowAt(this.FindControl<Button>("ChildWindowsButton")!);

    /// <summary>Folds the list once a window was chosen from it.</summary>
    private void CloseChildWindowMenu() => this.FindControl<Button>("ChildWindowsButton")?.ContextFlyout?.Hide();

    private void ObserveShell(ShellViewModel? shell)
    {
        if (_observedShell is not null)
        {
            _observedShell.PropertyChanged -= OnShellChanged;
            _observedShell.ChildWindowMenuRequested -= OpenChildWindowMenu;
            _observedShell.ChildWindowMenuCloseRequested -= CloseChildWindowMenu;
        }
        StopAttention();
        _observedShell = shell;
        if (shell is not null)
        {
            shell.PropertyChanged += OnShellChanged;
            shell.ChildWindowMenuRequested += OpenChildWindowMenu;
            shell.ChildWindowMenuCloseRequested += CloseChildWindowMenu;
            SizePage();
        }
    }

    /// <summary>Sizes the page for the zoom: the viewport in page units, but never less than
    /// <see cref="ShellViewModel.MinPageArea"/> while zoomed in, the scroller showing a bar only on an axis
    /// that really overflows.</summary>
    internal void SizePage()
    {
        var scroller = this.FindControl<ScrollViewer>("PageScroller");
        var host = this.FindControl<ContentControl>("PageHost");
        if (scroller is null || host is null) return;
        double zoom = (DataContext as ShellViewModel)?.ZoomScale ?? 1;
        if (zoom <= 0) zoom = 1;
        var view = scroller.Bounds.Size;
        if (view.Width <= 0 || view.Height <= 0) return;
        var min = zoom > 1 ? ShellViewModel.MinPageArea : default;
        double w = view.Width / zoom, h = view.Height / zoom;
        bool overW = min.Width > w + 0.5, overH = min.Height > h + 0.5;
        host.Width = overW ? min.Width : w;
        host.Height = overH ? min.Height : h;
        scroller.HorizontalScrollBarVisibility = overW ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        scroller.VerticalScrollBarVisibility = overH ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(sender, _observedShell)) OnShellChanged(sender, e); });
            return;
        }
        if (e.PropertyName is nameof(ShellViewModel.Zoom) or nameof(ShellViewModel.ZoomScale)) SizePage();
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
