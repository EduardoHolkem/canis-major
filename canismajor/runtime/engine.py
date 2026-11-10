"""Laço do jogo: abre a janela, carrega o projeto e roda as salas."""

from __future__ import annotations

import importlib
import inspect
import math
import os
import sys
import time
import traceback
from dataclasses import dataclass
from pathlib import Path

os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")

import moderngl  # noqa: E402
import pygame  # noqa: E402
from PIL import Image  # noqa: E402

from .. import shaderlib  # noqa: E402
from ..project import Project, ProjectError, load_room, load_sprite_meta  # noqa: E402
from . import api  # noqa: E402
from .mathutil import parse_color  # noqa: E402
from .renderer import Renderer  # noqa: E402


@dataclass
class Sprite:
    name: str
    texture: object
    width: int
    height: int
    frames: int
    origin: tuple[float, float]
    fps: float

    def uv(self, frame: float) -> tuple[float, float, float, float]:
        i = int(frame) % self.frames
        return (i / self.frames, 0.0, (i + 1) / self.frames, 1.0)


class GameError(Exception):
    pass


class Engine:
    def __init__(self, project: Project, room: str | None = None):
        self.project = project
        self.width = project.width
        self.height = project.height
        self.first_room = room or project.start_room
        self.room = ""
        self.room_width = self.width
        self.room_height = self.height
        self.background = (0.05, 0.06, 0.07, 1.0)
        self.post_shaders: list[str] = []
        self.camera_x = 0.0
        self.camera_y = 0.0
        self.time = 0.0
        self.dt = 0.0
        self.fps = 0.0
        self.frame_count = 0
        self.data: dict = {}  # variáveis que sobrevivem à troca de sala
        self._instances: list[api.GameObject] = []
        self._classes: dict[str, type] = {}
        self._sprites: dict[str, Sprite | None] = {}
        self._sounds: dict[str, object] = {}
        self._pending_room: str | None = None
        self._running = False
        self._audio = False
        self.renderer: Renderer | None = None

    # ---------- carregamento ----------

    def sprite_info(self, name: str | None) -> Sprite | None:
        if not name:
            return None
        if name not in self._sprites:
            png = self.project.folder("sprite") / f"{name}.png"
            if not png.exists():
                print(f"Aviso: o sprite '{name}' não existe em sprites/.", file=sys.stderr)
                self._sprites[name] = None
                return None
            meta = load_sprite_meta(png)
            image = Image.open(png).convert("RGBA")
            frames = meta["frames"]
            ox, oy = meta.get("origin", [0, 0])
            self._sprites[name] = Sprite(
                name=name,
                texture=self.renderer.texture_from_image(image),
                width=image.width // frames,
                height=image.height,
                frames=frames,
                origin=(float(ox), float(oy)),
                fps=float(meta.get("fps", 8)),
            )
        return self._sprites[name]

    def object_class(self, kind: str) -> type:
        cls = self._classes.get(kind)
        if cls is None:
            path = self.project.folder("object") / f"{kind}.py"
            if not path.exists():
                raise GameError(f"O objeto '{kind}' não existe em objects/.")
            module = importlib.import_module(f"objects.{kind}")
            found = [c for _, c in inspect.getmembers(module, inspect.isclass)
                     if issubclass(c, api.GameObject) and c is not api.GameObject and c.__module__ == module.__name__]
            if not found:
                raise GameError(f"{path}: nenhuma classe filha de GameObject foi encontrada.")
            # A classe declarada primeiro no arquivo é a do objeto.
            found.sort(key=lambda c: inspect.getsourcelines(c)[1])
            cls = self._classes[kind] = found[0]
        return cls

    # ---------- salas e instâncias ----------

    def goto(self, room: str) -> None:
        """Troca de sala no fim do quadro atual."""
        self._pending_room = room

    def restart(self) -> None:
        self._pending_room = self.room

    def quit(self) -> None:
        self._running = False

    def _load_room(self, name: str) -> None:
        path = self.project.folder("room") / f"{name}.json"
        if not path.exists():
            raise GameError(f"A sala '{name}' não existe em rooms/.")
        data = load_room(path, self.project)
        for obj in list(self._instances):
            if obj.alive:
                obj.alive = False
                obj.on_destroy()
        self._instances = []
        self.room = name
        self.room_width = int(data["width"])
        self.room_height = int(data["height"])
        self.background = parse_color(data.get("background", "#0D0F13"))
        self.post_shaders = list(data.get("post_shaders", []))
        for shader in self.post_shaders:
            self.renderer.post_program(shader)
        self.camera_x = self.camera_y = 0.0
        for inst in data.get("instances", []):
            props = {k: v for k, v in inst.items() if k not in ("object", "x", "y")}
            self.spawn(inst["object"], inst.get("x", 0), inst.get("y", 0), **props)

    def spawn(self, kind, x: float = 0.0, y: float = 0.0, **props) -> api.GameObject:
        """Cria um objeto na sala. `kind` é o nome do arquivo em objects/ ou a própria classe."""
        if isinstance(kind, type):
            cls = kind
            name = next((k for k, c in self._classes.items() if c is cls), cls.__name__.lower())
        else:
            name = kind
            cls = self.object_class(kind)
        obj = cls()
        obj.kind = name
        obj.x, obj.y = float(x), float(y)
        for key, value in props.items():
            setattr(obj, key, value)
        self._instances.append(obj)
        obj.create()
        return obj

    def destroy(self, obj: api.GameObject) -> None:
        if obj.alive:
            obj.alive = False
            obj.on_destroy()

    def instances(self, kind=None) -> list[api.GameObject]:
        if kind is None:
            return [o for o in self._instances if o.alive]
        if isinstance(kind, type):
            return [o for o in self._instances if o.alive and isinstance(o, kind)]
        return [o for o in self._instances if o.alive and o.kind == kind]

    def first(self, kind=None) -> api.GameObject | None:
        found = self.instances(kind)
        return found[0] if found else None

    def count(self, kind=None) -> int:
        return len(self.instances(kind))

    def follow(self, obj: api.GameObject, smooth: float = 0.0) -> None:
        """Centraliza a câmera no objeto, sem passar das bordas da sala. smooth de 0 (seco) a 1 (lento)."""
        tx = min(max(obj.x - self.width / 2, 0), max(self.room_width - self.width, 0))
        ty = min(max(obj.y - self.height / 2, 0), max(self.room_height - self.height, 0))
        k = 1.0 - min(max(smooth, 0.0), 0.99)
        self.camera_x += (tx - self.camera_x) * k
        self.camera_y += (ty - self.camera_y) * k

    def set_uniform(self, shader: str, name: str, value) -> None:
        self.renderer.uniforms.setdefault(shader, {})[name] = value

    # ---------- som ----------

    def play_sound(self, name: str, volume: float = 1.0, loop: bool = False):
        if not self._audio:
            return None
        snd = self._sounds.get(name)
        if snd is None:
            path = self.project.path("sound", name)
            if not path.exists():
                print(f"Aviso: o som '{name}' não existe em sounds/.", file=sys.stderr)
                return None
            snd = self._sounds[name] = pygame.mixer.Sound(str(path))
        snd.set_volume(volume)
        return snd.play(loops=-1 if loop else 0)

    def stop_sounds(self) -> None:
        if self._audio:
            pygame.mixer.stop()

    # ---------- desenho ----------

    def _offset(self, x, y, screen):
        return (x + self.camera_x, y + self.camera_y) if screen else (x, y)

    def draw_object(self, obj: api.GameObject) -> None:
        spr = self.sprite_info(obj.sprite)
        if spr is None:
            return
        color = parse_color(obj.color, obj.alpha)
        self.renderer.quad_instance(
            spr.texture, obj.x, obj.y, spr.width * obj.scale_x, spr.height * obj.scale_y,
            spr.origin[0] * obj.scale_x, spr.origin[1] * obj.scale_y, math.radians(obj.rotation),
            spr.uv(obj.frame), color, obj.shader,
        )

    def draw_sprite(self, name, x, y, frame, scl, rotation, color, alpha, shader, screen):
        spr = self.sprite_info(name)
        if spr is None:
            return
        x, y = self._offset(x, y, screen)
        sx, sy = (scl, scl) if isinstance(scl, (int, float)) else scl
        self.renderer.quad_instance(
            spr.texture, x, y, spr.width * sx, spr.height * sy, spr.origin[0] * sx, spr.origin[1] * sy,
            math.radians(rotation), spr.uv(frame), parse_color(color, alpha), shader,
        )

    def draw_text(self, text, x, y, size, color, align, alpha, screen):
        x, y = self._offset(x, y, screen)
        return self.renderer.text(text, x, y, int(size), parse_color(color, alpha), align)

    def draw_rect(self, x, y, w, h, color, alpha, outline, rotation, screen):
        x, y = self._offset(x, y, screen)
        self.renderer.rect(x, y, w, h, parse_color(color, alpha), rotation, outline)

    def draw_line(self, x1, y1, x2, y2, color, width, alpha, screen):
        x1, y1 = self._offset(x1, y1, screen)
        x2, y2 = self._offset(x2, y2, screen)
        self.renderer.line(x1, y1, x2, y2, parse_color(color, alpha), width)

    def draw_circle(self, x, y, radius, color, alpha, screen):
        x, y = self._offset(x, y, screen)
        tex = self._circle_texture()
        d = radius * 2
        self.renderer.quad_instance(tex, x, y, d, d, radius, radius, 0.0, (0, 0, 1, 1), parse_color(color, alpha))

    def _circle_texture(self):
        tex = getattr(self, "_circle", None)
        if tex is None:
            size = 128
            img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
            px = img.load()
            r = size / 2
            for j in range(size):
                for i in range(size):
                    d = math.hypot(i + 0.5 - r, j + 0.5 - r)
                    a = max(0.0, min(1.0, r - d))
                    px[i, j] = (255, 255, 255, int(a * 255))
            tex = self._circle = self.renderer.texture_from_image(img)
            tex.filter = (moderngl.LINEAR, moderngl.LINEAR)
        return tex

    def draw_mesh(self, mesh, x, y, size, rotation, color, alpha, z, sprite, light, ambient):
        tex = None
        if sprite:
            spr = self.sprite_info(sprite)
            tex = spr.texture if spr else None
        self.renderer.mesh(mesh, x, y, size, rotation, parse_color(color, alpha), z, tex, light, ambient)

    # ---------- laço ----------

    def run(self, max_frames: int = 0, screenshot: str | None = None) -> int:
        project = self.project
        sys.path.insert(0, str(project.root))
        pygame.init()
        try:
            pygame.mixer.init()
            self._audio = True
        except pygame.error:
            self._audio = False

        pygame.display.gl_set_attribute(pygame.GL_CONTEXT_MAJOR_VERSION, 3)
        pygame.display.gl_set_attribute(pygame.GL_CONTEXT_MINOR_VERSION, 3)
        pygame.display.gl_set_attribute(pygame.GL_CONTEXT_PROFILE_MASK, pygame.GL_CONTEXT_PROFILE_CORE)
        pygame.display.gl_set_attribute(pygame.GL_DOUBLEBUFFER, 1)
        size = (project.width * max(1, project.scale), project.height * max(1, project.scale))
        pygame.display.set_mode(size, pygame.OPENGL | pygame.DOUBLEBUF | pygame.RESIZABLE, vsync=1)
        pygame.display.set_caption(project.name)
        icon = Path(__file__).resolve().parent.parent / "assets" / "icon.png"
        if icon.exists():
            pygame.display.set_icon(pygame.image.load(str(icon)))

        ctx = moderngl.create_context(require=330)
        self.renderer = Renderer(ctx, project.width, project.height, project.pixel_art, project.folder("shader"))
        api._engine = self

        self._load_room(self.first_room)
        clock = pygame.time.Clock()
        start = time.perf_counter()
        last = start
        self._running = True
        while self._running:
            api.keys._begin_frame()
            api.mouse._begin_frame()
            for ev in pygame.event.get():
                if ev.type == pygame.QUIT:
                    self._running = False
                api.keys._event(ev)
                api.mouse._event(ev)

            now = time.perf_counter()
            self.dt = min(now - last, 0.1)  # uma pausa longa não vira um salto enorme
            last = now
            self.time = now - start
            self.renderer.time = self.time
            self.fps = clock.get_fps()

            mx, my = pygame.mouse.get_pos()
            api.mouse.screen_x, api.mouse.screen_y = mx, my
            api.mouse.x, api.mouse.y = self.renderer.screen_to_world(mx, my)

            for obj in list(self._instances):
                if obj.alive:
                    obj.step(self.dt)
            self._instances = [o for o in self._instances if o.alive]

            for obj in self._instances:
                spr = self.sprite_info(obj.sprite)
                if spr and spr.frames > 1:
                    speed = spr.fps if obj.animation_speed is None else obj.animation_speed
                    obj.frame = (obj.frame + speed * self.dt) % spr.frames

            self.renderer.begin(self.background, (self.camera_x, self.camera_y))
            for obj in sorted(self._instances, key=lambda o: o.layer):
                if obj.visible and obj.alive:
                    obj.draw()
            self.renderer.end(self.post_shaders, pygame.display.get_window_size())
            pygame.display.flip()

            self.frame_count += 1
            if max_frames and self.frame_count >= max_frames:
                if screenshot:
                    self._save_screenshot(screenshot)
                self._running = False

            if self._pending_room is not None:
                room, self._pending_room = self._pending_room, None
                self._load_room(room)
            clock.tick(project.fps)

        pygame.quit()
        api._engine = None
        return 0

    def _save_screenshot(self, path: str) -> None:
        data = self.renderer.read_scene()
        img = Image.frombytes("RGBA", (self.width, self.height), data).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        img.save(path)


