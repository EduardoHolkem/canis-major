"""Editor de sprites em pixel: lápis, borracha, balde, conta-gotas, quadros de animação e origem."""

from __future__ import annotations

from collections import deque

from PySide6.QtCore import QPoint, QRect, QSize, Qt, QTimer, Signal
from PySide6.QtGui import QColor, QImage, QPainter, QPen, QPixmap
from PySide6.QtWidgets import (QColorDialog, QGridLayout, QHBoxLayout, QLabel, QPushButton, QScrollArea, QSpinBox,
                               QVBoxLayout, QWidget)

from ..project import Project, load_sprite_meta, save_sprite_meta
from . import icons
from .theme import Palette

SWATCHES = [
    "#000000", "#16181D", "#3F434B", "#8A8F9A", "#ECEEF2", "#FFFFFF", "#7A2E3B", "#FF6B6B",
    "#F5A97F", "#F5C65B", "#9FD5A0", "#2F7A3B", "#5FD1C4", "#82AAFF", "#3D5A85", "#C792EA",
]


class PixelCanvas(QWidget):
    changed = Signal()
    color_picked = Signal(QColor)

    def __init__(self, image: QImage, palette: Palette):
        super().__init__()
        self.image = image.convertToFormat(QImage.Format.Format_ARGB32)
        self.palette_ = palette
        self.zoom = 16
        self.tool = "pencil"
        self.color = QColor("#ECEEF2")
        self.frames = 1
        self.origin = (0, 0)
        self.undo: deque[QImage] = deque(maxlen=60)
        self.redo: list[QImage] = []
        self._last: QPoint | None = None
        self._stroke = False
        self.setMouseTracking(True)
        self.setCursor(Qt.CursorShape.CrossCursor)
        self._resize()

    def _resize(self):
        self.setFixedSize(self.image.width() * self.zoom, self.image.height() * self.zoom)

    def set_zoom(self, zoom: int):
        self.zoom = max(1, min(64, zoom))
        self._resize()
        self.update()

    def set_image(self, image: QImage):
        self.image = image.convertToFormat(QImage.Format.Format_ARGB32)
        self._resize()
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        z = self.zoom
        w, h = self.image.width(), self.image.height()
        light, dark = QColor(self.palette_.checker_light), QColor(self.palette_.checker_dark)
        cell = max(z // 2, 4)
        r = event.rect()
        for y in range(r.top() // cell * cell, r.bottom() + 1, cell):
            for x in range(r.left() // cell * cell, r.right() + 1, cell):
                p.fillRect(x, y, cell, cell, light if (x // cell + y // cell) % 2 == 0 else dark)
        p.drawImage(QRect(0, 0, w * z, h * z), self.image)
        if z >= 8:
            c = QColor(self.palette_.ink)
            c.setAlpha(28)
            p.setPen(QPen(c, 1))
            for x in range(0, w + 1):
                p.drawLine(x * z, 0, x * z, h * z)
            for y in range(0, h + 1):
                p.drawLine(0, y * z, w * z, y * z)
        if self.frames > 1:
            fw = w // self.frames
            p.setPen(QPen(QColor(self.palette_.accent), 2))
            for f in range(1, self.frames):
                p.drawLine(f * fw * z, 0, f * fw * z, h * z)
        ox, oy = self.origin
        p.setPen(QPen(QColor(self.palette_.error), 2))
        cx, cy = int(ox * z), int(oy * z)
        p.drawLine(cx - 6, cy, cx + 6, cy)
        p.drawLine(cx, cy - 6, cx, cy + 6)

    def _pixel(self, pos) -> QPoint | None:
        x, y = int(pos.x() // self.zoom), int(pos.y() // self.zoom)
        if 0 <= x < self.image.width() and 0 <= y < self.image.height():
            return QPoint(x, y)
        return None

    def _push_undo(self):
        self.undo.append(self.image.copy())
        self.redo.clear()

    def undo_step(self):
        if self.undo:
            self.redo.append(self.image.copy())
            self.image = self.undo.pop()
            self.update()
            self.changed.emit()

    def redo_step(self):
        if self.redo:
            self.undo.append(self.image.copy())
            self.image = self.redo.pop()
            self.update()
            self.changed.emit()

    def _plot(self, pt: QPoint, erase: bool):
        self.image.setPixelColor(pt, QColor(0, 0, 0, 0) if erase else self.color)

    def _line(self, a: QPoint, b: QPoint, erase: bool):
        x0, y0, x1, y1 = a.x(), a.y(), b.x(), b.y()
        dx, dy = abs(x1 - x0), -abs(y1 - y0)
        sx, sy = (1 if x0 < x1 else -1), (1 if y0 < y1 else -1)
        err = dx + dy
        while True:
            self._plot(QPoint(x0, y0), erase)
            if x0 == x1 and y0 == y1:
                break
            e2 = 2 * err
            if e2 >= dy:
                err += dy
                x0 += sx
            if e2 <= dx:
                err += dx
                y0 += sy

    def _fill(self, start: QPoint):
        target = self.image.pixel(start)
        new = self.color.rgba()
        if target == new:
            return
        w, h = self.image.width(), self.image.height()
        stack = [(start.x(), start.y())]
        while stack:
            x, y = stack.pop()
            if 0 <= x < w and 0 <= y < h and self.image.pixel(x, y) == target:
                self.image.setPixel(x, y, new)
                stack += [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]

    def mousePressEvent(self, event):
        pt = self._pixel(event.position())
        if pt is None:
            return
        right = event.button() == Qt.MouseButton.RightButton
        if self.tool == "picker" or (event.modifiers() & Qt.KeyboardModifier.AltModifier):
            self.color_picked.emit(self.image.pixelColor(pt))
            return
        self._push_undo()
        if self.tool == "bucket":
            self._fill(pt)
        else:
            self._plot(pt, erase=right or self.tool == "eraser")
            self._last = pt
            self._stroke = True
        self.update()
        self.changed.emit()

    def mouseMoveEvent(self, event):
        if not self._stroke:
            return
        pt = self._pixel(event.position())
        if pt is None:
            return
        erase = bool(event.buttons() & Qt.MouseButton.RightButton) or self.tool == "eraser"
        self._line(self._last or pt, pt, erase)
        self._last = pt
        self.update()
        self.changed.emit()

    def mouseReleaseEvent(self, event):
        self._stroke = False
        self._last = None

    def wheelEvent(self, event):
        if event.modifiers() & Qt.KeyboardModifier.ControlModifier:
            self.set_zoom(self.zoom + (2 if event.angleDelta().y() > 0 else -2))
            return
        super().wheelEvent(event)


class SpritePage(QWidget):
    modified = Signal()

    def __init__(self, project: Project, name: str, palette: Palette):
        super().__init__()
        self.setObjectName("Page")
        self.project = project
        self.name = name
        self.path = project.path("sprite", name)
        self.palette_ = palette
        self.meta = load_sprite_meta(self.path)
        self.dirty = False
        image = QImage(str(self.path))
        if image.isNull():
            image = QImage(16, 16, QImage.Format.Format_ARGB32)
            image.fill(Qt.GlobalColor.transparent)

        root = QVBoxLayout(self)
        root.setContentsMargins(12, 8, 12, 10)
        root.setSpacing(8)

        bar = QHBoxLayout()
        bar.setSpacing(4)
        self.tool_buttons = {}
        for tool, icon_name, tip in (("pencil", "pencil", "Lápis (B). Botão direito apaga."),
                                     ("eraser", "eraser", "Borracha (E)"),
                                     ("bucket", "bucket", "Balde (G)"),
                                     ("picker", "picker", "Conta-gotas (I). Alt + clique também pega a cor.")):
            b = QPushButton()
            b.setProperty("role", "tool")
            b.setToolTip(tip)
            b.clicked.connect(lambda _=False, t=tool: self.set_tool(t))
            self.tool_buttons[tool] = (b, icon_name)
            bar.addWidget(b)
        bar.addSpacing(8)
        self.color_button = QPushButton()
        self.color_button.setFixedSize(28, 24)
        self.color_button.setToolTip("Escolher cor")
        self.color_button.clicked.connect(self._choose_color)
        bar.addWidget(self.color_button)
        swatches = QGridLayout()
        swatches.setSpacing(2)
        for i, hexc in enumerate(SWATCHES):
            s = QPushButton()
            s.setFixedSize(14, 11)
            s.setStyleSheet(f"background: {hexc}; border-radius: 2px; min-height: 0; padding: 0;")
            s.clicked.connect(lambda _=False, c=hexc: self.set_color(QColor(c)))
            swatches.addWidget(s, i // 8, i % 8)
        bar.addLayout(swatches)
        bar.addStretch(1)

        self.frames_box = self._spin(1, 256, self.meta["frames"], "Quadros")
        self.fps_box = self._spin(1, 120, int(self.meta.get("fps", 8)), "Quadros por segundo")
        ox, oy = self.meta.get("origin", [0, 0])
        self.ox_box = self._spin(-4096, 4096, int(ox), "Origem X")
        self.oy_box = self._spin(-4096, 4096, int(oy), "Origem Y")
        for label, box in (("Quadros", self.frames_box), ("fps", self.fps_box), ("Origem", self.ox_box), ("", self.oy_box)):
            if label:
                l = QLabel(label)
                l.setProperty("role", "muted")
                bar.addWidget(l)
            bar.addWidget(box)
        center = QPushButton("Centralizar")
        center.setToolTip("Põe a origem no centro do quadro")
        center.clicked.connect(self._center_origin)
        bar.addWidget(center)
        root.addLayout(bar)

        body = QHBoxLayout()
        self.canvas = PixelCanvas(image, palette)
        self.canvas.frames = self.meta["frames"]
        self.canvas.origin = (int(ox), int(oy))
        self.canvas.changed.connect(self._on_change)
        self.canvas.color_picked.connect(self.set_color)
        scroll = QScrollArea()
        scroll.setWidget(self.canvas)
        scroll.setAlignment(Qt.AlignmentFlag.AlignCenter)
        scroll.setStyleSheet("QScrollArea { background: transparent; border: none; } QScrollArea > QWidget > QWidget { background: transparent; }")
        body.addWidget(scroll, 1)

        side = QVBoxLayout()
        side.setSpacing(6)
        title = QLabel("Prévia")
        title.setProperty("role", "section")
        side.addWidget(title)
        self.preview = QLabel()
        self.preview.setFixedSize(128, 128)
        self.preview.setAlignment(Qt.AlignmentFlag.AlignCenter)
        side.addWidget(self.preview)
        self.info = QLabel()
        self.info.setProperty("role", "muted")
        self.info.setWordWrap(True)
        side.addWidget(self.info)
        zoom = QHBoxLayout()
        minus, plus = QPushButton("−"), QPushButton("+")
        minus.clicked.connect(lambda: self.canvas.set_zoom(self.canvas.zoom - 4))
        plus.clicked.connect(lambda: self.canvas.set_zoom(self.canvas.zoom + 4))
        zoom.addWidget(minus)
        zoom.addWidget(plus)
        side.addLayout(zoom)
        side.addStretch(1)
        body.addLayout(side)
        root.addLayout(body, 1)

        for box in (self.frames_box, self.fps_box, self.ox_box, self.oy_box):
            box.valueChanged.connect(self._meta_changed)

        self._frame = 0
        self.timer = QTimer(self)
        self.timer.timeout.connect(self._tick)
        self.timer.start(1000 // max(1, self.fps_box.value()))
        self.set_tool("pencil")
        self.set_color(QColor("#ECEEF2"))
        self.apply_palette(palette)
        self._update_info()

    def _spin(self, lo, hi, value, tip):
        box = QSpinBox()
        box.setRange(lo, hi)
        box.setValue(value)
        box.setToolTip(tip)
        box.setFixedWidth(58)
        return box

    def apply_palette(self, palette: Palette):
        self.palette_ = palette
        self.canvas.palette_ = palette
        for tool, (b, icon_name) in self.tool_buttons.items():
            b.setIcon(icons.icon(icon_name, palette.ink))
        self.canvas.update()

    def set_tool(self, tool: str):
        self.canvas.tool = tool
        for t, (b, _) in self.tool_buttons.items():
            b.setProperty("on", "true" if t == tool else "false")
            b.style().unpolish(b)
            b.style().polish(b)

    def set_color(self, color: QColor):
        if color.alpha() == 0:
            return
        self.canvas.color = color
        self.color_button.setStyleSheet(f"background: {color.name(QColor.NameFormat.HexArgb)}; border-radius: 6px; border: 1px solid {self.palette_.muted};")

    def _choose_color(self):
        chosen = QColorDialog.getColor(self.canvas.color, self, "Cor do pincel", QColorDialog.ColorDialogOption.ShowAlphaChannel)
        if chosen.isValid():
            self.set_color(chosen)

    def keyPressEvent(self, event):
        mapping = {Qt.Key.Key_B: "pencil", Qt.Key.Key_E: "eraser", Qt.Key.Key_G: "bucket", Qt.Key.Key_I: "picker"}
        if event.key() in mapping and not event.modifiers():
            self.set_tool(mapping[event.key()])
            return
        super().keyPressEvent(event)

    def undo(self):
        self.canvas.undo_step()

    def redo(self):
        self.canvas.redo_step()

    def _center_origin(self):
        fw = self.canvas.image.width() // max(1, self.frames_box.value())
        self.ox_box.setValue(fw // 2)
        self.oy_box.setValue(self.canvas.image.height() // 2)

    def _meta_changed(self):
        self.canvas.frames = self.frames_box.value()
        self.canvas.origin = (self.ox_box.value(), self.oy_box.value())
        self.timer.setInterval(1000 // max(1, self.fps_box.value()))
        self.canvas.update()
        self._update_info()
        self._on_change()

    def _update_info(self):
        img = self.canvas.image
        frames = max(1, self.frames_box.value())
        warn = "" if img.width() % frames == 0 else "\nA largura não divide por igual entre os quadros."
        self.info.setText(f"{img.width()} × {img.height()} px\n{frames} quadro(s) de {img.width() // frames} × {img.height()}{warn}")

    def _tick(self):
        img = self.canvas.image
        frames = max(1, self.frames_box.value())
        fw = max(1, img.width() // frames)
        self._frame = (self._frame + 1) % frames
        frame = img.copy(QRect(self._frame * fw, 0, fw, img.height()))
        px = QPixmap.fromImage(frame).scaled(QSize(120, 120), Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
        self.preview.setPixmap(px)

    def _on_change(self):
        self.dirty = True
        self.modified.emit()

    def save(self):
        self.canvas.image.save(str(self.path), "PNG")
        self.meta.update({"frames": self.frames_box.value(), "fps": self.fps_box.value(),
                          "origin": [self.ox_box.value(), self.oy_box.value()]})
        save_sprite_meta(self.path, self.meta)
        self.dirty = False
        self.modified.emit()
