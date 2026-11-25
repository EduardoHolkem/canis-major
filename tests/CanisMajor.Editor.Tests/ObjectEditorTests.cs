using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CanisEditor.Core;
using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

// O Editor de Objetos: eventos (adicionar, abrir, herdar, tirar) e variáveis definidas.
public class ObjectEditorTests
{
    private const string Script = """
        using CanisMajor;

        public class Bola : GameObject
        {
            private int vida = 3;
            public string Nome { get; set; } = "";

            public override void OnCreate()
            {
                vida = 1;
            }

            // anda
            public override void OnStep(float dt)
            {
                X += 1;
            }
        }
        """;

    [Fact]
    public void Adicionar_e_tirar_evento_mexe_so_no_metodo()
    {
        var added = BlockConverter.AddEvent(Script, "OnAlarm");
        Assert.Empty(BlockConverter.SyntaxErrors(added));
        Assert.Contains("public override void OnAlarm(int index)", added);
        Assert.Equal(added, BlockConverter.AddEvent(added, "OnAlarm")); // não repete

        var removed = BlockConverter.RemoveEvent(Script, "OnStep");
        Assert.Empty(BlockConverter.SyntaxErrors(removed));
        Assert.DoesNotContain("OnStep", removed);
        Assert.DoesNotContain("// anda", removed); // o comentário do método sai junto
        Assert.Contains("vida = 1;", removed);

        var first = BlockConverter.RemoveEvent(Script, "OnCreate");
        Assert.DoesNotContain("OnCreate", first);
        Assert.Contains("public override void OnStep(float dt)", first);
    }

