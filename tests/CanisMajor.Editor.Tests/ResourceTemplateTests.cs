using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CanisEditor.Core;
using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

// Os modelos de recurso (o "Create Asset" do GameMaker): cada um cria arquivos que funcionam.
public class ResourceTemplateTests
{
    private static string NewGame(TempFolder temp) =>
        CanisMajor.Templates.CreateProject(Path.Combine(temp.Path, "Jogo"), "Jogo Legal", "exemplo",
            GameFiles.EngineRoot(AppContext.BaseDirectory) ?? throw new InvalidOperationException("Rode os testes dentro do repositório do Canis Major."));

    // Um nome que serve para qualquer tipo (classe C# ou arquivo) e não repete.
    private static string NameFor(ResourceTemplate t) =>
        "T" + char.ToUpperInvariant(t.Kind[0]) + string.Concat(t.Id.Where(char.IsAsciiLetterOrDigit));

    [Fact]
    public void Todo_tipo_tem_modelos_e_os_ids_nao_repetem()
    {
        foreach (var (kind, _, _, _) in ResourceTemplates.Kinds)
            Assert.NotEmpty(ResourceTemplates.For(kind));
        Assert.Equal(ResourceTemplates.All.Length, ResourceTemplates.All.Select(t => t.Kind + "/" + t.Id).Distinct().Count());
        Assert.True(ResourceTemplates.For("object").Count() >= 10);
        Assert.Contains(ResourceTemplates.For("object"), t => t.Title.StartsWith("IA:", StringComparison.Ordinal));
        Assert.Equal(CanisMajor.ShaderPresets.All.Length + 1, ResourceTemplates.For("shader").Count());
    }

