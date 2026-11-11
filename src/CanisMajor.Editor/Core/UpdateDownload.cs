using System.ComponentModel;
using System.IO.Compression;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CanisEditor.Core;

// O pacote de uma versão, como a Release do GitHub o descreve. Sha256 é o que o GitHub registrou ao receber o arquivo.
public sealed record UpdatePackage(string Name, Uri Url, long Size, string Sha256);

// Atualizar pelo próprio Canis Major: acha o pacote da versão (CanisMajor-x.y.z-win-x64.zip), baixa, confere,
// abre numa pasta de espera e deixa um script trocando os arquivos assim que o editor fechar.
// Não há assinatura digital ainda, então a confiança é a mesma de baixar da página de Releases:
// só esse endereço é aceito, e o arquivo precisa ser exatamente o que o GitHub registrou.
public static partial class UpdateDownload
{
    private const string ReleaseApi = "https://api.github.com/repos/EduardoHolkem/canis-major/releases/tags/";
    private const string DownloadPrefix = "https://github.com/EduardoHolkem/canis-major/releases/download/";
    public const long MaxBytes = 300L * 1024 * 1024;
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    // O arquivo que marca a pasta de um pacote instalado. No repositório (git) ele não existe, e lá se atualiza com git pull.
    public const string PackageMarker = "pacote.json";

    private static readonly HttpClient Http = CreateClient();

