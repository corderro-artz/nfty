using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;

namespace Nfty.App.Controls;

/// <summary>
/// The one canvas-size picker: a width field, a link toggle, a height field. Every form that asks for a canvas
/// (New CookBook, New Ingredient's loose canvas) draws this control, so a size is always two numbers and never text
/// the reader has to spell as <c>16x16</c>.
/// </summary>
/// <remarks>
/// The control is a view only: it binds <see cref="PixelWidth"/>, <see cref="PixelHeight"/> and
/// <see cref="IsLinked"/> two-way, and the view model keeps the ratio (<see cref="ViewModels.AspectLink"/>), so a
/// test of the ratio needs no window. The toggle swaps its glyph (<c>IconLink</c> / <c>IconUnlink</c>) and its ink,
/// never its size, so linking moves nothing.
/// </remarks>
public class CanvasSizeField : UserControl
{
    /// <summary>Width in pixels.</summary>
    public static readonly StyledProperty<int> PixelWidthProperty = AvaloniaProperty.Register<CanvasSizeField, int>(
        nameof(PixelWidth), 32, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Height in pixels.</summary>
    public static readonly StyledProperty<int> PixelHeightProperty = AvaloniaProperty.Register<CanvasSizeField, int>(
        nameof(PixelHeight), 32, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Whether the two sides keep their ratio.</summary>
    public static readonly StyledProperty<bool> IsLinkedProperty = AvaloniaProperty.Register<CanvasSizeField, bool>(
        nameof(IsLinked), true, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Width in pixels.</summary>
    public int PixelWidth { get => GetValue(PixelWidthProperty); set => SetValue(PixelWidthProperty, value); }

    /// <summary>Height in pixels.</summary>
    public int PixelHeight { get => GetValue(PixelHeightProperty); set => SetValue(PixelHeightProperty, value); }

    /// <summary>Whether the two sides keep their ratio.</summary>
    public bool IsLinked { get => GetValue(IsLinkedProperty); set => SetValue(IsLinkedProperty, value); }

    private readonly Path _glyph;
    private readonly ToggleButton _link;
    private IDisposable? _glyphBinding;

    /// <summary>Builds the two fields and the toggle between them.</summary>
    public CanvasSizeField()
    {
        var w = Field("Width in pixels");
        w.Bind(NumericUpDown.ValueProperty, new Binding(nameof(PixelWidth)) { Source = this, Mode = BindingMode.TwoWay, Converter = IntDecimal.Instance });
        var h = Field("Height in pixels");
        h.Bind(NumericUpDown.ValueProperty, new Binding(nameof(PixelHeight)) { Source = this, Mode = BindingMode.TwoWay, Converter = IntDecimal.Instance });
        Grid.SetColumn(h, 2);

        _glyph = new Path();
        _glyph.Classes.Add("ico");
        _glyph.Classes.Add("sm");
        _link = new ToggleButton { Content = _glyph, VerticalAlignment = VerticalAlignment.Center };
        _link.Classes.Add("linktog");
        _link.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(IsLinked)) { Source = this, Mode = BindingMode.TwoWay });
        Grid.SetColumn(_link, 1);

        Content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Children = { w, _link, h } };
        Sync();
    }

    private static NumericUpDown Field(string tip)
    {
        var f = new NumericUpDown { Minimum = 1, Maximum = 16384, FormatString = "0", Increment = 1 };
        ToolTip.SetTip(f, tip);
        return f;
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsLinkedProperty) Sync();
    }

    private void Sync()
    {
        if (_glyph is null) return;
        _glyphBinding?.Dispose();
        _glyphBinding = _glyph.Bind(Path.DataProperty, this.GetResourceObservable(IsLinked ? "IconLink" : "IconUnlink", r => r as Geometry));
        ToolTip.SetTip(_link, IsLinked ? "Sides linked: changing one keeps the ratio. Click to unlink."
                                       : "Sides unlinked: each changes alone. Click to link.");
    }

    /// <summary>NumericUpDown speaks decimal; the canvas is whole pixels.</summary>
    private sealed class IntDecimal : Avalonia.Data.Converters.IValueConverter
    {
        public static readonly IntDecimal Instance = new();
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is int i ? (decimal?)i : null;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is decimal d ? (int)Math.Clamp(Math.Round(d), 1, 16384) : BindingOperations.DoNothing;
    }
}
