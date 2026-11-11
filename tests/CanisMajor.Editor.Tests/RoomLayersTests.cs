using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

public sealed class RoomLayersTests
{
    private const string Sala = """
        {
          "width": 320,
          "height": 180,
          "layers": [
            { "name": "Ceu", "kind": "background", "depth": -100, "color": "#203040", "parallax": 0.5 },
            { "name": "Atores", "kind": "instances", "depth": 0, "y_sort": true },
            { "name": "Frente", "kind": "instances", "depth": 50, "visible": false }
          ],
          "instances": [
            { "object": "Jogador", "x": 10, "y": 20 },
            { "object": "Poste", "x": 30, "y": 40, "layer": "Frente" }
          ]
        }
        """;

    [Fact]
    public void LayersRoundTrip()
    {
        var room = RoomModel.Parse(Sala);
        Assert.Equal(3, room.Layers.Count);
        Assert.True(room.Layers[0].IsBackground);
        Assert.Equal(0.5f, room.Layers[0].Parallax);
        Assert.True(room.Layers[1].YSort);
        Assert.False(room.Layers[2].Visible);

        var again = RoomModel.Parse(room.Serialize());
        Assert.Equal(3, again.Layers.Count);
        Assert.Equal("#203040", again.Layers[0].Color);
        Assert.Equal("Frente", again.Instances[1].Layer);
        // A camada não aparece como propriedade a mais e não some ao salvar.
        Assert.Contains("\"layer\": \"Frente\"", room.Serialize());
    }

    [Fact]
    public void InstanceWithoutLayerGoesToFirstInstanceLayer()
    {
        var room = RoomModel.Parse(Sala);
        Assert.Equal("Atores", room.LayerOf(room.Instances[0])!.Name);
        Assert.Equal("Frente", room.LayerOf(room.Instances[1])!.Name);
    }

    [Fact]
    public void OldRoomsStayWithoutLayers()
    {
        var room = RoomModel.Parse("""{ "width": 100, "height": 100, "instances": [ { "object": "A", "x": 1, "y": 2 } ] }""");
        Assert.Empty(room.Layers);
        Assert.DoesNotContain("layers", room.Serialize());
        Assert.Single(room.EffectiveLayers);
        room.EnsureLayers();
        Assert.Contains("\"layers\"", room.Serialize());
    }

    [Fact]
    public void LayersAreWrittenOnePerLine()
    {
        var text = RoomModel.Parse(Sala).Serialize();
        Assert.Contains("\n    { \"name\": \"Ceu\"", text);
        Assert.Contains("\n    { \"name\": \"Atores\"", text);
    }
}

public sealed class TileLayerTests
{
    [Fact]
    public void TileLayerRoundTripsAndTrimsEmptyEnd()
    {
        var room = RoomModel.Parse("""{ "width": 64, "height": 64, "layers": [ { "name": "Atores", "kind": "instances", "depth": 0 }, { "name": "Chao", "kind": "tiles", "depth": -10, "tileset": "terra", "columns": 4, "tiles": [0, 1, -1, 2] } ], "instances": [] }""");
        var layer = room.FindLayer("Chao")!;
        Assert.True(layer.IsTiles);
        Assert.Equal(2, layer.TileAt(3, 0));
        layer.SetTile(1, 5, -1); // apagar fora do pintado não suja o arquivo
        var text = room.Serialize();
        Assert.Contains("\"tileset\": \"terra\"", text);
        Assert.Contains("\"tiles\": [0, 1, -1, 2]", text);
        Assert.Equal(2, RoomModel.Parse(text).FindLayer("Chao")!.TileAt(3, 0));
    }

    [Fact]
    public void CanvasPaintsAndErasesOnActiveTileLayer()
    {
        var room = RoomModel.Parse("""{ "width": 64, "height": 64, "layers": [ { "name": "Atores", "kind": "instances", "depth": 0 }, { "name": "Chao", "kind": "tiles", "depth": -10, "tileset": "terra", "columns": 4, "tiles": [] } ], "instances": [] }""");
        var canvas = new RoomCanvas
        {
            Model = room,
            TilesetFor = _ => new CanisMajor.TilesetData { Sprite = "terra", TileWidth = 16, TileHeight = 16 },
            ActiveLayer = "Chao",
            TileBrush = 3,
        };
        Assert.True(canvas.PaintTile(new Avalonia.Point(40, 20), 3));
        Assert.Equal(3, room.FindLayer("Chao")!.TileAt(2, 1));
        Assert.False(canvas.PaintTile(new Avalonia.Point(40, 20), 3)); // igual, não muda
        Assert.True(canvas.PaintTile(new Avalonia.Point(40, 20), -1));
        Assert.Equal(-1, room.FindLayer("Chao")!.TileAt(2, 1));
        Assert.Empty(room.Instances);
    }
}