def _print_user_traceback(error: BaseException) -> None:
    """Mostra só os trechos do jogo do usuário; as linhas internas da engine só aparecem se não houver outras."""
    engine_dir = Path(__file__).resolve().parent.parent
    frames = traceback.extract_tb(error.__traceback__)

    def inside_engine(frame) -> bool:
        try:
            return Path(frame.filename).resolve().is_relative_to(engine_dir)
        except (OSError, ValueError):
            return False

    user = [f for f in frames if not inside_engine(f)]
    print("Erro no jogo (a linha mais recente fica embaixo):", file=sys.stderr)
    print("".join(traceback.format_list(user or frames)), end="", file=sys.stderr)
    print("".join(traceback.format_exception_only(error)), end="", file=sys.stderr)


def run_project(path, room: str | None = None, max_frames: int = 0, screenshot: str | None = None) -> int:
    try:
        project = Project.load(path)
    except ProjectError as e:
        print(e, file=sys.stderr)
        return 2
    engine = Engine(project, room)
    try:
        return engine.run(max_frames=max_frames, screenshot=screenshot)
    except shaderlib.ShaderError as e:
        print(e, file=sys.stderr)
        return 1
    except (GameError, ProjectError) as e:
        print(f"Erro: {e}", file=sys.stderr)
        return 1
    except Exception as e:
        _print_user_traceback(e)
        return 1
    finally:
        try:
            pygame.quit()
        except Exception:
            pass
