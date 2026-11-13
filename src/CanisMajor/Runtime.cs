using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanisMajor;

/// <summary>Um sprite carregado: textura e um ou mais quadros lado a lado.</summary>
public sealed record SpriteInfo(uint Texture, float Width, float Height, int Frames, float OriginX, float OriginY, float Fps)
{
    /// <summary>Máscara de colisão relativa ao canto do quadro, ou null para o quadro inteiro.</summary>
    public (float Left, float Top, float Right, float Bottom)? Mask { get; init; }

    public (float U0, float V0, float U1, float V1) Uv(float frame)
    {
        var i = ((int)frame % Frames + Frames) % Frames;
        return ((float)i / Frames, 0, (float)(i + 1) / Frames, 1);
    }
}

/// <summary>Estado da engine que está rodando. O jogo usa a fachada pública <see cref="Game"/>.</summary>
internal static unsafe partial class Runtime
{
    public static bool Started;
    public static GameProject? Project;
    public static Func<string, SpriteInfo?>? SpriteOverride; // para os testes, sem placa de vídeo

    public static readonly Dictionary<string, Type> Types = new(StringComparer.OrdinalIgnoreCase);
    public static List<GameObject> Instances = [];
    private static readonly Dictionary<string, SpriteInfo?> Sprites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, uint> Shaders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, uint> Sounds = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Dictionary<int, (float, float, float, float)>> PendingParams = new(StringComparer.OrdinalIgnoreCase);

    private static DrawCmd[] _cmds = new DrawCmd[1024];
    private static int _cmdCount;
    private static readonly List<byte> Strings = [];
    private static uint _circle;

    public static string Room = "";
    public static string? PendingRoom;
    public static bool Running;
    public static Color Background = Color.Black;
    public static List<string> PostShaders = [];
    public static int RoomWidth, RoomHeight;
    public static float CameraX, CameraY, Time, Dt, Fps;
    public static long FrameCount;
    public static readonly Dictionary<string, object?> Data = new();

    // ---------- carregamento ----------

