using System.Buffers.Binary;
using System.IO.Compression;

namespace CanisMajor;

/// <summary>Gravador de PNG RGBA sem dependências, usado pelos modelos de projeto e pelo editor.</summary>
public static class Png
{
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in type) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)data.Length);
        s.Write(buf);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buf, Crc(t, data));
        s.Write(buf);
    }

    public static void Save(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("tamanho dos pixels não bate com largura × altura × 4");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;  // bits por canal
        header[9] = 6;  // RGBA
        Chunk(file, "IHDR", header);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0); // sem filtro
                z.Write(rgba.Slice(y * width * 4, width * 4));
            }
        }
        Chunk(file, "IDAT", raw.ToArray());
        Chunk(file, "IEND", []);
    }

    /// <summary>
    /// Lê um PNG e devolve os pixels em RGBA, 8 bits por canal. Aceita cinza, RGB, paleta (com
    /// transparência), cinza com alfa e RGBA, de 1 a 16 bits. PNG entrelaçado não é aceito.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba) Load(string path) => Decode(File.ReadAllBytes(path));

    public static (int Width, int Height, byte[] Rgba) Decode(byte[] data)
    {
        if (data.Length < 8 || data[0] != 0x89 || data[1] != 0x50 || data[2] != 0x4E || data[3] != 0x47)
            throw new InvalidDataException("O arquivo não é um PNG.");
        int width = 0, height = 0, depth = 0, colorType = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        var pos = 8;
        while (pos + 8 <= data.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = data.AsSpan(pos + 8, Math.Min(length, data.Length - pos - 8));
            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                    depth = body[8];
                    colorType = body[9];
                    interlace = body[12];
                    break;
                case "PLTE": palette = body.ToArray(); break;
                case "tRNS": trns = body.ToArray(); break;
                case "IDAT": idat.Write(body); break;
            }
            if (type == "IEND") break;
            pos += 12 + length;
        }
        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG sem cabeçalho.");
        if (interlace != 0) throw new NotSupportedException("PNG entrelaçado não é aceito. Salve sem entrelaçamento.");

        var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException($"Tipo de cor {colorType} do PNG não é aceito.") };
        var bitsPerPixel = channels * depth;
        var stride = (width * bitsPerPixel + 7) / 8;
        var bpp = Math.Max(1, bitsPerPixel / 8);

        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[(stride + 1) * height];
        var read = 0;
        while (read < raw.Length)
        {
            var n = z.Read(raw, read, raw.Length - read);
            if (n == 0) break;
            read += n;
        }

        // Desfaz os filtros de cada linha.
        var lines = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = raw.AsSpan(y * (stride + 1) + 1, stride);
            var cur = lines.AsSpan(y * stride, stride);
            var prev = y > 0 ? lines.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= bpp ? cur[i - bpp] : 0;
                int b = y > 0 ? prev[i] : 0;
                int c = i >= bpp && y > 0 ? prev[i - bpp] : 0;
                cur[i] = filter switch
                {
                    1 => (byte)(src[i] + a),
                    2 => (byte)(src[i] + b),
                    3 => (byte)(src[i] + (a + b) / 2),
                    4 => (byte)(src[i] + Paeth(a, b, c)),
                    _ => src[i],
                };
            }
        }

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int Sample(int index)
                {
                    var line = y * stride;
                    if (depth == 8) return lines[line + index];
                    if (depth == 16) return lines[line + index * 2];
                    var bitPos = index * depth;
                    var value = (lines[line + bitPos / 8] >> (8 - depth - bitPos % 8)) & ((1 << depth) - 1);
                    return colorType == 3 ? value : value * 255 / ((1 << depth) - 1);
                }
                var o = (y * width + x) * 4;
                switch (colorType)
                {
                    case 0:
                    {
                        var g = Sample(x);
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = (byte)g;
                        rgba[o + 3] = 255;
                        break;
                    }
                    case 2:
                        rgba[o] = (byte)Sample(x * 3);
                        rgba[o + 1] = (byte)Sample(x * 3 + 1);
                        rgba[o + 2] = (byte)Sample(x * 3 + 2);
                        rgba[o + 3] = 255;
                        break;
                    case 3:
                    {
                        var i = Sample(x);
                        if (palette is not null && i * 3 + 2 < palette.Length)
                        {
                            rgba[o] = palette[i * 3];
                            rgba[o + 1] = palette[i * 3 + 1];
                            rgba[o + 2] = palette[i * 3 + 2];
                        }
                        rgba[o + 3] = trns is not null && i < trns.Length ? trns[i] : (byte)255;
                        break;
                    }
                    case 4:
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = (byte)Sample(x * 2);
                        rgba[o + 3] = (byte)Sample(x * 2 + 1);
                        break;
                    case 6:
                        rgba[o] = (byte)Sample(x * 4);
                        rgba[o + 1] = (byte)Sample(x * 4 + 1);
                        rgba[o + 2] = (byte)Sample(x * 4 + 2);
                        rgba[o + 3] = (byte)Sample(x * 4 + 3);
                        break;
                }
            }
        }
        return (width, height, rgba);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>
    /// O retângulo dos pixels visíveis (alfa acima de zero) somando todos os quadros, relativo ao
    /// quadro. Null quando a imagem é toda transparente.
    /// </summary>
    public static (int Left, int Top, int Right, int Bottom)? OpaqueBox(int width, int height, byte[] rgba, int frames)
    {
        var fw = Math.Max(1, width / Math.Max(1, frames));
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                if (rgba[(y * width + x) * 4 + 3] == 0) continue;
                var fx = x % fw;
                left = Math.Min(left, fx);
                right = Math.Max(right, fx + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
        return right < 0 ? null : (left, top, right, bottom);
    }
}
