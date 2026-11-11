using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CanisMajor;

/// <summary>Modelos de projeto ("exemplo" ou "vazio") e de recursos novos.</summary>
public static class Templates
{
    public static string NewObject(string ns, string cls, string? sprite = null) => $$"""
        using CanisMajor;

        namespace {{ns}};

        public class {{cls}} : GameObject
        {
            public {{cls}}()
            {
                Sprite = {{(sprite is null ? "null" : $"\"{sprite}\"")}};
            }

            public override void OnCreate()
            {
                // Uma vez, quando o objeto entra na sala.
            }

            public override void OnStep(float dt)
            {
                // A cada quadro. dt é o tempo desde o quadro anterior, em segundos.
            }
        }

        """.Replace("\r\n", "\n");

    public const string NewShader = """
        // Recebe a coordenada (uv, com 0,0 no canto de cima à esquerda) e a cor do sprite.
        // Devolve a cor final do pixel. Valem sample(uv), time(), resolution() e param(0..3).
        fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
            return sample(uv) * color;
        }

        """;

    /// <summary>Nome válido de namespace a partir do nome do jogo.</summary>
    public static string Namespace(string name)
    {
        var sb = new StringBuilder();
        foreach (var part in name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = new string(part.Normalize(NormalizationForm.FormD).Where(c => char.IsAsciiLetterOrDigit(c)).ToArray());
            if (clean.Length > 0) sb.Append(char.ToUpperInvariant(clean[0])).Append(clean[1..]);
        }
        var ns = sb.ToString();
        if (ns.Length == 0 || char.IsDigit(ns[0])) ns = "Jogo" + ns;
        return ns;
    }