    public static void RegisterTypes(IEnumerable<Assembly> assemblies)
    {
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            foreach (var t in types)
                if (t is { IsAbstract: false, IsClass: true } && t.IsSubclassOf(typeof(GameObject)) && t.GetConstructor(Type.EmptyTypes) != null)
                    Types[t.Name] = t;
                else ComponentTypes.Register(t);
        }
    }

    public static SpriteInfo? SpriteInfo(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (SpriteOverride != null) return SpriteOverride(name);
        if (Sprites.TryGetValue(name, out var cached)) return cached;
        SpriteInfo? info = null;
        var png = Path.Combine(Project!.Folder("sprite"), name + ".png");
        if (!File.Exists(png))
        {
            Console.Error.WriteLine($"Aviso: o sprite '{name}' não existe em sprites/.");
        }
        else
        {
            var tex = Native.WithUtf8(png, (p, len) => Native.cm_load_texture((byte*)p, len));
            if (tex == 0) throw new GameException(Native.LastError());
            uint w, h;
            Native.cm_texture_size(tex, &w, &h);
            var meta = GameProject.LoadSpriteMeta(png);
            info = new SpriteInfo(tex, (float)w / meta.Frames, h, meta.Frames, meta.Origin[0], meta.Origin[1], meta.Fps) { Mask = MaskOf(png, meta) };
            // A normal map, se houver, fica ao lado: sprites/nome_n.png.
            var normalPng = Path.Combine(Project.Folder("sprite"), name + "_n.png");
            if (File.Exists(normalPng))
            {
                var normal = Native.WithUtf8(normalPng, (p, len) => Native.cm_load_texture((byte*)p, len));
                if (normal != 0) Native.cm_set_normal_map(tex, normal);
            }
        }
        Sprites[name] = info;
        return info;
    }

    // A máscara de colisão do sprite: o retângulo do arquivo, ou o dos pixels visíveis no modo "auto".
    internal static (float, float, float, float)? MaskOf(string png, SpriteMeta meta)
    {
        if (meta.Mask is not { } mask) return null;
        if (!string.Equals(mask.Mode, "auto", StringComparison.OrdinalIgnoreCase))
            return mask.Right > mask.Left && mask.Bottom > mask.Top ? (mask.Left, mask.Top, mask.Right, mask.Bottom) : null;
        try
        {
            var (w, h, rgba) = Png.Load(png);
            return Png.OpaqueBox(w, h, rgba, meta.Frames) is { } box ? (box.Left, box.Top, box.Right, box.Bottom) : null;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or NotSupportedException)
        {
            Console.Error.WriteLine($"Aviso: não deu para calcular a máscara automática de {Path.GetFileName(png)} ({e.Message}). Usei o quadro inteiro.");
            return null;
        }
    }

    public static uint Shader(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        if (Shaders.TryGetValue(name, out var id)) return id;
        var path = Path.Combine(Project!.Folder("shader"), name + ".wgsl");
        if (!File.Exists(path)) throw new GameException($"O shader '{name}' não existe em shaders/ (procurei {name}.wgsl).");
        var source = File.ReadAllText(path);
        var result = Native.WithUtf8(source, (p, len) => Native.cm_load_shader((byte*)p, len));
        if (result < 0)
        {
            var lines = Native.LastError().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l =>
            {
                var cut = l.IndexOf(": ", StringComparison.Ordinal);
                var line = cut > 0 && int.TryParse(l[..cut], out var n) ? n : 0;
                var msg = cut > 0 ? l[(cut + 2)..] : l;
                return line > 0 ? $"{path}:{line}: {msg}" : $"{path}: {msg}";
            });
            throw new GameException($"Erro no shader '{name}':\n" + string.Join("\n", lines));
        }
        id = (uint)result;
        Shaders[name] = id;
        if (PendingParams.Remove(name, out var pending))
            foreach (var (i, v) in pending) Native.cm_set_shader_param(id, (uint)i, v.Item1, v.Item2, v.Item3, v.Item4);
        return id;
    }

    public static void SetShaderParam(string shader, int index, float x, float y, float z, float w)
    {
        if (Shaders.TryGetValue(shader, out var id)) Native.cm_set_shader_param(id, (uint)index, x, y, z, w);
        else
        {
            if (!PendingParams.TryGetValue(shader, out var d)) PendingParams[shader] = d = [];
            d[index] = (x, y, z, w);
        }
    }

    public static void PlaySound(string name, float volume, bool loop)
    {
        if (!Sounds.TryGetValue(name, out var id))
        {
            var folder = Project!.Folder("sound");
            var path = new[] { ".wav", ".ogg" }.Select(ext => Path.Combine(folder, name + ext)).FirstOrDefault(File.Exists);
            if (path is null)
            {
                Console.Error.WriteLine($"Aviso: o som '{name}' não existe em sounds/ (.wav ou .ogg).");
                Sounds[name] = 0;
                return;
            }
            id = Native.WithUtf8(path, (p, len) => Native.cm_load_sound((byte*)p, len));
            if (id == 0) Console.Error.WriteLine($"Aviso: {Native.LastError()}");
            Sounds[name] = id;
        }
        if (id != 0) Native.cm_play_sound(id, volume, loop ? 1u : 0u);
    }

    // ---------- salas e instâncias ----------

    public static void LoadRoom(string name)
    {
        var room = Project!.LoadRoom(name);
        foreach (var o in Instances.Where(o => o.Alive).ToList())
        {
            o.Alive = false;
            o.OnDestroy();
            o.DestroyComponents();
        }
        Instances = [];
        Room = name;
        RoomWidth = room.Width;
        RoomHeight = room.Height;
        Background = Color.Hex(room.Background);
        PostShaders = [.. room.PostShaders];
        Lighting.Reset();
        Pathfinding.Invalidate();
        if (room.Lighting is { } lighting)
        {
            Lighting.Enabled = lighting.Enabled;
            Lighting.Ambient = Color.Hex(lighting.Ambient);
            Lighting.AmbientIntensity = lighting.Intensity;
        }
        foreach (var s in PostShaders) Shader(s);
        CameraX = CameraY = 0;
        // Sem "layers" no arquivo, cada objeto fica na profundidade que ele mesmo define (salas antigas).
        var explicitLayers = room.Layers.Count > 0;
        Layers = RoomLayers.Resolve(room);
        var fallback = RoomLayers.DefaultInstanceLayer(Layers);
        foreach (var inst in room.Instances)
        {
            var kind = inst["object"]?.GetValue<string>() ?? throw new GameException($"Uma instância da sala '{name}' não diz qual é o objeto.");
            var x = inst["x"]?.GetValue<float>() ?? 0;
            var y = inst["y"]?.GetValue<float>() ?? 0;
            var obj = Spawn(kind, x, y, create: false);
            var layerName = inst["layer"]?.GetValue<string>();
            var layer = layerName is null ? fallback
                : Layers.FirstOrDefault(l => string.Equals(l.Name, layerName, StringComparison.OrdinalIgnoreCase))
                  ?? throw new GameException($"A sala '{name}' usa a camada '{layerName}', que não existe em \"layers\".");
            if (layer is not null)
            {
                obj.LayerName = layer.Name;
                if (explicitLayers)
                {
                    obj.Layer = layer.Depth;
                    obj.YSort = layer.YSort;
                }
                if (!layer.Visible) obj.Visible = false;
            }
            ApplyProps(obj, inst);
            Create(obj);
        }
    }

    /// <summary>As camadas da sala aberta.</summary>
    public static List<LayerData> Layers = [];

    // Fundos e instâncias numa pilha só, da profundidade menor para a maior.
    private static void DrawWorld()
    {
        var backgrounds = Layers.Where(l => (l.IsBackground || l.IsTiles) && l.Visible).OrderBy(l => l.Depth).ToList();
        var next = 0;
        foreach (var o in Instances.Where(o => o.Visible && o.Alive).OrderBy(RoomLayers.SortKey).ToArray())
        {
            while (next < backgrounds.Count && backgrounds[next].Depth <= o.Layer)
                DrawLayer(backgrounds[next++]);
            if (o.Visible && o.Alive)
            {
                DrawFlags = o.Unlit ? 1u : 0u;
                o.OnDraw();
                o.DrawComponents();
            }
        }
        while (next < backgrounds.Count)
            DrawLayer(backgrounds[next++]);
    }

    private static void DrawLayer(LayerData layer)
    {
        DrawFlags = 0;
        if (layer.IsTiles) DrawTiles(layer);
        else DrawBackground(layer);
    }

    private static void DrawBackground(LayerData layer)
    {
        var viewW = Project!.Info.Width;
        var viewH = Project.Info.Height;
        if (layer.Color is { } hex)
            Rect(0, 0, RoomWidth, RoomHeight, Color.Hex(hex), 0, 0, false);
        if (SpriteInfo(layer.Sprite) is not { } s) return;
        // Paralaxe: a camada anda uma fração do que a câmera anda.
        var baseX = CameraX * layer.Parallax;
        var baseY = CameraY * layer.Parallax;
        var startX = layer.TileX ? baseX + MathF.Floor((CameraX - baseX) / s.Width) * s.Width : baseX;
        var startY = layer.TileY ? baseY + MathF.Floor((CameraY - baseY) / s.Height) * s.Height : baseY;
        var endX = layer.TileX ? CameraX + viewW : startX + 1;
        var endY = layer.TileY ? CameraY + viewH : startY + 1;
        for (var y = startY; y < endY; y += s.Height)
            for (var x = startX; x < endX; x += s.Width)
                Quad(s.Texture, x, y, s.Width, s.Height, 0, 0, 0, s.Uv(0), Color.White, 0);
    }

    private static void ApplyProps(GameObject obj, JsonObject inst)
    {
        foreach (var (key, value) in inst)
        {
            if (key is "object" or "x" or "y" or "layer" || value is null) continue;
            var name = string.Concat(key.Split('_').Select(p => p.Length > 0 ? char.ToUpperInvariant(p[0]) + p[1..] : p));
            var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            var type = obj.GetType();
            if (type.GetProperty(name, flags) is { CanWrite: true } prop)
                prop.SetValue(obj, value.Deserialize(prop.PropertyType, GameProject.Json));
            else if (type.GetField(name, flags) is { } field)
                field.SetValue(obj, value.Deserialize(field.FieldType, GameProject.Json));
            else
                Console.Error.WriteLine($"Aviso: {type.Name} não tem a propriedade '{key}' usada na sala.");
        }
    }

    public static GameObject Spawn(string kind, float x, float y, bool create = true)
    {
        if (Types.TryGetValue(kind, out var type))
            return Spawn(type, x, y, create);
        // Sem classe, mas com a ficha em objects/Nome.json: o objeto existe só com os componentes.
        if (ObjectDefOf(kind) is not null)
            return Spawn(typeof(DefinedObject), x, y, create, kind);
        throw new GameException($"O objeto '{kind}' não existe. Crie a classe {kind} : GameObject em objects/ ou a ficha objects/{kind}.json.");
    }

    public static GameObject Spawn(Type type, float x, float y, bool create = true) => Spawn(type, x, y, create, null);

    private static GameObject Spawn(Type type, float x, float y, bool create, string? kind)
    {
        var obj = (GameObject)Activator.CreateInstance(type)!;
        obj.Kind = kind ?? type.Name;
        obj.X = x;
        obj.Y = y;
        ApplyDef(obj);
        Instances.Add(obj);
        if (create) Create(obj);
        return obj;
    }

    internal static void Create(GameObject obj)
    {
        obj.OnCreate();
        obj.CreateComponents();
    }

    // ---------- fichas dos objetos ----------

    private static readonly Dictionary<string, ObjectDef?> Defs = new(StringComparer.OrdinalIgnoreCase);
    public static Func<string, ObjectDef?>? DefOverride; // para os testes

    public static ObjectDef? ObjectDefOf(string kind)
    {
        if (DefOverride != null) return DefOverride(kind);
        if (Project is null) return null;
        if (!Defs.TryGetValue(kind, out var def))
            Defs[kind] = def = Project.LoadObjectDef(kind);
        return def;
    }

    // A ficha vem depois do construtor: o que o Inspetor diz vale mais que o padrão da classe.
    internal static void ApplyDef(GameObject obj)
    {
        if (ObjectDefOf(obj.Kind) is not { } def) return;
        if (def.Sprite is not null) obj.Sprite = def.Sprite;
        if (def.Shader is not null) obj.Shader = def.Shader;
        if (def.Layer is { } layer) obj.Layer = layer;
        if (def.Visible is { } visible) obj.Visible = visible;
        if (def.YSort is { } ysort) obj.YSort = ysort;
        if (def.SortOffset is { } offset) obj.SortOffset = offset;
        if (def.Color is { } color) obj.Color = Color.Hex(color);
        if (def.Alpha is { } alpha) obj.Alpha = alpha;
        if (def.ScaleX is { } sx) obj.ScaleX = sx;
        if (def.ScaleY is { } sy) obj.ScaleY = sy;
        if (def.Unlit is { } unlit) obj.Unlit = unlit;
        foreach (var json in def.Components)
            obj.Add(ComponentTypes.Create(json, $"objects/{obj.Kind}.json"));
    }

    public static void Destroy(GameObject obj)
    {
        if (!obj.Alive) return;
        obj.Alive = false;
        obj.OnDestroy();
        obj.DestroyComponents();
    }

    public static IEnumerable<GameObject> InstancesOf(string? kind) =>
        Instances.Where(o => o.Alive && (kind is null || string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase)));

    public static IEnumerable<T> InstancesOf<T>() where T : GameObject => Instances.OfType<T>().Where(o => o.Alive);

    // ---------- desenho ----------

    private static ref DrawCmd NewCmd(uint kind)
    {
        if (_cmdCount == _cmds.Length) Array.Resize(ref _cmds, _cmds.Length * 2);
        ref var c = ref _cmds[_cmdCount++];
        c = default;
        c.Kind = kind;
        c.Flags = DrawFlags;
        return ref c;
    }

    // Bit 1: não recebe luz. Vale para o que é desenhado preso à tela (interface) e para objetos Unlit.
    internal static uint DrawFlags;

    private static (float, float) Offset(float x, float y, bool screen)
    {
        // No Draw GUI, tudo é preso à tela.
        screen |= InGui;
        DrawFlags = screen ? 1u : 0u;
        return screen ? (x + CameraX, y + CameraY) : (x, y);
    }

    public static void Quad(uint tex, float x, float y, float w, float h, float ox, float oy, float rotDeg,
                            (float U0, float V0, float U1, float V1) uv, Color color, uint shader)
    {
        ref var c = ref NewCmd(1);
        c.Texture = tex; c.Shader = shader;
        c.X = x; c.Y = y; c.W = w; c.H = h; c.Ox = ox; c.Oy = oy; c.Rot = rotDeg * MathF.PI / 180f;
        (c.U0, c.V0, c.U1, c.V1) = uv;
        (c.R, c.G, c.B, c.A) = (color.R, color.G, color.B, color.A);
    }

    public static void DrawObject(GameObject o)
    {
        if (SpriteInfo(o.Sprite) is not { } s) return;
        DrawFlags = o.Unlit ? 1u : 0u;
        Quad(s.Texture, o.X, o.Y, s.Width * o.ScaleX, s.Height * o.ScaleY, s.OriginX * o.ScaleX, s.OriginY * o.ScaleY,
             o.Rotation, s.Uv(o.Frame), o.Color.WithAlpha(o.Alpha), Shader(o.Shader));
    }

    public static void Sprite(string name, float x, float y, float frame, float sx, float sy, float rot, Color color, string? shader, bool screen)
    {
        if (SpriteInfo(name) is not { } s) return;
        (x, y) = Offset(x, y, screen);
        Quad(s.Texture, x, y, s.Width * sx, s.Height * sy, s.OriginX * sx, s.OriginY * sy, rot, s.Uv(frame), color, Shader(shader));
    }

    public static void Text(string text, float x, float y, float size, Color color, Align align, bool screen)
    {
        (x, y) = Offset(x, y, screen);
        var bytes = Encoding.UTF8.GetBytes(text);
        ref var c = ref NewCmd(2);
        c.X = x; c.Y = y; c.Size = size; c.Align = (uint)align;
        (c.R, c.G, c.B, c.A) = (color.R, color.G, color.B, color.A);
        c.StrOff = (uint)Strings.Count;
        c.StrLen = (uint)bytes.Length;
        Strings.AddRange(bytes);
    }

    public static (float W, float H) Measure(string text, float size)
    {
        float w, h;
        var bytes = Encoding.UTF8.GetBytes(text);
        fixed (byte* p = bytes) Native.cm_measure_text(p, (nuint)bytes.Length, size, &w, &h);
        return (w, h);
    }

    public static void Rect(float x, float y, float w, float h, Color color, float outline, float rot, bool screen)
    {
        (x, y) = Offset(x, y, screen);
        if (outline > 0)
        {
            var t = outline;
            Rect(x, y, w, t, color, 0, 0, false);
            Rect(x, y + h - t, w, t, color, 0, 0, false);
            Rect(x, y + t, t, h - 2 * t, color, 0, 0, false);
            Rect(x + w - t, y + t, t, h - 2 * t, color, 0, 0, false);
            return;
        }
        Quad(0, x, y, w, h, 0, 0, rot, (0, 0, 1, 1), color, 0);
    }

    public static void Line(float x1, float y1, float x2, float y2, Color color, float width, bool screen)
    {
        (x1, y1) = Offset(x1, y1, screen);
        (x2, y2) = Offset(x2, y2, screen);
        var len = MathF.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        if (len <= 0) return;
        Quad(0, x1, y1, len, width, 0, width / 2, MathF.Atan2(y2 - y1, x2 - x1) * 180f / MathF.PI, (0, 0, 1, 1), color, 0);
    }

    public static void Circle(float x, float y, float radius, Color color, bool screen)
    {
        (x, y) = Offset(x, y, screen);
        if (_circle == 0)
        {
            const int size = 128;
            var px = new byte[size * size * 4];
            for (var j = 0; j < size; j++)
            for (var i = 0; i < size; i++)
            {
                var d = MathF.Sqrt((i + 0.5f - 64) * (i + 0.5f - 64) + (j + 0.5f - 64) * (j + 0.5f - 64));
                var a = Math.Clamp(64 - d, 0, 1);
                var k = (j * size + i) * 4;
                px[k] = px[k + 1] = px[k + 2] = 255;
                px[k + 3] = (byte)(a * 255);
            }
            fixed (byte* p = px) _circle = Native.cm_create_texture(p, size, size);
        }
        Quad(_circle, x, y, radius * 2, radius * 2, radius, radius, 0, (0, 0, 1, 1), color, 0);
    }

    public static void MeshCmd(Mesh mesh, float x, float y, float size, float rx, float ry, float rz, Color color, float z,
                               string? sprite, (float X, float Y, float Z) light, float ambient)
    {
        ref var c = ref NewCmd(3);
        c.Mesh = mesh.Handle();
        c.X = x; c.Y = y; c.Z = z; c.W = c.H = c.Size = size;
        c.Rx = rx; c.Ry = ry; c.Rz = rz;
        (c.R, c.G, c.B, c.A) = (color.R, color.G, color.B, color.A);
        c.Texture = SpriteInfo(sprite)?.Texture ?? 0;
        (c.L0, c.L1, c.L2, c.L3) = (light.X, light.Y, light.Z, ambient);
    }

    // ---------- laço ----------

    public static int Run(string projectDir, string? room, int maxFrames, string? screenshot, IEnumerable<Assembly> assemblies)
    {
        if (Native.cm_version() != Native.ApiVersion)
            throw new GameException($"A canis_core.dll é de outra versão ({Native.cm_version()}); esperava {Native.ApiVersion}.");
        Project = GameProject.Load(projectDir);
        RegisterTypes(assemblies);
        var info = Project.Info;
        var title = Encoding.UTF8.GetBytes(info.Name);
        fixed (byte* t = title)
            if (Native.cm_init(t, (nuint)title.Length, (uint)info.Width, (uint)info.Height, (uint)Math.Max(1, info.Scale), 1, info.PixelArt ? 1u : 0u) != 0)
                throw new GameException(Native.LastError());
        Started = true;
        LoadRoom(room ?? info.StartRoom);

        var clock = Stopwatch.StartNew();
        var last = 0.0;
        InputState input;
        var clear = stackalloc float[4];
        Running = true;
        while (Running)
        {
            if (Native.cm_poll(&input) == 0) break;
            for (var i = 0; i < 128; i++) Keys.State[i] = input.Keys[i];
            for (var i = 0; i < 4; i++) Mouse.State[i] = input.Buttons[i];
            Mouse.ScreenX = input.MouseX;
            Mouse.ScreenY = input.MouseY;
            Mouse.Wheel = input.Wheel;
            var scale = input.ViewScale > 0 ? input.ViewScale : 1;
            Mouse.X = (input.MouseX - input.ViewX) / scale + CameraX;
            Mouse.Y = (input.MouseY - input.ViewY) / scale + CameraY;

            var now = clock.Elapsed.TotalSeconds;
            Dt = (float)Math.Min(now - last, 0.1); // uma pausa longa não vira um salto enorme
            last = now;
            Time = (float)now;
            if (Dt > 0) Fps = Fps <= 0 ? 1 / Dt : Fps * 0.9f + 0.1f / Dt;

            StepFrame(Dt);
            Instances.RemoveAll(o => !o.Alive);

            foreach (var o in Instances)
                if (SpriteInfo(o.Sprite) is { Frames: > 1 } s)
                    o.Frame = (o.Frame + (o.AnimationSpeed ?? s.Fps) * Dt) % s.Frames;

            _cmdCount = 0;
            Strings.Clear();
            DrawWorld();
            DrawGui();
            SubmitLighting();

            var post = PostShaders.Select(Shader).ToArray();
            (clear[0], clear[1], clear[2], clear[3]) = (Background.R, Background.G, Background.B, Background.A);
            var strings = CollectionsMarshal.AsSpan(Strings);
            fixed (DrawCmd* cmds = _cmds)
            fixed (byte* str = strings)
            fixed (uint* p = post)
                if (Native.cm_frame(clear, CameraX, CameraY, Time, cmds, (nuint)_cmdCount, str, (nuint)strings.Length, p, (nuint)post.Length) != 0)
                    throw new GameException(Native.LastError());

            FrameCount++;
            if (maxFrames > 0 && FrameCount >= maxFrames)
            {
                if (screenshot != null)
                {
                    var full = Path.GetFullPath(screenshot);
                    var ok = Native.WithUtf8(full, (pp, len) => Native.cm_screenshot((byte*)pp, len));
                    if (ok != 0) throw new GameException(Native.LastError());
                }
                break;
            }
            if (PendingRoom != null)
            {
                var next = PendingRoom;
                PendingRoom = null;
                LoadRoom(next);
            }
        }
        return 0;
    }

    public static void Shutdown()
    {
        if (Started) Native.cm_shutdown();
        Started = false;
        Running = false;
    }

    /// <summary>Mostra o erro do jogo só com as linhas dos scripts do usuário, no formato arquivo:linha.</summary>
    public static void PrintUserError(Exception e) => ErrorReport.Show(UserErrorText(e));

    /// <summary>O texto do erro: a mensagem e as chamadas dos scripts do jogo, sem as da engine.</summary>
    public static string UserErrorText(Exception e)
    {
        while (e is TargetInvocationException { InnerException: { } inner }) e = inner;
        var text = new StringBuilder($"Erro no jogo: {e.GetType().Name}: {e.Message}");
        var all = new StackTrace(e, true).GetFrames();
        var mine = all.Where(f => f.GetMethod()?.DeclaringType?.Namespace?.StartsWith("CanisMajor", StringComparison.Ordinal) != true).ToList();
        // No jogo exportado não há arquivo e linha, mas o nome do método ainda ajuda.
        var frames = mine.Count > 0 ? mine : all.Where(f => f.GetFileName() != null).ToList();
        foreach (var f in frames)
        {
            var m = f.GetMethod();
            if (m is null) continue;
            text.Append($"\n  {m.DeclaringType?.Name}.{m.Name}");
            if (f.GetFileName() is { } file)
                text.Append($"  {file}:{f.GetFileLineNumber()}");
        }
        return text.ToString();
    }
}
