using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CanisEditor.Core;

namespace CanisEditor.Tests;

public class UpdateTests
{
    private static AppVersion V(string text) => AppVersion.Parse(text)!;

    private sealed class Fixture : IDisposable
    {
        private readonly TempFolder _settingsFolder = new();

        public Fixture(AppSettings? settings = null)
        {
            Settings = settings ?? new AppSettings();
            Window = new MainWindow(Settings, SettingsPath, null)
            {
                CurrentVersion = V("0.1.0-alpha.1"),
                // A consulta real falharia o teste: nenhum caso aqui pode sair para a internet.
                FetchLatestTag = _ =>
                {
                    Fetches++;
                    return Task.FromResult(Latest);
                },
                OpenUrl = url =>
                {
                    Opened.Add(url);
                    return OpenResult;
                },
                // Nunca baixa nem roda instalador de verdade.
                DownloadUpdate = (version, progress, _) =>
                {
                    Downloaded.Add(version.Tag);
                    return DownloadStep?.Invoke(progress) ?? Task.FromResult(DownloadResult);
                },
                CanSelfUpdate = () => true,
                RunInstaller = path =>
                {
                    Installers.Add(path);
                    return InstallerResult;
                },
            };
            Window.Language.IsInstalled = _ => false;
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public AppSettings Settings { get; }
        public MainWindow Window { get; }
        public string SettingsPath => Path.Combine(_settingsFolder.Path, "settings.json");
        public string? Latest { get; set; }
        public int Fetches { get; private set; }
        public List<string> Opened { get; } = [];
        public bool OpenResult { get; set; } = true;
        public List<string> Downloaded { get; } = [];
        public string? DownloadResult { get; set; } = @"C:\atualizacao\CanisMajor-0.7.0-win-x64.zip";
        public Func<IProgress<double>, Task<string?>>? DownloadStep { get; set; }
        public List<string> Installers { get; } = [];
        public bool InstallerResult { get; set; } = true;

        public void Dispose()
        {
            Window.Close();
            Dispatcher.UIThread.RunJobs();
            _settingsFolder.Dispose();
        }
    }

    private static async Task Answer(MainWindow window, bool primary, Task pending)
    {
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.IsDialogOpen);
        window.AnswerDialog(primary);
        await pending;
    }

    // Ordem das versões

    [Theory]
    [InlineData("0.1.0-alpha.1", "0.1.0-alpha.2")]
    [InlineData("0.1.0-alpha.9", "0.1.0-alpha.10")]
    [InlineData("0.1.0-alpha.2", "0.1.0-beta.1")]
    [InlineData("0.1.0-alpha", "0.1.0-alpha.1")]
    [InlineData("0.1.0-1", "0.1.0-alpha")]
    [InlineData("0.1.0-rc.1", "0.1.0")]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.1.9", "0.2.0")]
    [InlineData("0.1.0-alpha.7", "0.2.0")]
    [InlineData("0.9.0", "0.10.0")]
    [InlineData("0.99.99", "1.0.0-alpha.1")]
    public void Versoes_ficam_na_ordem_certa(string older, string newer)
    {
        Assert.True(V(older).CompareTo(V(newer)) < 0);
        Assert.True(V(newer).CompareTo(V(older)) > 0);
    }

