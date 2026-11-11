using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace CanisMajor;

/// <summary>O que exportar um jogo vai fazer: o projeto, a pasta de saída, o zip e os argumentos do dotnet.</summary>
public sealed record ExportPlan(string Root, string Project, string Name, string Output, string Zip, IReadOnlyList<string> Arguments)
{
    /// <summary>A linha de comando, para mostrar no terminal.</summary>
    public string CommandLine => "dotnet " + string.Join(' ', Arguments.Select(Quote));

    private static string Quote(string arg) => arg.Contains(' ') || arg.Contains('"') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}

/// <summary>
/// Exporta um jogo para Windows: uma pasta com o .exe (o .NET vai dentro), a canis_core.dll e os recursos,
/// e um .zip dela, pronto para mandar a alguém. Quem joga não precisa instalar nada.
/// </summary>
public static class GameExport
{
    /// <summary>As propriedades que valem só ao exportar. Ficam no .csproj do jogo para não mudar a engine junto.</summary>
    public const string ExportBlock = """
          <!-- Ao exportar (cm export ou Exportar jogo no editor): um .exe só, sem janela de console. -->
          <PropertyGroup Condition="'$(CanisExport)' == 'true'">
            <OutputType>WinExe</OutputType>
            <PublishSingleFile>true</PublishSingleFile>
            <IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>
            <DebugType>none</DebugType>
          </PropertyGroup>
        """;

    /// <summary>O .csproj do jogo, na pasta do projeto.</summary>
    public static string FindProject(string root)
    {
        var projects = Directory.GetFiles(root, "*.csproj");
        return projects.Length switch
        {
            1 => projects[0],
            0 => throw new GameException($"Não achei o .csproj do jogo em {root}."),
            _ => throw new GameException($"Há mais de um .csproj em {root}; deixe só o do jogo."),
        };
    }

    /// <summary>Monta o plano. <paramref name="selfContained"/> põe o .NET junto, para rodar num Windows sem nada.</summary>
    public static ExportPlan Plan(string root, bool selfContained = true)
    {
        root = Path.GetFullPath(root);
        var info = GameProject.Load(root).Info;
        var project = FindProject(root);
        var name = FileName(info.Name);
        var output = Path.Combine(root, "export", $"{name}-{info.Version}-win-x64");
        var args = new List<string>
        {
            "publish", project, "-c", "Release", "-r", "win-x64",
            "--self-contained", selfContained ? "true" : "false",
            "-o", output, "-p:CanisExport=true", "--nologo",
        };
        return new ExportPlan(root, project, name, output, output + ".zip", args);
    }

    /// <summary>
    /// Acrescenta o bloco de exportação ao .csproj, se faltar. Projetos criados antes da versão 0.3 não têm.
    /// Devolve verdadeiro se mudou o arquivo.
    /// </summary>
    public static bool PrepareProject(string project)
    {
        var text = File.ReadAllText(project);
        if (text.Contains("CanisExport", StringComparison.Ordinal))
            return false;
        var end = text.LastIndexOf("</Project>", StringComparison.Ordinal);
        if (end < 0)
            throw new GameException($"{project} não parece um .csproj.");
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var block = ExportBlock.Replace("\r\n", "\n").Replace("\n", newline);
        File.WriteAllText(project, text[..end] + block + newline + newline + text[end..], new UTF8Encoding(false));
        return true;
    }

    /// <summary>Antes do dotnet publish: prepara o .csproj e apaga a exportação anterior desta versão.</summary>
    public static void Begin(ExportPlan plan)
    {
        PrepareProject(plan.Project);
        if (Directory.Exists(plan.Output))
            Directory.Delete(plan.Output, true);
        if (File.Exists(plan.Zip))
            File.Delete(plan.Zip);
    }

    /// <summary>Depois do dotnet publish: tira o que quem joga não usa e faz o zip. Devolve o caminho do zip.</summary>
    public static string Finish(ExportPlan plan)
    {
        if (!Directory.Exists(plan.Output))
            throw new GameException("A exportação não gerou a pasta do jogo.");
        foreach (var pattern in new[] { "*.pdb", "*.xml" })
        {
            foreach (var file in Directory.EnumerateFiles(plan.Output, pattern, SearchOption.TopDirectoryOnly))
                File.Delete(file);
        }
        var exe = Directory.EnumerateFiles(plan.Output, "*.exe").FirstOrDefault()
            ?? throw new GameException("A exportação não gerou o .exe do jogo.");
        if (!File.Exists(Path.Combine(plan.Output, "canis_core.dll")))
            throw new GameException("A canis_core.dll não foi junto com o jogo.");
        if (!File.Exists(Path.Combine(plan.Output, GameProject.FileName)))
            throw new GameException("O projeto.json não foi junto com o jogo.");
        File.WriteAllText(Path.Combine(plan.Output, "LEIA-ME.txt"),
            $"{plan.Name}\r\n\r\nPara jogar, abra {Path.GetFileName(exe)}. Não precisa instalar nada.\r\n" +
            "Se o jogo der erro, a mensagem também fica em erro.txt, nesta pasta.\r\n\r\nFeito com Canis Major.\r\n",
            new UTF8Encoding(false));
        if (File.Exists(plan.Zip))
            File.Delete(plan.Zip);
        ZipFile.CreateFromDirectory(plan.Output, plan.Zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return plan.Zip;
    }

    /// <summary>Exporta de uma vez, rodando o dotnet aqui mesmo. É o que o cm export usa.</summary>
    public static int Run(ExportPlan plan, Action<string> log, string dotnet = "dotnet")
    {
        Begin(plan);
        log(plan.CommandLine);
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in plan.Arguments)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new GameException("Não foi possível rodar o dotnet.");
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        if (process.ExitCode != 0)
            return process.ExitCode;
        log($"Pronto: {Finish(plan)}");
        return 0;
    }

    /// <summary>Um nome que serve de pasta e de arquivo.</summary>
    public static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c).ToArray()).Trim('-', '.');
        return clean.Length == 0 ? "Jogo" : clean;
    }
}
