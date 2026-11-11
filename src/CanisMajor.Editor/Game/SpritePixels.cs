using CanisMajor;

namespace CanisEditor.Game;

// Os pixels de um sprite para editar: uma tira RGBA com os quadros lado a lado, todos do mesmo
// tamanho. Tudo aqui é conta pura, sem tela, para os testes conferirem.
public sealed class SpritePixels
{
    public int FrameWidth { get; private set; }
    public int Height { get; private set; }
    public int Frames { get; private set; }
    public byte[] Rgba { get; private set; }

    public int Width => FrameWidth * Frames;

    public SpritePixels(int frameWidth, int height, int frames, byte[]? rgba = null)
    {
        FrameWidth = Math.Max(1, frameWidth);
        Height = Math.Max(1, height);
        Frames = Math.Max(1, frames);
        Rgba = rgba ?? new byte[Width * Height * 4];
        if (Rgba.Length != Width * Height * 4)
            throw new ArgumentException("tamanho dos pixels não bate");
    }

    public static SpritePixels Load(string png, int frames)
    {
        var (w, h, rgba) = Png.Load(png);
        frames = Math.Clamp(frames, 1, Math.Max(1, w));
        // Uma largura que não divide pelos quadros perde a sobra da direita.
        var fw = Math.Max(1, w / frames);
        if (fw * frames == w)
            return new SpritePixels(fw, h, frames, rgba);
        var pixels = new SpritePixels(fw, h, frames);
        for (var y = 0; y < h; y++)
            Array.Copy(rgba, y * w * 4, pixels.Rgba, y * pixels.Width * 4, pixels.Width * 4);
        return pixels;
    }

    public void Save(string png) => Png.Save(png, Width, Height, Rgba);

    public SpritePixels Clone() => new(FrameWidth, Height, Frames, (byte[])Rgba.Clone());

    // Volta a um retrato antigo (para desfazer), inclusive com outro número de quadros.
    public void Restore(SpritePixels snapshot)
    {
        FrameWidth = snapshot.FrameWidth;
        Height = snapshot.Height;
        Frames = snapshot.Frames;
        Rgba = (byte[])snapshot.Rgba.Clone();
    }

    private int Index(int frame, int x, int y) => (y * Width + frame * FrameWidth + x) * 4;

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < FrameWidth && y < Height;

    public uint Get(int frame, int x, int y)
    {
        if (!Contains(x, y) || frame < 0 || frame >= Frames)
            return 0;
        var i = Index(frame, x, y);
        return (uint)(Rgba[i] << 24 | Rgba[i + 1] << 16 | Rgba[i + 2] << 8 | Rgba[i + 3]);
    }

    // A cor em RGBA, como 0xRRGGBBAA. Devolve se mudou.
    public bool Set(int frame, int x, int y, uint rgba)
    {
        if (!Contains(x, y) || frame < 0 || frame >= Frames || Get(frame, x, y) == rgba)
            return false;
        var i = Index(frame, x, y);
        Rgba[i] = (byte)(rgba >> 24);
        Rgba[i + 1] = (byte)(rgba >> 16);
        Rgba[i + 2] = (byte)(rgba >> 8);
        Rgba[i + 3] = (byte)rgba;
        return true;
    }

    // Uma linha entre dois pontos, para o lápis não deixar buracos quando o mouse anda rápido.
    public bool Line(int frame, int x0, int y0, int x1, int y1, uint rgba)
    {
        var changed = false;
        int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            changed |= Set(frame, x0, y0, rgba);
            if (x0 == x1 && y0 == y1)
                break;
            var e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
        return changed;
    }

    // O balde: pinta a área da mesma cor ligada pelos lados.
    public bool Fill(int frame, int x, int y, uint rgba)
    {
        if (!Contains(x, y))
            return false;
        var target = Get(frame, x, y);
        if (target == rgba)
            return false;
        var stack = new Stack<(int, int)>();
        stack.Push((x, y));
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (!Contains(cx, cy) || Get(frame, cx, cy) != target)
                continue;
            Set(frame, cx, cy, rgba);
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }
        return true;
    }

    // Monta uma tira nova a partir da lista de quadros (cada um é o índice de um quadro antigo, ou -1 vazio).
    private void Rebuild(IReadOnlyList<int> order, int frameWidth, int height)
    {
        var old = Clone();
        FrameWidth = frameWidth;
        Height = height;
        Frames = Math.Max(1, order.Count);
        Rgba = new byte[Width * Height * 4];
        for (var f = 0; f < order.Count; f++)
        {
            if (order[f] < 0)
                continue;
            var w = Math.Min(frameWidth, old.FrameWidth);
            var h = Math.Min(height, old.Height);
            for (var y = 0; y < h; y++)
                Array.Copy(old.Rgba, old.Index(order[f], 0, y), Rgba, Index(f, 0, y), w * 4);
        }
    }

    private List<int> Order() => Enumerable.Range(0, Frames).ToList();

    // Um quadro novo depois de "after": vazio, ou cópia dele.
    public int AddFrame(int after, bool copy)
    {
        var order = Order();
        var at = Math.Clamp(after + 1, 0, Frames);
        order.Insert(at, copy ? Math.Clamp(after, 0, Frames - 1) : -1);
        Rebuild(order, FrameWidth, Height);
        return at;
    }

    public bool DeleteFrame(int frame)
    {
        if (Frames <= 1 || frame < 0 || frame >= Frames)
            return false;
        var order = Order();
        order.RemoveAt(frame);
        Rebuild(order, FrameWidth, Height);
        return true;
    }

    public int MoveFrame(int frame, int direction)
    {
        var to = frame + direction;
        if (frame < 0 || frame >= Frames || to < 0 || to >= Frames)
            return frame;
        var order = Order();
        (order[frame], order[to]) = (order[to], order[frame]);
        Rebuild(order, FrameWidth, Height);
        return to;
    }

    // Muda o tamanho do quadro, mantendo o canto de cima à esquerda (corta ou acrescenta transparente).
    public void Resize(int frameWidth, int height) => Rebuild(Order(), Math.Max(1, frameWidth), Math.Max(1, height));

    public static uint ParseColor(string text)
    {
        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 6)
            hex += "FF";
        return hex.Length == 8 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : 0x000000FF;
    }

    public static string FormatColor(uint rgba) => (rgba & 0xFF) == 0xFF ? $"#{rgba >> 8:X6}" : $"#{rgba:X8}";
}
