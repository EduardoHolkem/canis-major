using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CanisEditor.Theming;
using LayerData = CanisMajor.LayerData;
using RoomLayers = CanisMajor.RoomLayers;

namespace CanisEditor.Game;

// A imagem de um sprite para desenhar na sala: o PNG inteiro, quantos quadros tem e a origem.
// O tamanho vem do PNG, não do Bitmap, que nos testes sem placa de vídeo tem só 1 pixel.
public sealed record SpriteImage(Bitmap Bitmap, int PixelWidth, int PixelHeight, int Frames, double OriginX, double OriginY)
{
    public double FrameWidth => Math.Max(1, PixelWidth / Math.Max(1, Frames));
    public double FrameHeight => Math.Max(1, PixelHeight);
}

// O editor visual da sala. Desenha as instâncias com os sprites e muda o RoomModel; a janela grava
// o texto no documento a cada mudança, para o desfazer, o salvar e o ponto de alterado continuarem iguais.
public sealed class RoomCanvas : Control
{
    private const double MarkerSize = 16;
    private const double MinZoom = 0.25;
    private const double MaxZoom = 16;
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
    private static readonly Typeface LabelFace = new("Inter", FontStyle.Normal, FontWeight.Medium);

    private enum Gesture { None, Move, Paint, Pan }

    private RoomModel? _model;
    private Gesture _gesture;
    private Point _gestureStart;
    private Vector _panAtStart;
    private Point _grabOffset;
    private bool _changedInGesture;
    private Point? _hover;
    private bool _spaceHeld;
    private bool _fitted;
    private double _zoom = 2;
    private Vector _pan;

    public RoomCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public RoomModel? Model
    {
        get => _model;
        set
        {
            var first = _model is null;
            _model = value;
            if (SelectedIndex >= (_model?.Instances.Count ?? 0))
                SelectedIndex = -1;
            if (first)
                _fitted = false;
            InvalidateVisual();
        }
    }

    public Dictionary<string, GameObjectInfo> Objects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // O sprite pelo nome, ou null se não existir.
    public Func<string, SpriteImage?> SpriteFor { get; set; } = _ => null;

    // O objeto que o clique num lugar vazio coloca. Null: o clique só escolhe.
    public string? Brush { get; set; }

    // A camada onde o pincel coloca as instâncias novas. Null: a primeira de instâncias.
    public string? ActiveLayer { get; set; }

    // Só a camada ativa recebe cliques, como no GameMaker. Desligado, qualquer instância visível se escolhe.
    public bool LockToActiveLayer { get; set; }

    // O tamanho da tela do jogo, desenhado como um retângulo para mostrar o que a câmera vê.
    public Size View { get; set; } = new(480, 270);

    public int SelectedIndex { get; private set; } = -1;

    public RoomInstance? Selected => _model is { } m && SelectedIndex >= 0 && SelectedIndex < m.Instances.Count ? m.Instances[SelectedIndex] : null;

    public double Zoom => _zoom;

    // A sala mudou e o gesto terminou: hora de gravar o texto.
    public event Action? Changed;

    public event Action? SelectionChanged;

    // Ctrl Z e Ctrl Y com o foco na sala vão para o documento.
    public event Action<bool>? UndoRequested;

