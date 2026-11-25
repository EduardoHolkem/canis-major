using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CanisMajor;

namespace CanisEditor.Game;

// Um tipo de recurso do jogo e a pasta onde ele mora.
public sealed record GameSection(string Kind, string Folder, string Title, string Singular, string[] Extensions);

public sealed record GameResource(string Kind, string Name, string Path);

// O que o editor descobre de um objeto lendo o .cs, sem compilar: a classe, o sprite, a camada e o
// pai (o "parent" da ficha ou a classe base). Script diz se há uma classe C# para este objeto.
public sealed record GameObjectInfo(string Class, string? Sprite, int Layer, string Path, string? Parent = null, bool Script = false);

// Os arquivos de um projeto de jogo: listar, reconhecer e criar recursos.
public static partial class GameFiles
{
    public static readonly GameSection[] Sections =
    [
        new("room", "rooms", "Salas", "sala", [".json"]),
        new("object", "objects", "Objetos", "objeto", [".cs", ".json"]),
        new("script", "scripts", "Scripts", "script", [".cs"]),
        new("sprite", "sprites", "Sprites", "sprite", [".png"]),
        new("shader", "shaders", "Shaders", "shader", [".wgsl"]),
        new("tileset", "tilesets", "Tilesets", "tileset", [".json"]),
        new("sound", "sounds", "Sons", "som", [".wav", ".ogg"]),
    ];

