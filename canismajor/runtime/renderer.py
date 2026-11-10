"""Renderização em lote com ModernGL: sprites, texto, retângulos, malhas 3D e pós-processamento."""

from __future__ import annotations

import math
from collections import OrderedDict
from pathlib import Path

import moderngl
import numpy as np

from .. import shaderlib
from .mathutil import gl_bytes, normal_matrix, ortho, rotate_xyz, scale, translate

FONT_DIR = Path(__file__).resolve().parent.parent / "assets" / "fonts"
MAX_BATCH = 4096
FLOATS = 16  # por instância: rect(4) origem+rot(3) pad(1) uv(4) cor(4)


class Renderer:
    def __init__(self, ctx: moderngl.Context, width: int, height: int, pixel_art: bool, shader_dir: Path | None):
        self.ctx = ctx
        self.width = width
        self.height = height
        self.pixel_art = pixel_art
        self.filter = (moderngl.NEAREST, moderngl.NEAREST) if pixel_art else (moderngl.LINEAR, moderngl.LINEAR)
        self.shader_dir = shader_dir
        self.time = 0.0
        self.camera = (0.0, 0.0)
        self.uniforms: dict[str, dict[str, object]] = {}

        ctx.enable(moderngl.BLEND)
        ctx.blend_func = moderngl.SRC_ALPHA, moderngl.ONE_MINUS_SRC_ALPHA

        self.quad = ctx.buffer(np.array([0, 0, 1, 0, 0, 1, 1, 1], dtype="f4").tobytes())
        self.instances = np.zeros((MAX_BATCH, FLOATS), dtype="f4")
        self.count = 0
        self.inst_buf = ctx.buffer(reserve=MAX_BATCH * FLOATS * 4, dynamic=True)

        self._sprite_programs: dict[str | None, moderngl.Program] = {}
        self._post_programs: dict[str | None, moderngl.Program] = {}
        self._vaos: dict[int, moderngl.VertexArray] = {}
        self.mesh_program = ctx.program(vertex_shader=shaderlib.MESH_VS, fragment_shader=shaderlib.MESH_FS)

        self.white = ctx.texture((1, 1), 4, b"\xff\xff\xff\xff")
        self.current_tex = None
        self.current_shader: str | None = None

        self.scene_tex = self._target_texture()
        self.scene_depth = ctx.depth_renderbuffer((width, height))
        self.scene_fbo = ctx.framebuffer([self.scene_tex], self.scene_depth)
        self.ping = [self._target_texture(), self._target_texture()]
        self.ping_fbo = [ctx.framebuffer([t]) for t in self.ping]

        self._fonts: dict[int, object] = {}
        self._text_cache: OrderedDict[tuple, tuple] = OrderedDict()
        self.viewport = (0, 0, width, height, 1.0)
        self.proj = ortho(0, width, height, 0)

    def _target_texture(self):
        tex = self.ctx.texture((self.width, self.height), 4)
        tex.filter = self.filter
        tex.repeat_x = tex.repeat_y = False
        return tex

    # ---------- texturas ----------

    def texture_from_image(self, image):
        """Recebe uma imagem do Pillow já em RGBA. A primeira linha da imagem fica em v = 0."""
        tex = self.ctx.texture(image.size, 4, image.tobytes())
        tex.filter = self.filter
        tex.repeat_x = tex.repeat_y = False
        return tex

    # ---------- programas ----------

    def _shader_source(self, name: str) -> tuple[str, str]:
        if self.shader_dir is None:
            raise shaderlib.ShaderError(name, name, "pasta de shaders não definida")
        path = self.shader_dir / f"{name}.glsl"
        try:
            return path.read_text("utf-8"), str(path)
        except FileNotFoundError:
            raise shaderlib.ShaderError(name, str(path), "arquivo não encontrado") from None

    def _compile(self, vertex: str, name: str | None) -> moderngl.Program:
        if name is None:
            return self.ctx.program(vertex_shader=vertex, fragment_shader=shaderlib.wrap_effect(shaderlib.DEFAULT_EFFECT))
        source, path = self._shader_source(name)
        try:
            return self.ctx.program(vertex_shader=vertex, fragment_shader=shaderlib.wrap_effect(source))
        except moderngl.Error as e:
            raise shaderlib.ShaderError(name, path, str(e)) from None

    def sprite_program(self, name: str | None) -> moderngl.Program:
        prog = self._sprite_programs.get(name)
        if prog is None:
            prog = self._sprite_programs[name] = self._compile(shaderlib.SPRITE_VS, name)
        return prog

    def post_program(self, name: str | None) -> moderngl.Program:
        prog = self._post_programs.get(name)
        if prog is None:
            prog = self._post_programs[name] = self._compile(shaderlib.FULLSCREEN_VS, name)
        return prog

    def preload_shader(self, name: str) -> None:
        self.sprite_program(name)

    def _vao(self, prog: moderngl.Program, instanced: bool) -> moderngl.VertexArray:
        key = id(prog)
        vao = self._vaos.get(key)
        if vao is None:
            content = [(self.quad, "2f", "in_corner")]
            if instanced:
                content.append((self.inst_buf, "4f 3f 4x 4f 4f/i", "i_rect", "i_origin_rot", "i_uv", "i_color"))
            vao = self._vaos[key] = self.ctx.vertex_array(prog, content, skip_errors=True)
        return vao

    def _set_uniforms(self, prog: moderngl.Program, shader: str | None, resolution) -> None:
        if "u_time" in prog:
            prog["u_time"].value = self.time
        if "u_resolution" in prog:
            prog["u_resolution"].value = resolution
        if "u_texture" in prog:
            prog["u_texture"].value = 0
        for key, value in self.uniforms.get(shader or "", {}).items():
            if key in prog:
                prog[key].value = value

    # ---------- quadro ----------

    def begin(self, clear_color, camera: tuple[float, float]) -> None:
        self.camera = camera
        cx, cy = camera
        self.proj = ortho(cx, cx + self.width, cy + self.height, cy)
        self.scene_fbo.use()
        self.ctx.viewport = (0, 0, self.width, self.height)
        self.ctx.clear(*clear_color, depth=1.0)
        self.current_tex = None
        self.current_shader = None
        self.count = 0

    def quad_instance(self, tex, x, y, w, h, ox, oy, rot, uv, color, shader: str | None = None) -> None:
        if tex is not self.current_tex or shader != self.current_shader or self.count >= MAX_BATCH:
            self.flush()
            self.current_tex = tex
            self.current_shader = shader
        r, g, b, a = color
        u0, v0, u1, v1 = uv
        self.instances[self.count] = (x, y, w, h, ox, oy, rot, 0.0, u0, v0, u1, v1, r, g, b, a)
        self.count += 1

    def flush(self) -> None:
        if self.count == 0 or self.current_tex is None:
            self.count = 0
            return
        prog = self.sprite_program(self.current_shader)
        if "u_proj" in prog:
            prog["u_proj"].write(gl_bytes(self.proj))
        self._set_uniforms(prog, self.current_shader, (float(self.width), float(self.height)))
        self.current_tex.use(0)
        self.inst_buf.write(self.instances[: self.count].tobytes())
        self._vao(prog, instanced=True).render(moderngl.TRIANGLE_STRIP, vertices=4, instances=self.count)
        self.count = 0

    def rect(self, x, y, w, h, color, rotation=0.0, outline: float = 0.0) -> None:
        if outline > 0:
            t = outline
            self.rect(x, y, w, t, color)
            self.rect(x, y + h - t, w, t, color)
            self.rect(x, y + t, t, h - 2 * t, color)
            self.rect(x + w - t, y + t, t, h - 2 * t, color)
            return
        self.quad_instance(self.white, x, y, w, h, 0.0, 0.0, math.radians(rotation), (0, 0, 1, 1), color)

    def line(self, x1, y1, x2, y2, color, width: float = 1.0) -> None:
        length = math.hypot(x2 - x1, y2 - y1)
        if length <= 0:
            return
        angle = math.atan2(y2 - y1, x2 - x1)
        self.quad_instance(self.white, x1, y1, length, width, 0.0, width / 2, angle, (0, 0, 1, 1), color)

    # ---------- texto ----------

    def _font(self, size: int):
        import pygame

        font = self._fonts.get(size)
        if font is None:
            path = FONT_DIR / "Geist-Medium.ttf"
            font = self._fonts[size] = pygame.font.Font(str(path) if path.exists() else None, size)
        return font

    def text(self, text: str, x, y, size: int, color, align: str = "left") -> tuple[int, int]:
        import pygame

        key = (text, size)
        entry = self._text_cache.get(key)
        if entry is None:
            surf = self._font(size).render(text, True, (255, 255, 255))
            w, h = surf.get_size()
            tex = self.ctx.texture((max(w, 1), max(h, 1)), 4, pygame.image.tobytes(surf, "RGBA") if w and h else b"\0\0\0\0")
            tex.filter = (moderngl.LINEAR, moderngl.LINEAR)
            entry = (tex, w, h)
            self._text_cache[key] = entry
            if len(self._text_cache) > 256:
                _, (old, _, _) = self._text_cache.popitem(last=False)
                if old is self.current_tex:
                    self.flush()
                old.release()
        else:
            self._text_cache.move_to_end(key)
        tex, w, h = entry
        if align == "center":
            x -= w / 2
        elif align == "right":
            x -= w
        self.quad_instance(tex, x, y, w, h, 0.0, 0.0, 0.0, (0, 0, 1, 1), color)
        return w, h

    # ---------- 3D ----------

    def mesh(self, mesh, x, y, size, rotation=(0.0, 0.0, 0.0), color=(1, 1, 1, 1), z: float = 0.0,
             texture=None, light=(-0.45, -0.65, 0.75), ambient: float = 0.35) -> None:
        self.flush()
        sx, sy, sz = (size, size, size) if isinstance(size, (int, float)) else size
        model = translate(x, y, z) @ rotate_xyz(*rotation) @ scale(sx, sy, sz)
        prog = self.mesh_program
        prog["u_proj"].write(gl_bytes(self.proj))
        prog["u_model"].write(gl_bytes(model))
        prog["u_normal_mat"].write(np.ascontiguousarray(normal_matrix(model).T, dtype="f4").tobytes())
        prog["u_color"].value = tuple(color)
        prog["u_light"].value = tuple(light)
        prog["u_ambient"].value = ambient
        if "u_textured" in prog:
            prog["u_textured"].value = texture is not None
        if "u_texture" in prog:
            prog["u_texture"].value = 0
        (texture or self.white).use(0)
        self.ctx.enable(moderngl.DEPTH_TEST)
        mesh.vao(self.ctx, prog).render(moderngl.TRIANGLES)
        self.ctx.disable(moderngl.DEPTH_TEST)

    # ---------- final do quadro ----------

    def end(self, post_shaders: list[str], window_size: tuple[int, int]) -> None:
        self.flush()
        source = self.scene_tex
        res = (float(self.width), float(self.height))
        for i, name in enumerate(post_shaders):
            prog = self.post_program(name)
            target = self.ping_fbo[i % 2]
            target.use()
            self.ctx.viewport = (0, 0, self.width, self.height)
            self.ctx.clear(0, 0, 0, 0)
            self._set_uniforms(prog, name, res)
            source.use(0)
            self.ctx.disable(moderngl.BLEND)
            self._vao(prog, instanced=False).render(moderngl.TRIANGLE_STRIP, vertices=4)
            self.ctx.enable(moderngl.BLEND)
            source = self.ping[i % 2]

        win_w, win_h = window_size
        factor = min(win_w / self.width, win_h / self.height) if self.width and self.height else 1.0
        if self.pixel_art and factor >= 1:
            factor = math.floor(factor)
        vw, vh = int(self.width * factor), int(self.height * factor)
        vx, vy = (win_w - vw) // 2, (win_h - vh) // 2
        self.viewport = (vx, vy, vw, vh, factor)

        self.ctx.screen.use()
        self.ctx.viewport = (0, 0, win_w, win_h)
        self.ctx.clear(0.0, 0.0, 0.0, 1.0)
        self.ctx.viewport = (vx, vy, vw, vh)
        prog = self.post_program(None)
        self._set_uniforms(prog, None, res)
        source.use(0)
        self.ctx.disable(moderngl.BLEND)
        self._vao(prog, instanced=False).render(moderngl.TRIANGLE_STRIP, vertices=4)
        self.ctx.enable(moderngl.BLEND)
        self.final_texture = source

    def screen_to_world(self, sx: float, sy: float) -> tuple[float, float]:
        vx, vy, vw, vh, factor = self.viewport
        if factor <= 0:
            return 0.0, 0.0
        return (sx - vx) / factor + self.camera[0], (sy - vy) / factor + self.camera[1]

    def read_scene(self) -> bytes:
        """Pixels do jogo depois do pós-processamento, de baixo para cima (padrão do OpenGL)."""
        return self.final_texture.read()
