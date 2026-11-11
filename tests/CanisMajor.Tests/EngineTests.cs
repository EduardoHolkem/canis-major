using System.Buffers.Binary;
using System.Diagnostics;
using CanisMajor;
using Xunit;

namespace CanisMajor.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(Environment.GetEnvironmentVariable("CM_TEST_TMP") ?? System.IO.Path.GetTempPath(), "cm-" + Guid.NewGuid().ToString("N")[..8]);
    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

internal static class Repo
{
    public static string Root
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(System.IO.Path.Combine(d.FullName, "core", "Cargo.toml"))) return d.FullName;
            throw new InvalidOperationException("raiz do repositório não encontrada");
        }
    }

    public static (int W, int H) PngSize(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, b[..4]);
        return ((int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16)), (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20)));
    }
}

public class ColorTests
{
    [Fact]
    public void ParsesHex()
    {
        Assert.Equal(new Color(1, 0, 0), Color.Hex("#FF0000"));
        Assert.Equal(new Color(1, 1, 1), Color.Hex("fff"));
        Assert.Equal(128 / 255f, Color.Hex("#00000080").A, 4);
        Color fromText = "#9DB8E0";
        Assert.Equal("#9DB8E0", fromText.ToString());
        Assert.Throws<FormatException>(() => Color.Hex("#12"));
    }
}

public class TemplateTests
{
    [Theory]
    [InlineData("Meu jogo", "MeuJogo")]
    [InlineData("ação-rápida", "AcaoRapida")]
    [InlineData("2 jogadores", "Jogo2Jogadores")]
    public void Namespaces(string name, string expected) => Assert.Equal(expected, Templates.Namespace(name));

    [Fact]
    public void ExampleProjectRoundTrip()
    {
        using var tmp = new TempDir();
        var root = Templates.CreateProject(tmp.Path, "Teste", "exemplo", Repo.Root);
        var project = GameProject.Load(root);
        Assert.Equal("Teste", project.Info.Name);
        var room = project.LoadRoom("inicio");
        Assert.Equal(["vinheta"], room.PostShaders);
        Assert.Contains(room.Instances, i => i["object"]!.GetValue<string>() == "Jogador");
        Assert.Equal(4, GameProject.LoadSpriteMeta(Path.Combine(root, "sprites", "estrela.png")).Frames);
        Assert.Equal((32, 8), Repo.PngSize(Path.Combine(root, "sprites", "estrela.png")));
        Assert.True(File.Exists(Path.Combine(root, "objects", "Estrela.cs")));
        Assert.Contains("namespace Teste;", File.ReadAllText(Path.Combine(root, "objects", "Jogador.cs")));
        Assert.Contains(Repo.Root, File.ReadAllText(Path.Combine(root, "Teste.csproj")));
    }

    [Fact]
    public void RefusesNonEmptyFolder()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Path);
        File.WriteAllText(Path.Combine(tmp.Path, "x.txt"), "x");
        Assert.Throws<IOException>(() => Templates.CreateProject(tmp.Path, "X", "vazio", Repo.Root));
    }
}

[Collection("Runtime")]
public class CollisionTests
{
    private class Caixa : GameObject
    {
        public Caixa() { Sprite = "caixa"; }
    }

    [Fact]
    public void MoveStopsAtWall()
    {
        Runtime.SpriteOverride = _ => new SpriteInfo(1, 16, 16, 1, 0, 0, 8);
        try
        {
            var jogador = new Caixa { Kind = "Jogador", X = 0 };
            var parede = new Caixa { Kind = "Parede", X = 40 };
            Runtime.Instances = [jogador, parede];
            var (hitX, hitY) = jogador.Move(30, 0, "Parede");
            Assert.True(hitX);
            Assert.False(hitY);
            Assert.Equal(24, jogador.X); // encostou: 24 + 16 = 40
            Assert.Same(parede, jogador.Collide("parede", 1));
            Assert.Null(jogador.Collide("Parede", -1));
        }
        finally
        {
            Runtime.SpriteOverride = null;
            Runtime.Instances = [];
        }
    }
}

