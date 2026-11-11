<div align="center">

# ✦ Canis Major

Engine de jogos **2D** com **núcleo em Rust** e **jogos em C#**, com **shaders** e **elementos 3D** dentro da cena.

![Rust](https://img.shields.io/badge/Rust-núcleo-B7410E?style=for-the-badge&logo=rust&logoColor=white)
![wgpu](https://img.shields.io/badge/wgpu-Vulkan_·_DirectX_12-5586A4?style=for-the-badge)
![C#](https://img.shields.io/badge/C%23_.NET_10-jogos-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows_10_e_11-0078D4?style=for-the-badge)

</div>

> [!NOTE]
> Fase 2 de 3. O núcleo, a API de jogos, o exemplo e o editor funcionam. Exportação e atualização vêm na fase 3.

## Como é feita

```
Seu jogo (C#)  ──►  CanisMajor.dll (C#)  ──P/Invoke──►  canis_core.dll (Rust)
 objects/*.cs        GameObject, Keys, Game,             janela (winit), GPU (wgpu),
 rooms/*.json        Draw, Mesh, salas, colisão          texto (fontdue), som (rodio),
 shaders/*.wgsl                                          malhas 3D, pós-processamento
```

- **O C# é dono do laço do jogo.** A cada quadro ele lê a entrada (`cm_poll`) e manda todo o desenho numa única chamada (`cm_frame`), num vetor de comandos. A ponte entre as linguagens custa quase nada.
- **O Rust desenha na placa de vídeo:** sprites instanciados em lote, texto, retângulos, linhas, círculos e malhas 3D com luz e teste de profundidade. Depois aplica a cadeia de shaders de tela e encaixa a imagem na janela com letterbox. Usa Vulkan quando há, ou DirectX 12, ou OpenGL.
- **Os erros apontam o seu código:** uma exceção mostra só as linhas dos seus scripts, no formato `arquivo.cs:linha`. Um shader com erro mostra `shaders/nome.wgsl:linha: mensagem`, conferido pelo naga antes de chegar à GPU.

## O editor

O editor nasceu do código da IDE [Sirius](https://github.com/erickfuzer/Sirius) e mantém tudo o que ela faz: abas, busca, terminal, Git, servidor de linguagem para C#, a cápsula do Executar. Num projeto de jogo, ele ganha:

- **Recursos do jogo na lista ao lado:** salas, objetos, sprites, shaders e sons, com o **+** para criar cada um. O botão **Arquivos** mostra a pasta como ela é.
- **Editor de salas:** a sala desenhada com os sprites de verdade. Escolha um objeto no pincel e clique para colocar, arraste para pintar vários, arraste uma instância para mover, use o botão direito para apagar. A grade prende as posições, e o Alt solta. A roda aproxima, e o espaço com o arrasto move a vista. O botão **Código** mostra o JSON, e Ctrl Z desfaz pelos dois lados.
- **Inspetor:** à direita, os dados da instância escolhida (objeto, X, Y e propriedades a mais, como `speed: 2`) e os da sala (tamanho, grade, fundo, shaders de tela).
- **Sprites:** o quadro animado em tamanho grande, a tira de quadros, e a origem marcada com um clique. Quadros, velocidade e origem se mudam no Inspetor.
- **Shaders conferidos enquanto você digita:** o editor usa o mesmo conferidor do jogo (naga, pela `canis_core.dll`), sublinha a linha do erro e conta os erros na cápsula.
- **Executar (F5):** compila e abre o jogo. Um erro no jogo aparece no terminal com `arquivo.cs:linha`, e o clique leva até a linha.
- **Novo jogo:** a partir do exemplo ou vazio, numa pasta à sua escolha.

Para abrir, rode `Editor.cmd` (compila na primeira vez).

## Um objeto do jogo

Cada arquivo em `objects/` é uma classe. A sala se refere a ela pelo nome da classe:

```csharp
using CanisMajor;

namespace MeuJogo;

public class Jogador : GameObject
{
    public Jogador()
    {
        Sprite = "jogador";      // sprites/jogador.png
        Layer = 10;              // maior fica por cima
    }

    public override void OnStep(float dt)
    {
        var dx = Keys.Axis("left", "right");
        var dy = Keys.Axis("up", "down");
        Move(dx * 110 * dt, dy * 110 * dt, solid: "Parede");
        if (Collide<Estrela>() is { } estrela) estrela.Destroy();
        Game.Follow(this, smooth: 0.2f);
    }

    public override void OnDraw()
    {
        DrawSelf();
        Draw.Text("Eu", X, Y - 14, size: 10, align: Align.Center);
    }
}
```

| Onde | O que tem |
|---|---|
| `GameObject` | `X`, `Y`, `ScaleX`, `ScaleY`, `Rotation`, `Color`, `Alpha`, `Frame`, `AnimationSpeed`, `Visible`, `Shader`, `Layer`, `Sprite`; eventos `OnCreate`, `OnStep`, `OnDraw`, `OnDestroy`; `Move()`, `Collide()`, `Collide<T>()`, `BBox()`, `Destroy()`, `DistanceTo()`, `DirectionTo()` |
| `Keys` | `Held("left")`, `Pressed("space")`, `Released("z")`, `Axis("a", "d")` |
| `Mouse` | `X`, `Y` (no mundo), `ScreenX`, `ScreenY`, `Held(1)`, `Pressed(1)`, `Released(1)`, `Wheel` |
| `Game` | `Goto()`, `Restart()`, `Quit()`, `Spawn<T>()`, `Instances<T>()`, `First<T>()`, `Count()`, `Follow()`, `PlaySound()`, `SetShaderParam()`, `CameraX`, `CameraY`, `Time`, `Fps`, `Data`, `PostShaders` |
| `Draw` | `Sprite()`, `Text()`, `Rect()`, `Line()`, `Circle()`, `Mesh()`; `screen: true` prende à tela |
| `Mesh` | `Cube()`, `Sphere()`, `Plane()`, `Load("modelo.obj")` |

Cores aceitam texto: `Color = "#9DB8E0"`.

## Um shader

Em `shaders/nome.wgsl`, escreva só a função `effect`. A engine completa o resto:

```wgsl
fn effect(uv: vec2<f32>, color: vec4<f32>) -> vec4<f32> {
    let pixel = sample(uv) * color;
    return vec4<f32>(pixel.rgb * (0.8 + 0.2 * sin(time() * 4.0)), pixel.a);
}
```

- Ajudas: `sample(uv)`, `time()`, `resolution()` e `param(0)` a `param(3)`, preenchidos por `Game.SetShaderParam("nome", 0, x, y, z, w)`. O uv `(0, 0)` é o canto de cima à esquerda.
- **Num sprite:** `Shader = "nome"` no objeto.
- **Na tela inteira:** coloque o nome em `post_shaders` na sala, ou use `Game.PostShaders`.

## Como compilar e jogar

Requisitos: Rust (GNU, sem precisar do Visual Studio) e o SDK do .NET 10. Num terminal, rode `ambiente.cmd` e depois:

```bash
cargo build --release --manifest-path core\Cargo.toml
```

```bash
dotnet build CanisMajor.slnx -c Release
```

```bash
dotnet run --project examples\Exemplo -c Release
```

Para criar um jogo novo:

```bash
src\CanisMajor.Cli\bin\Release\net10.0\cm.exe new D:\Jogos\MeuJogo --name "Meu jogo"
```

O jogo também aceita `--room sala`, `--frames N` e `--screenshot foto.png`. A variável `CANIS_BACKEND=vulkan|dx12|gl` força a API gráfica, e `CANIS_DEBUG=1` mostra qual placa e qual API foram usadas.

## Testes

```bash
cargo test --release --lib --manifest-path core\Cargo.toml
```

```bash
dotnet test tests\CanisMajor.Tests -c Release
```

```bash
dotnet test tests\CanisMajor.Editor.Tests
```

Os testes da engine incluem rodar o exemplo de verdade e conferir a imagem, e quebrar um shader de propósito para conferir a linha do erro. Os do editor usam a janela de verdade, sem tela: os que vieram do Sirius e os do editor de jogos (árvore de recursos, colocar e apagar na sala, desfazer, sprite, shader com erro, criar objeto e jogo novo).

## Estrutura

```
CanisMajor/
├── core/                 # núcleo em Rust (canis_core.dll)
│   └── src/              # lib.rs (funções exportadas), gfx.rs (wgpu), shader.rs, text.rs, mesh.rs, audio.rs, keys.rs
├── src/
│   ├── CanisMajor/       # API de jogos em C#: GameObject, Game, Draw, Keys, salas, PNG e modelos
│   ├── CanisMajor.Cli/   # cm.exe: cria projetos
│   └── CanisMajor.Editor/ # o editor (Avalonia), a partir da IDE Sirius; Game/ tem a sala, o sprite e os recursos
├── examples/Exemplo/     # jogo de exemplo: jogador, estrelas, paredes, cubo 3D, placar e shaders
├── tests/                # testes da engine e do editor
├── assets/               # fontes (OFL) e ícone
├── ambiente.cmd          # prepara o terminal com Rust e .NET no D:
├── Editor.cmd            # abre o editor
└── CanisMajor.slnx
```

## Próximas fases

- **Fase 3, entrega:** exportar o jogo (`dotnet publish`, com opção de Native AOT, mais a `canis_core.dll`) e atualização da engine pelo GitHub Releases.

## Limitações de hoje

- Só Windows, e a interface é em português do Brasil.
- Colisão por caixa alinhada, sem rotação. Não há física.
- Abrir o jogo leva alguns segundos na primeira vez, enquanto o driver compila os pipelines.
- O editor lê o sprite e a camada de cada objeto procurando `Sprite = "..."` e `Layer = N` no `.cs`, sem compilar. Um sprite escolhido por código, em tempo de jogo, não aparece na sala.
- A sala do editor não desenha os objetos 3D nem o que um objeto desenha por conta própria em `OnDraw`. Esses objetos aparecem como um marcador com o nome.
- Do Sirius vieram também ferramentas de PHP (servidor, Xdebug) que um jogo não usa. Continuam lá, sem atrapalhar.
- O `Cargo.lock` fixa `libloading 0.8.6`, `parking_lot 0.12.3`, `parking_lot_core 0.9.10` e `winapi-util 0.1.9`. As versões mais novas exigem o `as.exe` do MinGW, que o Rust GNU não traz.

## Créditos

Fontes [Geist](https://vercel.com/font) e [IBM Plex Mono](https://www.ibm.com/plex/) sob a licença SIL Open Font (textos em `assets/fonts`). Feito com [wgpu](https://wgpu.rs) e [naga](https://github.com/gfx-rs/wgpu) (MIT/Apache), [winit](https://github.com/rust-windowing/winit) (Apache), [fontdue](https://github.com/mooman219/fontdue) (MIT/Apache), [rodio](https://github.com/RustAudio/rodio) (MIT/Apache), [glam](https://github.com/bitshifter/glam-rs) (MIT/Apache) e [image](https://github.com/image-rs/image) (MIT/Apache). O editor é feito a partir da IDE [Sirius](https://github.com/erickfuzer/Sirius), de Erick Fuzer, sob a licença MIT (texto em `src/CanisMajor.Editor/LICENSE-Sirius.txt`), com [Avalonia](https://avaloniaui.net), [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit) e [TextMateSharp](https://github.com/danipen/TextMateSharp) (MIT), e os ícones do [Material Icon Theme](https://github.com/material-extensions/vscode-material-icon-theme) (MIT).

## Licença

[MIT](LICENSE).
