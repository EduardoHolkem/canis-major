using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CanisEditor.Theming;

namespace CanisEditor.Game;

// O sprite aberto: o quadro animado em tamanho grande, a origem marcada e a tira com todos os quadros.
// Clicar no quadro grande muda a origem.
public sealed class SpriteView : Control
{
    private const double StripHeight = 72;
    private static readonly Typeface LabelFace = new("Inter", FontStyle.Normal, FontWeight.Normal);

    private readonly DispatcherTimer _timer;
    private int _frame;
    private Rect _frameRect;
    private double _frameScale = 1;
    private bool _attached;

    public SpriteView()
    {
        Focusable = true;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(125), DispatcherPriority.Render, (_, _) => NextFrame());
    }

    public SpriteImage? Sprite { get; private set; }
    public Bitmap? Bitmap => Sprite?.Bitmap;
    public int Frames { get; private set; } = 1;
    public double OriginX { get; private set; }
    public double OriginY { get; private set; }
    public double Fps { get; private set; } = 8;

    public double FrameWidth => Sprite is null ? 0 : Math.Max(1, Sprite.PixelWidth / Math.Max(1, Frames));
    public double FrameHeight => Sprite?.PixelHeight ?? 0;

    // Um clique escolheu outra origem, já em pixels do quadro.
    public event Action<double, double>? OriginPicked;

    public void Show(SpriteImage? sprite, int frames, double originX, double originY, double fps)
    {
        Sprite = sprite;
        Frames = Math.Max(1, frames);
        OriginX = originX;
        OriginY = originY;
        Fps = fps;
        _frame %= Frames;
        _timer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(fps <= 0 ? 8 : fps, 0.5, 60));
        if (Frames > 1 && fps > 0 && _attached)
            _timer.Start();
        else
            _timer.Stop();
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        if (Frames > 1 && Fps > 0)
            _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop();
    }

    private void NextFrame()
    {
        if (!IsVisible || Frames <= 1)
            return;
        _frame = (_frame + 1) % Frames;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Bitmap is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var at = e.GetPosition(this);
        if (!_frameRect.Contains(at))
            return;
        var x = Math.Clamp(Math.Round((at.X - _frameRect.X) / _frameScale), 0, FrameWidth);
        var y = Math.Clamp(Math.Round((at.Y - _frameRect.Y) / _frameScale), 0, FrameHeight);
        OriginPicked?.Invoke(x, y);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var palette = Palette.Current;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Bitmap is not { } bitmap)
            return;

        var fw = FrameWidth;
        var fh = FrameHeight;
        var area = new Rect(0, 0, Bounds.Width, Math.Max(40, Bounds.Height - StripHeight - 80));
        var fit = Math.Min((area.Width - 48) / fw, (area.Height - 48) / fh);
        _frameScale = Math.Max(0.25, fit >= 1 ? Math.Floor(Math.Min(fit, 32)) : fit);
        var size = new Size(fw * _frameScale, fh * _frameScale);
        _frameRect = new Rect(new Point(Math.Round((area.Width - size.Width) / 2), Math.Round((area.Height - size.Height) / 2)), size);

        DrawChecker(context, _frameRect, palette, 8);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _frameScale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
            context.DrawImage(bitmap, new Rect(_frame * fw, 0, fw, fh), _frameRect);
        context.DrawRectangle(new Pen(palette.Muted, 1), _frameRect.Inflate(0.5));

        // A origem: o ponto do sprite que fica em X e Y.
        var origin = new Point(_frameRect.X + OriginX * _frameScale, _frameRect.Y + OriginY * _frameScale);
        var accent = new SolidColorBrush(Color.Parse("#FF6B6B"));
        var outline = new Pen(Brushes.Black, 3);
        var pen = new Pen(accent, 1.5);
        foreach (var p in new[] { outline, pen })
        {
            context.DrawLine(p, origin - new Vector(10, 0), origin + new Vector(10, 0));
            context.DrawLine(p, origin - new Vector(0, 10), origin + new Vector(0, 10));
        }
        context.DrawEllipse(null, pen, origin, 4, 4);

        var info = new FormattedText(
            $"{fw:0} × {fh:0} px · {Frames} {(Frames == 1 ? "quadro" : "quadros")} · clique para mudar a origem",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 11.5, palette.Muted);
        context.DrawText(info, new Point(Math.Round((Bounds.Width - info.Width) / 2), _frameRect.Bottom + 10));

        DrawStrip(context, bitmap, fw, fh, palette);
    }

    private void DrawStrip(DrawingContext context, Bitmap bitmap, double fw, double fh, Palette palette)
    {
        // Acima da cápsula do Executar, que fica sobre o rodapé da folha.
        var top = Bounds.Height - StripHeight - 64;
        var thumb = StripHeight - 16;
        var scale = Math.Min(thumb / fh, thumb / fw);
        var w = fw * scale;
        var total = Frames * (w + 8) - 8;
        var x = Math.Max(12, (Bounds.Width - total) / 2);
        for (var i = 0; i < Frames; i++)
        {
            var rect = new Rect(x, top + 8, w, fh * scale);
            DrawChecker(context, rect, palette, 4);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                context.DrawImage(bitmap, new Rect(i * fw, 0, fw, fh), rect);
            var pen = i == _frame ? new Pen(new SolidColorBrush(Color.Parse("#82AAFF")), 2) : new Pen(palette.Tint, 1);
            context.DrawRectangle(pen, rect.Inflate(1));
            x += w + 8;
        }
    }

    private static void DrawChecker(DrawingContext context, Rect rect, Palette palette, double cell)
    {
        context.FillRectangle(palette.CheckerLight, rect);
        using (context.PushClip(rect))
        {
            for (var y = 0; y * cell < rect.Height; y++)
            for (var x = y % 2; x * cell < rect.Width; x += 2)
                context.FillRectangle(palette.CheckerDark, new Rect(rect.X + x * cell, rect.Y + y * cell, cell, cell));
        }
    }
}

// A miniatura de um sprite: só o primeiro quadro, ampliado sem borrar. Usada no pincel da sala.
public sealed class SpriteThumb(SpriteImage sprite) : Control
{
    public override void Render(DrawingContext context)
    {
        var scale = Math.Min(Bounds.Width / sprite.FrameWidth, Bounds.Height / sprite.FrameHeight);
        var size = new Size(sprite.FrameWidth * scale, sprite.FrameHeight * scale);
        var dest = new Rect(new Point((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2), size);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
            context.DrawImage(sprite.Bitmap, new Rect(0, 0, sprite.FrameWidth, sprite.FrameHeight), dest);
    }
}
