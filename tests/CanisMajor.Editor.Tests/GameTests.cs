using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CanisEditor.Core;
using CanisEditor.Game;

namespace CanisEditor.Tests;

// O editor de jogos: projeto, árvore de recursos, sala, sprite, shaders e criação.
public class GameTests
{
    private static string EngineRoot() =>
        GameFiles.EngineRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Rode os testes dentro do repositório do Canis Major.");

    // Um jogo de exemplo novo, numa pasta temporária.
    private static string NewGame(TempFolder temp, string template = "exemplo") =>
        CanisMajor.Templates.CreateProject(Path.Combine(temp.Path, "Jogo"), "Jogo Legal", template, EngineRoot());

    private static MainWindow Open(TempFolder settings, string folder)
    {
        var window = new MainWindow(new AppSettings { CheckForUpdates = false }, Path.Combine(settings.Path, "settings.json"), folder);
        window.Language.IsInstalled = _ => false;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void TypeInDialog(MainWindow window, string text)
    {
        Assert.True(window.IsDialogOpen);
        window.FindControl<TextBox>("DialogInput")!.Text = text;
        window.AnswerDialog(primary: true);
        Dispatcher.UIThread.RunJobs();
    }

    // Arquivos do jogo

    [Fact]
    public void Le_os_objetos_com_sprite_e_camada()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp);

        var objects = GameFiles.Objects(root);

