using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CanisEditor.Core;
using CanisEditor.Theming;

namespace CanisEditor;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Palette.Apply(Palette.Claro.Name);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = StartOptions.Parse(desktop.Args ?? []);
            if (options.Shot is not null)
            {
                desktop.MainWindow = ShotWindow(options);
            }
            else
            {
                var path = AppSettings.DefaultPath;
                var window = new MainWindow(AppSettings.Load(path), path, options.Start);
                desktop.MainWindow = window;
                window.StartUpdateChecks();
                ListenForOtherInstances(window);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // "--shot foto.png": abre a pasta (e o arquivo de --open), espera desenhar, grava a foto da janela e fecha.
    // É para conferir a interface sem mexer nas preferências de quem usa: as configurações ficam num arquivo temporário.
    private static MainWindow ShotWindow(StartOptions options)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"canis-shot-{Environment.ProcessId}.json");
        var settings = new AppSettings { Theme = options.Theme ?? "claro", CheckForUpdates = false };
        var window = new MainWindow(settings, settingsPath, options.Start) { Width = 1440, Height = 900 };
        window.Opened += async (_, _) =>
        {
            if (options.Open is { } file)
                window.OpenFile(file);
            await Task.Delay(TimeSpan.FromSeconds(options.Wait));
            window.SaveShot(options.Shot!);
            File.Delete(settingsPath);
            Environment.Exit(0);
        };
        return window;
    }

    private sealed record StartOptions(string? Start, string? Shot, string? Open, string? Theme, double Wait)
    {
        public static StartOptions Parse(string[] args)
        {
            string? start = null, shot = null, open = null, theme = null;
            var wait = 1.5;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--shot" when i + 1 < args.Length:
                        shot = args[++i];
                        break;
                    case "--open" when i + 1 < args.Length:
                        open = args[++i];
                        break;
                    case "--theme" when i + 1 < args.Length:
                        theme = args[++i];
                        break;
                    case "--wait" when i + 1 < args.Length:
                        wait = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    default:
                        start ??= args[i];
                        break;
                }
            }
            return new StartOptions(start, shot, open, theme, wait);
        }
    }

    private static readonly TimeSpan TakeOverInterval = TimeSpan.FromSeconds(3);

    private static void ListenForOtherInstances(MainWindow window)
    {
        if (Program.Instance is not { } instance)
            return;
        instance.OnReceived(paths => Dispatcher.UIThread.Post(() => window.ReceiveForwarded(paths)));
        if (instance.IsServing)
            return;

        // Esta janela abriu com outra já aberta, como numa pasta. Quando a outra fechar, esta passa a receber os arquivos.
        var timer = new DispatcherTimer { Interval = TakeOverInterval };
        timer.Tick += (_, _) =>
        {
            if (instance.TryStart())
                timer.Stop();
        };
        timer.Start();
        window.Closed += (_, _) => timer.Stop();
    }
}
