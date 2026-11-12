using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CanisEditor.Core;
using CanisEditor.Game;
using Xunit;

namespace CanisEditor.Tests;

public class BlockWindowTests
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

    [AvaloniaFact]
    public void Script_vira_blocos_e_o_bloco_novo_vira_csharp()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var window = Open(settings, root);
        window.OpenFile(Path.Combine(root, "objects", "Jogador.cs"));
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.ShowsBlocks); // o script abre no código

        window.ToggleBlocks();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ShowsBlocks);
        var script = window.BlockViewControl.Script;
        Assert.NotNull(script);
        Assert.NotNull(script!.Find("OnStep"));

        // Clicar no bloco "destruir" da paleta põe Destroy(); no fim de "A cada quadro".
        var before = window.ActiveText;
        window.InsertPaletteBlock("destruir");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains("Destroy();", window.ActiveText);
        Assert.Empty(BlockConverter.SyntaxErrors(window.ActiveText));
        Assert.NotEqual(before, window.ActiveText);

        // De volta ao código, o texto é o mesmo que os blocos escreveram.
        window.ToggleBlocks();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.ShowsBlocks);
        Assert.Contains("Destroy();", window.ActiveText);
        window.Close();
    }

    [AvaloniaFact]
    public void Codigo_com_erro_nao_vira_blocos()
    {
        using var temp = new TempFolder();
        using var settings = new TempFolder();
        var root = NewGame(temp);
        var path = Path.Combine(root, "objects", "Quebrado.cs");
        File.WriteAllText(path, "public class Quebrado : CanisMajor.GameObject { public override void OnStep(float dt) { X += ; } }");
        var window = Open(settings, root);
        window.OpenFile(path);
        Dispatcher.UIThread.RunJobs();
        window.ToggleBlocks();
        Dispatcher.UIThread.RunJobs();
        Assert.Null(window.BlockViewControl.Script);
        // Nada foi reescrito.
        Assert.Contains("X += ;", window.ActiveText);
        window.Close();
    }
}
