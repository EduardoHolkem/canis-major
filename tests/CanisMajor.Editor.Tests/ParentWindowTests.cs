using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CanisEditor.Core;
using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

// Objetos pais e código da sala, no editor.
public class ParentWindowTests
{
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

    [Fact]
    public void Pais_vem_da_ficha_e_da_classe_e_passam_o_sprite()
    {
        using var temp = new TempFolder();
        var root = Path.Combine(temp.Path, "Jogo");
        Directory.CreateDirectory(Path.Combine(root, "objects"));
        Directory.CreateDirectory(Path.Combine(root, "sprites"));
        File.WriteAllText(Path.Combine(root, "projeto.json"), "{}");
        File.WriteAllText(Path.Combine(root, "objects", "Inimigo.cs"), "public class Inimigo : GameObject { public Inimigo() { Sprite = \"mau\"; Layer = 5; } }");
        File.WriteAllText(Path.Combine(root, "objects", "Morcego.cs"), "public sealed class Morcego : Inimigo { }");
        File.WriteAllText(Path.Combine(root, "objects", "Criatura.cs"), "public abstract class Criatura : CanisMajor.GameObject { }\npublic class Sapo : Criatura { }");
        File.WriteAllText(Path.Combine(root, "objects", "Vampiro.json"), "{ \"parent\": \"Morcego\" }");
        File.WriteAllText(Path.Combine(root, "objects", "Lista.cs"), "public class Lista : List<int> { }");

        var objects = GameFiles.Objects(root);
        Assert.Equal("Inimigo", objects["Morcego"].Parent);
        Assert.Equal("mau", objects["Morcego"].Sprite); // do pai
        Assert.Equal(5, objects["Morcego"].Layer);
        Assert.True(objects["Morcego"].Script);
        Assert.Equal("Morcego", objects["Vampiro"].Parent);
        Assert.Equal("mau", objects["Vampiro"].Sprite); // do avô
        Assert.False(objects["Vampiro"].Script);
        Assert.Equal("Criatura", objects["Sapo"].Parent);
        Assert.False(objects.ContainsKey("Criatura")); // abstrata não vai para a sala
        Assert.False(objects.ContainsKey("Lista")); // não é objeto do jogo
    }

    [Theory]
    [InlineData("noite", "SalaNoite")]
    [InlineData("fase-1", "SalaFase1")]
    [InlineData("2 andar", "Sala2Andar")]
    public void Nome_da_classe_da_sala(string room, string expected) => Assert.Equal(expected, GameFiles.RoomClassName(room));

    [AvaloniaFact]
    public void Escolher_o_pai_grava_a_ficha_e_muda_a_classe_do_script()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        File.WriteAllText(Path.Combine(root, "objects", "Inimigo.cs"), "using CanisMajor;\n\npublic class Inimigo : GameObject\n{\n    public Inimigo() { Sprite = \"estrela\"; }\n}\n");
        var morcego = Path.Combine(root, "objects", "Morcego.cs");
        File.WriteAllText(morcego, "using CanisMajor;\n\npublic class Morcego : GameObject\n{\n}\n");
        var window = Open(settings, root);
        window.OpenFile(morcego);
        Dispatcher.UIThread.RunJobs();
        window.ToggleRoomCode(); // mostra a ficha
        Dispatcher.UIThread.RunJobs();

        var picker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Tag as string == "o.parent");
        picker.SelectedItem = "Inimigo";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("\"parent\": \"Inimigo\"", File.ReadAllText(Path.Combine(root, "objects", "Morcego.json")));
        window.ToggleRoomCode(); // volta ao script
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("public class Morcego : Inimigo", window.ActiveText);
        Assert.Equal("estrela", GameFiles.Objects(root)["Morcego"].Sprite);

        // Tirar o pai volta para GameObject.
        window.ToggleRoomCode();
        Dispatcher.UIThread.RunJobs();
        picker = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Tag as string == "o.parent");
        picker.SelectedItem = "(nenhum)";
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain("parent", File.ReadAllText(Path.Combine(root, "objects", "Morcego.json")));
        window.ToggleRoomCode();
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("public class Morcego : GameObject", window.ActiveText);
        window.Close();
    }

    [AvaloniaFact]
    public void Codigo_da_sala_e_criado_e_abre_em_blocos()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);
        window.OpenFile(Path.Combine(root, "rooms", "inicio.json"));
        Dispatcher.UIThread.RunJobs();

        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Código da sala");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        var path = Path.Combine(root, "rooms", "inicio.cs");
        Assert.True(File.Exists(path));
        var code = File.ReadAllText(path);
        Assert.Contains("[Room(\"inicio\")]", code);
        Assert.Contains("public class SalaInicio : RoomScript", code);
        Assert.Empty(BlockConverter.SyntaxErrors(code));

        // O código da sala também vira blocos, com os eventos da sala.
        window.ToggleBlocks();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ShowsBlocks);
        var script = window.BlockViewControl.Script!;
        Assert.True(script.IsRoom);
        Assert.NotNull(script.Find("OnCreate"));
        window.InsertPaletteBlock("tocar som");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Game.PlaySound(\"coleta\");", window.ActiveText);
        window.Close();
    }
}
