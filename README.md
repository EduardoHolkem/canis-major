<div align="center">

# ✦ Canis Major

Engine de jogos **2D** com **núcleo em Rust** e **jogos em C#**, com **shaders** e **elementos 3D** dentro da cena.

![Rust](https://img.shields.io/badge/Rust-núcleo-B7410E?style=for-the-badge&logo=rust&logoColor=white)
![wgpu](https://img.shields.io/badge/wgpu-Vulkan_·_DirectX_12-5586A4?style=for-the-badge)
![C#](https://img.shields.io/badge/C%23_.NET_10-jogos-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![Windows](https://img.shields.io/badge/Windows_10_e_11-0078D4?style=for-the-badge)

</div>

> [!NOTE]
> Versão 0.3. O núcleo, a API de jogos, o editor, a exportação de jogos e a atualização pelo GitHub Releases funcionam.

## Instalar

Baixe o `CanisMajor-<versão>-win-x64.zip` da página de [Releases](https://github.com/EduardoHolkem/canis-major/releases), descompacte numa pasta sua e abra `Canis Major.cmd`. O editor traz o .NET dentro e abre sem instalar nada. Para **fazer** jogos, instale também o [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0); o Rust não é preciso, porque o núcleo já vem pronto em `native\`.

Quando sai uma versão nova, o editor avisa na cápsula. "Atualizar agora" baixa o pacote, confere o SHA-256 que o GitHub registrou, fecha, troca os arquivos e abre de novo. O seu exemplo, os seus jogos e as preferências ficam como estão.

## Exportar um jogo

No editor, **+ › Exportar jogo** (ou Ctrl K, "Exportar jogo"). Na linha de comando:

```bash
cm export D:\Jogos\MeuJogo
```

Sai em `export\` uma pasta com o `.exe` (o .NET vai dentro, num arquivo só), a `canis_core.dll` e os recursos, e um `.zip` dela para mandar a quem vai jogar. Quem joga não instala nada. O `.exe` abre sem janela de console; se o jogo der erro, a mensagem aparece numa caixa e fica em `erro.txt`. Com `--sem-dotnet`, o pacote fica bem menor, mas quem joga precisa do .NET 10.

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

## Objetos e componentes

Cada objeto tem uma **ficha** (`objects/Nome.json`), editada no Inspetor: sprite, shader, profundidade, visível, ordenar por Y, cor, alpha, escala e **componentes**. O script C# (`objects/Nome.cs`) é opcional e continua sendo o lugar do código. A ficha é aplicada depois do construtor, então o que estiver nela vale mais.

No editor, abra o objeto e clique em **Objeto** no alto para ver a ficha (o botão **Script** volta para o código). Um objeto só com ficha, sem script, já funciona e aparece na lista e no pincel da sala.

Componentes prontos, escolhidos em **+ Componente**:

| Componente | O que faz |
|---|---|
| `Motion` | Velocidade X e Y, gravidade, atrito, velocidade máxima e o tipo de objeto sólido em que para |
| `Collider` | Máscara de colisão própria (deslocamento, largura e altura), com a opção de desenhar para conferir |
| `ShaderParams` | Manda valores para o `param(0)` do shader do objeto, com o tempo do jogo se quiser |
| `Lifetime` | Destrói o objeto depois de alguns segundos |

Os seus componentes são classes que herdam de `Component`, com `Owner` (o objeto) e os eventos `OnCreate`, `OnStep`, `OnDraw` e `OnDestroy`. No código, `Add<Motion>()`, `Get<Motion>()` e `Remove()` mexem nos componentes durante o jogo.

```json
{
  "sprite": "jogador",
  "layer": 10,
  "components": [
    { "type": "Motion", "gravity": 600, "solid": "Parede" },
    { "type": "Collider", "offset_x": -6, "offset_y": 0, "width": 12, "height": 8 }
  ]
}
```

## Salas e camadas

Como no GameMaker, a sala é uma pilha de **camadas**. No editor, o Inspetor da sala mostra a lista de camadas (a de cima fica na frente), com o olho para esconder, **+ Instâncias** e **+ Fundo** para criar, **Subir** e **Descer** para mudar a ordem. O pincel coloca as instâncias na camada ativa, e cada instância pode trocar de camada no Inspetor.

- **Camada de instâncias:** guarda objetos. Com **Ordenar por Y**, quem está mais embaixo na tela fica na frente, e o personagem passa atrás do poste sem código. Um objeto ajusta o ponto de ordem com `SortOffset` (por exemplo, o pé de um poste alto) ou foge da regra com `YSort = false` e `Layer`.
- **Camada de fundo:** cor, imagem (sprite) repetida na horizontal e na vertical, e **paralaxe** (0 presa ao mundo, 1 presa à tela).
- **Salas:** no Inspetor, **▶ Testar esta sala**, **★ Sala inicial** e **Duplicar sala**. A sala inicial aparece com ★ na lista.

No arquivo, as camadas ficam em `layers`, e a instância diz a camada em `layer` (sem `layer`, vai para a primeira de instâncias). Salas sem `layers` continuam funcionando como antes.

```json
"layers": [
  { "name": "Ceu", "kind": "background", "depth": -100, "color": "#203040", "sprite": "nuvens", "tile_x": true, "parallax": 0.5 },
  { "name": "Atores", "kind": "instances", "depth": 0, "y_sort": true }
],
```

No código, `Game.Layer("Ceu")` devolve a camada (dá para mudar `Visible`, `Depth`, `Color` e `Parallax` durante o jogo), e `Game.SetLayerVisible("Frente", false)` esconde a camada e as instâncias dela.

## Tilesets e tiles

Um **tileset** (`tilesets/nome.json`) é uma imagem de `sprites/` cortada numa grade. Crie pelo **+ › Novo tileset**, escolha a imagem e o tamanho do tile no Inspetor e clique nos tiles para marcar os **sólidos** (ficam em vermelho). O tile 0 é o do canto de cima à esquerda, e a conta segue pela linha.

Na sala, **+ Tiles** cria uma camada de tiles. Com ela ativa, o Inspetor mostra a paleta: clique no tile para pôr no pincel. Na sala, o botão esquerdo pinta, o direito apaga, e arrastar continua. "Mostrar os tiles sólidos em vermelho" deixa a colisão à vista, e "Ajustar ao tamanho da sala" recalcula as colunas depois de mudar a largura. Uma sala pode ter várias camadas de tiles (chão, paredes, decoração na frente do personagem), cada uma na sua profundidade.

- **Colisão:** `Move(dx, dy, solid: "Chao")` para nos tiles sólidos da camada "Chao", do mesmo jeito que para em objetos. O componente `Motion` aceita o nome da camada em `Solid`. O movimento confere o caminho inteiro, então uma queda rápida não atravessa o chão.
- **No código:** `TouchingTiles("Chao", 0, 1)` diz se está pisando no chão, `Game.TileAt("Chao", x, y)` lê o tile num ponto e `Game.SetTile("Chao", x, y, -1)` apaga (para quebrar blocos ou abrir passagens).
- Só os tiles que a câmera vê são desenhados, então mapas grandes continuam leves.

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

Os testes da engine incluem rodar o exemplo de verdade e conferir a imagem, e quebrar um shader de propósito para conferir a linha do erro. Os do editor usam a janela de verdade, sem tela: os que vieram do Sirius e os do editor de jogos (árvore de recursos, colocar e apagar na sala, desfazer, sprite, shader com erro, criar objeto e jogo novo, exportar um jogo com o `dotnet publish` de verdade, e o script de atualização trocando os arquivos de um pacote).

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
├── tools/empacotar.ps1   # monta o pacote da Release em dist\
└── CanisMajor.slnx
```

## Publicar uma versão

1. Suba a `<Version>` em `src/CanisMajor/CanisMajor.csproj`, `src/CanisMajor.Editor/CanisMajor.Editor.csproj` e `src/CanisMajor.Cli/CanisMajor.Cli.csproj`.
2. Num terminal preparado pelo `ambiente.cmd`, rode `powershell -File tools\empacotar.ps1`. Ele compila, testa e gera `dist\CanisMajor-<versão>-win-x64.zip`.
3. Crie a Release `v<versão>` no GitHub e anexe o `.zip`. Os editores instalados acham a versão nova sozinhos.

## Limitações de hoje

- Sem Native AOT: ele precisaria das ferramentas de C++ do Visual Studio, e a engine cria os objetos das salas pelo nome da classe, o que o AOT corta. O jogo exportado leva o .NET inteiro dentro (cerca de 37 MB zipado).

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
