@echo off
rem Prepara este terminal para compilar o Canis Major: Rust e .NET 10 com tudo no D:.
rem Não muda nada permanente no Windows; vale só para esta janela.
set "RUSTUP_HOME=D:\Rust\rustup"
set "CARGO_HOME=D:\Rust\cargo"
set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
set "NUGET_PACKAGES=D:\NuGet\packages"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_NOLOGO=1"
set "PATH=%CARGO_HOME%\bin;%DOTNET_ROOT%;%PATH%"
echo Canis Major: ambiente pronto.
echo   cargo build --release --manifest-path core\Cargo.toml   compila o nucleo em Rust
echo   dotnet build CanisMajor.slnx -c Release                 compila a engine, o cm e o exemplo
echo   dotnet run --project examples\Exemplo -c Release        joga o exemplo
echo   dotnet test tests\CanisMajor.Tests -c Release           roda os testes da engine
echo   dotnet test tests\CanisMajor.Editor.Tests               roda os testes do editor
echo   Editor.cmd                                              abre o editor
