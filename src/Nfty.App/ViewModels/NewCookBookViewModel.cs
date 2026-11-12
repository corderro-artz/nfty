using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nfty.App.Services;
using Nfty.Core.Model;

namespace Nfty.App.ViewModels;

/// <summary>New CookBook wizard: collects the fields a CookBook manifest needs (name, symbol, canvas
/// size, description) and, on Create, closes the dialog with itself so the caller can build the
/// manifest and write the .cbk.</summary>
public partial class NewCookBookViewModel : WizardViewModelBase
{
    /// <summary>The largest intended supply accepted by the wizard.</summary>
    public const decimal MaxTargetSupply = CookBookManifest.MaxTargetSupply;
    /// <summary>The display name used to derive the CookBook id.</summary>
    [ObservableProperty] private string _name = "";
    /// <summary>The collection symbol written by the caller.</summary>
    [ObservableProperty] private string _symbol = "";
    /// <summary>Canvas width in pixels; changing it respects the aspect lock.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LargeCanvasOpacity))] private int _width = CanvasAdvice.DefaultSide;
    /// <summary>Canvas height in pixels; changing it respects the aspect lock.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LargeCanvasOpacity))] private int _height = CanvasAdvice.DefaultSide;

    /// <summary>Ink for the large-canvas warning: its line is always laid out, so it appearing
    /// moves nothing.</summary>
    public double LargeCanvasOpacity => CanvasAdvice.IsLarge(Width, Height) ? 1 : 0;
    /// <summary>Keep the current canvas ratio while editing either dimension.</summary>
    [ObservableProperty] private bool _aspectLocked = true;
    /// <summary>The collection description carried into the new manifest.</summary>
    [ObservableProperty] private string _description = "";

    /// <summary>How many assets the collection intends to mint. Optional — empty (or 0) means "not decided",
    /// a real answer at this point, stored as null rather than as a target of zero. Nullable so clearing
    /// the field IS that answer, not a conversion error printed under it.
    /// Purely declarative: it never constrains a cook, which takes its count from the Cook dialog.</summary>
    [ObservableProperty] private int? _targetSupply;

    /// <summary>Null when unset, so the optional target-supply field is omitted. Supported archives use the v2 floor.</summary>
    public int? TargetSupplyOrNull => TargetSupply > 0 ? Math.Min(TargetSupply.Value, CookBookManifest.MaxTargetSupply) : null;

    partial void OnTargetSupplyChanged(int? value)
    {
        if (value is > CookBookManifest.MaxTargetSupply) TargetSupply = CookBookManifest.MaxTargetSupply;
    }

    private readonly AspectLink _link = new();

    /// <summary>The id derived from the name: lower-case, spaces to dashes.</summary>
    public string DerivedId => DeriveId(Name);

    /// <summary>The derived id as the Identifier chip prints it.</summary>
    public string DerivedIdText => IdChipText(DerivedId);

    /// <summary>Creates the New CookBook wizard.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    public NewCookBookViewModel(IDialogService dialogs) : base(dialogs) { }

    /// <summary>Creates the wizard with the canvas starting at the side Settings remembers.</summary>
    /// <param name="dialogs">The dialog layer.</param>
    /// <param name="canvasSide">The side both dimensions start at (<see cref="Services.IAppSettings.DefaultCanvasSide"/>).</param>
    public NewCookBookViewModel(IDialogService dialogs, int canvasSide) : base(dialogs)
    {
        Width = canvasSide;
        Height = canvasSide;
    }

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(DerivedId));
        OnPropertyChanged(nameof(DerivedIdText));
        CreateCommand.NotifyCanExecuteChanged();
    }

    partial void OnAspectLockedChanged(bool value)
    {
        if (value) _link.Capture(Width, Height);   // W:H captured when locking
    }

    partial void OnWidthChanged(int value)
    {
        if (_link.WidthChanged(AspectLocked, value, Height) is { } h) _link.Follow(() => Height = h);
    }

    partial void OnHeightChanged(int value)
    {
        if (_link.HeightChanged(AspectLocked, Width, value) is { } w) _link.Follow(() => Width = w);
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(DerivedId);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create() => Dialogs.Close(this, this);
}