    [Theory]
    [InlineData("v0.1.0-alpha.1", "0.1.0-alpha.1")]
    [InlineData("0.1.0-alpha.1+fa61c7467739", "0.1.0-alpha.1")]
    public void Prefixo_e_codigo_do_commit_nao_mudam_a_versao(string text, string same)
    {
        Assert.Equal(0, V(text).CompareTo(V(same)));
        Assert.Equal(same, V(text).Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("v1")]
    [InlineData("1.2")]
    [InlineData("latest")]
    [InlineData("1.0.0/../../evil")]
    [InlineData("1.0.0 x")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-a..b")]
    [InlineData("99999999999999999999.0.0")]
    public void Texto_que_nao_e_versao_e_recusado(string? text) => Assert.Null(AppVersion.Parse(text));

    [Fact]
    public void Versao_atual_vem_do_programa_sem_o_codigo_do_commit()
    {
        Assert.DoesNotContain("+", AppVersion.Current.Text);
        Assert.True(AppVersion.Current.CompareTo(V("0.0.1")) > 0);
    }

    // Resposta do GitHub

    [Fact]
    public void Le_a_versao_da_resposta_do_github() =>
        Assert.Equal("v0.1.0-alpha.2", UpdateChecker.ParseLatestTag("""{"tag_name":"v0.1.0-alpha.2","name":"Sirius"}"""));

    [Theory]
    [InlineData("")]
    [InlineData("nao e json")]
    [InlineData("[]")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":123}""")]
    [InlineData("""{"tag_name":"https://exemplo.com/"}""")]
    public void Resposta_estranha_nao_vira_versao(string json) => Assert.Null(UpdateChecker.ParseLatestTag(json));

    [Fact]
    public void Endereco_aponta_para_a_pagina_da_versao() =>
        Assert.Equal("https://github.com/EduardoHolkem/canis-major/releases/tag/v0.1.0-alpha.2", UpdateChecker.ReleasePageFor(V("v0.1.0-alpha.2")));

    // O que anunciar

    [Theory]
    [InlineData("v0.1.0-alpha.2", null, "0.1.0-alpha.2")]
    [InlineData("v0.1.0-alpha.1", null, null)]
    [InlineData("v0.0.9", null, null)]
    [InlineData(null, null, null)]
    [InlineData("invalida", null, null)]
    [InlineData("v0.1.0-alpha.2", "v0.1.0-alpha.2", null)]
    [InlineData("v0.1.0-alpha.3", "v0.1.0-alpha.2", "0.1.0-alpha.3")]
    public void Anuncia_so_versao_mais_nova_e_nao_ignorada(string? latest, string? dismissed, string? expected) =>
        Assert.Equal(expected, UpdateChecker.Pending(V("0.1.0-alpha.1"), latest, dismissed)?.Text);

    // Na janela

    [AvaloniaFact]
    public void Abrir_a_janela_nao_consulta_a_internet()
    {
        using var fixture = new Fixture();
        Assert.Equal(0, fixture.Fetches);
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
    }

    [AvaloniaFact]
    public void Versao_nova_aparece_na_capsula_e_fica_guardada()
    {
        using var fixture = new Fixture { Latest = "v0.1.0-alpha.2" };

        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, fixture.Fetches);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
        Assert.Equal("0.1.0-alpha.2", fixture.Window.PendingUpdateText);
        var saved = AppSettings.Load(fixture.SettingsPath);
        Assert.Equal("v0.1.0-alpha.2", saved.LatestVersion);
    }

    [AvaloniaFact]
    public void Janela_aberta_antes_da_versao_nova_avisa_ao_voltar_depois_de_uma_hora()
    {
        var now = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        using var fixture = new Fixture();
        fixture.Window.UpdateClock = () => now;
        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, fixture.Fetches);
        Assert.False(fixture.Window.IsUpdateNoticeVisible);

