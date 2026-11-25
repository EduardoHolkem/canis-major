using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CanisMajor;

namespace CanisEditor.Game;

// Um modelo pronto de recurso, como os do GameMaker: escolhe-se o modelo, dá-se o nome, e os arquivos
// nascem prontos para usar. Create recebe a pasta do jogo e o nome, e devolve o arquivo para abrir.
public sealed record ResourceTemplate(string Kind, string Id, string Title, string Description, string Stem, Func<string, string, string> Create)
{
    // Sprite vazio: o tamanho, para a prévia mostrar a proporção.
    public (int Width, int Height)? Size { get; init; }

    // Personalizado: o editor pergunta também a largura e a altura, e cria com CreateSized.
    public bool AskSize { get; init; }
    public Func<string, string, int, int, string>? CreateSized { get; init; }
}

// Os pixels de um sprite feito pelo modelo (usado pelo tileset pronto).
public sealed record SpriteArt(int Width, int Height, byte[] Rgba, SpriteMeta Meta);

public static class ResourceTemplates
{
    // Os tipos, na ordem da janela de modelos.
    public static readonly (string Kind, string Title, string Color, string Glyph)[] Kinds =
    [
        ("room", "Sala", "#3F7FC4", "▦"),
        ("object", "Objeto", "#3A9A6A", "◆"),
        ("sprite", "Sprite", "#C65A3A", "▣"),
        ("shader", "Shader", "#8E5BD0", "✦"),
        ("script", "Script", "#D9822B", "{ }"),
        ("tileset", "Tileset", "#2E9AA0", "▤"),
    ];

    public static IEnumerable<ResourceTemplate> For(string kind) => All.Where(t => t.Kind == kind);

    public static ResourceTemplate Find(string kind, string id) =>
        All.FirstOrDefault(t => t.Kind == kind && t.Id == id) ?? throw new ArgumentException($"Não há o modelo {id} de {kind}.");

    public static readonly ResourceTemplate[] All = [.. Rooms(), .. Objects(), .. Sprites(), .. Shaders(), .. Scripts(), .. Tilesets()];

    // ---------- salas ----------

    private static IEnumerable<ResourceTemplate> Rooms()
    {
        yield return new("room", "vazia", "Sala vazia", "Do tamanho da tela, com uma camada de instâncias.", "sala",
            (root, name) => GameFiles.Create(root, "room", name));
        yield return new("room", "camadas", "Com fundo e camadas", "Fundo com cor, Atores com Ordenar por Y e Frente, prontos para pintar.", "fase",
            (root, name) => Room(root, name, info => new RoomData
            {
                Width = info.Width, Height = info.Height, Background = "#1E2430",
                Layers =
                [
                    new LayerData { Name = "Fundo", Kind = "background", Depth = -100, Color = "#283040" },
                    new LayerData { Name = "Atores", Kind = "instances", Depth = 0, YSort = true },
                    new LayerData { Name = "Frente", Kind = "instances", Depth = 100 },
                ],
            }));
        yield return new("room", "grande", "Sala grande", "Três telas de largura e duas de altura. Use Game.Follow(this) no jogador para a câmera seguir.", "mundo",
            (root, name) => Room(root, name, info => new RoomData { Width = info.Width * 3, Height = info.Height * 2 }));
        yield return new("room", "noite", "Noite com luz", "Iluminação 2D ligada, escura e azulada. Ponha tochas (componente Light) para clarear.", "noite",
            (root, name) => Room(root, name, info => new RoomData
            {
                Width = info.Width, Height = info.Height, Background = "#4A5260",
                Lighting = new LightingData { Enabled = true, Ambient = "#3A4A7A", Intensity = 0.3f },
            }));
        yield return new("room", "menu", "Tela de menu", "Título do jogo e \"Aperte Enter\" piscando, com o código da sala que leva para a primeira fase.", "menu",
            (root, name) =>
            {
                var info = GameFiles.Info(root);
                var next = string.Equals(info.StartRoom, name, StringComparison.OrdinalIgnoreCase) ? "inicio" : info.StartRoom;
                return Room(root, name, i => new RoomData { Width = i.Width, Height = i.Height, Background = "#101420" }, RoomCode(root, name, $$"""
                        // A sala que o Enter abre. Troque pelo nome da sua primeira fase.
                        private const string Proxima = "{{Escape(next)}}";
                        private float _tempo;

                        public override void OnStep(float dt)
                        {
                            _tempo += dt;
                            if (Keys.Pressed("enter") || Keys.Pressed("space"))
                                Game.Goto(Proxima);
                        }

                        public override void OnDrawGui()
                        {
                            Draw.Text("{{Escape(info.Name)}}", Game.Width / 2f, Game.Height / 3f, size: 28, color: "#ECEEF2", align: Align.Center);
                            // Pisca duas vezes por segundo.
                            if ((int)(_tempo * 2) % 2 == 0)
                                Draw.Text("Aperte Enter para começar", Game.Width / 2f, Game.Height * 0.6f, size: 12, color: "#A3A8B3", align: Align.Center);
                        }
                    """));
            });
        yield return new("room", "cronometro", "Fase com cronômetro", "Um minuto para terminar. O tempo aparece no alto, e a sala recomeça quando acaba.", "fase",
            (root, name) => Room(root, name, info => new RoomData { Width = info.Width, Height = info.Height }, RoomCode(root, name, """
                    // Segundos para terminar a fase.
                    private float _restante = 60;

                    public override void OnStep(float dt)
                    {
                        _restante -= dt;
                        if (_restante <= 0)
                            Game.Restart();
                    }

                    public override void OnDrawGui() =>
                        Draw.Text($"Tempo: {Math.Ceiling(_restante)}", Game.Width / 2f, 8, size: 14, color: "#ECEEF2", align: Align.Center);
                """)));
    }

