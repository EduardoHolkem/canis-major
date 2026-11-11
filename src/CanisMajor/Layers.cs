namespace CanisMajor;

/// <summary>
/// Uma camada da sala (rooms/nome.json, em "layers"). Como no GameMaker, a sala é uma pilha de camadas:
/// instâncias e fundos. Profundidade maior fica por cima, igual ao <see cref="GameObject.Layer"/>.
/// </summary>
public sealed class LayerData
{
    public string Name { get; set; } = "Instancias";
    /// <summary>"instances" ou "background".</summary>
    public string Kind { get; set; } = "instances";
    public int Depth { get; set; }
    public bool Visible { get; set; } = true;
    /// <summary>Instâncias desta camada se ordenam pela posição Y (quem está mais embaixo fica na frente).</summary>
    public bool YSort { get; set; }
    /// <summary>Fundo: cor que preenche a sala inteira. Null não pinta.</summary>
    public string? Color { get; set; }
    /// <summary>Fundo: sprite desenhado a partir do canto da sala.</summary>
    public string? Sprite { get; set; }
    public bool TileX { get; set; }
    public bool TileY { get; set; }
    /// <summary>Fundo: quanto a camada acompanha a câmera (0 = presa ao mundo, 1 = presa à tela). Dá o efeito de paralaxe.</summary>
    public float Parallax { get; set; }

    /// <summary>Tiles: o tileset usado (tilesets/nome.json).</summary>
    public string? Tileset { get; set; }
    /// <summary>Tiles: quantas colunas o mapa tem. As linhas saem do tamanho da lista.</summary>
    public int Columns { get; set; }
    /// <summary>Tiles: o número de cada tile, linha por linha, da esquerda para a direita. -1 é vazio.</summary>
    public List<int> Tiles { get; set; } = [];

    public bool IsBackground => string.Equals(Kind, "background", StringComparison.OrdinalIgnoreCase);
    public bool IsTiles => string.Equals(Kind, "tiles", StringComparison.OrdinalIgnoreCase);
    /// <summary>Camada que guarda instâncias (nem fundo nem tiles).</summary>
    public bool IsInstances => !IsBackground && !IsTiles;

    public int Rows => Columns <= 0 ? 0 : (Tiles.Count + Columns - 1) / Columns;

    /// <summary>O tile na coluna e linha, ou -1 fora do mapa.</summary>
    public int TileAt(int col, int row) =>
        col < 0 || row < 0 || col >= Columns || row * Columns + col >= Tiles.Count ? -1 : Tiles[row * Columns + col];

    /// <summary>Põe um tile, aumentando o mapa se precisar. -1 apaga.</summary>
    public void SetTile(int col, int row, int id)
    {
        if (col < 0 || row < 0 || Columns <= 0 || col >= Columns) return;
        var index = row * Columns + col;
        while (Tiles.Count <= index) Tiles.Add(-1);
        Tiles[index] = id;
    }
}

/// <summary>
/// Um tileset (tilesets/nome.json): uma imagem de sprites/ cortada numa grade. O tile número 0 é o
/// do canto de cima à esquerda, e a conta segue pela linha.
/// </summary>
public sealed class TilesetData
{
    /// <summary>O nome do sprite com a imagem (sprites/nome.png).</summary>
    public string Sprite { get; set; } = "";
    public int TileWidth { get; set; } = 16;
    public int TileHeight { get; set; } = 16;
    /// <summary>Os tiles que bloqueiam o movimento.</summary>
    public List<int> Solid { get; set; } = [];

    /// <summary>Colunas da imagem, para achar onde cada tile está.</summary>
    public int ColumnsIn(int imageWidth) => Math.Max(1, imageWidth / Math.Max(1, TileWidth));
}

/// <summary>Regras das camadas, usadas igual pelo jogo e pelo editor.</summary>
public static class RoomLayers
{
    public const string DefaultName = "Instancias";

    /// <summary>As camadas da sala, com uma de instâncias quando ela não define nenhuma.</summary>
    public static List<LayerData> Resolve(RoomData room) =>
        room.Layers.Count > 0 ? room.Layers : [new LayerData { Name = DefaultName }];

    /// <summary>A camada onde uma instância sem "layer" entra: a primeira de instâncias.</summary>
    public static LayerData? DefaultInstanceLayer(IEnumerable<LayerData> layers) =>
        layers.FirstOrDefault(l => l.IsInstances);

    /// <summary>
    /// Chave de ordem de desenho. Primeiro a profundidade; na mesma profundidade, com YSort, a posição Y.
    /// Sem YSort fica a ordem de criação (o OrderBy do .NET é estável).
    /// </summary>
    public static (int Depth, float Y) SortKey(GameObject o) => (o.Layer, o.YSort ? o.Y + o.SortOffset : float.MinValue);
}