    [Fact]
    public void Linha_do_evento_e_membros_do_script()
    {
        var line = BlockConverter.EventLine(Script, "OnStep")!.Value;
        Assert.Equal("        X += 1;", Script.Replace("\r\n", "\n").Split('\n')[line - 1]);
        Assert.Null(BlockConverter.EventLine(Script, "OnDraw"));
        Assert.Equal(["vida", "Nome"], BlockConverter.MemberNames(Script).Order().Reverse().ToList());
        var withField = BlockConverter.AddMember(Script, "public float velocidade = 2f;", "do editor");
        Assert.Empty(BlockConverter.SyntaxErrors(withField));
        Assert.Contains("    // do editor\n    public float velocidade = 2f;\n", withField.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData("3,5", "number", 3.5)]
    [InlineData("abc", "number", 0.0)]
    [InlineData("7", "integer", 7.0)]
    public void Valor_digitado_vira_o_tipo_da_variavel(string text, string type, double expected) =>
        Assert.Equal(expected, double.Parse(MainWindow.ParseVariableValue(text, type)!.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Sim_e_nao_e_texto()
    {
        Assert.True(MainWindow.ParseVariableValue("sim", "bool")!.GetValue<bool>());
        Assert.False(MainWindow.ParseVariableValue("não", "bool")!.GetValue<bool>());
        Assert.Equal(" oi ", MainWindow.ParseVariableValue(" oi ", "text")!.GetValue<string>());
    }

    private static string NewGame(TempFolder temp) =>
        CanisMajor.Templates.CreateProject(Path.Combine(temp.Path, "Jogo"), "Jogo Legal", "exemplo",
            GameFiles.EngineRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Rode os testes dentro do repositório do Canis Major."));

    private static MainWindow Open(TempFolder settings, string folder)
    {
        var window = new MainWindow(new AppSettings { CheckForUpdates = false }, Path.Combine(settings.Path, "settings.json"), folder);
        window.Language.IsInstalled = _ => false;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Find<T>(MainWindow window, string tag) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Tag as string == tag);

    [AvaloniaFact]
    public void Evento_novo_cria_o_script_e_abre_no_metodo()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var ficha = Path.Combine(root, "objects", "Moeda.json");
        File.WriteAllText(ficha, "{}");
        var window = Open(settings, root);
        window.OpenFile(ficha);
        Dispatcher.UIThread.RunJobs();

        // O + Adicionar evento lista os eventos; escolher um cria o script com o método.
        var add = Find<ComboBox>(window, "oe.addevent");
        add.SelectedIndex = ObjectScript.KnownEvents.ToList().FindIndex(k => k.Name == "OnCollision");
        Dispatcher.UIThread.RunJobs();
        var script = Path.Combine(root, "objects", "Moeda.cs");
        var code = File.ReadAllText(script);
        Assert.Contains("public class Moeda : GameObject", code);
        Assert.Contains("public override void OnCollision(GameObject other)", code);
        Assert.Empty(BlockConverter.SyntaxErrors(code));
        Assert.Equal(["OnCollision"], window.ObjectEvents("Moeda"));
        Assert.NotNull(Find<Button>(window, "oe.code.OnCollision"));

        // Código abre o script com o cursor dentro do evento.
        window.OpenObjectEvent("Moeda", "OnCollision", blocks: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(script, window.ActiveDocument!.FullPath);
        Assert.False(window.ShowsBlocks);
        window.OpenObjectEvent("Moeda", "OnCollision", blocks: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ShowsBlocks);

        window.RemoveObjectEvent("Moeda", "OnCollision");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.ObjectEvents("Moeda"));
        window.Close();
    }

    [AvaloniaFact]
    public void Evento_do_pai_aparece_herdado_e_pode_mudar_no_filho()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        File.WriteAllText(Path.Combine(root, "objects", "Inimigo.cs"), "using CanisMajor;\n\npublic class Inimigo : GameObject\n{\n    public override void OnStep(float dt)\n    {\n        X += 1;\n    }\n}\n");
        var ficha = Path.Combine(root, "objects", "Morcego.json");
        File.WriteAllText(ficha, "{ \"parent\": \"Inimigo\" }");
        var window = Open(settings, root);
        window.OpenFile(ficha);
        Dispatcher.UIThread.RunJobs();

        var inherit = Find<Button>(window, "oe.inherit.OnStep");
        inherit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var code = File.ReadAllText(Path.Combine(root, "objects", "Morcego.cs"));
        Assert.Contains("public class Morcego : Inimigo", code);
        Assert.Contains("base.OnStep(dt);", code);
        Assert.Empty(BlockConverter.SyntaxErrors(code));
        window.Close();
    }

    [AvaloniaFact]
    public void Variavel_definida_no_objeto_aparece_na_instancia_da_sala()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);
        window.OpenFile(Path.Combine(root, "objects", "Jogador.cs"));
        Dispatcher.UIThread.RunJobs();
        window.ToggleRoomCode(); // a ficha do Jogador
        Dispatcher.UIThread.RunJobs();

        Find<Button>(window, "oe.addvar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var value = Find<TextBox>(window, "oe.var.value.0");
        value.Text = "5";
        value.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        var def = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "objects", "Jogador.json")))!;
        Assert.Equal("variavel", def["variables"]![0]!["name"]!.GetValue<string>());
        Assert.Equal(5, def["variables"]![0]!["value"]!.GetValue<double>());

        // Criar campo escreve a variável no script.
        Find<Button>(window, "oe.var.field.0").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("public float variavel = 5f;", window.ActiveText);

        // Na sala, a instância do Jogador mostra a variável e grava o valor dela.
        window.OpenFile(Path.Combine(root, "rooms", "inicio.json"));
        Dispatcher.UIThread.RunJobs();
        var room = window.RoomCanvasControl;
        var index = room.Model!.Instances.FindIndex(i => i.Object == "Jogador");
        room.Select(index);
        Dispatcher.UIThread.RunJobs();
        var field = Find<TextBox>(window, "i.var.variavel");
        field.Text = "9";
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("\"variavel\": 9", window.ActiveText);
        window.Close();
    }
}
