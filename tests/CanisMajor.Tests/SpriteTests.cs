using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class SpriteTests
{
    private sealed class Boneco : GameObject
    {
        public Boneco() { Sprite = "boneco"; }
    }

    [Fact]
    public void PngSavesAndLoadsTheSamePixels()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "a.png");
        var rgba = new byte[3 * 2 * 4];
        for (var i = 0; i < rgba.Length; i++) rgba[i] = (byte)(i * 7);
        Png.Save(path, 3, 2, rgba);
        var (w, h, back) = Png.Load(path);
        Assert.Equal(3, w);
        Assert.Equal(2, h);
        Assert.Equal(rgba, back);
    }

    [Fact]
    public void PngWithFiltersDecodes()
    {
        // Um PNG RGB 2x2 com filtro Sub na primeira linha e Up na segunda, montado à mão.
        var raw = new byte[] { 1, 10, 20, 30, 5, 5, 5, 2, 1, 1, 1, 2, 2, 2 };
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(raw);
        var png = BuildPng(2, 2, 8, 2, ms.ToArray());
        var (_, _, rgba) = Png.Decode(png);
        Assert.Equal(new byte[] { 10, 20, 30, 255, 15, 25, 35, 255, 11, 21, 31, 255, 17, 27, 37, 255 }, rgba);
    }

    private static byte[] BuildPng(int w, int h, byte depth, byte colorType, byte[] idat)
    {
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        void Chunk(string type, byte[] data)
        {
            var len = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
            file.Write(len);
            file.Write(System.Text.Encoding.ASCII.GetBytes(type));
            file.Write(data);
            file.Write(new byte[4]); // o leitor não confere o CRC
        }
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, (uint)w);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)h);
        header[8] = depth;
        header[9] = colorType;
        Chunk("IHDR", header);
        Chunk("IDAT", idat);
        Chunk("IEND", []);
        return file.ToArray();
    }

    [Fact]
    public void OpaqueBoxCoversAllFrames()
    {
        // Dois quadros de 4x4: um pixel em (1,1) no primeiro e em (2,3) no segundo.
        var rgba = new byte[8 * 4 * 4];
        rgba[(1 * 8 + 1) * 4 + 3] = 255;
        rgba[(3 * 8 + 4 + 2) * 4 + 3] = 255;
        Assert.Equal((1, 1, 3, 4), Png.OpaqueBox(8, 4, rgba, 2));
        Assert.Null(Png.OpaqueBox(8, 4, new byte[8 * 4 * 4], 2));
    }

    [Fact]
    public void SpriteMaskShrinksTheCollisionBox()
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 32, 1, 8, 32, 8) { Mask = (4, 24, 12, 32) };
        try
        {
            var boneco = new Boneco { Kind = "Boneco", X = 100, Y = 100 };
            // Só os pés colidem: 8 de largura, 8 de altura, encostados na origem (embaixo, no meio).
            Assert.Equal((96f, 92f, 104f, 100f), boneco.BBox());
        }
        finally
        {
            Runtime.SpriteOverride = null;
        }
    }

    [Fact]
    public void AutoMaskComesFromVisiblePixels()
    {
        using var temp = new TempDir();
        var png = Path.Combine(temp.Path, "s.png");
        var rgba = new byte[4 * 4 * 4];
        rgba[(2 * 4 + 1) * 4 + 3] = 255;
        Png.Save(png, 4, 4, rgba);
        var auto = Runtime.MaskOf(png, new SpriteMeta { Mask = new SpriteMask { Mode = "auto" } });
        Assert.Equal((1f, 2f, 2f, 3f), auto);
        var rect = Runtime.MaskOf(png, new SpriteMeta { Mask = new SpriteMask { Left = 1, Top = 1, Right = 3, Bottom = 4 } });
        Assert.Equal((1f, 1f, 3f, 4f), rect);
        Assert.Null(Runtime.MaskOf(png, new SpriteMeta()));
    }
}
