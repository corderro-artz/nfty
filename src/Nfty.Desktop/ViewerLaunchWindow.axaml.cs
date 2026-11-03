using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;

namespace Nfty.Desktop;

/// <summary>
/// The window <c>nfty --viewer &lt;path&gt;</c> opens while the file is read: a dialog layer for the
/// busy, passphrase and error cards, and nothing else. <see cref="App"/> closes it once the viewer
/// is on screen, or once opening has failed and its error card was dismissed.
/// </summary>
public partial class ViewerLaunchWindow : Window
{
    private readonly DialogService _dialogs = new();

    /// <summary>Builds the window for one path.</summary>
    /// <param name="path">What is being opened, named in the window while it is read.</param>
    public ViewerLaunchWindow(string path)
    {
        AvaloniaXamlLoader.Load(this);
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
            Command = new RelayCommand(() => { if (_dialogs.Active is not null) _dialogs.Close(null); }),
        });
    }

    /// <summary>Exists only so Avalonia's runtime XAML loader and designers find a public constructor.</summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public ViewerLaunchWindow() => throw new NotSupportedException("A launch window needs the path it opens.");

    /// <summary>The window's dialog layer, which the open reports into.</summary>
    public IDialogService Dialogs => _dialogs;
}