    // Toda classe com uma base. Depois ficam só as que chegam em GameObject, direto ou por um pai.
    [GeneratedRegex(@"(?<abstract>\babstract\s+(?:\w+\s+)*)?\bclass\s+(?<name>[A-Za-z_]\w*)\s*(?:\([^)]*\))?\s*:\s*(?:CanisMajor\.)?(?<base>[A-Za-z_]\w*)\b")]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"\bSprite\s*=\s*""(?<sprite>[^""]+)""")]
    private static partial Regex SpritePattern();

    [GeneratedRegex(@"\bLayer\s*=\s*(?<layer>-?\d+)")]
    private static partial Regex LayerPattern();

    [GeneratedRegex(@"^\s*namespace\s+(?<ns>[\w.]+)", RegexOptions.Multiline)]
    private static partial Regex NamespacePattern();

    public static bool IsGame(string? root) => root is not null && GameProject.IsProject(root);

    public static GameSection Section(string kind) => Sections.First(s => s.Kind == kind);

    public static string FolderOf(string root, string kind) => Path.Combine(root, Section(kind).Folder);

    public static List<GameResource> List(string root, string kind)
    {
        var section = Section(kind);
        var folder = Path.Combine(root, section.Folder);
        if (!Directory.Exists(folder))
            return [];
        try
        {
            return Directory.EnumerateFiles(folder)
                .Where(path => section.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                // Um objeto com script e ficha aparece uma vez só, pelo script.
                .Where(path => kind != "object" || !IsObjectDefFile(path) || !File.Exists(Path.ChangeExtension(path, ".cs")))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Select(path => new GameResource(kind, Path.GetFileNameWithoutExtension(path), path))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // A que tipo de recurso o arquivo pertence, se estiver direto na pasta dele.
    public static string? KindOf(string? root, string path)
    {
        if (!IsGame(root))
            return null;
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        foreach (var section in Sections)
        {
            if (string.Equals(folder, Path.Combine(root!, section.Folder), StringComparison.OrdinalIgnoreCase)
                && section.Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                return section.Kind;
        }
        return null;
    }

    public static bool IsRoom(string? root, string path) => KindOf(root, path) == "room";

    public static bool IsSprite(string? root, string path) => KindOf(root, path) == "sprite";

    public static bool IsObjectDefFile(string path) => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);

    public static ProjectInfo Info(string root)
    {
        try
        {
            return GameProject.Load(root).Info;
        }
        catch (GameException)
        {
            return new ProjectInfo();
        }
    }

    // Lê os objetos do jogo. Uma classe sem Sprite = "..." usa o sprite com o mesmo nome, se houver.
    public static Dictionary<string, GameObjectInfo> Objects(string root)
    {
        var sprites = List(root, "sprite").Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
        var bases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var abstracts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in List(root, "object"))
        {
            string text;
            try
            {
                text = File.ReadAllText(file.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (Match match in ClassPattern().Matches(text))
            {
                var name = match.Groups["name"].Value;
                // O que vem depois desta classe, até a próxima, é dela.
                var next = ClassPattern().Match(text, match.Index + match.Length);
                var body = text[match.Index..(next.Success ? next.Index : text.Length)];
                var sprite = SpritePattern().Match(body) is { Success: true } s ? s.Groups["sprite"].Value : null;
                int? layer = LayerPattern().Match(body) is { Success: true } l && int.TryParse(l.Groups["layer"].Value, out var n) ? n : null;
                bases[name] = match.Groups["base"].Value;
                if (match.Groups["abstract"].Success)
                    abstracts.Add(name);
                found[name] = new Found(sprite, layer, null, file.Path, true);
            }
        }
        // Só as classes que chegam em GameObject, direto ou por um pai que chega.
        var objectClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (name, baseName) in bases)
                if (!objectClasses.Contains(name) && (baseName == "GameObject" || objectClasses.Contains(baseName)))
                    grew = objectClasses.Add(name) || grew;
        }
        foreach (var name in found.Keys.ToList())
        {
            if (!objectClasses.Contains(name))
                found.Remove(name);
            else if (bases[name] != "GameObject")
                found[name] = found[name] with { Parent = bases[name] };
        }

        // A ficha (Nome.json) vale mais que o construtor, e um objeto só com ficha também existe.
        var folder = FolderOf(root, "object");
        if (Directory.Exists(folder))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(path);
                ObjectDef? def;
                try
                {
                    def = System.Text.Json.JsonSerializer.Deserialize<ObjectDef>(File.ReadAllText(path), GameProject.Json);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                {
                    continue;
                }
                if (def is null)
                    continue;
                found.TryGetValue(name, out var known);
                found[name] = new Found(def.Sprite ?? known?.Sprite, def.Layer ?? known?.Layer,
                    string.IsNullOrWhiteSpace(def.Parent) ? known?.Parent : def.Parent.Trim(), known?.Path ?? path, known?.Script ?? false);
            }
        }

        // O sprite e a camada que faltam vêm do pai (do mais perto ao mais longe).
        var objects = new Dictionary<string, GameObjectInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, item) in found)
        {
            if (abstracts.Contains(name))
                continue; // classe abstrata só serve de pai: não dá para pôr na sala
            string? sprite = item.Sprite ?? (sprites.Contains(name) ? name : null);
            var layer = item.Layer;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };
            for (var parent = item.Parent; parent is not null && seen.Add(parent) && found.TryGetValue(parent, out var p); parent = p.Parent)
            {
                sprite ??= p.Sprite ?? (sprites.Contains(parent) ? parent : null);
                layer ??= p.Layer;
            }
            objects[name] = new GameObjectInfo(name, sprite, layer ?? 0, item.Path, item.Parent, item.Script);
        }
        return objects;
    }

    private sealed record Found(string? Sprite, int? Layer, string? Parent, string Path, bool Script);

    // O namespace dos objetos que já existem, ou um feito do nome do jogo.
    public static string Namespace(string root)
    {
        foreach (var file in List(root, "object"))
        {
            try
            {
                if (NamespacePattern().Match(File.ReadAllText(file.Path)) is { Success: true } match)
                    return match.Groups["ns"].Value;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return Templates.Namespace(Info(root).Name);
    }

    // Devolve null se o nome serve, ou o motivo de não servir.
    public static string? CheckName(string root, string kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Escreva um nome.";
        if (kind is "object" or "script")
        {
            if (!IsIdentifier(name))
                return $"O nome de um {Section(kind).Singular} vira o nome da classe: comece com letra e use só letras, números e _.";
            // Objetos e scripts são classes do mesmo jogo: dois com o mesmo nome não compilam.
            if (Objects(root).ContainsKey(name) || File.Exists(Path.Combine(FolderOf(root, "script"), name + ".cs")))
                return "Já existe um objeto ou script com esse nome.";
        }
        else if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            return "Use só letras sem acento, números, _ e -.";
        }
        var section = Section(kind);
        if (section.Extensions.Any(ext => File.Exists(Path.Combine(root, section.Folder, name + ext))))
            return $"Já existe um {section.Singular} com esse nome.";
        return null;
    }

    // Um nome de classe C# feito do nome da sala: "fase-1" vira SalaFase1. O "Sala" na frente evita
    // trombar com um objeto de mesmo nome; quem liga a classe à sala é o atributo [Room].
    public static string RoomClassName(string room) =>
        "Sala" + string.Concat(Regex.Split(room, @"[^\p{L}\p{Nd}]+").Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static bool IsIdentifier(string name) =>
        name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    // Cria o recurso e devolve o arquivo para abrir.
    public static string Create(string root, string kind, string name)
    {
        var folder = FolderOf(root, kind);
        Directory.CreateDirectory(folder);
        switch (kind)
        {
            case "room":
            {
                var info = Info(root);
                var path = Path.Combine(folder, name + ".json");
                Write(path, RoomJson.Format(new RoomData { Width = info.Width, Height = info.Height }));
                return path;
            }
            case "object":
            {
                var sprite = List(root, "sprite").FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;
                var path = Path.Combine(folder, name + ".cs");
                Write(path, Templates.NewObject(Namespace(root), name, sprite));
                return path;
            }
            case "sprite":
            {
                const int size = 16;
                var path = Path.Combine(folder, name + ".png");
                var pixels = new byte[size * size * 4];
                // Um quadrado com borda, para o sprite novo aparecer na sala antes de ser desenhado.
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var i = (y * size + x) * 4;
                    var edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                    (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = edge ? ((byte)0x82, (byte)0xAA, (byte)0xFF, (byte)255) : ((byte)0x2C, (byte)0x3A, (byte)0x55, (byte)255);
                }
                Png.Save(path, size, size, pixels);
                SaveSpriteMeta(path, new SpriteMeta());
                return path;
            }
            case "shader":
            {
                var path = Path.Combine(folder, name + ".wgsl");
                Write(path, Templates.NewShader.Replace("\r\n", "\n"));
                return path;
            }
            case "tileset":
            {
                // Aponta para um sprite de mesmo nome, se houver, ou para o primeiro.
                var sprites = List(root, "sprite");
                var sprite = sprites.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Name ?? sprites.FirstOrDefault()?.Name ?? "";
                var path = Path.Combine(folder, name + ".json");
                Write(path, JsonSerializer.Serialize(new TilesetData { Sprite = sprite }, GameProject.Json) + "\n");
                return path;
            }
            default:
                throw new ArgumentException($"Não dá para criar um recurso do tipo {kind}.");
        }
    }

    public static SpriteMeta LoadSpriteMeta(string png) => GameProject.LoadSpriteMeta(png);

    // Largura e altura do PNG, lidas do cabeçalho. Não depende de decodificar a imagem.
    public static (int Width, int Height)? PngSize(byte[] data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 24 || !data.AsSpan(0, 8).SequenceEqual(signature))
            return null;
        var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(16, 4));
        var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(20, 4));
        return width > 0 && height > 0 ? (width, height) : null;
    }

    public static void SaveSpriteMeta(string png, SpriteMeta meta) =>
        Write(Path.ChangeExtension(png, ".json"), JsonSerializer.Serialize(meta, GameProject.Json).Replace("\r\n", "\n") + "\n");

    private static void Write(string path, string text) => File.WriteAllText(path, text, new UTF8Encoding(false));

    // A pasta da engine (onde fica src/CanisMajor), subindo a partir de onde o editor está.
    public static string? EngineRoot(string? start = null)
    {
        for (var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "CanisMajor", "CanisMajor.csproj")))
                return dir.FullName;
        }
        return null;
    }
}
