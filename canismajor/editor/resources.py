"""Leituras rápidas dos recursos para o editor, sem rodar o jogo."""

from __future__ import annotations

import re
from pathlib import Path

from PySide6.QtCore import QRect, Qt
from PySide6.QtGui import QColor, QFont, QPainter, QPixmap

from ..project import Project, load_sprite_meta

_SPRITE_ATTR = re.compile(r"^\s+sprite\s*=\s*[\"']([A-Za-z_]\w*)[\"']", re.MULTILINE)


def object_sprite(project: Project, obj: str) -> str | None:
    """Lê `sprite = "nome"` do script do objeto, sem importá-lo."""
    try:
        text = project.path("object", obj).read_text("utf-8", errors="replace")
    except OSError:
        return None
    m = _SPRITE_ATTR.search(text)
    return m.group(1) if m else None


def sprite_frame(project: Project, name: str) -> tuple[QPixmap, tuple[float, float]] | None:
    png = project.path("sprite", name)
    if not png.exists():
        return None
    full = QPixmap(str(png))
    if full.isNull():
        return None
    meta = load_sprite_meta(png)
    w = full.width() // meta["frames"]
    ox, oy = meta.get("origin", [0, 0])
    return full.copy(QRect(0, 0, max(1, w), full.height())), (float(ox), float(oy))


def placeholder(name: str, accent: str, size: int = 16) -> QPixmap:
    px = QPixmap(size, size)
    px.fill(Qt.GlobalColor.transparent)
    p = QPainter(px)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    c = QColor(accent)
    fill = QColor(c)
    fill.setAlpha(60)
    p.setBrush(fill)
    p.setPen(c)
    p.drawRoundedRect(0.5, 0.5, size - 1, size - 1, 3, 3)
    font = QFont("Geist")
    font.setPixelSize(max(7, size // 2))
    font.setBold(True)
    p.setFont(font)
    p.drawText(QRect(0, 0, size, size), Qt.AlignmentFlag.AlignCenter, (name[:1] or "?").upper())
    p.end()
    return px


def object_pixmap(project: Project, obj: str, accent: str) -> tuple[QPixmap, tuple[float, float]]:
    sprite = object_sprite(project, obj)
    if sprite:
        found = sprite_frame(project, sprite)
        if found:
            return found
    return placeholder(obj, accent), (0.0, 0.0)


def thumbnail(pix: QPixmap, size: int = 18) -> QPixmap:
    if pix.isNull():
        return pix
    scaled = pix.scaled(size, size, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.FastTransformation)
    out = QPixmap(size, size)
    out.fill(Qt.GlobalColor.transparent)
    p = QPainter(out)
    p.drawPixmap((size - scaled.width()) // 2, (size - scaled.height()) // 2, scaled)
    p.end()
    return out


def is_post_shader_hint(path: Path) -> bool:
    try:
        return "tela" in path.read_text("utf-8", errors="replace")[:200].lower()
    except OSError:
        return False
