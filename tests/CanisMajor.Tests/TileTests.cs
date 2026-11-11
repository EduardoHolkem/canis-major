using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

[Collection("Runtime")]
public sealed class TileTests
{
    private sealed class Caixa : GameObject
    {
        public Caixa() { Sprite = "caixa"; }
    }

    // Um chão de tiles sólidos na linha 3 (y de 48 a 64), com tiles de 16.
    private static LayerData Chao()
    {
        var layer = new LayerData { Name = "Chao", Kind = "tiles", Tileset = "terra", Columns = 10 };
        for (var c = 0; c < 10; c++) layer.SetTile(c, 3, 1);
        layer.SetTile(4, 2, 0); // um tile decorativo, não sólido
        return layer;
    }

    private static void WithTiles(LayerData layer, Action test)
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        Runtime.TilesetOverride = _ => new TilesetData { Sprite = "terra", TileWidth = 16, TileHeight = 16, Solid = [1] };
        Runtime.Layers = [layer];
        try { test(); }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.TilesetOverride = null;
            Runtime.Layers = [];
            Runtime.Instances = [];
        }
    }

    [Fact]
    public void LayerGrowsWhenPainting()
    {
        var layer = new LayerData { Kind = "tiles", Columns = 4 };
        layer.SetTile(2, 1, 7);
        Assert.Equal(7, layer.TileAt(2, 1));
        Assert.Equal(-1, layer.TileAt(0, 0));
        Assert.Equal(2, layer.Rows);
        Assert.Equal(-1, layer.TileAt(9, 9));
    }

    [Fact]
    public void SolidTilesBlockAndDecorativeDoNot()
    {
        WithTiles(Chao(), () =>
        {
            Assert.True(Runtime.TilesBlock("Chao", (0, 50, 10, 60)));
            Assert.False(Runtime.TilesBlock("Chao", (64, 32, 80, 48))); // tile 0 não é sólido
            Assert.False(Runtime.TilesBlock("Chao", (0, 32, 16, 48))); // só encosta por cima
            Assert.False(Runtime.TilesBlock("Outra", (0, 50, 10, 60)));
        });
    }

    [Fact]
    public void MoveStopsOnTiles()
    {
        WithTiles(Chao(), () =>
        {
            var caixa = new Caixa { Kind = "Caixa", X = 20, Y = 0 };
            Runtime.Instances = [caixa];
            var (_, hitY) = caixa.Move(0, 100, solid: "Chao");
            Assert.True(hitY);
            Assert.Equal(32, caixa.Y); // 32 + 16 = 48, o topo do chão
            Assert.True(caixa.TouchingTiles("Chao", 0, 1));
        });
    }

    [Fact]
    public void GameReadsAndChangesTilesInPixels()
    {
        WithTiles(Chao(), () =>
        {
            Assert.Equal(1, Game.TileAt("Chao", 70, 50));
            Game.SetTile("Chao", 70, 50, -1);
            Assert.Equal(-1, Game.TileAt("Chao", 70, 50));
            Assert.False(Runtime.TilesBlock("Chao", (66, 50, 78, 60)));
        });
    }

    [Fact]
    public void TilesLayerIsNotAnInstanceLayer()
    {
        var layers = new List<LayerData> { Chao(), new() { Name = "Atores" } };
        Assert.Equal("Atores", RoomLayers.DefaultInstanceLayer(layers)!.Name);
    }
}
