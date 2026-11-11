@echo off
rem Abre o editor do Canis Major. As variáveis valem só para ele e para o que ele abrir, como o jogo.
setlocal
set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
set "NUGET_PACKAGES=D:\NuGet\packages"
rem O cache de downloads do NuGet também fica no D:, porque o C: deste computador está quase cheio.
set "NUGET_HTTP_CACHE_PATH=D:\canis major\NuGet\http-cache"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "PATH=%DOTNET_ROOT%;%PATH%"
set "EDITOR=%~dp0src\CanisMajor.Editor\bin\Release\net10.0\CanisMajor.Editor.exe"
if not exist "%EDITOR%" (
    echo Compilando o editor pela primeira vez...
    "%DOTNET_ROOT%\dotnet.exe" build "%~dp0src\CanisMajor.Editor" -c Release || exit /b 1
)
start "" "%EDITOR%" %*
