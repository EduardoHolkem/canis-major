using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

public sealed class SpritePixelsTests
{
    private const uint Red = 0xFF0000FF;
    private const uint Blue = 0x0000FFFF;

    [Fact]
    public void PencilLineHasNoGaps()
    {
        var p = new SpritePixels(8, 8, 1);
        Assert.True(p.Line(0, 0, 0, 7, 7, Red));
        for (var i = 0; i < 8; i++)
            Assert.Equal(Red, p.Get(0, i, i));
        Assert.Equal(0u, p.Get(0, 1, 0));
    }

    [Fact]
    public void FillStopsAtOtherColorsAndStaysInFrame()
    {
        var p = new SpritePixels(4, 4, 2);
        p.Line(0, 2, 0, 2, 3, Red); // parede vertical no primeiro quadro
        Assert.True(p.Fill(0, 0, 0, Blue));
        Assert.Equal(Blue, p.Get(0, 1, 3));
        Assert.Equal(0u, p.Get(0, 3, 0)); // do outro lado da parede
        Assert.Equal(0u, p.Get(1, 0, 0)); // o segundo quadro não muda
        Assert.False(p.Fill(0, 0, 0, Blue));
    }

    [Fact]
    public void FramesAddDuplicateMoveAndDelete()
    {
        var p = new SpritePixels(2, 2, 1);
        p.Set(0, 0, 0, Red);
        Assert.Equal(1, p.AddFrame(0, copy: true));
        Assert.Equal(2, p.Frames);
        Assert.Equal(4, p.Width);
        Assert.Equal(Red, p.Get(1, 0, 0));

        Assert.Equal(2, p.AddFrame(1, copy: false));
        p.Set(2, 1, 1, Blue);
        Assert.Equal(1, p.MoveFrame(2, -1));
        Assert.Equal(Blue, p.Get(1, 1, 1));
        Assert.Equal(Red, p.Get(2, 0, 0));

        Assert.True(p.DeleteFrame(1));
        Assert.Equal(2, p.Frames);
        Assert.Equal(Red, p.Get(1, 0, 0));
        Assert.True(p.DeleteFrame(0));
        Assert.False(p.DeleteFrame(0)); // o último quadro fica
    }

    [Fact]
    public void ResizeKeepsTopLeft()
    {
        var p = new SpritePixels(2, 2, 2);
        p.Set(1, 1, 1, Red);
        p.Resize(4, 3);
        Assert.Equal(4, p.FrameWidth);
        Assert.Equal(3, p.Height);
        Assert.Equal(Red, p.Get(1, 1, 1));
        p.Resize(1, 1);
        Assert.Equal(0u, p.Get(1, 0, 0));
    }

    [Fact]
    public void SaveAndLoadRoundTripsFrames()
    {
        var path = Path.Combine(Path.GetTempPath(), "cm-px-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        try
        {
            var p = new SpritePixels(3, 2, 3);
            p.Set(2, 2, 1, Blue);
            p.Save(path);
            var back = SpritePixels.Load(path, 3);
            Assert.Equal(3, back.FrameWidth);
            Assert.Equal(Blue, back.Get(2, 2, 1));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RestoreBringsBackFrameCount()
    {
        var p = new SpritePixels(2, 2, 1);
        var before = p.Clone();
        p.AddFrame(0, copy: false);
        p.Restore(before);
        Assert.Equal(1, p.Frames);
    }

    [Fact]
    public void ColorsParseAndFormat()
    {
        Assert.Equal(0xFF8000FFu, SpritePixels.ParseColor("#FF8000"));
        Assert.Equal(0x11223344u, SpritePixels.ParseColor("11223344"));
        Assert.Equal("#FF8000", SpritePixels.FormatColor(0xFF8000FF));
        Assert.Equal("#11223344", SpritePixels.FormatColor(0x11223344));
    }
}
