<div align="center">

# ✦ Canis Major

Engine de jogos **2D** feita **totalmente em Python**, com **shaders** e **elementos 3D** dentro da cena, e um editor com a interface e o estilo da IDE [Sirius](https://github.com/erickfuzer/Sirius).

![Python](https://img.shields.io/badge/Python_3.13-3776AB?style=for-the-badge&logo=python&logoColor=white)
![PySide6](https://img.shields.io/badge/PySide6-41CD52?style=for-the-badge&logo=qt&logoColor=white)
![ModernGL](https://img.shields.io/badge/ModernGL_·_OpenGL_3.3-5586A4?style=for-the-badge)
![Windows](https://img.shields.io/badge/Windows_10_e_11-0078D4?style=for-the-badge)

</div>

## O que tem

- **Editor no estilo do Sirius:** janela sem moldura, temas grafite e claro com as mesmas cores, fontes Geist e IBM Plex Mono, pílula <kbd>Ctrl</kbd> <kbd>K</kbd> no topo, árvore de recursos, folha arredondada com abas e a cápsula flutuante com **Executar**, linha e coluna, console, modo foco e troca de tema.
- **Editor de código:** Python e GLSL com sintaxe colorida, números de linha, recuo automático, pares que se fecham, comentar linhas (<kbd>Ctrl</kbd> <kbd>/</kbd>), mover e duplicar linhas (<kbd>Alt</kbd> <kbd>↑</kbd> <kbd>↓</kbd>), ir para linha, busca e autocompletar com a API da engine.
- **Editor de salas:** clique para pôr o objeto escolhido, arraste para mover (encaixa na grade, <kbd>Alt</kbd> solta), botão direito apaga, desfazer e refazer, cor de fundo, tamanho da sala e shaders de tela da sala.
- **Editor de sprites:** pixel art com lápis, borracha, balde e conta-gotas, cores rápidas, quadros de animação em tira, origem e prévia animada.
- **Editor de shaders com prévia ao vivo:** você escreve só a função `effect`. A prévia usa o mesmo montador do jogo, em tela inteira ou num sprite, e o erro de compilação aparece na linha certa.
- **Runtime:** janela pygame-ce com OpenGL 3.3 pelo ModernGL, sprites desenhados em lote, câmera, animação, texto, retângulos, linhas, círculos, sons, teclado, mouse, colisão por caixa e `move()` que para nas paredes.
- **3D dentro do 2D:** `Mesh.cube()`, `Mesh.sphere()`, `Mesh.plane()` ou `Mesh.load("modelo.obj")`, com luz e textura de um sprite, na mesma câmera do jogo.
- **Console com erros clicáveis:** se o jogo quebrar, o console abre e a linha do seu script fica sublinhada. Clicar abre o arquivo naquela linha.
- **Exportar para Windows:** gera uma pasta de uns 85 MB com Python próprio que roda em qualquer Windows 64 bits, sem instalar nada.
- **Atualização pela própria engine:** aviso discreto na cápsula quando sai versão nova no GitHub Releases, e "Atualizar agora" baixa, confere o SHA-256 registrado pelo GitHub, troca os arquivos e reabre o editor.

## Como usar

Com o ambiente já criado em `.venv`:

```bash
CanisMajor.cmd
```

Ou, pelo terminal:

```bash
.venv\Scripts\python -m canismajor                      # abre o editor
.venv\Scripts\python -m canismajor "D:\Jogos\Meu jogo"  # abre o editor com um projeto
.venv\Scripts\python -m canismajor "D:\Jogos\Meu jogo" --run   # só roda o jogo
```

Para criar o ambiente do zero (Python 3.11 ou mais novo):

```bash
python -m venv .venv
.venv\Scripts\python -m pip install -e .
```

## Um objeto do jogo

Cada arquivo em `objects/` é um objeto. Os eventos são métodos:

```python
from canismajor import GameObject, keys, game, draw, Mesh


class Jogador(GameObject):
    sprite = "jogador"      # sprites/jogador.png
    layer = 10              # maior fica por cima

    def create(self):       # uma vez, ao entrar na sala
        self.velocidade = 110

    def step(self, dt):     # a cada quadro; dt em segundos
        dx = keys.axis("left", "right")
        dy = keys.axis("up", "down")
        self.move(dx * self.velocidade * dt, dy * self.velocidade * dt, solid="parede")
        if self.collide("estrela"):
            game.play_sound("coleta")
        game.follow(self, smooth=0.2)

    def draw(self):         # o padrão desenha o próprio sprite
        self.draw_self()
        draw.text("Eu", self.x, self.y - 14, size=10, align="center")
```

| Onde | O que tem |
|---|---|
| `GameObject` | `x`, `y`, `scale_x`, `scale_y`, `rotation`, `color`, `alpha`, `frame`, `animation_speed`, `visible`, `shader`, `layer`; `move()`, `collide()`, `collides()`, `bbox()`, `destroy()`, `distance_to()`, `direction_to()` |
| `keys` | `held("left")`, `pressed("space")`, `released("z")`, `axis("a", "d")` |
| `mouse` | `x`, `y` (no mundo), `held(1)`, `pressed(1)`, `released(1)`, `wheel` |
| `game` | `goto("sala")`, `restart()`, `quit()`, `spawn("objeto", x, y)`, `instances("tipo")`, `first()`, `count()`, `follow()`, `play_sound()`, `set_uniform()`, `camera_x`, `camera_y`, `time`, `fps`, `data`, `post_shaders` |
| `draw` | `sprite()`, `text()`, `rect()`, `line()`, `circle()`, `mesh()`. Com `screen=True`, desenha preso à tela (placar, menu) |
| `Mesh` | `cube()`, `sphere()`, `plane()`, `load("arquivo.obj")` |

## Um shader

Em `shaders/nome.glsl`, escreva só a função `effect`. A engine completa o resto:

```glsl
vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    vec4 pixel = texture(tex, uv) * color;
    pixel.rgb *= 0.8 + 0.2 * sin(u_time * 4.0);
    return pixel;
}
```

- **Num sprite:** `self.shader = "nome"` no objeto.
- **Na tela inteira:** marque o shader em "Shaders de tela" no editor da sala, ou use `game.post_shaders`.
- Valem `u_time`, `u_resolution` e qualquer `uniform` extra, preenchido por `game.set_uniform("nome", "u_x", valor)`.

## Atalhos

| Atalho | Ação |
|---|---|
| <kbd>Ctrl</kbd> <kbd>K</kbd> | Barra de comando: recursos e ações |
| <kbd>F5</kbd> | Executar ou parar o jogo |
| <kbd>Ctrl</kbd> <kbd>S</kbd> / <kbd>Ctrl</kbd> <kbd>Shift</kbd> <kbd>S</kbd> | Salvar / salvar tudo |
| <kbd>Ctrl</kbd> <kbd>W</kbd> | Fechar aba |
| <kbd>Ctrl</kbd> <kbd>Z</kbd> / <kbd>Ctrl</kbd> <kbd>Y</kbd> | Desfazer / refazer (código, sala e sprite) |
| <kbd>Ctrl</kbd> <kbd>F</kbd> | Procurar no arquivo |
| <kbd>Ctrl</kbd> <kbd>G</kbd> | Ir para uma linha |
| <kbd>Ctrl</kbd> <kbd>/</kbd> | Comentar ou descomentar (no ABNT2, também <kbd>Ctrl</kbd> <kbd>;</kbd>) |
| <kbd>Alt</kbd> <kbd>↑</kbd> <kbd>↓</kbd> | Mover linhas. Com <kbd>Shift</kbd>, duplicar |
| <kbd>Ctrl</kbd> <kbd>J</kbd> | Console do jogo |
| <kbd>Ctrl</kbd> <kbd>.</kbd> | Modo foco |
| <kbd>Ctrl</kbd> <kbd>,</kbd> | Configurações |
| <kbd>Ctrl</kbd> <kbd>+</kbd> <kbd>-</kbd> <kbd>0</kbd> | Letra do código |
| Sprite: <kbd>B</kbd> <kbd>E</kbd> <kbd>G</kbd> <kbd>I</kbd> | Lápis, borracha, balde, conta-gotas |

O <kbd>AltGr</kbd> nunca vira atalho no editor de código, então `?` e `/` funcionam no ABNT2.

## Atualizações

1. Publique o código num repositório do GitHub.
2. Em cada versão nova, aumente `__version__` em `canismajor/__init__.py`, crie um Release com a tag (por exemplo `v0.2.0`) e anexe o zip do projeto com `canismajor` no nome (por exemplo `CanisMajor-0.2.0.zip`), sem a pasta `.venv`.
3. Em **Configurações, Atualizações**, informe `dono/repositorio`.

O editor consulta a API pública do GitHub no máximo uma vez por hora, sem enviar nada do seu projeto. Nada é baixado sem o seu clique, e o pacote só é usado se conferir com o SHA-256 que o GitHub registrou para o arquivo.

## Estrutura

```
CanisMajor/
├── canismajor/
│   ├── runtime/        # laço do jogo, renderizador ModernGL, malhas 3D, entrada e API dos scripts
│   ├── editor/         # editor PySide6: janela, código, salas, sprites, shaders, tema e atualização
│   ├── assets/         # fontes (OFL) e ícone
│   ├── project.py      # formato do projeto (projeto.json + pastas)
│   ├── shaderlib.py    # shaders da engine e montador da função effect
│   ├── templates.py    # modelos de projeto e de recursos
│   ├── export.py       # exportar para Windows
│   └── update_apply.py # aplicador da atualização
├── tests/              # python -m unittest discover -s tests
├── CanisMajor.cmd      # abre o editor
└── pyproject.toml
```

Um projeto de jogo é uma pasta com `projeto.json` e as subpastas `sprites/`, `objects/`, `rooms/`, `shaders/` e `sounds/`, tudo em texto ou PNG, fácil de versionar no Git.

## Limitações de hoje

- Só Windows, e a interface é só em português do Brasil.
- A colisão é por caixa alinhada, sem rotação. Não há física.
- Sem tilemap dedicado: as salas usam instâncias de objetos, inclusive para paredes.
- A prévia de shader e o jogo precisam de OpenGL 3.3.
- O jogo exportado leva o Python inteiro (uns 85 MB) e não vira um único .exe.

## Créditos

Interface inspirada na IDE [Sirius](https://github.com/erickfuzer/Sirius), de Erick Fuzer, sob a licença MIT. Fontes [Geist](https://vercel.com/font) e [IBM Plex Mono](https://www.ibm.com/plex/) sob a licença SIL Open Font, com os textos em `canismajor/assets/fonts`. Feito com [PySide6](https://doc.qt.io/qtforpython-6/) (LGPL), [ModernGL](https://github.com/moderngl/moderngl) (MIT), [pygame-ce](https://pyga.me) (LGPL), [NumPy](https://numpy.org) (BSD) e [Pillow](https://python-pillow.org) (MIT-CMU).

## Licença

[MIT](LICENSE).
