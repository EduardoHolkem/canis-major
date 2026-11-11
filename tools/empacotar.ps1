# Monta o pacote da engine para uma Release do GitHub: dist\CanisMajor-<versão>-win-x64.zip.
# Uso, num terminal preparado pelo ambiente.cmd:  powershell -File tools\empacotar.ps1 [-SemTestes]
#
# O pacote traz:
#   editor\      o editor, com o .NET dentro (abre sem instalar nada)
#   src\         a engine em C#, que os jogos compilam junto
#   native\      a canis_core.dll pronta (quem usa o pacote não precisa do Rust)
#   examples\    o jogo de exemplo
#   pacote.json  a versão; é o que faz o editor se atualizar sozinho
# Para fazer jogos, ainda é preciso o SDK do .NET 10.
param([switch]$SemTestes)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'pacote'
$dotnet = if ($env:DOTNET_ROOT -and (Test-Path (Join-Path $env:DOTNET_ROOT 'dotnet.exe'))) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' } else { 'dotnet' }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Check($step) {
    if ($LASTEXITCODE -ne 0) { throw "$step falhou (código $LASTEXITCODE)" }
}

# Copia uma pasta sem bin, obj, export e target.
function CopyTree($from, $to) {
    robocopy $from $to /E /XD bin obj export target .vs /XF *.user /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copiar $from falhou (robocopy $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
}

$editorProject = Join-Path $root 'src\CanisMajor.Editor\CanisMajor.Editor.csproj'
$version = (& $dotnet msbuild $editorProject -getProperty:Version).Trim()
Check 'Ler a versão'
Write-Output "Canis Major $version"

Write-Output 'Núcleo em Rust...'
& cargo build --release --manifest-path (Join-Path $root 'core\Cargo.toml')
Check 'cargo build'

if (-not $SemTestes) {
    Write-Output 'Testes...'
    & cargo test --release --lib --manifest-path (Join-Path $root 'core\Cargo.toml')
    Check 'Testes do núcleo'
    & $dotnet test (Join-Path $root 'tests\CanisMajor.Tests') -c Release
    Check 'Testes da engine'
    & $dotnet test (Join-Path $root 'tests\CanisMajor.Editor.Tests') -c Release
    Check 'Testes do editor'
}

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory $stage | Out-Null

Write-Output 'Editor...'
& $dotnet publish $editorProject -c Release -r win-x64 --self-contained true -o (Join-Path $stage 'editor') `
    -p:DebugType=none -p:DebugSymbols=false --nologo -v quiet
Check 'Publicar o editor'
Get-ChildItem (Join-Path $stage 'editor') -Recurse -Include *.pdb, CanisMajor.xml | Remove-Item -Force

Write-Output 'Engine, núcleo e exemplo...'
CopyTree (Join-Path $root 'src\CanisMajor') (Join-Path $stage 'src\CanisMajor')
New-Item -ItemType Directory (Join-Path $stage 'native') | Out-Null
Copy-Item (Join-Path $root 'core\target\release\canis_core.dll') (Join-Path $stage 'native')
CopyTree (Join-Path $root 'examples\Exemplo') (Join-Path $stage 'examples\Exemplo')
foreach ($file in 'README.md', 'LICENSE') { Copy-Item (Join-Path $root $file) $stage }
Set-Content -Encoding ascii (Join-Path $stage 'Canis Major.cmd') "@echo off`r`nstart `"`" `"%~dp0editor\CanisMajor.Editor.exe`" %*`r`n"
@{ name = 'Canis Major'; version = $version; built = (Get-Date -Format 'yyyy-MM-dd') } | ConvertTo-Json |
    Set-Content -Encoding utf8 (Join-Path $stage 'pacote.json')

$zip = Join-Path $dist "CanisMajor-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Output ''
Write-Output "Pacote: $zip ($size MB)"
Write-Output "SHA-256: $hash"
Write-Output ''
Write-Output "Para publicar: crie a Release v$version em https://github.com/EduardoHolkem/canis-major/releases/new"
Write-Output 'e anexe o .zip. O editor instalado acha a versão nova sozinho e confere o arquivo pelo SHA-256 do GitHub.'