    /// <summary>Cria um projeto de jogo. <paramref name="engineRoot"/> é a pasta do Canis Major (onde fica src/CanisMajor).</summary>
    public static string CreateProject(string folder, string name, string template, string engineRoot)
    {
        var root = Path.GetFullPath(folder);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"A pasta {root} já existe e não está vazia.");
        foreach (var sub in new[] { "sprites", "objects", "rooms", "shaders", "sounds" })
            Directory.CreateDirectory(Path.Combine(root, sub));
        var ns = Namespace(name);
        var info = new ProjectInfo { Name = name, Engine = Engine.Version };
        Write(Path.Combine(root, GameProject.FileName), JsonSerializer.Serialize(info, GameProject.Json));
        Write(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
        Write(Path.Combine(root, "Program.cs"), "return CanisMajor.Engine.Run(args);\n");

        // Dentro do repositório da engine, o caminho fica relativo; fora, absoluto.
        var engine = Path.GetFullPath(engineRoot);
        var rel = Path.GetRelativePath(root, engine);
        var insideEngine = !Path.IsPathRooted(rel) && rel.Split(Path.DirectorySeparatorChar).All(p => p == "..");
        var engineProp = insideEngine ? $"$(MSBuildThisFileDirectory){rel}" : engine;
        Write(Path.Combine(root, ns + ".csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <RootNamespace>{{ns}}</RootNamespace>
                <AssemblyName>{{ns}}</AssemblyName>
                <!-- Pasta do Canis Major. Troque se a engine mudar de lugar. -->
                <CanisMajorRoot Condition="'$(CanisMajorRoot)' == ''">{{engineProp}}</CanisMajorRoot>
              </PropertyGroup>

              <ItemGroup>
                <ProjectReference Include="$(CanisMajorRoot)\src\CanisMajor\CanisMajor.csproj" />
                <Content Include="projeto.json;sprites\**;rooms\**;shaders\**;sounds\**" CopyToOutputDirectory="PreserveNewest" />
              </ItemGroup>

            </Project>

            """);

        if (template == "exemplo") Example(root, ns, info);
        else SaveRoom(root, "inicio", new RoomData { Width = info.Width, Height = info.Height });
        return root;
    }

    private static void Write(string path, string text) =>
        File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));

    private static void SaveRoom(string root, string name, RoomData room) =>
        Write(Path.Combine(root, "rooms", name + ".json"), JsonSerializer.Serialize(room, GameProject.Json) + "\n");

    private static void SaveSprite(string root, string name, int w, int h, byte[] rgba, SpriteMeta meta)
    {
        Png.Save(Path.Combine(root, "sprites", name + ".png"), w, h, rgba);
        Write(Path.Combine(root, "sprites", name + ".json"), JsonSerializer.Serialize(meta, GameProject.Json) + "\n");
    }

    // ---------- pixel art do exemplo ----------

    private static void Put(byte[] px, int w, int x, int y, string hex)
    {
        var c = Color.Hex(hex);
        var i = (y * w + x) * 4;
        px[i] = (byte)(c.R * 255); px[i + 1] = (byte)(c.G * 255); px[i + 2] = (byte)(c.B * 255); px[i + 3] = (byte)(c.A * 255);
    }

    private static byte[] Player()
    {
        var px = new byte[16 * 16 * 4];
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
        {
            var d = MathF.Sqrt(MathF.Pow(x + 0.5f - 8f, 2) + MathF.Pow(y + 0.5f - 8f, 2));
            if (d <= 6.6f) Put(px, 16, x, y, d > 5.5f ? "#1B2A44" : (x + y) < 12 ? "#C9D9F2" : "#82AAFF");
        }
        foreach (var ex in new[] { 5, 10 })
        {
            Put(px, 16, ex, 7, "#FFFFFF");
            Put(px, 16, ex, 8, "#16181D");
        }
        Put(px, 16, 7, 11, "#1B2A44");
        Put(px, 16, 8, 11, "#1B2A44");
        return px;
    }

    private static byte[] StarStrip()
    {
        var px = new byte[32 * 8 * 4];
        int[] arms = [2, 3, 4, 3];
        for (var f = 0; f < 4; f++)
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 8; x++)
        {
            float dx = MathF.Abs(x - 3.5f), dy = MathF.Abs(y - 3.5f);
            var onAxis = (dx < 1 && dy <= arms[f]) || (dy < 1 && dx <= arms[f]);
            var core = dx + dy <= 2;
            if (core && dx < 1 && dy < 1) Put(px, 32, f * 8 + x, y, "#FFFFFF");
            else if (core || onAxis) Put(px, 32, f * 8 + x, y, "#F5C65B");
        }
        return px;
    }

    private static byte[] Wall()
    {
        var px = new byte[16 * 16 * 4];
        for (var y = 0; y < 16; y++)
        for (var x = 0; x < 16; x++)
            Put(px, 16, x, y, x == 0 || y == 0 ? "#3F434B" : x == 15 || y == 15 ? "#1C1E22" : "#2C2F35");
        foreach (var (x, y) in new[] { (4, 5), (10, 3), (7, 10), (12, 11), (3, 12) }) Put(px, 16, x, y, "#34373E");
        return px;
    }

    /// <summary>Um "plim" curto subindo de tom, em WAV.</summary>
    public static void Blip(string path)
    {
        const int rate = 22050;
        const float length = 0.16f;
        var n = (int)(rate * length);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + n * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 2);
        var phase = 0.0;
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)rate;
            phase += 2 * Math.PI * (880 + 900 * t / length) / rate;
            var env = Math.Min(1, t * 200) * Math.Pow(1 - t / length, 2);
            w.Write((short)(Math.Sin(phase) * env * 12000));
        }
        w.Flush();
        File.WriteAllBytes(path, ms.ToArray());
    }

    private static void Example(string root, string ns, ProjectInfo info)
    {
        SaveSprite(root, "jogador", 16, 16, Player(), new SpriteMeta { Frames = 1, Origin = [8, 8] });
        SaveSprite(root, "estrela", 32, 8, StarStrip(), new SpriteMeta { Frames = 4, Origin = [4, 4], Fps = 6 });
        SaveSprite(root, "parede", 16, 16, Wall(), new SpriteMeta());
        Blip(Path.Combine(root, "sounds", "coleta.wav"));

        foreach (var (file, code) in ExampleObjects)
            Write(Path.Combine(root, "objects", file + ".cs"), code.Replace("namespace Exemplo;", $"namespace {ns};"));
        foreach (var (file, code) in ExampleShaders)
            Write(Path.Combine(root, "shaders", file + ".wgsl"), code);

        int w = info.Width, h = info.Height, t = 16;
        var list = new List<JsonObject>();
        JsonObject I(string obj, int x, int y) => new() { ["object"] = obj, ["x"] = x, ["y"] = y };
        for (var x = 0; x < w; x += t) { list.Add(I("Parede", x, 0)); list.Add(I("Parede", x, h - t)); }
        for (var y = t; y < h - t; y += t) { list.Add(I("Parede", 0, y)); list.Add(I("Parede", w - t, y)); }
        for (var x = 160; x < 320; x += t) list.Add(I("Parede", x, 96));
        list.Add(I("Jogador", 80, 140));
        list.Add(I("Estrela", 200, 60));
        list.Add(I("Estrela", 300, 180));
        list.Add(I("Estrela", 120, 210));
        list.Add(I("Cubo", 390, 70));
        list.Add(I("Placar", 0, 0));
        SaveRoom(root, "inicio", new RoomData { Width = w, Height = h, Background = "#16181D", Grid = t, PostShaders = ["vinheta"], Instances = list });
    }

    private static readonly (string, string)[] ExampleObjects =
    [
        ("Jogador", """
            using CanisMajor;

            namespace Exemplo;

            public class Jogador : GameObject
            {
                private const float Velocidade = 110;

                public Jogador()
                {
                    Sprite = "jogador";
                    Layer = 10;
                }

                public override void OnStep(float dt)
                {
                    float dx = Keys.Axis("left", "right"), dy = Keys.Axis("up", "down");
                    if (dx == 0) dx = Keys.Axis("a", "d");
                    if (dy == 0) dy = Keys.Axis("w", "s");
                    if (dx != 0 && dy != 0)
                    {
                        dx *= 0.7071f;
                        dy *= 0.7071f;
                    }
                    Move(dx * Velocidade * dt, dy * Velocidade * dt, solid: "Parede");
                    if (dx != 0) ScaleX = dx > 0 ? 1 : -1;

                    if (Collide<Estrela>() is { } estrela) estrela.Coletar();
                    if (Keys.Pressed("r")) Game.Restart();
                }
            }

            """),
        ("Estrela", """
            using CanisMajor;

            namespace Exemplo;

            public class Estrela : GameObject
            {
                private float _baseY;
                private float _fase;

                public Estrela()
                {
                    Sprite = "estrela";
                    Shader = "brilho";
                    Layer = 5;
                    ScaleX = ScaleY = 2;
                }

                public override void OnCreate()
                {
                    _baseY = Y;
                    _fase = Random.Shared.NextSingle() * 6.28f;
                }

                public override void OnStep(float dt)
                {
                    Y = _baseY + MathF.Sin(Game.Time * 3 + _fase) * 3;
                    Rotation += 40 * dt;
                }

                public void Coletar()
                {
                    var pontos = Game.Data.TryGetValue("pontos", out var p) ? (int)p! : 0;
                    Game.Data["pontos"] = pontos + 1;
                    Game.PlaySound("coleta");
                    Destroy();
                    // Nasce outra num lugar livre.
                    for (var i = 0; i < 50; i++)
                    {
                        var nova = Game.Spawn<Estrela>(Random.Shared.Next(32, Game.RoomWidth - 32), Random.Shared.Next(32, Game.RoomHeight - 32));
                        if (nova.Collide("Parede") is null) break;
                        nova.Destroy();
                    }
                }
            }

            """),
        ("Parede", """
            using CanisMajor;

            namespace Exemplo;

            public class Parede : GameObject
            {
                public Parede()
                {
                    Sprite = "parede";
                }
            }

            """),
        ("Cubo", """
            using CanisMajor;

            namespace Exemplo;

            /// <summary>Um elemento 3D dentro do jogo 2D: um cubo iluminado que gira.</summary>
            public class Cubo : GameObject
            {
                private readonly Mesh _malha = Mesh.Cube();

                public Cubo()
                {
                    Layer = 4;
                }

                public override void OnDraw()
                {
                    var t = Game.Time;
                    Draw.Mesh(_malha, X, Y, 34, rotationX: t * 40, rotationY: t * 65, rotationZ: 15, color: "#9DB8E0");
                }
            }

            """),
        ("Placar", """
            using CanisMajor;

            namespace Exemplo;

            public class Placar : GameObject
            {
                public Placar()
                {
                    Layer = 100;
                }

                public override void OnDraw()
                {
                    var pontos = Game.Data.TryGetValue("pontos", out var p) ? (int)p! : 0;
                    Draw.Rect(8, 8, 150, 22, "#0D0F13", alpha: 0.6f, screen: true);
                    Draw.Text($"Estrelas: {pontos}", 16, 11, size: 14, color: "#ECEEF2", screen: true);
                    Draw.Text($"{Game.Fps:0} fps", Game.Width - 12, 11, size: 12, color: "#A3A8B3", align: Align.Right, screen: true);
                }
            }

            """),
    ];

    private static readonly (string, string)[] ExampleShaders =
    [
        ("brilho", """
            // Shader de sprite: a estrela pulsa e ganha um brilho dourado.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let pixel = sample(uv) * color;
                let pulso = 0.75 + 0.25 * sin(time() * 6.0);
                return vec4<f32>(mix(pixel.rgb, vec3<f32>(1.0, 0.92, 0.6), 0.35 * pulso), pixel.a);
            }

            """),
        ("vinheta", """
            // Shader de tela: escurece as bordas e separa um pouco as cores nas pontas.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let centro = uv - vec2<f32>(0.5);
                let d = length(centro);
                let desvio = centro * 0.01 * d;
                let rgb = vec3<f32>(sample(uv + desvio).r, sample(uv).g, sample(uv - desvio).b);
                let vinheta = 1.0 - smoothstep(0.35, 0.85, d);
                return vec4<f32>(rgb * vinheta, 1.0);
            }

            """),
        ("crt", """
            // Shader de tela: linhas de TV antiga e leve curvatura.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                var c = uv * 2.0 - vec2<f32>(1.0);
                c = c * (1.0 + dot(c, c) * 0.04);
                let tela = c * 0.5 + vec2<f32>(0.5);
                if (tela.x < 0.0 || tela.x > 1.0 || tela.y < 0.0 || tela.y > 1.0) {
                    return vec4<f32>(0.0, 0.0, 0.0, 1.0);
                }
                let linha = 0.85 + 0.15 * sin(tela.y * resolution().y * 3.14159);
                return vec4<f32>(sample(tela).rgb * linha, 1.0);
            }

            """),
        ("ondas", """
            // Shader de tela: a imagem ondula, como vista através da água.
            fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
                let onda = vec2<f32>(sin(uv.y * 30.0 + time() * 3.0), cos(uv.x * 25.0 + time() * 2.0)) * 0.003;
                return sample(uv + onda);
            }

            """),
    ];
}
