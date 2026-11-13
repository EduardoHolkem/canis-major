# Canis Major

Engine de jogos 2D do Eduardo, inspirada no GameMaker. Núcleo em Rust (wgpu), jogos em C# .NET 10 e editor feito a partir da IDE Sirius (Avalonia). Repositório público `EduardoHolkem/canis-major`, branch `main`. O índice de todos os projetos fica em `D:\codigo\contexto.md`.

## Como responder

- Sempre em português do Brasil, inclusive nos comentários do código e nas mensagens de commit.
- No texto corrido, evitar travessão e dois pontos (usar vírgula, ponto final ou parênteses). Em código, comandos e caminhos pode usar normalmente.
- Windows 10 e PowerShell 5.1. Caminhos com espaço entre aspas, e cada comando com a explicação do que faz.
- Antes de dar instruções sobre uma ferramenta, pesquisar na web a informação atual.
- Antes de apagar qualquer coisa, perguntar. O disco C está quase cheio, então nada de gravar no C (usar `D:\tmp` como `TEMP`).
- O usuário aprova cada entrega com "Pode enviar". Só fazer commit e push depois disso. Nunca commitar a mudança dele no `Editor.cmd`.

## Estrutura

| Pasta | O que tem |
|---|---|
| `core\src` | Núcleo em Rust: janela (winit), desenho (`gfx.rs`), luz (`light.rs`), texto, som, malhas, shaders e a ponte (`lib.rs`) |
| `src\CanisMajor` | A engine em C#: `Runtime` (laço do jogo), `GameObject`, componentes (`Components.cs`, `Lighting.cs`, `AI.cs`), camadas e tiles, `Pathfinding`, `Png`, `ShaderPresets` |
| `src\CanisMajor.Editor` | O editor (Sirius adaptado). A parte de jogo fica em `MainWindow.Game.cs`, `MainWindow.Layers.cs`, `MainWindow.Objects.cs`, `MainWindow.Tilesets.cs`, `MainWindow.SpriteEditing.cs`, `MainWindow.Lighting.cs`, `MainWindow.AI.cs`, `MainWindow.Blocks.cs` e na pasta `Game` |
| `src\CanisMajor.Cli` | O `cm` (novo jogo, exportar) |
| `examples\Exemplo` | Jogo de exemplo, com as salas `inicio` e `noite` |
| `tests` | Testes da engine (`CanisMajor.Tests`) e do editor (`CanisMajor.Editor.Tests`) |
| `tools\empacotar.ps1` | Gera o zip de Releases em `dist` |

Formato dos arquivos do jogo: `projeto.json`, `rooms/*.json` (com `layers` e `lighting`), `objects/Nome.cs` (script) e `objects/Nome.json` (ficha com componentes), `sprites/nome.png` e `nome.json` (quadros, origem, máscara), `sprites/nome_n.png` (normal map), `tilesets/*.json`, `shaders/*.wgsl` e `sounds/`.

## Compilar e testar

O `ambiente.cmd` prepara o terminal. Na mão:

```powershell
$env:DOTNET_ROOT="$env:LOCALAPPDATA\Microsoft\dotnet"; $env:NUGET_PACKAGES="D:\NuGet\packages"; $env:RUSTUP_HOME="D:\Rust\rustup"; $env:CARGO_HOME="D:\Rust\cargo"
```

- Núcleo: `cargo build --release --manifest-path core\Cargo.toml` e `cargo test --release --lib --manifest-path core\Cargo.toml`.
- Engine: `dotnet test tests\CanisMajor.Tests -c Release`.
- Editor: `dotnet test tests\CanisMajor.Editor.Tests` (demora uns 7 minutos, melhor rodar em segundo plano).
- Testes que mexem no estado global do `Runtime` usam `[Collection("Runtime")]`.
- `SingleInstanceTests.Varias_ao_mesmo_tempo_chegam_todas` é instável (passa sozinho). `SvgWindowTests.Svg_abre_com_o_desenho_ao_lado` falha só junto com os outros de SVG, já no código antigo. `McpTests.Claude_pergunta_pelo_canal_e_a_janela_responde` às vezes falha na rodada completa, mas passa sozinho (conferido 4 vezes em 13/11/2026).
- Conferir o visual: `dotnet run --project examples\Exemplo -c Release -- --room noite --frames 60 --screenshot D:\tmp\foto.png` (jogo) e `CanisMajor.Editor.exe <pasta> --open <arquivo> --blocks --shot D:\tmp\editor.png` (editor). A GPU é uma Radeon RX 560.
- O `.csproj` dos jogos precisa levar `objects\*.json` e `tilesets\**` no `Content`.

## Histórico

| Commit | O que entrou |
|---|---|
| `82f4372` | Salas com camadas, Ordenar por Y, sala inicial, duplicar e testar sala (etapas 1 e 6) |
| `35c6053` | Objetos com componentes e ficha no Inspetor (etapa 2) |
| `8138b09` | Tileset, camadas de tiles e colisão com tiles; movimento que não atravessa paredes (etapa 3) |
| `d0b7a2b` | Editor de sprites completo e máscara de colisão (etapa 4) |
| `7dc1de5` | Iluminação 2D, sombras, normal maps e shaders prontos (etapa 5) |
| `b96ee78` | Objeto com IA e pathfinding A* (etapa 7) |
| `8a1d4ef` | C# Código e C# Blocos, conversíveis (etapa 8) |
| `22b3b3c` | Versão 0.4.0. O zip está em `dist` e a Release está em rascunho no GitHub |

## Próximos passos

Da lista `C:\Users\Holkem\Downloads\Mecanicas-GameMaker-Canis-Major.md`, nesta ordem:

1. ~~Eventos que faltam: Alarm, Collision, teclado, mouse, Begin e End Step e Draw GUI~~ (feito em 12/11/2026).
2. Parent Objects (herança) e Room Creation Code.
3. Partículas, câmeras e viewports.
4. Sequences e Animation Curves.

Também faltam Paths, física, Surfaces, Fonts, Timelines, GamePad, saves, rede, UI Layers e as ferramentas de Debugger e Profiler.
