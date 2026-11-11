using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CanisEditor.Theming;
using TilesetData = CanisMajor.TilesetData;

namespace CanisEditor.Game;

// A imagem do tileset com a grade por cima. Serve de duas formas: no editor do tileset, o clique
// marca e desmarca tiles sólidos; na sala, o clique escolhe o tile do pincel.
public sealed class TilesetView : Control
{
    private SpriteImage? _image;
    private TilesetData? _set;

    public TilesetView()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    // Com true, mostra os sólidos em vermelho e o clique troca. Com false, o clique escolhe.
    public bool EditSolid { get; set; }

    public int Selected { get; set; } = -1;

    // Quando o tileset é pequeno, cada pixel vira vários na tela, para dar para clicar.
    public double MaxScale { get; set; } = 4;

    public event Action<int>? TileClicked;

    public void Show(SpriteImage? image, TilesetData? set)
    {
        _image = image;
        _set = set;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private int Columns => _image is null || _set is null ? 0 : Math.Max(1, _image.PixelWidth / Math.Max(1, _set.TileWidth));
    private int Rows => _image is null || _set is null ? 0 : Math.Max(1, _image.PixelHeight / Math.Max(1, _set.TileHeight));

    private double Scale(double width)
    {
        if (_image is null || _image.PixelWidth == 0)
            return 1;
        var fit = width / _image.PixelWidth;
        return Math.Max(0.25, Math.Min(MaxScale, fit >= 1 ? Math.Floor(fit) : fit));
    }

    protected override Size MeasureOverride(Size available)
    {
        if (_image is null)
            return new Size(0, 24);
        var width = double.IsInfinity(available.Width) ? _image.PixelWidth * MaxScale : available.Width;
        var scale = Scale(width);
        return new Size(_image.PixelWidth * scale, _image.PixelHeight * scale);
    }

    public int TileAt(Point point)
    {
        if (_image is null || _set is null)
            return -1;
        var scale = Scale(Bounds.Width);
        var col = (int)(point.X / (scale * _set.TileWidth));
        var row = (int)(point.Y / (scale * _set.TileHeight));
        return col < 0 || row < 0 || col >= Columns || row >= Rows ? -1 : row * Columns + col;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var id = TileAt(e.GetPosition(this));
        if (id < 0)
            return;
        Selected = id;
        TileClicked?.Invoke(id);
        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var palette = Palette.Current;
        if (_image is null || _set is null)
        {
            context.DrawText(new FormattedText("Escolha a imagem do tileset no Inspetor.", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Inter"), 12, palette.Muted), new Point(0, 4));
            return;
        }
        var scale = Scale(Bounds.Width);
        var size = new Rect(0, 0, _image.PixelWidth * scale, _image.PixelHeight * scale);
        // Xadrez atrás, para a transparência aparecer.
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(60, 62, 68)), size);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
            context.DrawImage(_image.Bitmap, new Rect(0, 0, _image.Bitmap.Size.Width, _image.Bitmap.Size.Height), size);

        var tw = _set.TileWidth * scale;
        var th = _set.TileHeight * scale;
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), 1);
        for (var c = 0; c <= Columns; c++)
            context.DrawLine(grid, new Point(Math.Round(c * tw) + 0.5, 0), new Point(Math.Round(c * tw) + 0.5, Rows * th));
        for (var r = 0; r <= Rows; r++)
            context.DrawLine(grid, new Point(0, Math.Round(r * th) + 0.5), new Point(Columns * tw, Math.Round(r * th) + 0.5));

        if (EditSolid)
        {
            var solid = new SolidColorBrush(Color.FromArgb(110, 255, 70, 70));
            foreach (var id in _set.Solid)
            {
                if (id < 0 || id >= Columns * Rows)
                    continue;
                context.FillRectangle(solid, new Rect(id % Columns * tw, id / Columns * th, tw, th));
            }
        }

        if (Selected >= 0 && Selected < Columns * Rows)
        {
            var rect = new Rect(Selected % Columns * tw, Selected / Columns * th, tw, th).Inflate(1);
            context.DrawRectangle(new Pen(Brushes.Black, 3), rect);
            context.DrawRectangle(new Pen(palette.Accent, 1.5), rect);
        }
    }
}