        Assert.Equal("jogador", objects["Jogador"].Sprite);
        Assert.Equal(10, objects["Jogador"].Layer);
        Assert.Equal("estrela", objects["Estrela"].Sprite);
        Assert.Null(objects["Placar"].Sprite);
        Assert.Equal("JogoLegal", GameFiles.Namespace(root));
    }

    [Fact]
    public void Sala_guarda_o_que_nao_conhece_e_poe_uma_instancia_por_linha()
    {
        const string text = """
            {
              "width": 320, "height": 180, "music": "tema",
              "instances": [ { "object": "A", "x": 1, "y": 2.5, "speed": 3 } ]
            }
            """;

        var room = RoomModel.Parse(text);
        room.Instances.Add(new RoomInstance { Object = "Bloco", X = 16, Y = 32 });
        var saved = room.Serialize();

        Assert.Contains("\"music\": \"tema\"", saved);
        Assert.Contains("    { \"object\": \"A\", \"x\": 1, \"y\": 2.5, \"speed\": 3 },", saved);
        Assert.Contains("    { \"object\": \"Bloco\", \"x\": 16, \"y\": 32 }\n", saved);
        var again = RoomModel.Parse(saved);
        Assert.Equal(320, again.Width);
        Assert.Equal(2, again.Instances.Count);
        Assert.Equal(3, again.Instances[0].Extra["speed"]!.GetValue<int>());
    }

    [Fact]
    public void Nome_de_recurso_precisa_servir()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp);

        Assert.NotNull(GameFiles.CheckName(root, "object", "2Inimigo"));
        Assert.NotNull(GameFiles.CheckName(root, "object", "Jogador"));
        Assert.NotNull(GameFiles.CheckName(root, "room", "inicio"));
        Assert.NotNull(GameFiles.CheckName(root, "sprite", "com espaço"));
        Assert.Null(GameFiles.CheckName(root, "object", "Inimigo"));
        Assert.Null(GameFiles.CheckName(root, "room", "fase-2"));
    }

    [Fact]
    public void Cria_cada_tipo_de_recurso()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp, "vazio");

        var room = GameFiles.Create(root, "room", "fase2");
        var sprite = GameFiles.Create(root, "sprite", "inimigo");
        var obj = GameFiles.Create(root, "object", "Inimigo");
        var shader = GameFiles.Create(root, "shader", "brilho");

        Assert.Equal(480, RoomModel.Parse(File.ReadAllText(room)).Width);
        Assert.True(File.Exists(Path.ChangeExtension(sprite, ".json")));
        // O objeto novo já usa o sprite de mesmo nome.
        Assert.Contains("Sprite = \"inimigo\";", File.ReadAllText(obj));
        Assert.Equal("inimigo", GameFiles.Objects(root)["Inimigo"].Sprite);
        Assert.Contains("fn effect", File.ReadAllText(shader));
        Assert.Empty(CanisMajor.ShaderCheck.Check(File.ReadAllText(shader)));
    }

    [Fact]
    public void Executar_um_jogo_roda_o_projeto_dele()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp, "vazio");

        Assert.Equal("dotnet run --project \"JogoLegal.csproj\"", RunDetector.Detect(root, null));
    }

    [Fact]
    public void O_dotnet_do_DOTNET_ROOT_passa_na_frente()
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Path, "dotnet.exe"), "");

        var path = ToolPaths.WithDotNetRoot(@"C:\Program Files\dotnet;C:\Windows;" + temp.Path, temp.Path);

        Assert.Equal(temp.Path, path.Split(';')[0]);
        Assert.Single(path.Split(';'), part => part == temp.Path);
        Assert.Equal("C:\\x", ToolPaths.WithDotNetRoot("C:\\x", Path.Combine(temp.Path, "nao-existe")));
    }

    // A janela

    [AvaloniaFact]
    public void Projeto_de_jogo_abre_na_arvore_de_recursos()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);

        var window = Open(settings, root);

        Assert.True(window.IsGameProject);
        Assert.True(window.IsGameView);
        Assert.Equal(["Salas", "inicio", "Objetos", "Cubo", "Estrela", "Jogador", "Parede", "Placar"], window.TreeRows.Take(8).Select(r => r.Name));
        Assert.Contains(window.TreeRows, r => r.Name == "Sons");

        window.SetGameView(false);
        Assert.Contains(window.TreeRows, r => r.Name == "projeto.json");
        window.Close();
    }

    [AvaloniaFact]
    public void Pasta_comum_continua_com_a_arvore_de_arquivos()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        temp.File("a.txt", "a");

        var window = Open(settings, temp.Path);

        Assert.False(window.IsGameProject);
        Assert.Equal(["a.txt"], window.TreeRows.Select(r => r.Name));
        window.Close();
    }

    [AvaloniaFact]
    public void Sala_abre_desenhada_e_colocar_um_objeto_muda_o_texto()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);

        window.OpenFile(Path.Combine(root, "rooms", "inicio.json"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsRoomShown);
        Assert.False(window.EditorVisible);
        var before = window.CurrentRoom!.Instances.Count;

        window.SetRoomBrush("Estrela");
        // A estrela tem 8 pixels e origem no meio: o canto cai na célula (32, 32) e a posição é a origem.
        window.RoomCanvasControl.Place(new Point(40, 40));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before + 1, window.CurrentRoom!.Instances.Count);
        Assert.True(window.ActiveDocument!.IsDirty);
        Assert.Contains("{ \"object\": \"Estrela\", \"x\": 36, \"y\": 36 }", window.ActiveDocument.Document.Text);

        // Ctrl Z desfaz pelo documento, e a sala acompanha.
        window.ActiveDocument.Document.UndoStack.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(before, window.CurrentRoom!.Instances.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void Botao_direito_apaga_e_o_codigo_mostra_o_json()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);
        window.OpenFile(Path.Combine(root, "rooms", "inicio.json"));
        Dispatcher.UIThread.RunJobs();
        var canvas = window.RoomCanvasControl;
        var player = window.CurrentRoom!.Instances.FindIndex(i => i.Object == "Jogador");

        canvas.DeleteAt(player);
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain("Jogador", window.ActiveDocument!.Document.Text);
        window.ToggleRoomCode();
        Assert.True(window.EditorVisible);
        Assert.False(window.IsRoomShown);
        window.ToggleRoomCode();
        Assert.True(window.IsRoomShown);
        window.Close();
    }

    [AvaloniaFact]
    public void Sala_com_erro_no_texto_explica_em_vez_de_desenhar()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);
        window.OpenFile(Path.Combine(root, "rooms", "inicio.json"));
        Dispatcher.UIThread.RunJobs();

        window.ActiveDocument!.Document.Text = "{ \"width\": 480, ";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("tem um erro", window.GameMessageText);
        window.ActiveDocument.Document.UndoStack.Undo();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsRoomShown);
        window.Close();
    }

    [AvaloniaFact]
    public void Sprite_mostra_os_quadros_e_grava_a_origem()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);
        var png = Path.Combine(root, "sprites", "estrela.png");

        window.OpenFile(png);
        Dispatcher.UIThread.RunJobs();

        Assert.True(window.IsSpriteShown);
        Assert.Equal(4, window.SpriteViewControl.Frames);
        Assert.Equal(8, window.SpriteViewControl.FrameWidth);

        window.SetSpriteOrigin(0, 0);
        window.SetSpriteFrames(2);

        var meta = GameFiles.LoadSpriteMeta(png);
        Assert.Equal([0f, 0f], meta.Origin);
        Assert.Equal(2, meta.Frames);
        Assert.Equal(16, window.SpriteViewControl.FrameWidth);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Shader_com_erro_marca_a_linha()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var shader = Path.Combine(root, "shaders", "quebrado.wgsl");
        File.WriteAllText(shader, "fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {\n    return sample(uv) * colr;\n}\n");
        var window = Open(settings, root);

        window.OpenFile(shader);
        await window.ShaderChecked;
        Dispatcher.UIThread.RunJobs();

        var errors = window.Language.DiagnosticsFor(shader);
        Assert.Single(errors);
        Assert.Equal(1, errors[0].Line);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Criar_objeto_pelo_mais_abre_a_classe_nova()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);

        var creating = window.CreateResourceAsync("object");
        Dispatcher.UIThread.RunJobs();
        TypeInDialog(window, "Inimigo");
        await creating;

        var path = Path.Combine(root, "objects", "Inimigo.cs");
        Assert.Equal(path, window.ActiveDocument!.FullPath);
        Assert.Contains("public class Inimigo : GameObject", File.ReadAllText(path));
        Assert.Contains(window.TreeRows, r => r.Name == "Inimigo");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Novo_jogo_cria_a_pasta_e_abre_a_sala()
    {
        using var parent = new TempFolder();
        using var settings = new TempFolder();
        var window = Open(settings, parent.Path);
        window.PickParentFolder = () => Task.FromResult<string?>(parent.Path);

        var creating = window.NewGameAsync();
        Dispatcher.UIThread.RunJobs();
        TypeInDialog(window, "Fuga da Nave");
        window.AnswerDialog(primary: false);
        Dispatcher.UIThread.RunJobs();
        await creating;

        var root = Path.Combine(parent.Path, "Fuga da Nave");
        Assert.Equal(root, window.RootFolder);
        Assert.True(window.IsGameProject);
        Assert.True(File.Exists(Path.Combine(root, "FugaDaNave.csproj")));
        Assert.Equal(Path.Combine(root, "rooms", "inicio.json"), window.ActiveDocument!.FullPath);
        Assert.True(window.IsRoomShown);
        window.Close();
    }
}