public class GameRunTests
{
    [Fact]
    public void ExampleRunsAndDraws()
    {
        var exe = Path.Combine(Repo.Root, "examples", "Exemplo", "bin", "Release", "net10.0", "Exemplo.exe");
        Assert.SkipUnless(File.Exists(exe), "Compile o exemplo antes: dotnet build examples/Exemplo -c Release");
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Path);
        var shot = Path.Combine(tmp.Path, "quadro.png");
        var psi = new ProcessStartInfo(exe, ["--frames", "10", "--screenshot", shot])
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "o jogo não fechou sozinho");
        Assert.True(p.ExitCode == 0, err);
        Assert.Equal((480, 270), Repo.PngSize(shot));
    }

    [Fact]
    public void ShaderErrorPointsAtUserLine()
    {
        var exe = Path.Combine(Repo.Root, "examples", "Exemplo", "bin", "Release", "net10.0", "Exemplo.exe");
        Assert.SkipUnless(File.Exists(exe), "Compile o exemplo antes: dotnet build examples/Exemplo -c Release");
        using var tmp = new TempDir();
        var root = Templates.CreateProject(tmp.Path, "Quebrado", "exemplo", Repo.Root);
        File.WriteAllText(Path.Combine(root, "shaders", "vinheta.wgsl"),
            "fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {\n    let x = 1.0;\n    return cor;\n}\n");
        var psi = new ProcessStartInfo(exe, [root, "--frames", "5"]) { RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit(60_000);
        Assert.Equal(1, p.ExitCode);
        Assert.Contains("vinheta.wgsl:3:", err);
    }

    [Fact]
    public void ExportPlanPublishesTheGameAlone()
    {
        using var tmp = new TempDir();
        var root = Templates.CreateProject(tmp.Path, "Fuga da Nave", "vazio", Repo.Root);

        var plan = GameExport.Plan(root);

        Assert.Equal(Path.Combine(root, "FugaDaNave.csproj"), plan.Project);
        Assert.Equal(Path.Combine(root, "export", "Fuga-da-Nave-0.1.0-win-x64"), plan.Output);
        Assert.Equal(plan.Output + ".zip", plan.Zip);
        Assert.Contains("-p:CanisExport=true", plan.Arguments);
        Assert.Equal(["--self-contained", "true"], plan.Arguments.SkipWhile(a => a != "--self-contained").Take(2));
        Assert.Equal("false", GameExport.Plan(root, selfContained: false).Arguments.SkipWhile(a => a != "--self-contained").Skip(1).First());
        // O projeto novo já vem pronto para exportar, e preparar de novo não duplica.
        Assert.Contains("'$(CanisExport)' == 'true'", File.ReadAllText(plan.Project));
        Assert.False(GameExport.PrepareProject(plan.Project));
        Assert.Contains("export/", File.ReadAllText(Path.Combine(root, ".gitignore")));
    }

    [Fact]
    public void OldProjectGetsTheExportBlockOnce()
    {
        using var tmp = new TempDir();
        Directory.CreateDirectory(tmp.Path);
        var project = Path.Combine(tmp.Path, "Velho.csproj");
        File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n  <PropertyGroup />\r\n</Project>\r\n");

        Assert.True(GameExport.PrepareProject(project));
        Assert.False(GameExport.PrepareProject(project));
        var text = File.ReadAllText(project);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "CanisExport"));
        Assert.EndsWith("</Project>\r\n", text);
        // As quebras de linha do arquivo continuam as mesmas: nenhuma linha só com \n.
        Assert.DoesNotMatch("[^\r]\n", text);
    }

    [Fact]
    public void FinishCleansAndZipsTheExport()
    {
        using var tmp = new TempDir();
        var root = Templates.CreateProject(tmp.Path, "Jogo", "vazio", Repo.Root);
        var plan = GameExport.Plan(root);
        Directory.CreateDirectory(plan.Output);
        foreach (var file in new[] { "Jogo.exe", "canis_core.dll", "projeto.json", "Jogo.pdb", "CanisMajor.xml" })
            File.WriteAllText(Path.Combine(plan.Output, file), "x");

        var zip = GameExport.Finish(plan);

        Assert.False(File.Exists(Path.Combine(plan.Output, "Jogo.pdb")));
        Assert.False(File.Exists(Path.Combine(plan.Output, "CanisMajor.xml")));
        Assert.True(File.Exists(Path.Combine(plan.Output, "LEIA-ME.txt")));
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName.Replace('\\', '/') == "Jogo-0.1.0-win-x64/Jogo.exe");
    }

    [Fact]
    public void FinishRefusesAnExportWithoutTheCore()
    {
        using var tmp = new TempDir();
        var root = Templates.CreateProject(tmp.Path, "Jogo", "vazio", Repo.Root);
        var plan = GameExport.Plan(root);
        Directory.CreateDirectory(plan.Output);
        File.WriteAllText(Path.Combine(plan.Output, "Jogo.exe"), "x");
        File.WriteAllText(Path.Combine(plan.Output, "projeto.json"), "x");

        Assert.Contains("canis_core.dll", Assert.Throws<GameException>(() => GameExport.Finish(plan)).Message);
    }

    [Fact]
    public void ErrorTextKeepsTheGameMethodsOnly()
    {
        Exception error;
        try { throw new InvalidOperationException("quebrou"); }
        catch (Exception e) { error = e; }

        var text = Runtime.UserErrorText(error);

        Assert.StartsWith("Erro no jogo: InvalidOperationException: quebrou", text);
        Assert.Contains("ErrorTextKeepsTheGameMethodsOnly", text);
    }
}