        // Falhou: tenta logo, mas sem insistir a cada troca de janela.
        fixture.Latest = "v0.1.0-alpha.2";
        now = now.AddMinutes(4);
        fixture.Window.CheckForUpdatesIfDue();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, fixture.Fetches);

        now = now.AddMinutes(1);
        fixture.Window.CheckForUpdatesIfDue();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, fixture.Fetches);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);

        // Respondeu: a próxima consulta só depois de uma hora.
        now = now.AddMinutes(59);
        fixture.Window.CheckForUpdatesIfDue();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, fixture.Fetches);

        // Passou o tempo: voltar para a janela é o que dispara a consulta.
        now = now.AddMinutes(1);
        ((Window)fixture.Window).Activate();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, fixture.Fetches);
    }

    [AvaloniaFact]
    public void Aviso_desligado_nao_consulta_ao_voltar_para_a_janela()
    {
        using var fixture = new Fixture(new AppSettings { CheckForUpdates = false });
        fixture.Window.CheckForUpdatesIfDue();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, fixture.Fetches);
    }

    [AvaloniaFact]
    public void Aviso_cabe_inteiro_mesmo_com_muitos_problemas_na_capsula()
    {
        using var fixture = new Fixture(new AppSettings { LatestVersion = "v0.1.0-alpha.2" });
        fixture.Window.StartUpdateChecks();
        fixture.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var notice = fixture.Window.FindControl<Button>("UpdateButton")!;
        var natural = notice.Bounds.Width;
        Assert.True(natural > 0);

        var problems = fixture.Window.FindControl<Button>("ProblemsButton")!;
        problems.Content = "120 erros, 300 avisos, 500 dicas, 700 erros, 900 avisos, 1000 dicas, 2000 erros";
        problems.IsVisible = true;
        fixture.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(natural, notice.Bounds.Width);
    }

    [AvaloniaFact]
    public void Texto_encurta_ao_lado_do_aviso_de_instalar_suporte()
    {
        using var temp = new TempFolder();
        var php = temp.File("index.php", "<?php\n");
        using var fixture = new Fixture(new AppSettings { LatestVersion = "v0.1.0-alpha.2" });
        fixture.Window.StartUpdateChecks();
        Assert.Equal("Versão 0.1.0-alpha.2 disponível", fixture.Window.UpdateNoticeLabel);

        fixture.Window.OpenFile(php);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("PHP", fixture.Window.MissingServerName);
        Assert.Equal("Nova: 0.1.0-alpha.2", fixture.Window.UpdateNoticeLabel);
    }

    [AvaloniaFact]
    public void Mesma_versao_nao_mostra_aviso()
    {
        using var fixture = new Fixture { Latest = "v0.1.0-alpha.1" };

        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, fixture.Fetches);
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
    }

    [AvaloniaFact]
    public void Consulta_toda_vez_que_a_sirius_abre()
    {
        using var fixture = new Fixture { Latest = "v0.1.0-alpha.2" };

        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("0.1.0-alpha.2", fixture.Window.PendingUpdateText);

        // Uma versão publicada um minuto depois já aparece ao abrir de novo.
        fixture.Latest = "v0.1.0-alpha.3";
        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, fixture.Fetches);
        Assert.Equal("0.1.0-alpha.3", fixture.Window.PendingUpdateText);
    }

    [AvaloniaFact]
    public void Sem_internet_mantem_o_aviso_que_ja_sabia()
    {
        using var fixture = new Fixture(new AppSettings { LatestVersion = "v0.1.0-alpha.2" });

        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, fixture.Fetches);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
        Assert.Equal("v0.1.0-alpha.2", fixture.Settings.LatestVersion);
    }

    [AvaloniaFact]
    public void Falha_silenciosa_tenta_de_novo_na_proxima_vez()
    {
        using var fixture = new Fixture();

        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, fixture.Fetches);
        Assert.Equal("", fixture.Window.StatusText);
        Assert.False(fixture.Window.IsDialogOpen);
    }

    [AvaloniaFact]
    public void Desligado_nao_consulta_nem_mostra_aviso_guardado()
    {
        using var fixture = new Fixture(new AppSettings { CheckForUpdates = false, LatestVersion = "v0.1.0-alpha.2" }) { Latest = "v0.1.0-alpha.3" };

        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, fixture.Fetches);
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
    }

    [AvaloniaFact]
    public void Desligar_esconde_e_ligar_volta_a_consultar()
    {
        using var fixture = new Fixture(new AppSettings { LatestVersion = "v0.1.0-alpha.2" });
        fixture.Window.StartUpdateChecks();
        Assert.True(fixture.Window.IsUpdateNoticeVisible);

        fixture.Window.ToggleUpdateChecks();
        Assert.False(fixture.Window.IsUpdateNoticeVisible);
        Assert.False(AppSettings.Load(fixture.SettingsPath).CheckForUpdates);

        fixture.Window.ToggleUpdateChecks();
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
        Assert.True(AppSettings.Load(fixture.SettingsPath).CheckForUpdates);
    }

    private static Fixture Pending()
    {
        var fixture = new Fixture { Latest = "v0.1.0-alpha.2" };
        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();
        return fixture;
    }

    [AvaloniaFact]
    public async Task Ver_as_novidades_abre_a_pagina_e_nao_baixa_nada()
    {
        using var fixture = Pending();

        await Answer(fixture.Window, primary: false, fixture.Window.ShowUpdateAsync());

        Assert.Equal(["https://github.com/EduardoHolkem/canis-major/releases/tag/v0.1.0-alpha.2"], fixture.Opened);
        Assert.Empty(fixture.Downloaded);
        // O aviso continua até a versão nova estar instalada.
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
    }

    [AvaloniaFact]
    public async Task Navegador_que_nao_abre_avisa_na_capsula()
    {
        using var fixture = Pending();
        fixture.OpenResult = false;

        await Answer(fixture.Window, primary: false, fixture.Window.ShowUpdateAsync());

        Assert.Contains("navegador", fixture.Window.StatusText);
    }

    [AvaloniaFact]
    public async Task Atualizar_baixa_roda_o_instalador_e_fecha()
    {
        using var fixture = Pending();

        await Answer(fixture.Window, primary: true, fixture.Window.ShowUpdateAsync());
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["v0.1.0-alpha.2"], fixture.Downloaded);
        Assert.Equal([@"C:\atualizacao\CanisMajor-0.7.0-win-x64.zip"], fixture.Installers);
        Assert.False(fixture.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Andamento_aparece_no_botao_durante_o_download()
    {
        using var fixture = Pending();
        var release = new TaskCompletionSource<string?>();
        fixture.DownloadStep = progress =>
        {
            progress.Report(0.42);
            return release.Task;
        };

        var updating = fixture.Window.InstallUpdateAsync(AppVersion.Parse("v0.1.0-alpha.2")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Baixando 42%", fixture.Window.UpdateNoticeLabel);
        Assert.True(fixture.Window.IsUpdating);

        // Uma consulta no meio não troca o andamento.
        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Baixando 42%", fixture.Window.UpdateNoticeLabel);

        release.SetResult(null);
        await updating;
        Assert.False(fixture.Window.IsUpdating);
        Assert.Contains("Não foi possível baixar", fixture.Window.StatusText);
        Assert.Empty(fixture.Installers);
        Assert.True(fixture.Window.IsVisible);
        Assert.Equal("Versão 0.1.0-alpha.2 disponível", fixture.Window.UpdateNoticeLabel);
    }

    [AvaloniaFact]
    public async Task Arquivo_sem_salvar_pergunta_antes_de_instalar()
    {
        using var fixture = Pending();
        fixture.Window.NewUntitled();
        fixture.Window.ActiveDocument!.Document.Text = "rascunho";

        var updating = fixture.Window.InstallUpdateAsync(AppVersion.Parse("v0.1.0-alpha.2")!);
        Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Window.IsDialogOpen);

        // Fechar a pergunta sem escolher adia a atualização, sem perder nada.
        fixture.Window.CancelDialog();
        await updating;
        Assert.Empty(fixture.Installers);
        Assert.Contains("adiada", fixture.Window.StatusText);
        Assert.Equal("rascunho", fixture.Window.ActiveDocument?.Document.Text);
        Assert.True(fixture.Window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Pacote_que_nao_serve_deixa_o_editor_aberto()
    {
        using var fixture = Pending();
        fixture.InstallerResult = false;

        await fixture.Window.InstallUpdateAsync(AppVersion.Parse("v0.1.0-alpha.2")!);

        Assert.True(fixture.Window.IsVisible);
        Assert.Contains("pacote baixado", fixture.Window.StatusText);
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
    }

    [AvaloniaFact]
    public void Ignorar_esconde_ate_sair_outra_versao()
    {
        using var fixture = Pending();
        Assert.Contains("Atualizar o Canis Major", fixture.Window.PaletteCommandNames("canis major"));

        fixture.Window.IgnorePendingUpdate();

        Assert.False(fixture.Window.IsUpdateNoticeVisible);
        Assert.Equal("v0.1.0-alpha.2", AppSettings.Load(fixture.SettingsPath).DismissedVersion);
        Assert.DoesNotContain("Atualizar o Canis Major", fixture.Window.PaletteCommandNames("canis major"));

        fixture.Latest = "v0.1.0-alpha.3";
        fixture.Window.StartUpdateChecks();
        Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Window.IsUpdateNoticeVisible);
        Assert.Equal("0.1.0-alpha.3", fixture.Window.PendingUpdateText);
    }

    // Baixar e conferir o instalador

    private const string Hash = "6d752e3c4d433d9e4d4e0178900902ba164f452614aad0cb90cd1227c1a4be85";

    private static string Release(string name = "CanisMajor-0.7.0-win-x64.zip", string url = "https://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip",
        string? digest = "sha256:" + Hash, long size = 42541308) =>
        $$"""{"assets":[{"name":"CanisMajor.zip","browser_download_url":"https://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor.zip","size":1,"digest":"sha256:{{Hash}}"},{"name":"{{name}}","browser_download_url":"{{url}}","size":{{size}}{{(digest is null ? "" : $",\"digest\":\"{digest}\"")}}}]}""";

    [Fact]
    public void Acha_o_pacote_da_versao_com_o_registro_do_github()
    {
        var package = UpdateDownload.ParsePackage(Release(), V("v0.7.0"));

        Assert.NotNull(package);
        Assert.Equal("CanisMajor-0.7.0-win-x64.zip", package.Name);
        Assert.Equal(42541308, package.Size);
        Assert.Equal(Hash, package.Sha256);
    }

    [Theory]
    [InlineData("https://github.com/outra-pessoa/Sirius/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip", "sha256:" + Hash, 10)]
    [InlineData("https://github.com/EduardoHolkem/canis-major/releases/download/v0.6.0/CanisMajor-0.7.0-win-x64.zip", "sha256:" + Hash, 10)]
    [InlineData("http://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip", "sha256:" + Hash, 10)]
    [InlineData("https://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip", null, 10)]
    [InlineData("https://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip", "md5:abc", 10)]
    [InlineData("https://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip", "sha256:" + Hash, 0)]
    [InlineData("https://github.com/EduardoHolkem/canis-major/releases/download/v0.7.0/CanisMajor-0.7.0-win-x64.zip", "sha256:" + Hash, 999_999_999)]
    public void Recusa_pacote_de_fora_ou_sem_registro(string url, string? digest, long size) =>
        Assert.Null(UpdateDownload.ParsePackage(Release(url: url, digest: digest, size: size), V("v0.7.0")));

    [Theory]
    [InlineData("..\\CanisMajor-0.7.0-win-x64.zip")]
    [InlineData("CanisMajor-0.7.0.zip")]
    public void Recusa_nome_estranho(string name) =>
        Assert.Null(UpdateDownload.ParsePackage(Release(name: name.Replace("\\", "\\\\")), V("v0.7.0")));

    private static UpdatePackage Package(byte[] content) =>
        new("CanisMajor-9.9.9-win-x64.zip", new Uri("https://github.com/x"), content.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content)));

    private static Func<Uri, CancellationToken, Task<Stream>> Serve(byte[] content, Action? opened = null) => (_, _) =>
    {
        opened?.Invoke();
        return Task.FromResult<Stream>(new MemoryStream(content));
    };

    [Fact]
    public async Task Baixa_e_confere_o_arquivo()
    {
        using var folder = new TempFolder();
        var old = folder.File("CanisMajor-0.1.0-win-x64.zip", "velho");
        var content = "instalador"u8.ToArray();
        var reports = new List<double>();

        var path = await UpdateDownload.DownloadAsync(Package(content), folder.Path, Serve(content), new SyncProgress(reports.Add), CancellationToken.None);

        Assert.Equal(Path.Combine(folder.Path, "CanisMajor-9.9.9-win-x64.zip"), path);
        Assert.Equal(content, File.ReadAllBytes(path!));
        Assert.False(File.Exists(old));
        Assert.Equal(1, reports.Last());
    }

    [Fact]
    public async Task Arquivo_diferente_do_registrado_e_descartado()
    {
        using var folder = new TempFolder();
        var package = Package("instalador"u8.ToArray());

        Assert.Null(await UpdateDownload.DownloadAsync(package, folder.Path, Serve("instalad0r"u8.ToArray()), null, CancellationToken.None));
        Assert.Null(await UpdateDownload.DownloadAsync(package, folder.Path, Serve("instalador e mais"u8.ToArray()), null, CancellationToken.None));
        Assert.Null(await UpdateDownload.DownloadAsync(package, folder.Path, Serve("inst"u8.ToArray()), null, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(folder.Path));
    }

    [Fact]
    public async Task Instalador_ja_baixado_e_aproveitado()
    {
        using var folder = new TempFolder();
        var content = "instalador"u8.ToArray();
        var package = Package(content);
        var opens = 0;

        await UpdateDownload.DownloadAsync(package, folder.Path, Serve(content, () => opens++), null, CancellationToken.None);
        var again = await UpdateDownload.DownloadAsync(package, folder.Path, Serve(content, () => opens++), null, CancellationToken.None);

        Assert.NotNull(again);
        Assert.Equal(1, opens);
    }

    [Fact]
    public void Pacote_instalado_e_achado_pela_pasta_do_editor()
    {
        using var install = new TempFolder();
        install.File(UpdateDownload.PackageMarker, "{}");
        var editor = install.File(Path.Combine("editor", "CanisMajor.Editor.exe"), "");

        Assert.Equal(install.Path, UpdateDownload.PackageRoot(Path.GetDirectoryName(editor)!));
        using var repo = new TempFolder();
        Assert.Null(UpdateDownload.PackageRoot(repo.Path));
    }

    // Um pacote falso da versão nova, com o que o script troca e o que ele preserva.
    private static string FakePackage(TempFolder folder, string version)
    {
        var stage = Path.Combine(folder.Path, "stage");
        Directory.CreateDirectory(Path.Combine(stage, "editor"));
        Directory.CreateDirectory(Path.Combine(stage, "src", "CanisMajor"));
        Directory.CreateDirectory(Path.Combine(stage, "native"));
        Directory.CreateDirectory(Path.Combine(stage, "examples", "Exemplo"));
        File.WriteAllText(Path.Combine(stage, UpdateDownload.PackageMarker), $$"""{"version":"{{version}}"}""");
        File.WriteAllText(Path.Combine(stage, "editor", "CanisMajor.Editor.exe"), version);
        File.WriteAllText(Path.Combine(stage, "src", "CanisMajor", "Game.cs"), version);
        File.WriteAllText(Path.Combine(stage, "native", "canis_core.dll"), version);
        File.WriteAllText(Path.Combine(stage, "examples", "Exemplo", "projeto.json"), version);
        var zip = Path.Combine(folder.Path, $"CanisMajor-{version}-win-x64.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(stage, zip);
        return zip;
    }

    [Fact]
    public void Pacote_sem_editor_nao_serve()
    {
        using var folder = new TempFolder();
        var zip = Path.Combine(folder.Path, "CanisMajor-9.9.9-win-x64.zip");
        var stage = Path.Combine(folder.Path, "stage");
        Directory.CreateDirectory(stage);
        File.WriteAllText(Path.Combine(stage, UpdateDownload.PackageMarker), "{}");
        System.IO.Compression.ZipFile.CreateFromDirectory(stage, zip);

        Assert.Null(UpdateDownload.Unpack(zip, folder.Path));
    }

    [Fact]
    public async Task Script_troca_o_pacote_e_preserva_o_exemplo()
    {
        using var install = new TempFolder();
        install.File(UpdateDownload.PackageMarker, """{"version":"0.3.0"}""");
        var editor = install.File(Path.Combine("editor", "CanisMajor.Editor.exe"), "0.3.0");
        install.File(Path.Combine("editor", "velho.dll"), "sai");
        install.File(Path.Combine("src", "CanisMajor", "Game.cs"), "0.3.0");
        install.File(Path.Combine("examples", "Exemplo", "projeto.json"), "meu jogo mudado");
        using var download = new TempFolder();
        var zip = FakePackage(download, "0.4.0");

        // O "editor" que precisa fechar antes da troca é um processo que já terminou.
        using var gone = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        gone.WaitForExit();
        using var script = UpdateDownload.StartUpdate(zip, Path.GetDirectoryName(editor)!, gone.Id, launch: false);
        Assert.NotNull(script);
        await script.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        Assert.StartsWith("atualizado", File.ReadAllText(Path.Combine(download.Path, "atualizar.log")));
        Assert.Equal("0.4.0", File.ReadAllText(editor));
        Assert.False(File.Exists(Path.Combine(install.Path, "editor", "velho.dll")));
        Assert.Equal("0.4.0", File.ReadAllText(Path.Combine(install.Path, "native", "canis_core.dll")));
        Assert.Contains("0.4.0", File.ReadAllText(Path.Combine(install.Path, UpdateDownload.PackageMarker)));
        Assert.Equal("meu jogo mudado", File.ReadAllText(Path.Combine(install.Path, "examples", "Exemplo", "projeto.json")));
    }

    [AvaloniaFact]
    public async Task Rodando_do_repositorio_manda_para_a_pagina_da_versao()
    {
        using var fixture = Pending();
        fixture.Window.CanSelfUpdate = () => false;

        await fixture.Window.InstallUpdateAsync(V("v0.1.0-alpha.2"));

        Assert.Empty(fixture.Downloaded);
        Assert.Single(fixture.Opened);
        Assert.Contains("git pull", fixture.Window.StatusText);
    }

    // Progress<T> manda para a thread da interface. Aqui o relato precisa chegar na hora.
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    [AvaloniaFact]
    public async Task Procurar_de_proposito_mostra_o_resultado()
    {
        using var fixture = new Fixture();

        await fixture.Window.CheckForUpdatesAsync(manual: true);
        Assert.Contains("Não foi possível", fixture.Window.StatusText);

        fixture.Latest = "v0.1.0-alpha.1";
        await fixture.Window.CheckForUpdatesAsync(manual: true);
        Assert.Contains("mais recente (0.1.0-alpha.1)", fixture.Window.StatusText);
        Assert.False(fixture.Window.IsDialogOpen);
    }

    [AvaloniaFact]
    public async Task Procurar_de_proposito_traz_de_volta_a_versao_ignorada()
    {
        using var fixture = new Fixture(new AppSettings { DismissedVersion = "v0.1.0-alpha.2" }) { Latest = "v0.1.0-alpha.2" };

        await Answer(fixture.Window, primary: false, fixture.Window.CheckForUpdatesAsync(manual: true));

        Assert.Null(fixture.Settings.DismissedVersion);
        Assert.Single(fixture.Opened);
    }

    [AvaloniaFact]
    public void Barra_de_comando_oferece_procurar_e_desligar()
    {
        using var fixture = new Fixture();

        var names = fixture.Window.PaletteCommandNames("versão nova").ToList();

        Assert.Contains("Procurar versão nova", names);
        Assert.Contains("Aviso de versão nova", names);
    }

    // Preferências

    [Fact]
    public void Preferencias_antigas_continuam_com_o_aviso_ligado()
    {
        using var folder = new TempFolder();
        var path = folder.File("settings.json", """{"Theme":"grafite"}""");

        var settings = AppSettings.Load(path);

        Assert.True(settings.CheckForUpdates);
        Assert.Null(settings.LatestVersion);
    }
}
