"""Editor de shaders com prévia ao vivo, usando o mesmo montador de shader do runtime."""

from __future__ import annotations

import math
import time

import moderngl
import numpy as np
from PySide6.QtCore import QRectF, Qt, QTimer, Signal
from PySide6.QtGui import QColor, QFont, QImage, QLinearGradient, QPainter, QPainterPath, QPen
from PySide6.QtOpenGLWidgets import QOpenGLWidget
from PySide6.QtWidgets import QHBoxLayout, QLabel, QPushButton, QSplitter, QVBoxLayout, QWidget

from .. import shaderlib
from ..project import Project
from .code_editor import CodeEditor, CodePage
from .theme import Palette

PREVIEW_VS = """#version 330
uniform vec4 u_rect;
uniform float u_flip;
in vec2 in_corner;
out vec2 v_uv;
out vec4 v_color;
void main() {
    gl_Position = vec4(mix(u_rect.xy, u_rect.zw, in_corner), 0.0, 1.0);
    v_uv = vec2(in_corner.x, mix(in_corner.y, 1.0 - in_corner.y, u_flip));
    v_color = vec4(1.0);
}
"""

CHECKER_FS = """#version 330
uniform vec3 u_light;
uniform vec3 u_dark;
in vec2 v_uv;
out vec4 f_color;
void main() {
    vec2 cell = floor(gl_FragCoord.xy / 10.0);
    f_color = vec4(mod(cell.x + cell.y, 2.0) < 1.0 ? u_light : u_dark, 1.0);
}
"""


def _test_scene(w: int = 480, h: int = 270) -> QImage:
    """Uma cena de teste com cores e formas, para ver o efeito dos shaders de tela."""
    img = QImage(w, h, QImage.Format.Format_RGBA8888)
    p = QPainter(img)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    grad = QLinearGradient(0, 0, 0, h)
    grad.setColorAt(0, QColor("#1B2235"))
    grad.setColorAt(1, QColor("#0D0F13"))
    p.fillRect(0, 0, w, h, grad)
    p.setPen(QPen(QColor(255, 255, 255, 18), 1))
    for x in range(0, w, 16):
        p.drawLine(x, 0, x, h)
    for y in range(0, h, 16):
        p.drawLine(0, y, w, y)
    p.setPen(Qt.PenStyle.NoPen)
    for i, c in enumerate(("#FF6B6B", "#F5A97F", "#F5C65B", "#9FD5A0", "#5FD1C4", "#82AAFF", "#C792EA")):
        p.setBrush(QColor(c))
        p.drawRoundedRect(QRectF(40 + i * 58, 190, 46, 46), 8, 8)
    star = QPainterPath()
    cx, cy = 240, 95
    for k in range(8):
        r = 52 if k % 2 == 0 else 12
        a = math.pi / 4 * k - math.pi / 2
        pt = (cx + math.cos(a) * r, cy + math.sin(a) * r)
        star.moveTo(*pt) if k == 0 else star.lineTo(*pt)
    star.closeSubpath()
    p.setBrush(QColor("#ECEEF2"))
    p.drawPath(star)
    font = QFont("Geist")
    font.setPixelSize(18)
    font.setWeight(QFont.Weight.DemiBold)
    p.setFont(font)
    p.setPen(QColor("#ECEEF2"))
    p.drawText(QRectF(0, 150, w, 24), Qt.AlignmentFlag.AlignCenter, "Canis Major")
    p.end()
    return img


