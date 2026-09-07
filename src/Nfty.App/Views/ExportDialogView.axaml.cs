using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>
/// The export dialog. Everything is bound except one thing: arming the seal has to scroll the
/// passphrase box into view.
/// </summary>
/// <remarks>
/// <para><b>Why this cannot be a binding.</b> Ticking "Seal this export" reveals a passphrase pair,
/// a note and a caveat — about 85px more than the smallest window this app opens at can show. The
/// form scrolls, so the field the user now has to type into can be below the fold at the moment it
/// appears, which is the one thing a reveal must never do.</para>
///
/// <para><b>And it cannot be solved by making the card taller.</b> That was the first attempt: a
/// fixed height chosen so the boxes happened to land above the fold. It worked, and it was one line
/// of caveat text away from not working — the kind of fix that passes today and fails silently to
/// whoever edits the copy next. Scrolling to the control is true at any size and any content.</para>
///
/// <para>Posted at <see cref="DispatcherPriority.Loaded"/> because the panel is revealed by an
/// <c>IsVisible</c> binding and has no bounds to scroll to until layout has run — the same reason
/// the layer table restores focus at that priority after a reorder.</para>
/// </remarks>
public partial class ExportDialogView : UserControl
{
    private INotifyPropertyChanged? _watched;

    /// <summary>Loads the view.</summary>
    public ExportDialogView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_watched is not null) _watched.PropertyChanged -= OnViewModelChanged;
        _watched = DataContext as ExportDialogViewModel;
        if (_watched is not null) _watched.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExportDialogViewModel.IsSealed)) return;
        if (DataContext is not ExportDialogViewModel { IsSealed: true }) return;

        Dispatcher.UIThread.Post(
            () => this.FindControl<TextBox>("PassphraseBox")?.BringIntoView(),
            DispatcherPriority.Loaded);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
