using System.ComponentModel;
using System.Diagnostics;

namespace CanisEditor.Core;

// Ferramentas instaladas com o Canis Major aberta ainda não estão no PATH deste processo.
// Acrescentar as pastas conhecidas faz o terminal e os servidores as encontrarem na hora.
public static class ToolPaths
{
    private const string PhpInstall = "winget install --id PHP.PHP.8.4 --exact --source winget --silent --accept-package-agreements --accept-source-agreements";

    // O PHP depende do Visual C++, e o winget sempre tenta instalá-lo, o que pede administrador.
    // Se ele já está no computador, pular evita a pergunta, que num PC de escola nunca seria aprovada.
    public static string PhpInstallCommand(bool hasVisualCpp) =>
        hasVisualCpp ? PhpInstall + " --skip-dependencies" : PhpInstall;

    // O "--scope user" não basta para evitar o administrador: o instalador do Git pede o nível mais alto da conta
    // (ElevationRequirement: elevatesSelf no manifesto). Numa conta comum, como num PC de escola, ele instala só
    // para a pessoa, em Programs\Git, sem pedir nada. Numa de administrador, o Windows pede uma confirmação e ele
    // vai para Program Files. Os dois lugares estão em Folders.
    public const string GitInstallCommand = "winget install --id Git.Git --exact --source winget --scope user --silent --accept-package-agreements --accept-source-agreements";

    public static bool HasVisualCpp => File.Exists(Path.Combine(Environment.SystemDirectory, "vcruntime140.dll"));

    // Encontrar não basta: sem o Visual C++, o php.exe existe mas não abre.
    public static bool PhpRuns(string executable)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, "-v")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });
            if (process is null)
                return false;
            if (process.WaitForExit(5000))
                return process.ExitCode == 0;
            process.Kill();
            return false;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    public static IEnumerable<string> Folders
    {
        get
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(programFiles, "dotnet");
            yield return Path.Combine(programFiles, "nodejs");
            yield return Path.Combine(programFiles, "Git", "cmd");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
            // O XAMPP traz o PHP, mas não o coloca no PATH.
            yield return @"C:\xampp\php";

            // O winget cria um atalho em Links ou, sem permissão para isso, deixa o programa
            // na pasta do pacote. As duas formas acontecem, dependendo do Windows.
            var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet");
            yield return Path.Combine(winget, "Links");
            var packages = Path.Combine(winget, "Packages");
            if (Directory.Exists(packages))
            {
                foreach (var package in Directory.EnumerateDirectories(packages, "PHP.PHP.*"))
                    yield return package;
            }
        }
    }

    public static void EnsureOnPath()
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        var updated = WithDotNetRoot(WithTools(current), Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        if (updated != current)
            Environment.SetEnvironmentVariable("PATH", updated);
    }

    // Os jogos pedem o .NET 10. Quando o Canis Major abre com DOTNET_ROOT apontando para ele, como pelo atalho,
    // esse dotnet passa na frente do que estiver no PATH, só dentro do editor e do que ele abrir.
    public static string WithDotNetRoot(string current, string? dotnetRoot)
    {
        if (string.IsNullOrWhiteSpace(dotnetRoot) || !File.Exists(Path.Combine(dotnetRoot, "dotnet.exe")))
            return current;
        var root = dotnetRoot.Trim().TrimEnd(Path.DirectorySeparatorChar);
        var rest = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(folder => !string.Equals(folder.Trim().TrimEnd(Path.DirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase));
        return string.Join(Path.PathSeparator, rest.Prepend(root));
    }

    public static string WithTools(string current)
    {
        var known = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => folder.Trim().TrimEnd(Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = Folders.Where(folder => !known.Contains(folder) && Directory.Exists(folder));
        return string.Join(Path.PathSeparator, missing.Prepend(current));
    }
}
