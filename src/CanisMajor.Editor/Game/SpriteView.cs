using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CanisEditor.Theming;

namespace CanisEditor.Game;

// As ferramentas do editor de sprites.
public enum SpriteTool { Origin, Pencil, Eraser, Fill, Picker, Mask }

// O sprite aberto: o quadro em tamanho grande, a origem, a máscara e a tira com todos os quadros.
// Com os pixels carregados, dá para desenhar: lápis, borracha, balde e conta-gotas. Clicar na tira
// escolhe o quadro, e a animação toca ou para.
public sealed class SpriteView : Control
{
    private const double StripHeight = 72;
    private const int UndoLimit = 50;
    private static readonly Typeface LabelFace = new("Inter", FontStyle.Normal, FontWeight.Normal);

    private readonly DispatcherTimer _timer;
    private readonly Stack<SpritePixels> _undo = new();
    private readonly Stack<SpritePixels> _redo = new();
    private readonly List<Rect> _stripRects = new();
    private int _frame;
    private Rect _frameRect;
    private double _frameScale = 1;
    private bool _attached;
    private WriteableBitmap? _edited;
    private bool _stroking;
    private bool _changedInStroke;
    private (int X, int Y)? _last;
    private (int X, int Y)? _maskStart;

    public SpriteView()
    {
        Focusable = true;
        ClipToBounds = true;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(125), DispatcherPriority.Render, (_, _) => NextFrame());
    }

    public SpriteImage? Sprite { get; private set; }
    public Bitmap? Bitmap => _edited ?? Sprite?.Bitmap;
    public int Frames { get; private set; } = 1;
    public double OriginX { get; private set; }
    public double OriginY { get; private set; }
    public double Fps { get; private set; } = 8;

    // Os pixels para editar. Null: só mostra (PNG que não deu para ler).
    public SpritePixels? Pixels { get; private set; }

    public SpriteTool Tool { get; set; } = SpriteTool.Origin;

    // A cor do lápis e do balde, como 0xRRGGBBAA.
    public uint Color { get; set; } = 0xFFFFFFFF;

    // Casca de cebola: o quadro anterior aparece clarinho atrás, para animar.
    public bool Onion { get; set; }

    // A animação toca sozinha; parada, o quadro escolhido fica na tela para editar.
    public bool Playing { get; private set; } = true;

    public int CurrentFrame => _frame;

    // Máscara de colisão para mostrar, relativa ao quadro (esquerda, topo, direita, base).
    public (double L, double T, double R, double B)? Mask { get; set; }

    public double FrameWidth => Pixels?.FrameWidth ?? (Sprite is null ? 0 : Math.Max(1, Sprite.PixelWidth / Math.Max(1, Frames)));
    public double FrameHeight => Pixels?.Height ?? Sprite?.PixelHeight ?? 0;

    // Um clique escolheu outra origem, já em pixels do quadro.
    public event Action<double, double>? OriginPicked;

    // Um traço terminou e mudou os pixels: hora de gravar.
    public event Action? PixelsChanged;

    // O conta-gotas pegou uma cor.
    public event Action<uint>? ColorPicked;

    // A ferramenta de máscara desenhou um retângulo.
    public event Action<int, int, int, int>? MaskDrawn;

    // Trocou o quadro escolhido ou parou e voltou a tocar.
    public event Action? FrameChanged;

    public void Show(SpriteImage? sprite, int frames, double originX, double originY, double fps)
    {
        Sprite = sprite;
        Frames = Math.Max(1, frames);
        OriginX = originX;
        OriginY = originY;
        Fps = fps;
        _frame %= Frames;
        UpdateTimer();
        InvalidateVisual();
    }

    // Liga a edição. Chamado depois do Show, com os pixels lidos do PNG.
    public void Edit(SpritePixels? pixels)
    {
        if (!ReferenceEquals(pixels, Pixels))
        {
            _undo.Clear();
            _redo.Clear();
        }
        Pixels = pixels;
        if (pixels is not null)
            Frames = pixels.Frames;
        _frame = Math.Clamp(_frame, 0, Frames - 1);
        RefreshBitmap();
        UpdateTimer();
        InvalidateVisual();
    }

    public void SelectFrame(int frame)
    {
        _frame = Math.Clamp(frame, 0, Frames - 1);
        Playing = false;
        UpdateTimer();
        InvalidateVisual();
        FrameChanged?.Invoke();
    }

    public void SetPlaying(bool playing)
    {
        Playing = playing;
        UpdateTimer();
        InvalidateVisual();
        FrameChanged?.Invoke();
    }

    // Antes de mudar os pixels por fora (novo quadro, excluir...), guarda para desfazer.
    public void Remember()
    {
        if (Pixels is null)
            return;
        _undo.Push(Pixels.Clone());
        if (_undo.Count > UndoLimit)
        {
            var keep = _undo.Take(UndoLimit).Reverse().ToList();
            _undo.Clear();
            foreach (var item in keep)
                _undo.Push(item);
        }
        _redo.Clear();
    }

    public bool Undo() => Swap(_undo, _redo);

    public bool Redo() => Swap(_redo, _undo);

    private bool Swap(Stack<SpritePixels> from, Stack<SpritePixels> to)
    {
        if (Pixels is null || from.Count == 0)
            return false;
        to.Push(Pixels.Clone());
        Pixels.Restore(from.Pop());
        PixelsUpdated();
        PixelsChanged?.Invoke();
        return true;
    }

    // Depois de mudar os pixels, a imagem na tela acompanha.
    public void PixelsUpdated()
    {
        if (Pixels is not null)
            Frames = Pixels.Frames;
        _frame = Math.Clamp(_frame, 0, Frames - 1);
        RefreshBitmap();
        UpdateTimer();
        InvalidateVisual();
    }

    private void RefreshBitmap()
    {
        if (Pixels is not { } pixels)
        {
            _edited = null;
            return;
        }
        try
        {
            if (_edited is null || _edited.PixelSize.Width != pixels.Width || _edited.PixelSize.Height != pixels.Height)
                _edited = new WriteableBitmap(new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using var buffer = _edited.Lock();
            var row = pixels.Width * 4;
            for (var y = 0; y < pixels.Height; y++)
                Marshal.Copy(pixels.Rgba, y * row, buffer.Address + y * buffer.RowBytes, row);
        }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            // Sem placa de vídeo (nos testes), fica a imagem do arquivo.
            _edited = null;
        }
    }

    private void UpdateTimer()
    {
        _timer.Interval = TimeSpan.FromSeconds(1 / Math.Clamp(Fps <= 0 ? 8 : Fps, 0.5, 60));
        if (Frames > 1 && Fps > 0 && _attached && Playing)
            _timer.Start();
        else
            _timer.Stop();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _timer.Stop();
    }

    private void NextFrame()
    {
        if (!IsVisible || Frames <= 1 || !Playing)
            return;
        _frame = (_frame + 1) % Frames;
        InvalidateVisual();
    }

    private (int X, int Y) PixelAt(Point at) =>
        ((int)Math.Floor((at.X - _frameRect.X) / _frameScale), (int)Math.Floor((at.Y - _frameRect.Y) / _frameScale));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Bitmap is null)
            return;
        var point = e.GetCurrentPoint(this);
        var at = point.Position;

        // A tira de quadros: clique escolhe o quadro e para a animação para editar.
        for (var i = 0; i < _stripRects.Count; i++)
        {
            if (_stripRects[i].Contains(at))
            {
                SelectFrame(i);
                e.Handled = true;
                return;
            }
        }

        if (!_frameRect.Contains(at))
            return;
        var right = point.Properties.IsRightButtonPressed;
        if (!point.Properties.IsLeftButtonPressed && !right)
            return;

        var (px, py) = PixelAt(at);
        switch (Tool)
        {
            case SpriteTool.Origin:
                var x = Math.Clamp(Math.Round((at.X - _frameRect.X) / _frameScale), 0, FrameWidth);
                var y = Math.Clamp(Math.Round((at.Y - _frameRect.Y) / _frameScale), 0, FrameHeight);
                OriginPicked?.Invoke(x, y);
                break;
            case SpriteTool.Picker when Pixels is not null:
                ColorPicked?.Invoke(Pixels.Get(_frame, px, py));
                break;
            case SpriteTool.Mask:
                _maskStart = (Math.Clamp(px, 0, (int)FrameWidth - 1), Math.Clamp(py, 0, (int)FrameHeight - 1));
                Mask = (_maskStart.Value.X, _maskStart.Value.Y, _maskStart.Value.X + 1, _maskStart.Value.Y + 1);
                e.Pointer.Capture(this);
                InvalidateVisual();
                break;
            case SpriteTool.Pencil or SpriteTool.Eraser or SpriteTool.Fill when Pixels is not null:
                StopForEditing();
                Remember();
                _stroking = true;
                // Botão direito sempre apaga, como borracha.
                var color = right || Tool == SpriteTool.Eraser ? 0u : Color;
                _changedInStroke = Tool == SpriteTool.Fill ? Pixels.Fill(_frame, px, py, color) : Pixels.Set(_frame, px, py, color);
                _last = (px, py);
                PixelsUpdated();
                if (Tool == SpriteTool.Fill)
                    EndStroke();
                else
                    e.Pointer.Capture(this);
                break;
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetCurrentPoint(this);
        var (px, py) = PixelAt(point.Position);
        if (_maskStart is { } start)
        {
            var cx = Math.Clamp(px, 0, (int)FrameWidth - 1);
            var cy = Math.Clamp(py, 0, (int)FrameHeight - 1);
            Mask = (Math.Min(start.X, cx), Math.Min(start.Y, cy), Math.Max(start.X, cx) + 1, Math.Max(start.Y, cy) + 1);
            InvalidateVisual();
            return;
        }
        if (!_stroking || Pixels is null || _last is not { } last)
            return;
        var color = point.Properties.IsRightButtonPressed || Tool == SpriteTool.Eraser ? 0u : Color;
        if (Pixels.Line(_frame, last.X, last.Y, px, py, color))
        {
            _changedInStroke = true;
            PixelsUpdated();
        }
        _last = (px, py);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        if (_maskStart is not null && Mask is { } m)
        {
            _maskStart = null;
            MaskDrawn?.Invoke((int)m.L, (int)m.T, (int)m.R, (int)m.B);
            return;
        }
        EndStroke();
    }

    private void EndStroke()
    {
        if (!_stroking)
            return;
        _stroking = false;
        _last = null;
        if (_changedInStroke)
            PixelsChanged?.Invoke();
        else if (_undo.Count > 0)
            _undo.Pop(); // nada mudou: o retrato não serve para desfazer
    }

    // Desenhar com a animação tocando confunde: para no quadro que está na tela.
    private void StopForEditing()
    {
        if (Playing && Frames > 1)
            SetPlaying(false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (ctrl && e.Key == Key.Z)
            e.Handled = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Redo() : Undo();
        else if (ctrl && e.Key == Key.Y)
            e.Handled = Redo();
        else if (e.Key == Key.Left && Frames > 1)
        {
            SelectFrame((_frame - 1 + Frames) % Frames);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && Frames > 1)
        {
            SelectFrame((_frame + 1) % Frames);
            e.Handled = true;
        }
    }

    public override void Render(DrawingContext context)
    {
        var palette = Palette.Current;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        _stripRects.Clear();
        if (Bitmap is not { } bitmap)
            return;

        var fw = FrameWidth;
        var fh = FrameHeight;
        var area = new Rect(0, 0, Bounds.Width, Math.Max(40, Bounds.Height - StripHeight - 80));
        var fit = Math.Min((area.Width - 48) / fw, (area.Height - 48) / fh);
        _frameScale = Math.Max(0.25, fit >= 1 ? Math.Floor(Math.Min(fit, 32)) : fit);
        var size = new Size(fw * _frameScale, fh * _frameScale);
        _frameRect = new Rect(new Point(Math.Round((area.Width - size.Width) / 2), Math.Round((area.Height - size.Height) / 2)), size);

        // O Bitmap pode ter outro tamanho que o PNG (nos testes, 1 pixel): a conta usa a proporção.
        var sx = bitmap.Size.Width / Math.Max(1, fw * Frames);
        var sy = bitmap.Size.Height / Math.Max(1, fh);

        DrawChecker(context, _frameRect, palette, 8);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _frameScale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
        {
            if (Onion && Frames > 1 && !Playing)
            {
                var previous = (_frame - 1 + Frames) % Frames;
                using (context.PushOpacity(0.3))
                    context.DrawImage(bitmap, new Rect(previous * fw * sx, 0, fw * sx, fh * sy), _frameRect);
            }
            context.DrawImage(bitmap, new Rect(_frame * fw * sx, 0, fw * sx, fh * sy), _frameRect);
        }
        context.DrawRectangle(new Pen(palette.Muted, 1), _frameRect.Inflate(0.5));

        // Grade de pixels quando o zoom é grande, para acertar o lápis.
        if (_frameScale >= 8 && Pixels is not null)
        {
            var grid = new Pen(new SolidColorBrush(Avalonia.Media.Color.FromArgb(28, 128, 128, 128)), 1);
            for (var gx = 1; gx < fw; gx++)
                context.DrawLine(grid, new Point(Math.Round(_frameRect.X + gx * _frameScale) + 0.5, _frameRect.Top), new Point(Math.Round(_frameRect.X + gx * _frameScale) + 0.5, _frameRect.Bottom));
            for (var gy = 1; gy < fh; gy++)
                context.DrawLine(grid, new Point(_frameRect.Left, Math.Round(_frameRect.Y + gy * _frameScale) + 0.5), new Point(_frameRect.Right, Math.Round(_frameRect.Y + gy * _frameScale) + 0.5));
        }

        // A máscara de colisão, tracejada em verde.
        if (Mask is { } mask)
        {
            var rect = new Rect(_frameRect.X + mask.L * _frameScale, _frameRect.Y + mask.T * _frameScale, (mask.R - mask.L) * _frameScale, (mask.B - mask.T) * _frameScale);
            context.FillRectangle(new SolidColorBrush(Avalonia.Media.Color.FromArgb(40, 80, 220, 120)), rect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Avalonia.Media.Color.Parse("#50DC78")), 1.5, new DashStyle([4, 3], 0)), rect);
        }

        // A origem: o ponto do sprite que fica em X e Y.
        var origin = new Point(_frameRect.X + OriginX * _frameScale, _frameRect.Y + OriginY * _frameScale);
        var accent = new SolidColorBrush(Avalonia.Media.Color.Parse("#FF6B6B"));
        var outline = new Pen(Brushes.Black, 3);
        var pen = new Pen(accent, 1.5);
        foreach (var p in new[] { outline, pen })
        {
            context.DrawLine(p, origin - new Vector(10, 0), origin + new Vector(10, 0));
            context.DrawLine(p, origin - new Vector(0, 10), origin + new Vector(0, 10));
        }
        context.DrawEllipse(null, pen, origin, 4, 4);

        var hint = Tool switch
        {
            SpriteTool.Pencil => "esquerdo pinta, direito apaga",
            SpriteTool.Eraser => "clique e arraste para apagar",
            SpriteTool.Fill => "clique para encher a área da mesma cor",
            SpriteTool.Picker => "clique para pegar a cor",
            SpriteTool.Mask => "arraste para desenhar a máscara de colisão",
            _ => "clique para mudar a origem",
        };
        var state = Frames > 1 ? $" · quadro {_frame + 1} de {Frames}{(Playing ? " (tocando)" : "")}" : "";
        var info = new FormattedText(
            $"{fw:0} × {fh:0} px{state} · {hint}",
            CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 11.5, palette.Muted);
        context.DrawText(info, new Point(Math.Round((Bounds.Width - info.Width) / 2), _frameRect.Bottom + 10));

        DrawStrip(context, bitmap, fw, fh, sx, sy, palette);
    }

    private void DrawStrip(DrawingContext context, Bitmap bitmap, double fw, double fh, double sx, double sy, Palette palette)
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
            _stripRects.Add(rect);
            DrawChecker(context, rect, palette, 4);
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
                context.DrawImage(bitmap, new Rect(i * fw * sx, 0, fw * sx, fh * sy), rect);
            var pen = i == _frame ? new Pen(new SolidColorBrush(Avalonia.Media.Color.Parse("#82AAFF")), 2) : new Pen(palette.Tint, 1);
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
