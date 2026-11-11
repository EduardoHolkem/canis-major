using System.Runtime.InteropServices;
using System.Text;

namespace CanisMajor;

/// <summary>
/// Mostra um erro que fecha o jogo. No terminal, só escreve. No jogo exportado, que abre sem console,
/// a mensagem sumiria: ela vai também para erro.txt, ao lado do .exe, e aparece numa caixa de aviso.
/// </summary>
internal static partial class ErrorReport
{
    private const uint IconError = 0x10;

    private const int StdError = -12;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetStdHandle(int which);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);

    public static void Show(string text)
    {
        Console.Error.WriteLine(text);
        // Com console, ou com a saída ligada a um arquivo ou a outro programa (testes, editor), quem olha já vê.
        // Num .exe sem console aberto pelo Explorer, não há saída nenhuma.
        if (!OperatingSystem.IsWindows() || GetConsoleWindow() != 0 || GetStdHandle(StdError) is not (0 or -1))
            return;
        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "erro.txt"), text + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Sem permissão na pasta: a caixa de aviso ainda mostra.
        }
        MessageBox(0, text, "Erro no jogo", IconError);
    }
}
