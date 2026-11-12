using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CanisMajor;

/// <summary>projeto.json: as configurações do jogo.</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "Meu jogo";
    public string Version { get; set; } = "0.1.0";
    public string StartRoom { get; set; } = "inicio";
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 270;
    public int Scale { get; set; } = 2;
    public int Fps { get; set; } = 60;
    public bool PixelArt { get; set; } = true;
    public string Engine { get; set; } = "0.3.0";
}

/// <summary>Uma sala (rooms/nome.json).</summary>
public sealed class RoomData
{
    public int Width { get; set; } = 480;
    public int Height { get; set; } = 270;
    public string Background { get; set; } = "#16181D";
    public int Grid { get; set; } = 16;
    public List<string> PostShaders { get; set; } = [];
    /// <summary>Camadas da sala, como no GameMaker. Vazia, a sala tem uma camada de instâncias só.</summary>
    public List<LayerData> Layers { get; set; } = [];
    /// <summary>Iluminação 2D da sala. Null deixa a sala sem luz (tudo com a cor normal).</summary>
    public LightingData? Lighting { get; set; }
    public List<JsonObject> Instances { get; set; } = [];
}

/// <summary>Dados de um sprite (sprites/nome.json, ao lado do PNG).</summary>
public sealed class SpriteMeta
{
    public int Frames { get; set; } = 1;
    public float[] Origin { get; set; } = [0, 0];
    public float Fps { get; set; } = 8;
    /// <summary>Máscara de colisão. Null usa o quadro inteiro.</summary>
    public SpriteMask? Mask { get; set; }
}

/// <summary>
/// Máscara de colisão do sprite, relativa ao canto do quadro. "rect" usa Left, Top, Right e Bottom;
/// "auto" calcula o retângulo dos pixels visíveis de todos os quadros.
/// </summary>
public sealed class SpriteMask
{
    public string Mode { get; set; } = "rect";
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }
}

/// <summary>Uma pasta de projeto: projeto.json mais sprites/, objects/, rooms/, shaders/ e sounds/.</summary>
public sealed class GameProject
{
    public const string FileName = "projeto.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Root { get; }
    public ProjectInfo Info { get; }

    private GameProject(string root, ProjectInfo info)
    {
        Root = root;
        Info = info;
    }

    public static bool IsProject(string folder) => File.Exists(Path.Combine(folder, FileName));

    public static GameProject Load(string folder)
    {
        var root = Path.GetFullPath(folder);
        var file = Path.Combine(root, FileName);
        if (!File.Exists(file)) throw new GameException($"Esta pasta não tem um {FileName}: {root}");
        try
        {
            var info = JsonSerializer.Deserialize<ProjectInfo>(File.ReadAllText(file), Json) ?? new ProjectInfo();
            return new GameProject(root, info);
        }
        catch (JsonException e)
        {
            throw new GameException($"{file}: {e.Message}");
        }
    }

    public string Folder(string kind) => Path.Combine(Root, kind switch
    {
        "sprite" => "sprites",
        "object" => "objects",
        "room" => "rooms",
        "shader" => "shaders",
        "sound" => "sounds",
        "tileset" => "tilesets",
        _ => throw new ArgumentException($"tipo de recurso desconhecido: {kind}"),
    });

    public RoomData LoadRoom(string name)
    {
        var path = Path.Combine(Folder("room"), name + ".json");
        if (!File.Exists(path)) throw new GameException($"A sala '{name}' não existe em rooms/.");
        try
        {
            var room = JsonSerializer.Deserialize<RoomData>(File.ReadAllText(path), Json) ?? new RoomData();
            return room;
        }
        catch (JsonException e)
        {
            throw new GameException($"{path}: {e.Message}");
        }
    }

    /// <summary>A ficha do objeto (objects/Nome.json), ou null se ele não tiver uma.</summary>
    public TilesetData LoadTileset(string name)
    {
        var path = Path.Combine(Folder("tileset"), name + ".json");
        if (!File.Exists(path)) throw new GameException($"O tileset '{name}' não existe em tilesets/.");
        try
        {
            return JsonSerializer.Deserialize<TilesetData>(File.ReadAllText(path), Json) ?? new TilesetData();
        }
        catch (JsonException e)
        {
            throw new GameException($"{path}: {e.Message}");
        }
    }

    public ObjectDef? LoadObjectDef(string name)
    {
        var path = Path.Combine(Folder("object"), name + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ObjectDef>(File.ReadAllText(path), Json) ?? new ObjectDef();
        }
        catch (JsonException e)
        {
            throw new GameException($"{path}: {e.Message}");
        }
    }

    public static SpriteMeta LoadSpriteMeta(string pngPath)
    {
        var meta = Path.ChangeExtension(pngPath, ".json");
        if (!File.Exists(meta)) return new SpriteMeta();
        try
        {
            var m = JsonSerializer.Deserialize<SpriteMeta>(File.ReadAllText(meta), Json) ?? new SpriteMeta();
            m.Frames = Math.Max(1, m.Frames);
            if (m.Origin.Length < 2) m.Origin = [0, 0];
            return m;
        }
        catch (JsonException)
        {
            return new SpriteMeta();
        }
    }

    /// <summary>Acha a pasta do projeto: o argumento, a pasta atual (ou acima dela) ou a pasta do executável.</summary>
    public static string? Find(string? hint)
    {
        if (!string.IsNullOrEmpty(hint)) return IsProject(hint) ? Path.GetFullPath(hint) : null;
        for (var dir = new DirectoryInfo(Environment.CurrentDirectory); dir != null; dir = dir.Parent)
            if (IsProject(dir.FullName)) return dir.FullName;
        return IsProject(AppContext.BaseDirectory) ? AppContext.BaseDirectory : null;
    }
}

/// <summary>Erro do jogo com mensagem pronta para mostrar, sem a pilha de chamadas da engine.</summary>
public sealed class GameException(string message) : Exception(message);
