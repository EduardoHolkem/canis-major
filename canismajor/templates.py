"""Modelos de projeto e de recursos novos."""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image

from .project import KINDS, Project, save_room, save_sprite_meta, write_text_atomic

NEW_OBJECT = '''from canismajor import GameObject, keys, game, draw


class {cls}(GameObject):
    sprite = None

    def create(self):
        """Roda uma vez, quando o objeto entra na sala."""

    def step(self, dt):
        """Roda a cada quadro. dt é o tempo desde o quadro anterior, em segundos."""

    def draw(self):
        self.draw_self()
'''

NEW_SHADER = '''// Recebe a textura, a coordenada (uv) e a cor do sprite. Devolve a cor final do pixel.
// Também valem u_time (segundos) e u_resolution (tamanho do jogo em pixels).
vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    vec4 pixel = texture(tex, uv) * color;
    return pixel;
}
'''

GITIGNORE = "__pycache__/\n*.pyc\n"


def class_name(name: str) -> str:
    return "".join(part.capitalize() for part in name.split("_")) or "Objeto"


def _make_folders(root: Path) -> None:
    for folder, _ in KINDS.values():
        (root / folder).mkdir(parents=True, exist_ok=True)


def create_project(root: Path, name: str, template: str = "exemplo") -> Project:
    root = Path(root)
    if root.exists() and any(root.iterdir()):
        raise FileExistsError(f"A pasta {root} já existe e não está vazia.")
    root.mkdir(parents=True, exist_ok=True)
    _make_folders(root)
    project = Project(root=root.resolve(), name=name)
    project.save()
    write_text_atomic(root / ".gitignore", GITIGNORE)
    if template == "exemplo":
        _example(project)
    else:
        save_room(project.path("room", "inicio"), {
            "width": project.width, "height": project.height, "background": "#16181D",
            "grid": 16, "post_shaders": [], "instances": [],
        })
    return project


# ---------- pixel art do exemplo ----------

def _hex(c: str, a: int = 255) -> tuple[int, int, int, int]:
    c = c.lstrip("#")
    return int(c[0:2], 16), int(c[2:4], 16), int(c[4:6], 16), a


def _player_image() -> Image.Image:
    img = Image.new("RGBA", (16, 16), (0, 0, 0, 0))
    px = img.load()
    cx, cy, r = 7.5, 8.0, 6.6
    for y in range(16):
        for x in range(16):
            d = math.hypot(x + 0.5 - cx - 0.5, y + 0.5 - cy)
            if d <= r:
                if d > r - 1.1:
                    px[x, y] = _hex("#1B2A44")
                else:
                    light = (x + y) < 12
                    px[x, y] = _hex("#C9D9F2" if light else "#82AAFF")
    for ex in (5, 10):
        px[ex, 7] = _hex("#FFFFFF")
        px[ex, 8] = _hex("#16181D")
    px[7, 11] = px[8, 11] = _hex("#1B2A44")
    return img


def _star_strip() -> Image.Image:
    frames = 4
    img = Image.new("RGBA", (8 * frames, 8), (0, 0, 0, 0))
    px = img.load()
    for f, arm in enumerate((2, 3, 4, 3)):
        for y in range(8):
            for x in range(8):
                dx, dy = abs(x - 3.5), abs(y - 3.5)
                on_axis = (dx < 1 and dy <= arm) or (dy < 1 and dx <= arm)
                core = dx + dy <= 2
                if core and dx < 1 and dy < 1:
                    px[f * 8 + x, y] = _hex("#FFFFFF")
                elif core or on_axis:
                    px[f * 8 + x, y] = _hex("#F5C65B")
    return img


def _wall_image() -> Image.Image:
    img = Image.new("RGBA", (16, 16), _hex("#2C2F35"))
    px = img.load()
    for i in range(16):
        px[i, 0] = px[0, i] = _hex("#3F434B")
        px[i, 15] = px[15, i] = _hex("#1C1E22")
    for x, y in ((4, 5), (10, 3), (7, 10), (12, 11), (3, 12)):
        px[x, y] = _hex("#34373E")
    return img


def _blip_wav(path: Path) -> None:
    """Um 'plim' curto subindo de tom, sintetizado na hora."""
    import struct
    import wave

    rate, length = 22050, 0.16
    frames = bytearray()
    phase = 0.0
    for i in range(int(rate * length)):
        t = i / rate
        freq = 880 + 900 * t / length
        phase += 2 * math.pi * freq / rate
        envelope = min(1.0, t * 200) * (1 - t / length) ** 2
        frames += struct.pack("<h", int(math.sin(phase) * envelope * 12000))
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(bytes(frames))


def _example(project: Project) -> None:
    sprites = project.folder("sprite")
    _player_image().save(sprites / "jogador.png")
    save_sprite_meta(sprites / "jogador.png", {"frames": 1, "origin": [8, 8], "fps": 8})
    _star_strip().save(sprites / "estrela.png")
    save_sprite_meta(sprites / "estrela.png", {"frames": 4, "origin": [4, 4], "fps": 6})
    _wall_image().save(sprites / "parede.png")
    save_sprite_meta(sprites / "parede.png", {"frames": 1, "origin": [0, 0], "fps": 8})

    _blip_wav(project.folder("sound") / "coleta.wav")

    objects = project.folder("object")
    for name, code in EXAMPLE_OBJECTS.items():
        write_text_atomic(objects / f"{name}.py", code)
    shaders = project.folder("shader")
    for name, code in EXAMPLE_SHADERS.items():
        write_text_atomic(shaders / f"{name}.glsl", code)

    w, h, t = project.width, project.height, 16
    instances = []
    for x in range(0, w, t):
        instances.append({"object": "parede", "x": x, "y": 0})
        instances.append({"object": "parede", "x": x, "y": h - t})
    for y in range(t, h - t, t):
        instances.append({"object": "parede", "x": 0, "y": y})
        instances.append({"object": "parede", "x": w - t, "y": y})
    for x in range(160, 320, t):
        instances.append({"object": "parede", "x": x, "y": 96})
    instances += [
        {"object": "jogador", "x": 80, "y": 140},
        {"object": "estrela", "x": 200, "y": 60},
        {"object": "estrela", "x": 300, "y": 180},
        {"object": "estrela", "x": 120, "y": 210},
        {"object": "cubo", "x": 390, "y": 70},
        {"object": "placar", "x": 0, "y": 0},
    ]
    save_room(project.path("room", "inicio"), {
        "width": w, "height": h, "background": "#16181D", "grid": t,
        "post_shaders": ["vinheta"], "instances": instances,
    })