    private static string Room(string root, string name, Func<ProjectInfo, RoomData> make, string? code = null)
    {
        var folder = GameFiles.FolderOf(root, "room");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".json");
        Write(path, RoomJson.Format(make(GameFiles.Info(root))));
        if (code is not null)
            Write(Path.Combine(folder, name + ".cs"), code);
        return path;
    }

    // O código da sala (rooms/nome.cs) com os membros dados.
    public static string RoomCode(string root, string room, string members) => $$"""
        using CanisMajor;

        namespace {{GameFiles.Namespace(root)}};

        // O código da sala {{room}}. Roda quando a sala abre, depois do OnCreate das instâncias.
        [Room("{{Escape(room)}}")]
        public class {{GameFiles.RoomClassName(room)}} : RoomScript
        {
        {{Indent(members)}}
        }

        """;

    // ---------- objetos ----------

    private static IEnumerable<ResourceTemplate> Objects()
    {
        yield return new("object", "vazio", "Objeto com script", "Uma classe C# com OnCreate e OnStep, para escrever do zero.", "Objeto",
            (root, name) => GameFiles.Create(root, "object", name));
        yield return new("object", "ficha", "Só ficha, sem código", "Sprite e componentes escolhidos no Inspetor. Bom para cenário e coisas simples.", "Objeto",
            (root, name) => Ficha(root, name, new JsonObject()));
        yield return new("object", "parede", "Parede sólida", "Para usar em Move(dx, dy, solid: \"Parede\"). Usa o sprite parede, se houver.", "Parede",
            (root, name) => Ficha(root, name, new JsonObject { ["layer"] = 1 }, preferSprite: "parede"));
        yield return new("object", "plataforma", "Jogador de plataforma", "Anda com as setas, pula com espaço, cai com gravidade e para nas paredes. A câmera segue.", "Jogador",
            (root, name) => Script(root, name, """
                    private const float Velocidade = 100, Pulo = 260, Gravidade = 700;
                    private float _vy;

                    public override void OnStep(float dt)
                    {
                        var dx = Keys.Axis("left", "right") * Velocidade * dt;
                        _vy += Gravidade * dt;
                        var noChao = Collide("Parede", 0, 1) != null;
                        if (noChao && (Keys.Pressed("space") || Keys.Pressed("up")))
                            _vy = -Pulo;
                        var (_, bateuY) = Move(dx, _vy * dt, solid: "Parede");
                        if (bateuY)
                            _vy = 0;
                        Game.Follow(this, smooth: 0.15f);
                    }
                """));
        yield return new("object", "cima", "Jogador visto de cima", "Anda nas oito direções, para nas paredes e passa atrás das coisas (Ordenar por Y).", "Jogador",
            (root, name) => Script(root, name, """
                    private const float Velocidade = 90;

                    public override void OnCreate() => YSort = true;

                    public override void OnStep(float dt)
                    {
                        float dx = Keys.Axis("left", "right");
                        float dy = Keys.Axis("up", "down");
                        // Na diagonal, a mesma velocidade das retas.
                        if (dx != 0 && dy != 0)
                        {
                            dx *= 0.7071f;
                            dy *= 0.7071f;
                        }
                        Move(dx * Velocidade * dt, dy * Velocidade * dt, solid: "Parede");
                        Game.Follow(this, smooth: 0.15f);
                    }
                """));
        yield return new("object", "coletavel", "Coletável", "Some ao encostar no Jogador, toca o som coleta e soma um em Game.Data[\"pontos\"].", "Moeda",
            (root, name) => Script(root, name, """
                    public override void OnCollision(GameObject other)
                    {
                        if (!other.Is("Jogador"))
                            return;
                        Game.Data["pontos"] = (Game.Data.TryGetValue("pontos", out var p) && p is int n ? n : 0) + 1;
                        Game.PlaySound("coleta");
                        Destroy();
                    }
                """));
        yield return new("object", "projetil", "Projétil", "Voa numa direção, some ao bater numa parede ou depois de três segundos. Crie com Game.Spawn e ajuste Direcao.", "Tiro",
            (root, name) => Script(root, name, """
                    // Graus: 0 é para a direita, 90 para baixo.
                    public float Direcao;
                    public float Velocidade = 240;

                    public override void OnCreate() => SetAlarm(0, 3f);

                    public override void OnAlarm(int index) => Destroy();

                    public override void OnStep(float dt)
                    {
                        var rad = Direcao * MathF.PI / 180f;
                        X += MathF.Cos(rad) * Velocidade * dt;
                        Y += MathF.Sin(rad) * Velocidade * dt;
                        if (Collide("Parede") != null)
                            Destroy();
                    }
                """));
        yield return new("object", "botao", "Botão de menu", "Clicável, acende com o mouse em cima e leva para outra sala. Texto e destino mudam por instância.", "Botao",
            (root, name) =>
            {
                var path = Script(root, name, """
                        // Mude por instância, na sala, com as propriedades texto e destino.
                        public string Texto = "Jogar";
                        public string Destino = "inicio";
                        private bool _emCima;

                        public override void OnMouseEnter() => _emCima = true;

                        public override void OnMouseLeave() => _emCima = false;

                        public override void OnClick(int button)
                        {
                            if (button == 1)
                                Game.Goto(Destino);
                        }

                        public override void OnDraw()
                        {
                            Draw.Rect(X, Y, 96, 24, _emCima ? "#3F7FC4" : "#2C3A55");
                            Draw.Text(Texto, X + 48, Y + 6, size: 12, color: "#ECEEF2", align: Align.Center);
                        }
                    """, sprite: false);
                // O tamanho do botão para o mouse, sem depender de um sprite.
                Ficha(root, name, new JsonObject { ["components"] = new JsonArray(new JsonObject { ["type"] = "Collider", ["width"] = 96, ["height"] = 24 }) }, sprite: false);
                return path;
            });
        yield return new("object", "placar", "Placar na tela", "Mostra os pontos no canto da tela, por cima de tudo e fora da luz (Draw GUI).", "Placar",
            (root, name) => Script(root, name, """
                    // Não desenha nada no mundo, só na tela.
                    public override void OnDraw() { }

                    public override void OnDrawGui()
                    {
                        var pontos = Game.Data.TryGetValue("pontos", out var p) && p is int n ? n : 0;
                        Draw.Rect(8, 8, 120, 22, "#0D0F13", alpha: 0.6f);
                        Draw.Text($"Pontos: {pontos}", 16, 11, size: 14, color: "#ECEEF2");
                    }
                """, sprite: false));
        yield return new("object", "porta", "Porta para outra sala", "Leva o Jogador para a sala do destino ao encostar. Cada porta da sala pode ter um destino.", "Porta",
            (root, name) => Script(root, name, """
                    // Mude por instância, na sala, com a propriedade destino.
                    public string Destino = "inicio";

                    public override void OnCollision(GameObject other)
                    {
                        if (other.Is("Jogador"))
                            Game.Goto(Destino);
                    }
                """));
        yield return new("object", "gerador", "Gerador", "Invisível. Cria um objeto no lugar dele a cada dois segundos, com um alarme.", "Gerador",
            (root, name) => Script(root, name, """
                    // O que criar e de quanto em quanto tempo. Mude por instância, na sala.
                    public string Objeto = "Estrela";
                    public float Intervalo = 2;

                    public override void OnCreate() => SetAlarm(0, Intervalo);

                    public override void OnDraw() { }

                    public override void OnAlarm(int index)
                    {
                        Game.Spawn(Objeto, X, Y);
                        SetAlarm(0, Intervalo);
                    }
                """, sprite: false));
        foreach (var ai in AiPresets())
            yield return new("object", "ia-" + ai.Id, "IA: " + ai.Title, ai.Description, ai.Id,
                (root, name) => Ficha(root, name, new JsonObject { ["layer"] = 5, ["y_sort"] = true, ["components"] = new JsonArray(ai.Ai.DeepClone()) }));
    }

    private sealed record AiPreset(string Id, string Title, string Description, JsonObject Ai);

    // A ficha já nasce com o componente AI ajustado para um papel comum.
    private static AiPreset[] AiPresets() =>
    [
        new("Guarda", "Guarda que patrulha", "Anda entre pontos e persegue o jogador quando vê. Edite os pontos em PatrolPoints.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "patrol", ["patrol_points"] = "64,64; 192,64", ["on_sight"] = "chase", ["sight_range"] = 120, ["speed"] = 40 }),
        new("Perseguidor", "Perseguidor", "Anda à toa e persegue o jogador quando vê, desviando das paredes.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "wander", ["on_sight"] = "chase", ["sight_range"] = 140, ["speed"] = 45 }),
        new("Sentinela", "Sentinela", "Fica parada até ver o jogador, aí persegue.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "idle", ["on_sight"] = "chase", ["sight_range"] = 160, ["speed"] = 50 }),
        new("Medroso", "Medroso que foge", "Anda à toa e foge do jogador quando ele chega perto.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "wander", ["on_sight"] = "flee", ["sight_range"] = 90, ["speed"] = 40, ["safe_distance"] = 150 }),
        new("Ajudante", "Ajudante que segue", "Acompanha o jogador de perto, sem atacar.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "follow", ["on_sight"] = "none", ["speed"] = 70, ["stop_distance"] = 16 }),
        new("Andarilho", "Andarilho", "Só anda à toa perto de onde nasceu, como um animal ou um morador.",
            new JsonObject { ["type"] = "AI", ["behavior"] = "wander", ["on_sight"] = "none", ["speed"] = 30, ["wander_radius"] = 80 }),
    ];

    // Um script de objeto com os membros dados. O sprite de mesmo nome, se houver, entra no construtor.
    private static string Script(string root, string name, string members, bool sprite = true)
    {
        var folder = GameFiles.FolderOf(root, "object");
        Directory.CreateDirectory(folder);
        var spriteName = sprite ? SameNameSprite(root, name) : null;
        var constructor = spriteName is null ? "" : $$"""
                public {{name}}()
                {
                    Sprite = "{{spriteName}}";
                }


            """;
        var path = Path.Combine(folder, name + ".cs");
        Write(path, $$"""
            using CanisMajor;

            namespace {{GameFiles.Namespace(root)}};

            public class {{name}} : GameObject
            {
            {{constructor}}{{Indent(members)}}
            }

            """);
        return path;
    }

    // A ficha (objects/Nome.json). Sem script, o objeto funciona só com ela.
    private static string Ficha(string root, string name, JsonObject def, bool sprite = true, string? preferSprite = null)
    {
        var folder = GameFiles.FolderOf(root, "object");
        Directory.CreateDirectory(folder);
        if (sprite && !def.ContainsKey("sprite"))
        {
            var chosen = SameNameSprite(root, name) ?? (preferSprite is null ? null : SameNameSprite(root, preferSprite));
            if (chosen is not null)
            {
                // O sprite vai primeiro, como o Inspetor escreve.
                var ordered = new JsonObject { ["sprite"] = chosen };
                foreach (var (key, value) in def.ToList())
                {
                    def.Remove(key);
                    ordered[key] = value;
                }
                def = ordered;
            }
        }
        var path = Path.Combine(folder, name + ".json");
        Write(path, def.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        return path;
    }

    private static string? SameNameSprite(string root, string name) =>
        GameFiles.List(root, "sprite").FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))?.Name;

    // ---------- sprites ----------

    private static IEnumerable<ResourceTemplate> Sprites()
    {
        // Como no GameMaker, o sprite novo nasce vazio: só muda o tamanho. A origem fica no canto de cima.
        yield return EmptySprite("pequeno", "Pequeno", 16, 16, "Bom para pixel art pequena: itens, tiles e personagens de 16 pixels.");
        yield return EmptySprite("medio", "Médio", 32, 32, "O tamanho padrão do GameMaker. Bom para personagens e objetos comuns.");
        yield return EmptySprite("grande", "Grande", 64, 64, "Para chefes, objetos grandes, retratos e telas de interface.");
        yield return new("sprite", "personalizado", "Personalizado", "Você escolhe a largura e a altura, como 16 por 24 ou 128 por 32.", "sprite",
            (root, name) => SaveEmptySprite(root, name, 32, 32))
        {
            AskSize = true,
            CreateSized = (root, name, w, h) => SaveEmptySprite(root, name, w, h),
        };
    }

    private static ResourceTemplate EmptySprite(string id, string title, int w, int h, string description) =>
        new("sprite", id, $"{title} ({w} por {h})", description, "sprite", (root, name) => SaveEmptySprite(root, name, w, h)) { Size = (w, h) };

    private static string SaveEmptySprite(string root, string name, int w, int h)
    {
        var folder = GameFiles.FolderOf(root, "sprite");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".png");
        Png.Save(path, w, h, new byte[w * h * 4]);
        GameFiles.SaveSpriteMeta(path, new SpriteMeta());
        return path;
    }

    // O tamanho digitado no Personalizado: "32x32", "16 x 24" ou "128 por 32". Null se não servir.
    public static (int Width, int Height)? ParseSize(string text)
    {
        var parts = System.Text.RegularExpressions.Regex.Split(text.Trim().ToLowerInvariant(), @"\s*(?:x|×|por|,)\s*");
        if (parts.Length != 2 || !int.TryParse(parts[0], out var w) || !int.TryParse(parts[1], out var h))
            return null;
        return w is >= 1 and <= 2048 && h is >= 1 and <= 2048 ? (w, h) : null;
    }

    private static void Put(byte[] p, int w, int x, int y, uint rgba)
    {
        var i = (y * w + x) * 4;
        p[i] = (byte)(rgba >> 24);
        p[i + 1] = (byte)(rgba >> 16);
        p[i + 2] = (byte)(rgba >> 8);
        p[i + 3] = (byte)rgba;
    }


    private static SpriteArt Bricks()
    {
        const int size = 16;
        var p = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var band = y / 8;
                var shifted = (x + (band % 2) * 4) % 8;
                var mortar = y % 8 == 7 || shifted == 7;
                var light = y % 8 == 0 && !mortar;
                Put(p, size, x, y, mortar ? 0x3A3F4AFFu : light ? 0xA8705AFFu : 0x8C5A44FFu);
            }
        return new SpriteArt(size, size, p, new SpriteMeta());
    }

    private static SpriteArt Grass()
    {
        const int size = 16;
        var p = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                uint c = y == 0 ? 0x7CCF5AFF
                    : y < 4 || (y == 4 && x % 3 == 0) ? 0x4CAF50FF
                    : (x * 7 + y * 3) % 11 == 0 ? 0x6B4528FF
                    : 0x8B5E3CFF;
                Put(p, size, x, y, c);
            }
        return new SpriteArt(size, size, p, new SpriteMeta());
    }

    // ---------- shaders ----------

    private static IEnumerable<ResourceTemplate> Shaders()
    {
        yield return new("shader", "vazio", "Shader vazio", "Desenha o sprite como ele é, com a função effect pronta para mudar.", "efeito",
            (root, name) => GameFiles.Create(root, "shader", name));
        foreach (var preset in ShaderPresets.All)
            yield return new("shader", preset.Name, preset.Title, $"Para {preset.Use}.", preset.Name, (root, name) =>
            {
                var folder = GameFiles.FolderOf(root, "shader");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, name + ".wgsl");
                Write(path, preset.Source);
                return path;
            });
    }

    // ---------- scripts ----------

    private static IEnumerable<ResourceTemplate> Scripts()
    {
        yield return new("script", "vazio", "Funções do jogo", "Uma classe estática para guardar funções que qualquer objeto chama, como Funcoes.Dobro(2).", "Funcoes",
            (root, name) => Code(root, name, $$"""
                // Funções que qualquer objeto pode chamar: {{name}}.Dobro(2).
                public static class {{name}}
                {
                    public static int Dobro(int valor) => valor * 2;
                }
                """));
        yield return new("script", "matematica", "Matemática do jogo", "Aproximar, Lerp, chance em porcento, sortear, distância e ângulo entre objetos.", "Mat",
            (root, name) => Code(root, name, $$"""
                // Contas que todo jogo usa: {{name}}.Chance(30), {{name}}.Angulo(this, jogador)...
                public static class {{name}}
                {
                    private static readonly Random Sorte = new();

                    /// <summary>Anda de atual até alvo, no máximo passo por vez.</summary>
                    public static float Aproximar(float atual, float alvo, float passo) =>
                        atual < alvo ? MathF.Min(atual + passo, alvo) : MathF.Max(atual - passo, alvo);

                    /// <summary>O ponto entre a e b: t 0 é a, t 1 é b.</summary>
                    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

                    /// <summary>Verdade em tantos porcento das vezes.</summary>
                    public static bool Chance(float porcento) => Sorte.NextSingle() * 100 < porcento;

                    /// <summary>Um número inteiro de min a max, incluindo os dois.</summary>
                    public static int Entre(int min, int max) => Sorte.Next(min, max + 1);

                    public static T Sortear<T>(params T[] opcoes) => opcoes[Sorte.Next(opcoes.Length)];

                    public static float Distancia(GameObject a, GameObject b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

                    /// <summary>Em graus, de um objeto para o outro: 0 é para a direita, 90 para baixo.</summary>
                    public static float Angulo(GameObject de, GameObject para) => MathF.Atan2(para.Y - de.Y, para.X - de.X) * 180f / MathF.PI;
                }
                """));
        yield return new("script", "salvar", "Salvar e carregar", "Guarda o Game.Data num arquivo e lê de volta, para continuar o jogo depois.", "Save",
            (root, name) => Code(root, name, $$"""
                // Guarda o Game.Data (pontos, vidas, fase...) e lê de volta: {{name}}.Salvar() e {{name}}.Carregar().
                public static class {{name}}
                {
                    private static string Arquivo => Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "{{GameFiles.Namespace(root)}}", "save.json");

                    public static bool Existe => File.Exists(Arquivo);

                    public static void Salvar()
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(Arquivo)!);
                        File.WriteAllText(Arquivo, JsonSerializer.Serialize(Game.Data));
                    }

                    public static bool Carregar()
                    {
                        if (!Existe)
                            return false;
                        var lido = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Arquivo)) ?? [];
                        foreach (var (chave, valor) in lido)
                            Game.Data[chave] = valor.ValueKind switch
                            {
                                JsonValueKind.Number => valor.TryGetInt32(out var inteiro) ? (object)inteiro : valor.GetDouble(),
                                JsonValueKind.String => valor.GetString(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                _ => null,
                            };
                        return true;
                    }

                    public static void Apagar()
                    {
                        if (Existe)
                            File.Delete(Arquivo);
                    }
                }
                """, usings: "using System.Text.Json;\n"));
        yield return new("script", "cronometro", "Cronômetro", "Conta o tempo dentro de um objeto: um tiro a cada meio segundo, um efeito que dura três.", "Cronometro",
            (root, name) => Code(root, name, $$"""
                // Use dentro de um objeto:
                //   private readonly {{name}} _tiro = new(0.5f);
                //   if (_tiro.Passou(dt)) Atirar();
                public sealed class {{name}}(float segundos)
                {
                    public float Segundos { get; set; } = segundos;
                    public float Restante { get; private set; } = segundos;

                    /// <summary>Conta o tempo. Devolve true quando chega a zero, e começa de novo.</summary>
                    public bool Passou(float dt)
                    {
                        Restante -= dt;
                        if (Restante > 0)
                            return false;
                        Restante += Segundos;
                        return true;
                    }

                    public void Reiniciar() => Restante = Segundos;
                }
                """));
        yield return new("script", "suavizar", "Suavizar movimento", "Curvas para animar sem tranco: entrada e saída suaves e elástico. t vai de 0 a 1.", "Curvas",
            (root, name) => Code(root, name, $$"""
                // Curvas de animação: X = Mat.Lerp(inicio, fim, {{name}}.Suave(t)), com t de 0 a 1.
                public static class {{name}}
                {
                    public static float EntradaSuave(float t) => t * t;

                    public static float SaidaSuave(float t) => 1 - (1 - t) * (1 - t);

                    public static float Suave(float t) => t * t * (3 - 2 * t);

                    public static float Elastico(float t) =>
                        t is <= 0 or >= 1 ? Math.Clamp(t, 0, 1) : MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * (2 * MathF.PI / 3)) + 1;
                }
                """));
    }

    // Um script em scripts/Nome.cs. É compilado junto com o jogo, como os objetos.
    private static string Code(string root, string name, string body, string usings = "")
    {
        var folder = GameFiles.FolderOf(root, "script");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name + ".cs");
        Write(path, $"{usings}using CanisMajor;\n\nnamespace {GameFiles.Namespace(root)};\n\n{body}\n");
        return path;
    }

    // ---------- tilesets ----------

    private static IEnumerable<ResourceTemplate> Tilesets()
    {
        yield return new("tileset", "vazio", "Tileset de um sprite", "Corta um sprite que já existe em tiles de 16. Escolha a imagem no Inspetor.", "tiles",
            (root, name) => GameFiles.Create(root, "tileset", name));
        yield return new("tileset", "exemplo", "Parede e chão prontos", "Cria o sprite com tijolo e grama e o tileset com os dois sólidos, para pintar salas na hora.", "tiles",
            (root, name) =>
            {
                var sprites = GameFiles.FolderOf(root, "sprite");
                Directory.CreateDirectory(sprites);
                var spriteName = name + "_img";
                var (bricks, grass) = (Bricks(), Grass());
                var p = new byte[32 * 16 * 4];
                for (var y = 0; y < 16; y++)
                {
                    Array.Copy(bricks.Rgba, y * 64, p, y * 128, 64);
                    Array.Copy(grass.Rgba, y * 64, p, y * 128 + 64, 64);
                }
                var png = Path.Combine(sprites, spriteName + ".png");
                Png.Save(png, 32, 16, p);
                GameFiles.SaveSpriteMeta(png, new SpriteMeta());
                var folder = GameFiles.FolderOf(root, "tileset");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, name + ".json");
                Write(path, JsonSerializer.Serialize(new TilesetData { Sprite = spriteName, Solid = [0, 1] }, GameProject.Json).Replace("\r\n", "\n") + "\n");
                return path;
            });
    }

    // ---------- ajudantes ----------

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // Os membros já vêm recuados em quatro espaços (pelo texto do modelo): só acerta as quebras de linha.
    private static string Indent(string members) => members.Replace("\r\n", "\n").TrimEnd();

    private static void Write(string path, string text) => File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
}
