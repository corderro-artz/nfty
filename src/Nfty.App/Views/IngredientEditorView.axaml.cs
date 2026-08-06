using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Nfty.App.ViewModels;

namespace Nfty.App.Views;

/// <summary>The ingredient editor view. Code-behind is limited to loading the XAML and the few
/// interactions that genuinely need a control reference; everything else is bound.</summary>
public partial class IngredientEditorView : UserControl
{
    private readonly List<(int x, int y)> _points = new();
    private bool _drawing;

    /// <summary>Loads the view.</summary>
    public IngredientEditorView()
    {
        InitializeComponent();

        var img = this.FindControl<Image>("CanvasImage")!;
        img.PointerPressed += (_, e) => { _drawing = true; _points.Clear(); AddPoint(img, e); };
        img.PointerMoved += (_, e) => { if (_drawing) AddPoint(img, e); };
        img.PointerReleased += (_, e) =>
        {
            if (!_drawing) return;
            _drawing = false;
            AddPoint(img, e);
            if (DataContext is IngredientEditorViewModel vm && _points.Count > 0)
                vm.ApplyToolStroke(_points.ToArray());
            _points.Clear();
        };
    }

    // Map the pointer position (control space) to value-map pixel coords, honouring Stretch=Uniform.
    private void AddPoint(Image img, PointerEventArgs e)
    {
        if (img.Source is not Bitmap bmp) return;
        var p = e.GetPosition(img);
        double imgW = bmp.PixelSize.Width, imgH = bmp.PixelSize.Height;
        double cw = img.Bounds.Width, ch = img.Bounds.Height;
        if (imgW <= 0 || imgH <= 0 || cw <= 0 || ch <= 0) return;
        double scale = System.Math.Min(cw / imgW, ch / imgH);
        double offX = (cw - imgW * scale) / 2, offY = (ch - imgH * scale) / 2;
        int px = (int)((p.X - offX) / scale);
        int py = (int)((p.Y - offY) / scale);
        px = System.Math.Clamp(px, 0, (int)imgW - 1);
        py = System.Math.Clamp(py, 0, (int)imgH - 1);
        var pt = (px, py);
        if (_points.Count == 0 || _points[^1] != pt) _points.Add(pt);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
