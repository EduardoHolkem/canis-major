using System.Globalization;
using System.Reflection;
using System.Text;

namespace CanisMajor;

public enum Align { Left = 0, Center = 1, Right = 2 }

/// <summary>O jogo que está rodando: salas, objetos, câmera, tempo, som e shaders.</summary>
public static class Game
{
    public static int Width => Runtime.Project?.Info.Width ?? 0;
    public static int Height => Runtime.Project?.Info.Height ?? 0;
    public static int RoomWidth => Runtime.RoomWidth;
    public static int RoomHeight => Runtime.RoomHeight;
    public static string Room => Runtime.Room;
    /// <summary>Segundos desde o começo do jogo.</summary>
    public static float Time => Runtime.Time;
    public static float Dt => Runtime.Dt;
    public static float Fps => Runtime.Fps;
    public static float CameraX { get => Runtime.CameraX; set => Runtime.CameraX = value; }
    public static float CameraY { get => Runtime.CameraY; set => Runtime.CameraY = value; }
    public static Color Background { get => Runtime.Background; set => Runtime.Background = value; }
    /// <summary>Shaders de tela da sala atual, na ordem em que são aplicados.</summary>
    public static List<string> PostShaders => Runtime.PostShaders;
    /// <summary>Variáveis que sobrevivem à troca de sala.</summary>
    public static Dictionary<string, object?> Data => Runtime.Data;

    /// <summary>Troca de sala no fim do quadro atual.</summary>
    public static void Goto(string room) => Runtime.PendingRoom = room;
    public static void Restart() => Runtime.PendingRoom = Runtime.Room;
    public static void Quit() => Runtime.Running = false;

    public static T Spawn<T>(float x, float y) where T : GameObject => (T)Runtime.Spawn(typeof(T), x, y);
    public static GameObject Spawn(string kind, float x, float y) => Runtime.Spawn(kind, x, y);

    public static IEnumerable<GameObject> Instances(string? kind = null) => Runtime.InstancesOf(kind);
    public static IEnumerable<T> Instances<T>() where T : GameObject => Runtime.InstancesOf<T>();
    public static T? First<T>() where T : GameObject => Runtime.InstancesOf<T>().FirstOrDefault();
    public static GameObject? First(string kind) => Runtime.InstancesOf(kind).FirstOrDefault();
    public static int Count(string? kind = null) => Runtime.InstancesOf(kind).Count();
    public static int Count<T>() where T : GameObject => Runtime.InstancesOf<T>().Count();

    /// <summary>Centraliza a câmera no objeto sem passar das bordas da sala. smooth de 0 (seco) a 1 (lento).</summary>
    public static void Follow(GameObject obj, float smooth = 0)
    {
        var tx = Math.Clamp(obj.X - Width / 2f, 0, Math.Max(RoomWidth - Width, 0));
        var ty = Math.Clamp(obj.Y - Height / 2f, 0, Math.Max(RoomHeight - Height, 0));
        var k = 1 - Math.Clamp(smooth, 0, 0.99f);
        CameraX += (tx - CameraX) * k;
        CameraY += (ty - CameraY) * k;
    }

    public static void PlaySound(string name, float volume = 1, bool loop = false) => Runtime.PlaySound(name, volume, loop);
    public static void StopSounds() => Native.cm_stop_sounds();

    /// <summary>Preenche param(index) no shader: quatro números por índice, de 0 a 3.</summary>
    public static void SetShaderParam(string shader, int index, float x, float y = 0, float z = 0, float w = 0) =>
        Runtime.SetShaderParam(shader, index, x, y, z, w);

    public static (float Width, float Height) MeasureText(string text, float size = 16) => Runtime.Measure(text, size);
}

/// <summary>Ponto de entrada: <c>return CanisMajor.Engine.Run(args);</c> no Program.cs do jogo.</summary>
public static class Engine
{
    public static string Version => typeof(Engine).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static int Run(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = Encoding.UTF8;

        string? project = null, room = null, screenshot = null;
        var frames = 0;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--room" when i + 1 < args.Length: room = args[++i]; break;
                case "--frames" when i + 1 < args.Length: frames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                case "--screenshot" when i + 1 < args.Length: screenshot = args[++i]; break;
                case "--project" when i + 1 < args.Length: project = args[++i]; break;
                default: if (!args[i].StartsWith("--", StringComparison.Ordinal)) project = args[i]; break;
            }
        }

        try
        {
            var dir = GameProject.Find(project) ?? throw new GameException(
                project is null ? "Não achei o projeto.json: rode o jogo de dentro da pasta do projeto." : $"Esta pasta não tem um projeto.json: {project}");
            var assemblies = new[] { Assembly.GetEntryAssembly(), Assembly.GetCallingAssembly() }.Where(a => a != null).Distinct()!;
            return Runtime.Run(dir, room, frames, screenshot, assemblies!);
        }
        catch (GameException e)
        {
            Console.Error.WriteLine($"Erro: {e.Message}");
            return 1;
        }
        catch (DllNotFoundException)
        {
            Console.Error.WriteLine("Erro: não achei a canis_core.dll ao lado do jogo. Compile o núcleo com: cargo build --release, na pasta core.");
            return 1;
        }
        catch (Exception e)
        {
            Runtime.PrintUserError(e);
            return 1;
        }
        finally
        {
            Runtime.Shutdown();
        }
    }
}