    [Fact]
    public void Sprites_nascem_vazios_em_tres_tamanhos_e_um_personalizado()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp);
        var sizes = ResourceTemplates.For("sprite").Where(t => t.Size is not null).Select(t => t.Size!.Value).ToList();
        Assert.Equal([(16, 16), (32, 32), (64, 64)], sizes);
        foreach (var t in ResourceTemplates.For("sprite").Where(t => t.Size is not null))
        {
            var path = t.Create(root, NameFor(t));
            var (w, h, rgba) = CanisMajor.Png.Load(path);
            Assert.Equal(t.Size!.Value, (w, h));
            Assert.All(rgba, b => Assert.Equal(0, b)); // vazio e transparente
        }
        var custom = ResourceTemplates.Find("sprite", "personalizado");
        Assert.True(custom.AskSize);
        var tall = custom.CreateSized!(root, "Alto", 16, 24);
        var (cw, ch, _) = CanisMajor.Png.Load(tall);
        Assert.Equal((16, 24), (cw, ch));
    }

    [Theory]
    [InlineData("32x32", 32, 32)]
    [InlineData("16 x 24", 16, 24)]
    [InlineData("128 por 32", 128, 32)]
    [InlineData("64×48", 64, 48)]
    public void Tamanho_personalizado_aceita_varios_jeitos(string text, int w, int h) =>
        Assert.Equal((w, h), ResourceTemplates.ParseSize(text));

    [Theory]
    [InlineData("32")]
    [InlineData("0x16")]
    [InlineData("5000x5000")]
    [InlineData("abc")]
    public void Tamanho_personalizado_recusa_o_que_nao_serve(string text) => Assert.Null(ResourceTemplates.ParseSize(text));

    [Fact]
    public void Objetos_dos_modelos_aparecem_com_pai_sprite_e_componentes()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp);
        // Um sprite de mesmo nome entra sozinho no objeto novo.
        ResourceTemplates.Find("sprite", "pequeno").Create(root, "Moeda");
        var coin = ResourceTemplates.Find("object", "coletavel").Create(root, "Moeda");
        Assert.Contains("Sprite = \"Moeda\";", File.ReadAllText(coin));
        var button = ResourceTemplates.Find("object", "botao").Create(root, "Botao");
        var def = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(button, ".json")))!;
        Assert.Equal("Collider", def["components"]![0]!["type"]!.GetValue<string>());
        ResourceTemplates.Find("object", "ia-Guarda").Create(root, "Guarda");
        var objects = GameFiles.Objects(root);
        Assert.True(objects["Moeda"].Script);
        Assert.Equal("Moeda", objects["Moeda"].Sprite);
        Assert.False(objects["Guarda"].Script);
        // Um script não pode ter o nome de um objeto (seriam duas classes iguais).
        Assert.NotNull(GameFiles.CheckName(root, "script", "Moeda"));
        Assert.Null(GameFiles.CheckName(root, "script", "Ajuda"));
    }

    [Fact]
    public void Sala_de_menu_vem_com_o_codigo_da_sala()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp);
        var path = ResourceTemplates.Find("room", "menu").Create(root, "menu");
        var code = File.ReadAllText(Path.ChangeExtension(path, ".cs"));
        Assert.Contains("[Room(\"menu\")]", code);
        Assert.Contains("public class SalaMenu : RoomScript", code);
        Assert.Contains("private const string Proxima = \"inicio\";", code);
        Assert.Contains("Jogo Legal", code);
        Assert.Empty(BlockConverter.SyntaxErrors(code));
        Assert.True(BlockConverter.Parse(code)!.IsRoom);
        var room = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.Equal("#101420", room["background"]!.GetValue<string>());
    }

    // Cria todos os modelos num jogo só e compila o jogo de verdade: todo C# gerado precisa compilar.
    [Fact]
    public async Task Todos_os_modelos_juntos_compilam()
    {
        using var temp = new TempFolder();
        var root = NewGame(temp);
        foreach (var t in ResourceTemplates.All)
        {
            var name = t.Kind == "room" ? NameFor(t).ToLowerInvariant() : NameFor(t);
            Assert.Null(GameFiles.CheckName(root, t.Kind, name));
            var path = t.Create(root, name);
            Assert.True(File.Exists(path), $"{t.Kind}/{t.Id} não criou {path}");
            if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                Assert.Empty(BlockConverter.SyntaxErrors(File.ReadAllText(path)));
            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                Assert.NotNull(JsonNode.Parse(File.ReadAllText(path)));
            if (path.EndsWith(".wgsl", StringComparison.OrdinalIgnoreCase))
                Assert.Empty(CanisMajor.ShaderCheck.Check(File.ReadAllText(path)));
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var dotnet = dotnetRoot is not null && File.Exists(Path.Combine(dotnetRoot, "dotnet.exe")) ? Path.Combine(dotnetRoot, "dotnet.exe") : "dotnet";
        var csproj = Directory.GetFiles(root, "*.csproj").Single();
        // A engine já foi compilada junto com os testes: não compila de novo (e não briga com outros testes).
        var start = new ProcessStartInfo(dotnet, ["build", csproj, "-nologo", "-v", "q", "-p:BuildProjectReferences=false"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        using var process = Process.Start(start)!;
        var token = TestContext.Current.CancellationToken;
        var output = process.StandardOutput.ReadToEndAsync(token);
        var errors = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromMinutes(4), token);
        Assert.True(process.ExitCode == 0, await output + await errors);
    }

    private static MainWindow Open(TempFolder settings, string folder)
    {
        var window = new MainWindow(new AppSettings { CheckForUpdates = false }, Path.Combine(settings.Path, "settings.json"), folder);
        window.Language.IsInstalled = _ => false;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Click(MainWindow window, string tag)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag as string == tag);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Janela_de_modelos_troca_de_tipo_e_cria_pelo_cartao()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);

        window.ShowTemplatePicker("sprite");
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsTemplatePickerOpen);
        var cards = window.GetVisualDescendants().OfType<Button>().Count(b => (b.Tag as string)?.StartsWith("tpl.", StringComparison.Ordinal) == true);
        Assert.Equal(ResourceTemplates.For("sprite").Count(), cards);

        // Trocar o tipo à esquerda troca os cartões.
        Click(window, "tk.object");
        Assert.Equal("object", window.TemplateKind);
        Click(window, "tpl.object.plataforma");
        Assert.False(window.IsTemplatePickerOpen);
        Assert.True(window.IsDialogOpen);
        window.FindControl<TextBox>("DialogInput")!.Text = "Heroi";
        window.AnswerDialog(primary: true);
        Dispatcher.UIThread.RunJobs();

        var path = Path.Combine(root, "objects", "Heroi.cs");
        Assert.True(File.Exists(path));
        Assert.Equal(path, window.ActiveDocument!.FullPath);
        Assert.Contains("Gravidade", File.ReadAllText(path));
        Assert.Contains(window.TreeRows, r => r.Name == "Heroi");

        // Script novo vai para scripts/ e aparece na lista.
        window.ShowTemplatePicker("script");
        Dispatcher.UIThread.RunJobs();
        Click(window, "tpl.script.matematica");
        window.FindControl<TextBox>("DialogInput")!.Text = "Mat";
        window.AnswerDialog(primary: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(File.Exists(Path.Combine(root, "scripts", "Mat.cs")));
        Assert.Contains(window.TreeRows, r => r.Name == "Mat");

        // O sprite personalizado pergunta o nome e depois o tamanho.
        window.ShowTemplatePicker("sprite");
        Dispatcher.UIThread.RunJobs();
        Click(window, "tpl.sprite.personalizado");
        window.FindControl<TextBox>("DialogInput")!.Text = "faixa";
        window.AnswerDialog(primary: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsDialogOpen);
        window.FindControl<TextBox>("DialogInput")!.Text = "128x32";
        window.AnswerDialog(primary: true);
        Dispatcher.UIThread.RunJobs();
        var (w, h, _) = CanisMajor.Png.Load(Path.Combine(root, "sprites", "faixa.png"));
        Assert.Equal((128, 32), (w, h));
        window.Close();
    }
}
