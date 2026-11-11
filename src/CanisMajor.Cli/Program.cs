using CanisMajor;

// cm new <pasta> [--name "Meu jogo"] [--template exemplo|vazio]
// cm version

static string EngineRoot()
{
    // Sobe a partir do cm.exe até achar src/CanisMajor/CanisMajor.csproj.
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "src", "CanisMajor", "CanisMajor.csproj"))) return dir.FullName;
    throw new InvalidOperationException("Não achei a pasta do Canis Major acima do cm.exe.");
}

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine("""
        Canis Major

          cm new <pasta> [--name "Meu jogo"] [--template exemplo|vazio]   cria um projeto de jogo
          cm version                                                     mostra a versão

        Para jogar: dotnet run --project <pasta do jogo>
        """);
    return 0;
}

switch (args[0])
{
    case "version":
        Console.WriteLine($"Canis Major {Engine.Version}");
        return 0;
    case "new" when args.Length >= 2:
        var folder = args[1];
        string? name = null;
        var template = "exemplo";
        for (var i = 2; i < args.Length - 1; i++)
        {
            if (args[i] == "--name") name = args[++i];
            else if (args[i] == "--template") template = args[++i];
        }
        name ??= Path.GetFileName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar));
        try
        {
            var root = Templates.CreateProject(folder, name, template, EngineRoot());
            Console.WriteLine($"Projeto criado em {root}");
            Console.WriteLine($"Para jogar: dotnet run --project \"{root}\"");
            return 0;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Erro: {e.Message}");
            return 1;
        }
    default:
        Console.Error.WriteLine($"Comando desconhecido: {string.Join(' ', args)}. Use: cm help");
        return 1;
}
