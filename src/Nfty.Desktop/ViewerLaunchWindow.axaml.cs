using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.App.Viewer;

namespace Nfty.Desktop;

/// <summary>
/// The window <c>nfty --viewer &lt;path&gt;</c> opens while the file is read: a dialog layer for the
/// busy, passphrase and error cards, and nothing else. <see cref="App"/> closes it once the viewer
/// is on screen, or once opening has failed and its error card was dismissed.
/// </summary>
public partial class ViewerLaunchWindow : Window
{
    private readonly DialogService _dialogs = new();
    private readonly DesktopDialogLifetime _dialogLifetime;
    private readonly CancellationTokenSource _openingCancellation = new();
    private readonly string _path;
    private bool _closing;

    /// <summary>Builds the window for one path.</summary>
    /// <param name="path">What is being opened, named in the window while it is read.</param>
    public ViewerLaunchWindow(string path)
    {
        AvaloniaXamlLoader.Load(this);
        _path = path;
        _dialogLifetime = new DesktopDialogLifetime(this, _dialogs);
        ChromeWiring.Attach(this, this.FindControl<Nfty.App.Controls.WindowFrame>("Frame")!, captionCommands: true);
        this.FindControl<TextBlock>("Opening")!.Text = $"Opening {Path.GetFileName(path.TrimEnd('\\', '/'))}…";

        var scrim = this.FindControl<Panel>("DialogScrim")!;
        var slot = this.FindControl<ContentControl>("DialogSlot")!;
        _dialogs.Changed += () =>
        {
            slot.Content = _dialogs.Active;
            scrim.IsVisible = _dialogs.Active is not null;
        };
        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.Escape),
            Command = new AsyncRelayCommand(_dialogLifetime.DismissActiveAsync),
        });
    }

    /// <summary>Exists only so Avalonia's runtime XAML loader and designers find a public constructor.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public ViewerLaunchWindow() => throw new NotSupportedException("A launch window needs the path it opens.");

    /// <summary>The window's dialog layer, which the open reports into.</summary>
    public IDialogService Dialogs => _dialogs;

    /// <summary>The opening operation, including its dialogs and cleanup; null until opening starts.</summary>
    public Task? PendingOpen { get; private set; }

    /// <summary>Starts this launch's one owned open operation.</summary>
    internal Task BeginOpen(IViewerHost host, Func<IEnumerable<string>> bookCandidates) =>
        PendingOpen ??= OpenAsync(host, bookCandidates);

    private async Task OpenAsync(IViewerHost host, Func<IEnumerable<string>> bookCandidates)
    {
        ViewerSubject? subject = null;
        try
        {
            if (_closing) return;
            var full = Path.GetFullPath(_path);
            subject = await ViewerOpener.OpenAsync(full, Dialogs, _openingCancellation.Token,
                host: host, bookCandidates: bookCandidates);
            if (subject is not null && !_closing)
            {
                var transferred = subject;
                subject = null;
                host.Open(transferred);
            }
        }
        catch (OperationCanceledException) when (_openingCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_closing) await Dialogs.ShowErrorAsync("Could not open", ViewerOpener.Failure(_path, ex));
        }
        finally
        {
            subject?.Dispose();
            Close();
        }
    }

    /// <summary>Marks the launch canceled before any asynchronous result can open a new viewer.</summary>
    /// <param name="e">The close request, which waits if a busy card still owns work.</param>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_closing)
        {
            _closing = true;
            _openingCancellation.Cancel();
        }
        base.OnClosing(e);
    }

    /// <summary>Dismisses pending prompts so their asynchronous callers can finish.</summary>
    /// <param name="e">The closed notification.</param>
    protected override void OnClosed(EventArgs e)
    {
        while (_dialogs.Active is not null) _dialogs.Close(null);
        _openingCancellation.Dispose();
        base.OnClosed(e);
    }
}