    [GeneratedRegex("^sha256:([0-9a-f]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex Digest();

    [GeneratedRegex(@"^CanisMajor-[0-9A-Za-z.\-]+-win-x64\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageName();

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CanisMajor", "atualizacao");

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CanisMajor", AppVersion.Current.Text));
        return client;
    }

    // O pacote da Release: CanisMajor-<versão>-win-x64.zip.
    public static UpdatePackage? ParsePackage(string json, AppVersion version)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object
                    || asset.TryGetProperty("name", out var nameValue) is false || nameValue.GetString() is not { } name
                    || !PackageName().IsMatch(name)
                    || Path.GetFileName(name) != name || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    continue;

                // Só o endereço de download desta versão, neste repositório.
                var url = asset.TryGetProperty("browser_download_url", out var urlValue) ? urlValue.GetString() : null;
                if (url is null || !url.StartsWith(DownloadPrefix + version.Tag + "/", StringComparison.Ordinal)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    continue;

                // Sem o registro do GitHub não há como conferir o arquivo, e então ele não é usado.
                var digest = asset.TryGetProperty("digest", out var digestValue) && digestValue.ValueKind == JsonValueKind.String
                    ? Digest().Match(digestValue.GetString()!)
                    : null;
                var size = asset.TryGetProperty("size", out var sizeValue) && sizeValue.TryGetInt64(out var bytes) ? bytes : 0;
                if (digest is not { Success: true } || size <= 0 || size > MaxBytes)
                    continue;

                return new UpdatePackage(name, uri, size, digest.Groups[1].Value);
            }
            return null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static async Task<UpdatePackage?> FindAsync(AppVersion version, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi + version.Tag);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(LookupTimeout);
        using var response = await Http.SendAsync(request, timeout.Token);
        if (!response.IsSuccessStatusCode)
            return null;
        return ParsePackage(await response.Content.ReadAsStringAsync(timeout.Token), version);
    }

    // Baixa para a pasta e devolve o caminho só se o arquivo for exatamente o registrado. Um que já
    // estava lá, inteiro, é aproveitado: quem adiou a atualização não baixa de novo.
    public static async Task<string?> DownloadAsync(UpdatePackage package, string folder, Func<Uri, CancellationToken, Task<Stream>> open,
        IProgress<double>? progress, CancellationToken cancel)
    {
        var target = Path.Combine(folder, package.Name);
        var partial = target + ".parcial";
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(target) && await HashOfFileAsync(target, cancel) == package.Sha256)
            {
                progress?.Report(1);
                return target;
            }

            // Instaladores de outras versões, que não servem mais.
            foreach (var old in Directory.EnumerateFiles(folder))
                TryDelete(old);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var source = await open(package.Url, cancel))
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                int read;
                // Mais do que o registrado já não é o arquivo certo: para de ler e descarta.
                while (total <= package.Size && (read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    total += read;
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                    progress?.Report(Math.Min(1, (double)total / package.Size));
                }
            }

            if (total != package.Size || Convert.ToHexStringLower(hash.GetHashAndReset()) != package.Sha256)
                return Fail(partial);
            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or OperationCanceledException)
        {
            return Fail(partial);
        }
    }

    // Acha, baixa e confere a versão, ou devolve null em qualquer falha.
    public static async Task<string?> DownloadVersionAsync(AppVersion version, IProgress<double>? progress, CancellationToken cancel)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(DownloadTimeout);
            if (await FindAsync(version, timeout.Token) is not { } package)
                return null;
            return await DownloadAsync(package, Folder, OpenAsync, progress, timeout.Token);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            return null;
        }
    }

    private static async Task<Stream> OpenAsync(Uri url, CancellationToken cancel)
    {
        var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancel);
    }

    // A pasta do pacote instalado (a que tem o pacote.json), a partir da pasta do editor, que fica em editor\.
    public static string? PackageRoot(string editorFolder)
    {
        var folder = Path.GetFullPath(editorFolder).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var candidate in new[] { folder, Path.GetDirectoryName(folder) })
        {
            if (candidate is not null && File.Exists(Path.Combine(candidate, PackageMarker)))
                return candidate;
        }
        return null;
    }

    public static bool IsPackaged => PackageRoot(AppContext.BaseDirectory) is not null;

    // Abre o pacote baixado na pasta de espera e devolve o caminho dela, ou null se o pacote não servir.
    public static string? Unpack(string zip, string folder)
    {
        var staging = Path.Combine(folder, "nova-versao");
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(zip, staging);
            var editor = Path.Combine(staging, "editor", "CanisMajor.Editor.exe");
            return File.Exists(Path.Combine(staging, PackageMarker)) && File.Exists(editor) ? staging : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    // O script que espera o editor fechar, troca as pastas do pacote e abre o editor de novo.
    // O exemplo só entra se não existir: quem mexeu nele não perde o que fez.
    public const string ApplyScript = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [switch]$NoLaunch)
        $log = Join-Path $PSScriptRoot 'atualizar.log'
        try {
            $editor = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
            if ($editor) { $editor.WaitForExit(60000) | Out-Null }
            Start-Sleep -Milliseconds 300
            foreach ($dir in 'editor', 'src', 'native') {
                robocopy (Join-Path $Source $dir) (Join-Path $Target $dir) /MIR /R:20 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
                if ($LASTEXITCODE -ge 8) { throw "robocopy $dir terminou com $LASTEXITCODE" }
            }
            if (-not (Test-Path (Join-Path $Target 'examples'))) {
                robocopy (Join-Path $Source 'examples') (Join-Path $Target 'examples') /E /NFL /NDL /NJH /NJS /NP | Out-Null
            }
            Get-ChildItem $Source -File | ForEach-Object { Copy-Item $_.FullName $Target -Force }
            "atualizado $(Get-Date -Format s)" | Set-Content -Encoding utf8 $log
        } catch {
            "falhou: $_" | Set-Content -Encoding utf8 $log
        }
        if (-not $NoLaunch) { Start-Process (Join-Path $Target 'editor\CanisMajor.Editor.exe') }
        """;

    // Prepara e deixa rodando a troca de arquivos. Devolve falso se este editor não é de um pacote ou se o pacote não serve.
    public static bool StartUpdate(string zip) => StartUpdate(zip, AppContext.BaseDirectory, Environment.ProcessId, launch: true) is not null;

    // Devolve o processo do script, para os testes esperarem por ele.
    public static Process? StartUpdate(string zip, string editorFolder, int processId, bool launch)
    {
        if (PackageRoot(editorFolder) is not { } target)
            return null;
        var folder = Path.GetDirectoryName(Path.GetFullPath(zip))!;
        if (Unpack(zip, folder) is not { } staging)
            return null;
        try
        {
            var script = Path.Combine(folder, "atualizar.ps1");
            // Com BOM, para o PowerShell 5 ler os acentos como UTF-8.
            File.WriteAllText(script, ApplyScript.Replace("\r\n", "\n").Replace("\n", "\r\n"), new System.Text.UTF8Encoding(true));
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                         "-ProcessId", processId.ToString(System.Globalization.CultureInfo.InvariantCulture), "-Source", staging, "-Target", target })
                start.ArgumentList.Add(arg);
            if (!launch)
                start.ArgumentList.Add("-NoLaunch");
            return Process.Start(start);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<string> HashOfFileAsync(string path, CancellationToken cancel)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancel));
    }

    private static string? Fail(string partial)
    {
        TryDelete(partial);
        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Fica para a próxima vez: um arquivo velho na pasta não atrapalha.
        }
    }
}