    public void Select(int index)
    {
        var count = _model?.Instances.Count ?? 0;
        var next = index >= 0 && index < count ? index : -1;
        if (next == SelectedIndex)
            return;
        SelectedIndex = next;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    public void ResetView()
    {
        _fitted = false;
        InvalidateVisual();
    }

    // Coordenadas

    private Point ToRoom(Point screen) => new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    private Point ToScreen(Point room) => new(room.X * _zoom + _pan.X, room.Y * _zoom + _pan.Y);

    private void FitIfNeeded()
    {
        if (_fitted || _model is null || Bounds.Width < 10 || Bounds.Height < 10)
            return;
        _fitted = true;
        const double margin = 48;
        var fit = Math.Min((Bounds.Width - margin) / _model.Width, (Bounds.Height - margin) / _model.Height);
        // Pixel art fica nítida em zoom inteiro; entre 1 e 2, meio passo ainda aproveita melhor a tela.
        _zoom = Math.Clamp(fit >= 2 ? Math.Floor(fit) : fit >= 1 ? Math.Floor(fit * 2) / 2 : fit, MinZoom, MaxZoom);
        _pan = new Vector((Bounds.Width - _model.Width * _zoom) / 2, (Bounds.Height - _model.Height * _zoom) / 2);
    }

    // Retângulo da instância na sala: o quadro do sprite, ou um marcador quando o objeto não tem sprite.
    public Rect BoundsOf(RoomInstance instance)
    {
        if (SpriteOf(instance) is { } sprite)
            return new Rect(instance.X - sprite.OriginX, instance.Y - sprite.OriginY, sprite.FrameWidth, sprite.FrameHeight);
        return new Rect(instance.X, instance.Y, MarkerSize, MarkerSize);
    }

    private SpriteImage? SpriteOf(RoomInstance instance) =>
        Objects.TryGetValue(instance.Object, out var info) && info.Sprite is { } name ? SpriteFor(name) : null;

    private SpriteImage? SpriteOfObject(string? objectName) =>
        objectName is not null && Objects.TryGetValue(objectName, out var info) && info.Sprite is { } name ? SpriteFor(name) : null;

    // A profundidade, igual ao jogo: com "layers" no arquivo vale a da camada; sem, a do objeto.
    private int LayerOf(RoomInstance instance) =>
        _model is { Layers.Count: > 0 } m && m.LayerOf(instance) is { } layer ? layer.Depth
        : Objects.TryGetValue(instance.Object, out var info) ? info.Layer : 0;

    private bool YSortOf(RoomInstance instance) =>
        _model is { Layers.Count: > 0 } m && m.LayerOf(instance) is { YSort: true };

    private bool IsLayerVisible(RoomInstance instance) => _model?.LayerOf(instance) is not { Visible: false };

    private bool IsPickable(RoomInstance instance) =>
        IsLayerVisible(instance) && (!LockToActiveLayer || _model is null || ActiveLayer is null
            || string.Equals(_model.LayerOf(instance)?.Name, ActiveLayer, StringComparison.OrdinalIgnoreCase));

    // A ordem de desenho: profundidade maior por cima; na mesma, pela posição Y quando a camada ordena por Y,
    // senão a que vem depois no arquivo. Camadas escondidas ficam de fora.
    private List<int> DrawOrder()
    {
        if (_model is null)
            return [];
        return Enumerable.Range(0, _model.Instances.Count)
            .Where(i => IsLayerVisible(_model.Instances[i]))
            .OrderBy(i => LayerOf(_model.Instances[i]))
            .ThenBy(i => YSortOf(_model.Instances[i]) ? _model.Instances[i].Y : double.MinValue)
            .ThenBy(i => i)
            .ToList();
    }

    public int HitTest(Point room)
    {
        if (_model is null)
            return -1;
        var order = DrawOrder();
        for (var k = order.Count - 1; k >= 0; k--)
        {
            if (IsPickable(_model.Instances[order[k]]) && BoundsOf(_model.Instances[order[k]]).Contains(room))
                return order[k];
        }
        return -1;
    }

    // Onde o objeto fica ao clicar num ponto: o canto do sprite cai na célula da grade, e a posição é a da origem.
    public Point SnapPlace(Point room, string objectName, bool snap)
    {
        var sprite = SpriteOfObject(objectName);
        var (ox, oy) = sprite is null ? (0.0, 0.0) : (sprite.OriginX, sprite.OriginY);
        if (!snap || _model is null)
            return new Point(Math.Round(room.X), Math.Round(room.Y));
        var g = _model.Grid;
        return new Point(Math.Floor(room.X / g) * g + ox, Math.Floor(room.Y / g) * g + oy);
    }

    private Point SnapMove(RoomInstance instance, Point topLeft, bool snap)
    {
        var sprite = SpriteOf(instance);
        var (ox, oy) = sprite is null ? (0.0, 0.0) : (sprite.OriginX, sprite.OriginY);
        if (!snap || _model is null)
            return new Point(Math.Round(topLeft.X + ox), Math.Round(topLeft.Y + oy));
        var g = _model.Grid;
        return new Point(Math.Round(topLeft.X / g) * g + ox, Math.Round(topLeft.Y / g) * g + oy);
    }

    // Mudanças

    public void PlaceAt(Point room, bool snap)
    {
        if (_model is null || Brush is null)
            return;
        var at = SnapPlace(room, Brush, snap);
        // Pintando, a mesma célula não ganha duas cópias iguais.
        if (_model.Instances.Any(i => string.Equals(i.Object, Brush, StringComparison.OrdinalIgnoreCase) && i.X == at.X && i.Y == at.Y))
            return;
        var placed = new RoomInstance { Object = Brush, X = at.X, Y = at.Y };
        // Na camada ativa. A padrão não precisa ir escrita no arquivo.
        if (ActiveLayer is { } layer && _model.Layers.Count > 0
            && !string.Equals(layer, RoomLayers.DefaultInstanceLayer(_model.Layers)?.Name, StringComparison.OrdinalIgnoreCase))
            placed.Layer = layer;
        _model.Instances.Add(placed);
        SelectedIndex = _model.Instances.Count - 1;
        _changedInGesture = true;
        InvalidateVisual();
        SelectionChanged?.Invoke();
    }

    // Coloca o objeto do pincel e grava, como um clique de uma vez só.
    public void Place(Point room, bool snap = true)
    {
        _changedInGesture = false;
        PlaceAt(room, snap);
        if (_changedInGesture)
        {
            _changedInGesture = false;
            Changed?.Invoke();
        }
    }

    public void DeleteAt(int index)
    {
        if (_model is null || index < 0 || index >= _model.Instances.Count)
            return;
        _model.Instances.RemoveAt(index);
        SelectedIndex = SelectedIndex == index ? -1 : SelectedIndex > index ? SelectedIndex - 1 : SelectedIndex;
        InvalidateVisual();
        SelectionChanged?.Invoke();
        Changed?.Invoke();
    }

    public void Nudge(double dx, double dy)
    {
        if (Selected is not { } instance)
            return;
        instance.X += dx;
        instance.Y += dy;
        InvalidateVisual();
        Changed?.Invoke();
    }

    public void DuplicateSelected()
    {
        if (_model is null || Selected is not { } instance)
            return;
        var copy = instance.Clone();
        copy.X += _model.Grid;
        _model.Instances.Add(copy);
        SelectedIndex = _model.Instances.Count - 1;
        InvalidateVisual();
        SelectionChanged?.Invoke();
        Changed?.Invoke();
    }

    // Mouse e teclado

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (_model is null)
            return;
        var point = e.GetCurrentPoint(this);
        var screen = point.Position;
        var room = ToRoom(screen);
        var snap = !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _gestureStart = screen;
        _changedInGesture = false;

        if (point.Properties.IsMiddleButtonPressed || (point.Properties.IsLeftButtonPressed && _spaceHeld))
        {
            _gesture = Gesture.Pan;
            _panAtStart = _pan;
            Cursor = HandCursor;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        var hit = HitTest(room);
        if (point.Properties.IsRightButtonPressed)
        {
            // Botão direito apaga, como no GameMaker. Sem instância embaixo, só tira o pincel.
            if (hit >= 0)
                DeleteAt(hit);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (hit >= 0 && !(Brush is not null && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            Select(hit);
            var bounds = BoundsOf(_model.Instances[hit]);
            _grabOffset = new Point(room.X - bounds.X, room.Y - bounds.Y);
            _gesture = Gesture.Move;
            Cursor = MoveCursor;
        }
        else if (Brush is not null)
        {
            _gesture = Gesture.Paint;
            PlaceAt(room, snap);
        }
        else
        {
            Select(-1);
            _gesture = Gesture.None;
        }
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var screen = e.GetPosition(this);
        _hover = screen;
        var room = ToRoom(screen);
        var snap = !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (_gesture)
        {
            case Gesture.Pan:
                _pan = _panAtStart + (screen - _gestureStart);
                break;
            case Gesture.Move when Selected is { } instance:
                var at = SnapMove(instance, new Point(room.X - _grabOffset.X, room.Y - _grabOffset.Y), snap);
                if (at.X != instance.X || at.Y != instance.Y)
                {
                    instance.X = at.X;
                    instance.Y = at.Y;
                    _changedInGesture = true;
                    SelectionChanged?.Invoke();
                }
                break;
            case Gesture.Paint:
                PlaceAt(room, snap);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        _gesture = Gesture.None;
        Cursor = null;
        if (_changedInGesture)
        {
            _changedInGesture = false;
            Changed?.Invoke();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var at = e.GetPosition(this);
        var room = ToRoom(at);
        var factor = e.Delta.Y > 0 ? 1.25 : 1 / 1.25;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        // Perto de um número inteiro, encaixa nele, para a pixel art não borrar.
        if (Math.Abs(_zoom - Math.Round(_zoom)) < 0.12 && _zoom >= 1)
            _zoom = Math.Round(_zoom);
        _pan = new Vector(at.X - room.X * _zoom, at.Y - room.Y * _zoom);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) && _model is not null ? _model.Grid : 1;
        switch (e.Key)
        {
            case Key.Space:
                _spaceHeld = true;
                Cursor = HandCursor;
                break;
            case Key.Delete or Key.Back when SelectedIndex >= 0:
                DeleteAt(SelectedIndex);
                break;
            case Key.Escape:
                if (SelectedIndex >= 0)
                    Select(-1);
                break;
            case Key.Left when SelectedIndex >= 0:
                Nudge(-step, 0);
                break;
            case Key.Right when SelectedIndex >= 0:
                Nudge(step, 0);
                break;
            case Key.Up when SelectedIndex >= 0:
                Nudge(0, -step);
                break;
            case Key.Down when SelectedIndex >= 0:
                Nudge(0, step);
                break;
            case Key.D when ctrl:
                DuplicateSelected();
                break;
            case Key.Z when ctrl:
                UndoRequested?.Invoke(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                break;
            case Key.Y when ctrl:
                UndoRequested?.Invoke(false);
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                ResetView();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key == Key.Space)
        {
            _spaceHeld = false;
            Cursor = null;
        }
    }

    // Desenho

    public override void Render(DrawingContext context)
    {
        FitIfNeeded();
        var palette = Palette.Current;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_model is null)
            return;

        var background = Color.TryParse(_model.Background, out var parsed) ? parsed : Color.Parse("#16181D");
        var roomRect = new Rect(0, 0, _model.Width, _model.Height);
        var screenRoom = new Rect(ToScreen(roomRect.TopLeft), ToScreen(roomRect.BottomRight));

        context.FillRectangle(new SolidColorBrush(background), screenRoom);
        DrawGrid(context, screenRoom, background);

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = _zoom >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.MediumQuality }))
        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y)))
        {
            // Fundos e instâncias numa pilha só, como no jogo.
            var backgrounds = _model.Layers.Where(l => l.IsBackground && l.Visible).OrderBy(l => l.Depth).ToList();
            var next = 0;
            foreach (var index in DrawOrder())
            {
                var instance = _model.Instances[index];
                while (next < backgrounds.Count && backgrounds[next].Depth <= LayerOf(instance))
                    DrawBackgroundLayer(context, backgrounds[next++]);
                // Fora da camada ativa, com a trava ligada, a instância fica apagadinha.
                DrawInstance(context, instance, IsPickable(instance) ? 1 : 0.45);
            }
            while (next < backgrounds.Count)
                DrawBackgroundLayer(context, backgrounds[next++]);
            DrawGhost(context);
        }

        // Marcadores, câmera e seleção ficam com o mesmo traço em qualquer zoom.
        foreach (var index in DrawOrder())
        {
            var instance = _model.Instances[index];
            if (SpriteOf(instance) is null)
                DrawMarker(context, instance, palette);
        }

        if (View.Width > 0 && (View.Width < _model.Width || View.Height < _model.Height))
        {
            var view = new Rect(ToScreen(new Point(0, 0)), ToScreen(new Point(View.Width, View.Height)));
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1, new DashStyle([4, 4], 0)), view);
        }

        context.DrawRectangle(new Pen(palette.Muted, 1), screenRoom.Inflate(0.5));

        if (_hover is { } hover && _gesture == Gesture.None && HitTest(ToRoom(hover)) is var hoverIndex and >= 0 && hoverIndex != SelectedIndex)
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), 1), ScreenBounds(_model.Instances[hoverIndex]).Inflate(0.5));

        if (Selected is { } selected)
        {
            var rect = ScreenBounds(selected).Inflate(1.5);
            context.DrawRectangle(new Pen(Brushes.Black, 3), rect);
            context.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#82AAFF")), 1.5), rect);
        }
    }

    private Rect ScreenBounds(RoomInstance instance)
    {
        var bounds = BoundsOf(instance);
        if (SpriteOf(instance) is null)
        {
            var at = ToScreen(new Point(instance.X, instance.Y));
            return new Rect(at.X, at.Y, MarkerSize, MarkerSize);
        }
        return new Rect(ToScreen(bounds.TopLeft), ToScreen(bounds.BottomRight));
    }

    private void DrawGrid(DrawingContext context, Rect screenRoom, Color background)
    {
        if (_model is null)
            return;
        var step = _model.Grid * _zoom;
        if (step < 6)
            return;
        var light = (background.R * 0.299 + background.G * 0.587 + background.B * 0.114) > 140;
        var pen = new Pen(new SolidColorBrush(light ? Color.FromArgb(28, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255)), 1);
        using (context.PushClip(screenRoom))
        {
            for (var x = screenRoom.X; x <= screenRoom.Right + 0.5; x += step)
                context.DrawLine(pen, new Point(Math.Round(x) + 0.5, screenRoom.Top), new Point(Math.Round(x) + 0.5, screenRoom.Bottom));
            for (var y = screenRoom.Y; y <= screenRoom.Bottom + 0.5; y += step)
                context.DrawLine(pen, new Point(screenRoom.Left, Math.Round(y) + 0.5), new Point(screenRoom.Right, Math.Round(y) + 0.5));
        }
    }

    private void DrawInstance(DrawingContext context, RoomInstance instance, double opacity)
    {
        if (SpriteOf(instance) is not { } sprite)
            return;
        var source = new Rect(0, 0, sprite.FrameWidth, sprite.FrameHeight);
        var dest = new Rect(instance.X - sprite.OriginX, instance.Y - sprite.OriginY, sprite.FrameWidth, sprite.FrameHeight);
        using (context.PushOpacity(opacity))
            context.DrawImage(sprite.Bitmap, source, dest);
    }

    // Uma camada de fundo: a cor na sala inteira e o sprite a partir do canto, repetido se pedido.
    private void DrawBackgroundLayer(DrawingContext context, LayerData layer)
    {
        if (_model is null)
            return;
        var room = new Rect(0, 0, _model.Width, _model.Height);
        if (layer.Color is { } hex && Color.TryParse(hex, out var color))
            context.FillRectangle(new SolidColorBrush(color), room);
        if (layer.Sprite is not { } name || SpriteFor(name) is not { } sprite)
            return;
        var source = new Rect(0, 0, sprite.FrameWidth, sprite.FrameHeight);
        using (context.PushClip(room))
        {
            var endX = layer.TileX ? _model.Width : 1;
            var endY = layer.TileY ? _model.Height : 1;
            for (double y = 0; y < endY; y += sprite.FrameHeight)
                for (double x = 0; x < endX; x += sprite.FrameWidth)
                    context.DrawImage(sprite.Bitmap, source, new Rect(x, y, sprite.FrameWidth, sprite.FrameHeight));
        }
    }

    // A prévia do pincel embaixo do mouse, onde o clique vai colocar.
    private void DrawGhost(DrawingContext context)
    {
        if (Brush is null || _hover is not { } hover || _gesture is Gesture.Move or Gesture.Pan || _spaceHeld)
            return;
        var room = ToRoom(hover);
        if (HitTest(room) >= 0 && _gesture != Gesture.Paint)
            return;
        var at = SnapPlace(room, Brush, true);
        var ghost = new RoomInstance { Object = Brush, X = at.X, Y = at.Y };
        if (SpriteOf(ghost) is not null)
            DrawInstance(context, ghost, 0.45);
    }

    private void DrawMarker(DrawingContext context, RoomInstance instance, Palette palette)
    {
        var at = ToScreen(new Point(instance.X, instance.Y));
        var rect = new Rect(at.X, at.Y, MarkerSize, MarkerSize);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, 40, 46, 60)), new Pen(new SolidColorBrush(Color.Parse("#82AAFF")), 1), rect, 4, 4);
        var initial = instance.Object.Length > 0 ? instance.Object[..1].ToUpperInvariant() : "?";
        var letter = new FormattedText(initial, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 10, Brushes.White);
        context.DrawText(letter, new Point(rect.X + (MarkerSize - letter.Width) / 2, rect.Y + (MarkerSize - letter.Height) / 2));
        var label = new FormattedText(instance.Object, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 11, palette.Ink);
        var labelRect = new Rect(rect.Right + 4, rect.Y + (MarkerSize - label.Height) / 2 - 1, label.Width + 8, label.Height + 2);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 22, 24, 29)), null, labelRect, 4, 4);
        context.DrawText(new FormattedText(instance.Object, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 11, Brushes.White),
            new Point(labelRect.X + 4, labelRect.Y + 1));
    }
}