class ShaderPreview(QOpenGLWidget):
    compiled = Signal(bool, str)

    def __init__(self, project: Project, palette: Palette):
        super().__init__()
        self.project = project
        self.palette_ = palette
        self.mode = "screen"
        self.source = shaderlib.DEFAULT_EFFECT
        self.ctx = None
        self.program = None
        self._pending = True
        self.start = time.perf_counter()
        self.sprite_name: str | None = None
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.update)
        self.timer.start(16)
        self.setMinimumSize(240, 160)

    def set_source(self, source: str):
        self.source = source
        self._pending = True
        self.update()

    def set_mode(self, mode: str):
        self.mode = mode
        self.update()

    def initializeGL(self):
        try:
            self.ctx = moderngl.create_context(require=330)
        except Exception as e:  # sem OpenGL 3.3: o editor continua funcionando, só sem prévia
            self.ctx = None
            self.compiled.emit(False, f"Prévia indisponível: {e}")
            return
        self.quad = self.ctx.buffer(np.array([0, 0, 1, 0, 0, 1, 1, 1], dtype="f4").tobytes())
        self.checker = self.ctx.program(vertex_shader=PREVIEW_VS, fragment_shader=CHECKER_FS)
        self.checker_vao = self.ctx.vertex_array(self.checker, [(self.quad, "2f", "in_corner")], skip_errors=True)
        scene = _test_scene().mirrored(False, True)  # v = 0 embaixo, como na tela do jogo
        self.scene_tex = self._texture(scene, linear=True)
        self.sprite_tex = None
        self._load_sprite()

    def _texture(self, image: QImage, linear: bool):
        image = image.convertToFormat(QImage.Format.Format_RGBA8888)
        data = bytes(image.constBits())[: image.width() * image.height() * 4]
        tex = self.ctx.texture((image.width(), image.height()), 4, data)
        f = moderngl.LINEAR if linear else moderngl.NEAREST
        tex.filter = (f, f)
        return tex

    def _load_sprite(self):
        names = self.project.resources("sprite")
        image = None
        for name in names:
            img = QImage(str(self.project.path("sprite", name)))
            if not img.isNull():
                from ..project import load_sprite_meta
                frames = load_sprite_meta(self.project.path("sprite", name))["frames"]
                image = img.copy(0, 0, img.width() // frames, img.height())
                self.sprite_name = name
                break
        if image is None:
            image = _test_scene(64, 64)
        self.sprite_tex = self._texture(image, linear=False)
        self.sprite_size = (image.width(), image.height())

    def _compile(self):
        self._pending = False
        if self.program is not None:
            self.program.release()
            self.program = None
        try:
            self.program = self.ctx.program(vertex_shader=PREVIEW_VS, fragment_shader=shaderlib.wrap_effect(self.source))
            self.vao = self.ctx.vertex_array(self.program, [(self.quad, "2f", "in_corner")], skip_errors=True)
            self.compiled.emit(True, "")
        except moderngl.Error as e:
            self.program = None
            self.compiled.emit(False, str(e))

    def paintGL(self):
        if self.ctx is None:
            return
        if self._pending:
            self._compile()
        ratio = self.devicePixelRatioF()
        w, h = int(self.width() * ratio), int(self.height() * ratio)
        fbo = self.ctx.detect_framebuffer(self.defaultFramebufferObject())
        fbo.use()
        self.ctx.viewport = (0, 0, w, h)
        bg = QColor(self.palette_.sheet)
        self.ctx.clear(bg.redF(), bg.greenF(), bg.blueF(), 1.0)
        self.ctx.enable(moderngl.BLEND)
        self.ctx.blend_func = moderngl.SRC_ALPHA, moderngl.ONE_MINUS_SRC_ALPHA

        if self.mode == "screen":
            aspect = 480 / 270
            vw, vh = (w, w / aspect) if w / h < aspect else (h * aspect, h)
            rect = (-vw / w, -vh / h, vw / w, vh / h)
            tex, flip = self.scene_tex, 0.0
        else:
            sw, sh = self.sprite_size
            k = max(1, int(min(w * 0.6 / sw, h * 0.6 / sh)))
            vw, vh = sw * k, sh * k
            rect = (-vw / w, -vh / h, vw / w, vh / h)
            tex, flip = self.sprite_tex, 1.0
            light, dark = QColor(self.palette_.checker_light), QColor(self.palette_.checker_dark)
            self.checker["u_rect"].value = rect
            self.checker["u_light"].value = (light.redF(), light.greenF(), light.blueF())
            self.checker["u_dark"].value = (dark.redF(), dark.greenF(), dark.blueF())
            self.checker_vao.render(moderngl.TRIANGLE_STRIP, vertices=4)

        if self.program is None:
            return
        prog = self.program
        for name, value in (("u_rect", rect), ("u_flip", flip), ("u_time", time.perf_counter() - self.start),
                            ("u_resolution", (480.0, 270.0) if self.mode == "screen" else tuple(map(float, self.sprite_size))),
                            ("u_texture", 0)):
            if name in prog:
                prog[name].value = value
        tex.use(0)
        self.vao.render(moderngl.TRIANGLE_STRIP, vertices=4)


class ShaderPage(QWidget):
    modified = Signal()

    def __init__(self, project: Project, name: str, palette: Palette, code_size: float, auto_pairs: bool):
        super().__init__()
        self.setObjectName("Page")
        self.project = project
        self.name = name
        self.path = project.path("shader", name)
        self.palette_ = palette
        self.dirty = False

        self.editor = CodeEditor("glsl", palette, code_size, auto_pairs)
        try:
            self.editor.setPlainText(self.path.read_text("utf-8"))
        except FileNotFoundError:
            self.editor.setPlainText(shaderlib.DEFAULT_EFFECT)
        self.editor.document().setModified(False)
        self.code_page = CodePage(self.editor, palette)

        right = QWidget()
        rl = QVBoxLayout(right)
        rl.setContentsMargins(8, 8, 12, 12)
        rl.setSpacing(8)
        seg = QHBoxLayout()
        seg.setSpacing(2)
        self.screen_btn = QPushButton("Tela inteira")
        self.sprite_btn = QPushButton("Sprite")
        for b, mode in ((self.screen_btn, "screen"), (self.sprite_btn, "sprite")):
            b.setProperty("role", "segment")
            b.clicked.connect(lambda _=False, m=mode: self.set_mode(m))
            seg.addWidget(b)
        seg.addStretch(1)
        rl.addLayout(seg)
        self.preview = ShaderPreview(project, palette)
        self.preview.compiled.connect(self._on_compiled)
        rl.addWidget(self.preview, 1)
        self.status = QLabel()
        self.status.setWordWrap(True)
        self.status.setProperty("role", "muted")
        self.status.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        rl.addWidget(self.status)
        help_ = QLabel("Escreva <b>effect(tex, uv, color)</b>. Valem <b>u_time</b> e <b>u_resolution</b>. "
                       "Em tela inteira, ligue o shader na sala. Num sprite, use <b>self.shader = \"" + name + "\"</b>.")
        help_.setWordWrap(True)
        help_.setProperty("role", "muted")
        rl.addWidget(help_)

        split = QSplitter(Qt.Orientation.Horizontal)
        split.addWidget(self.code_page)
        split.addWidget(right)
        split.setStretchFactor(0, 3)
        split.setStretchFactor(1, 2)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.addWidget(split)

        self._debounce = QTimer(self)
        self._debounce.setSingleShot(True)
        self._debounce.setInterval(300)
        self._debounce.timeout.connect(lambda: self.preview.set_source(self.editor.toPlainText()))
        self.editor.textChanged.connect(self._on_text)
        self.preview.set_source(self.editor.toPlainText())
        self.set_mode("screen")

    def set_mode(self, mode: str):
        self.preview.set_mode(mode)
        for b, m in ((self.screen_btn, "screen"), (self.sprite_btn, "sprite")):
            b.setProperty("on", "true" if m == mode else "false")
            b.style().unpolish(b)
            b.style().polish(b)

    def _on_text(self):
        if self.editor.document().isModified():
            self.dirty = True
            self.modified.emit()
        self._debounce.start()

    def _on_compiled(self, ok: bool, message: str):
        if ok:
            self.status.setProperty("role", "muted")
            self.status.setText("Compilou sem erros.")
            self.editor.set_errors({})
        else:
            errors = shaderlib.parse_errors(message)
            self.status.setProperty("role", "error")
            lines = [f"Linha {l}: {t}" if l else t for l, t in errors] or [message.strip()]
            self.status.setText("\n".join(lines[:6]))
            self.editor.set_errors({l: t for l, t in errors if l})
        self.status.style().unpolish(self.status)
        self.status.style().polish(self.status)

    def apply_palette(self, palette: Palette):
        self.palette_ = palette
        self.preview.palette_ = palette
        self.editor.apply_palette(palette)

    def save(self):
        from ..project import write_text_atomic
        write_text_atomic(self.path, self.editor.toPlainText())
        self.editor.document().setModified(False)
        self.dirty = False
        self.modified.emit()
