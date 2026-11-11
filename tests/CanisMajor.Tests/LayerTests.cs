using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

public sealed class LayerTests
{
    private sealed class Coisa : GameObject;

    [Fact]
    public void YSortPutsLowerObjectInFront()
    {
        // Mesma profundidade, com YSort: quem está mais embaixo (Y maior) é desenhado depois, na frente.
        var poste = new Coisa { Kind = "Poste", Layer = 0, Y = 100, YSort = true };
        var jogador = new Coisa { Kind = "Jogador", Layer = 0, Y = 80, YSort = true };
        var order = new[] { poste, jogador }.OrderBy(RoomLayers.SortKey).ToList();
        Assert.Same(jogador, order[0]);

        jogador.Y = 120;
        order = new[] { poste, jogador }.OrderBy(RoomLayers.SortKey).ToList();
        Assert.Same(poste, order[0]);
    }

    [Fact]
    public void DepthWinsOverY()
    {
        var chao = new Coisa { Layer = -10, Y = 500, YSort = true };
        var nuvem = new Coisa { Layer = 10, Y = 0, YSort = true };
        Assert.Same(chao, new[] { nuvem, chao }.OrderBy(RoomLayers.SortKey).First());
    }

    [Fact]
    public void SortOffsetMovesTheSortPoint()
    {
        // O pé de um poste alto fica abaixo da origem.
        var poste = new Coisa { Y = 50, SortOffset = 40, YSort = true };
        var jogador = new Coisa { Y = 80, YSort = true };
        Assert.Same(jogador, new[] { poste, jogador }.OrderBy(RoomLayers.SortKey).First());
    }

    [Fact]
    public void WithoutYSortKeepsCreationOrder()
    {
        var a = new Coisa { Y = 90 };
        var b = new Coisa { Y = 10 };
        Assert.Equal([a, b], new[] { a, b }.OrderBy(RoomLayers.SortKey).ToList());
    }

    [Fact]
    public void RoomWithoutLayersGetsDefaultInstanceLayer()
    {
        var layers = RoomLayers.Resolve(new RoomData());
        Assert.Single(layers);
        Assert.Equal(RoomLayers.DefaultName, RoomLayers.DefaultInstanceLayer(layers)!.Name);
    }

    [Fact]
    public void BackgroundIsNeverTheDefaultInstanceLayer()
    {
        var layers = new List<LayerData>
        {
            new() { Name = "Ceu", Kind = "background", Depth = -100 },
            new() { Name = "Atores", Depth = 0 },
        };
        Assert.Equal("Atores", RoomLayers.DefaultInstanceLayer(layers)!.Name);
    }
}
