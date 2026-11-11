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

    public bool IsBackground => string.Equals(Kind, "background", StringComparison.OrdinalIgnoreCase);
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
        layers.FirstOrDefault(l => !l.IsBackground);

    /// <summary>
    /// Chave de ordem de desenho. Primeiro a profundidade; na mesma profundidade, com YSort, a posição Y.
    /// Sem YSort fica a ordem de criação (o OrderBy do .NET é estável).
    /// </summary>
    public static (int Depth, float Y) SortKey(GameObject o) => (o.Layer, o.YSort ? o.Y + o.SortOffset : float.MinValue);
}
