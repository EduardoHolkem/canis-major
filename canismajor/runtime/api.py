"""O que os scripts do jogo usam: GameObject, keys, mouse, game, draw e Mesh.

Cada arquivo em objects/ define uma classe filha de GameObject:

    from canismajor import GameObject, keys

    class Jogador(GameObject):
        sprite = "jogador"

        def create(self):
            self.velocidade = 120

        def step(self, dt):
            self.x += keys.axis("left", "right") * self.velocidade * dt
"""

from __future__ import annotations

import math

from .inputs import Keys, Mouse
from .mesh import Mesh

keys = Keys()
mouse = Mouse()

_engine = None  # preenchido pela Engine ao começar


def _require():
    if _engine is None:
        raise RuntimeError("A engine ainda não está rodando. Use isto dentro dos eventos do objeto (create, step, draw).")
    return _engine


class _GameProxy:
    """Acesso à engine que está rodando: `game.goto("sala")`, `game.spawn("estrela", x, y)` e outros."""

    def __getattr__(self, name):
        return getattr(_require(), name)

    def __setattr__(self, name, value):
        setattr(_require(), name, value)

    def __repr__(self):
        return "<game do Canis Major>"


game = _GameProxy()


class _Draw:
    """Desenho imediato, para usar dentro de `draw(self)`. Coordenadas do mundo, ou da tela com screen=True."""

    def sprite(self, name, x, y, frame=0, scale=1.0, rotation=0.0, color="#FFFFFF", alpha=1.0, shader=None, screen=False):
        _require().draw_sprite(name, x, y, frame, scale, rotation, color, alpha, shader, screen)

    def text(self, text, x, y, size=16, color="#FFFFFF", align="left", alpha=1.0, screen=False):
        return _require().draw_text(str(text), x, y, size, color, align, alpha, screen)

    def rect(self, x, y, w, h, color="#FFFFFF", alpha=1.0, outline=0.0, rotation=0.0, screen=False):
        _require().draw_rect(x, y, w, h, color, alpha, outline, rotation, screen)

    def line(self, x1, y1, x2, y2, color="#FFFFFF", width=1.0, alpha=1.0, screen=False):
        _require().draw_line(x1, y1, x2, y2, color, width, alpha, screen)

    def circle(self, x, y, radius, color="#FFFFFF", alpha=1.0, screen=False):
        _require().draw_circle(x, y, radius, color, alpha, screen)

    def mesh(self, mesh, x, y, size, rotation=(0.0, 0.0, 0.0), color="#FFFFFF", alpha=1.0, z=0.0, sprite=None,
             light=(-0.45, -0.65, 0.75), ambient=0.35):
        _require().draw_mesh(mesh, x, y, size, rotation, color, alpha, z, sprite, light, ambient)


draw = _Draw()


class GameObject:
    """Base de todo objeto do jogo. Sobrescreva os eventos create, step, draw e on_destroy."""

    sprite: str | None = None
    layer: int = 0  # maior fica por cima
    x: float = 0.0
    y: float = 0.0
    scale_x: float = 1.0
    scale_y: float = 1.0
    rotation: float = 0.0  # graus, sentido horário
    color = "#FFFFFF"
    alpha: float = 1.0
    frame: float = 0.0
    animation_speed: float | None = None  # quadros por segundo; None usa o do sprite
    visible: bool = True
    shader: str | None = None
    alive: bool = True
    kind: str = ""  # nome do arquivo em objects/, preenchido pela engine

    # ---------- eventos ----------

    def create(self):
        """Chamado uma vez, quando o objeto entra na sala."""

    def step(self, dt: float):
        """Chamado a cada quadro. dt é o tempo desde o quadro anterior, em segundos."""

    def draw(self):
        """Chamado a cada quadro para desenhar. O padrão desenha o próprio sprite."""
        self.draw_self()

    def on_destroy(self):
        """Chamado quando o objeto é destruído."""

    # ---------- ajudas ----------

    def draw_self(self):
        _require().draw_object(self)

    def destroy(self):
        _require().destroy(self)

    @property
    def width(self) -> float:
        spr = _require().sprite_info(self.sprite)
        return spr.width * abs(self.scale_x) if spr else 0.0

    @property
    def height(self) -> float:
        spr = _require().sprite_info(self.sprite)
        return spr.height * abs(self.scale_y) if spr else 0.0

    def bbox(self, dx: float = 0.0, dy: float = 0.0) -> tuple[float, float, float, float]:
        """Retângulo de colisão (esquerda, topo, direita, base), sem considerar a rotação."""
        spr = _require().sprite_info(self.sprite)
        if spr is None:
            return (self.x + dx, self.y + dy, self.x + dx, self.y + dy)
        ox, oy = spr.origin
        left = self.x + dx - ox * self.scale_x
        top = self.y + dy - oy * self.scale_y
        right = left + spr.width * self.scale_x
        bottom = top + spr.height * self.scale_y
        return (min(left, right), min(top, bottom), max(left, right), max(top, bottom))

    def collides(self, other: "GameObject", dx: float = 0.0, dy: float = 0.0) -> bool:
        if other is self or not other.alive:
            return False
        a = self.bbox(dx, dy)
        b = other.bbox()
        return a[0] < b[2] and a[2] > b[0] and a[1] < b[3] and a[3] > b[1]

    def collide(self, kind, dx: float = 0.0, dy: float = 0.0) -> "GameObject | None":
        """Primeiro objeto do tipo `kind` (nome ou classe) que encostaria em mim se eu andasse dx, dy."""
        for other in _require().instances(kind):
            if self.collides(other, dx, dy):
                return other
        return None

    def move(self, dx: float, dy: float, solid=None) -> tuple[bool, bool]:
        """Anda dx, dy parando ao encostar em `solid`. Devolve (bateu_na_horizontal, bateu_na_vertical)."""
        if solid is None:
            self.x += dx
            self.y += dy
            return False, False
        hit_x = self._move_axis(dx, 0, solid)
        hit_y = self._move_axis(0, dy, solid)
        return hit_x, hit_y

    def _move_axis(self, dx: float, dy: float, solid) -> bool:
        amount = dx or dy
        if amount == 0:
            return False
        if not self.collide(solid, dx, dy):
            self.x += dx
            self.y += dy
            return False
        sign = 1 if amount > 0 else -1
        step = 0.0
        while abs(step + sign) <= abs(amount):
            nx, ny = (step + sign, 0) if dx else (0, step + sign)
            if self.collide(solid, nx, ny):
                break
            step += sign
        if dx:
            self.x += step
        else:
            self.y += step
        return True

    def distance_to(self, other) -> float:
        return math.hypot(other.x - self.x, other.y - self.y)

    def direction_to(self, other) -> float:
        """Ângulo em graus até o outro objeto (0 = direita, 90 = baixo)."""
        return math.degrees(math.atan2(other.y - self.y, other.x - self.x))

    def __repr__(self):
        return f"<{type(self).__name__} {self.kind} em ({self.x:.0f}, {self.y:.0f})>"


__all__ = ["GameObject", "Mesh", "keys", "mouse", "game", "draw"]