EXAMPLE_OBJECTS = {
    "jogador": '''from canismajor import GameObject, keys, game


class Jogador(GameObject):
    sprite = "jogador"
    layer = 10

    def create(self):
        self.velocidade = 110

    def step(self, dt):
        dx = keys.axis("left", "right") or keys.axis("a", "d")
        dy = keys.axis("up", "down") or keys.axis("w", "s")
        if dx and dy:
            dx, dy = dx * 0.7071, dy * 0.7071
        self.move(dx * self.velocidade * dt, dy * self.velocidade * dt, solid="parede")
        if dx:
            self.scale_x = 1 if dx > 0 else -1

        estrela = self.collide("estrela")
        if estrela:
            estrela.coletar()

        if keys.pressed("r"):
            game.restart()
''',
    "estrela": '''import random

from canismajor import GameObject, game


class Estrela(GameObject):
    sprite = "estrela"
    shader = "brilho"
    layer = 5

    def create(self):
        self.base_y = self.y
        self.fase = random.uniform(0, 6.28)
        self.scale_x = self.scale_y = 2

    def step(self, dt):
        import math
        self.y = self.base_y + math.sin(game.time * 3 + self.fase) * 3
        self.rotation += 40 * dt

    def coletar(self):
        game.data["pontos"] = game.data.get("pontos", 0) + 1
        game.play_sound("coleta")
        self.destroy()
        # Nasce outra num lugar livre.
        for _ in range(50):
            x, y = random.randint(32, game.room_width - 32), random.randint(32, game.room_height - 32)
            nova = game.spawn("estrela", x, y)
            if not nova.collide("parede"):
                break
            nova.destroy()
''',
    "parede": '''from canismajor import GameObject


class Parede(GameObject):
    sprite = "parede"
''',
    "cubo": '''from canismajor import GameObject, Mesh, draw, game


class Cubo(GameObject):
    """Um elemento 3D dentro do jogo 2D: um cubo iluminado que gira."""

    layer = 4

    def create(self):
        self.malha = Mesh.cube()

    def draw(self):
        t = game.time
        draw.mesh(self.malha, self.x, self.y, 34, rotation=(t * 40, t * 65, 15), color="#9DB8E0")
''',
    "placar": '''from canismajor import GameObject, draw, game


class Placar(GameObject):
    layer = 100

    def draw(self):
        pontos = game.data.get("pontos", 0)
        draw.rect(8, 8, 150, 22, "#0D0F13", alpha=0.6, screen=True)
        draw.text(f"Estrelas: {pontos}", 16, 11, size=14, color="#ECEEF2", screen=True)
        draw.text(f"{game.fps:.0f} fps", game.width - 12, 11, size=12, color="#A3A8B3", align="right", screen=True)
''',
}

EXAMPLE_SHADERS = {
    "brilho": '''// Shader de sprite: a estrela pulsa e ganha um brilho dourado.
vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    vec4 pixel = texture(tex, uv) * color;
    float pulso = 0.75 + 0.25 * sin(u_time * 6.0);
    pixel.rgb = mix(pixel.rgb, vec3(1.0, 0.92, 0.6), 0.35 * pulso);
    return pixel;
}
''',
    "vinheta": '''// Shader de tela: escurece as bordas e separa um pouco as cores nas pontas.
vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    vec2 centro = uv - 0.5;
    float d = length(centro);
    float desvio = 0.0025 * d;
    vec3 rgb;
    rgb.r = texture(tex, uv + centro * desvio * 4.0).r;
    rgb.g = texture(tex, uv).g;
    rgb.b = texture(tex, uv - centro * desvio * 4.0).b;
    float vinheta = smoothstep(0.85, 0.35, d);
    return vec4(rgb * vinheta, 1.0);
}
''',
    "crt": '''// Shader de tela: linhas de TV antiga e leve curvatura.
vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    vec2 c = uv * 2.0 - 1.0;
    c *= 1.0 + dot(c, c) * 0.04;
    vec2 tela = c * 0.5 + 0.5;
    if (tela.x < 0.0 || tela.x > 1.0 || tela.y < 0.0 || tela.y > 1.0) return vec4(0.0, 0.0, 0.0, 1.0);
    vec3 rgb = texture(tex, tela).rgb;
    float linha = 0.85 + 0.15 * sin(tela.y * u_resolution.y * 3.14159);
    return vec4(rgb * linha, 1.0);
}
''',
    "ondas": '''// Shader de tela: a imagem ondula, como vista através da água.
vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    uv.x += sin(uv.y * 30.0 + u_time * 3.0) * 0.003;
    uv.y += cos(uv.x * 25.0 + u_time * 2.0) * 0.003;
    return texture(tex, uv);
}
''',
}
