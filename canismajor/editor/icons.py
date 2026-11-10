"""Ícones de traço fino, desenhados em SVG e coloridos na hora conforme o tema."""

from __future__ import annotations

from functools import lru_cache

from PySide6.QtCore import QByteArray, QRectF, QSize, Qt
from PySide6.QtGui import QColor, QIcon, QPainter, QPixmap
from PySide6.QtSvg import QSvgRenderer

_STROKE = 'fill="none" stroke="{c}" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"'

PATHS = {
    "star": '<path d="M12 2.5c.6 4.8 3.7 7.9 8.5 8.5v1c-4.8.6-7.9 3.7-8.5 8.5h-1c-.6-4.8-3.7-7.9-8.5-8.5v-1c4.8-.6 7.9-3.7 8.5-8.5z" fill="{c}"/>',
    "sliders": '<g {s}><path d="M4 7h9M17 7h3M4 17h3M11 17h9"/><circle cx="15" cy="7" r="2"/><circle cx="9" cy="17" r="2"/></g>',
    "minimize": '<path d="M6 12h12" {s}/>',
    "maximize": '<rect x="6.5" y="6.5" width="11" height="11" rx="1" {s}/>',
    "restore": '<g {s}><rect x="5.5" y="8.5" width="10" height="10" rx="1"/><path d="M8.5 5.5h9a1 1 0 0 1 1 1v9"/></g>',
    "close": '<path d="M7 7l10 10M17 7L7 17" {s}/>',
    "x": '<path d="M8 8l8 8M16 8l-8 8" {s}/>',
    "play": '<path d="M8 5.5v13l10.5-6.5z" fill="{c}"/>',
    "stop": '<rect x="7" y="7" width="10" height="10" rx="1.5" fill="{c}"/>',
    "terminal": '<g {s}><path d="M5 8l4 4-4 4M12 17h7"/></g>',
    "focus": '<g {s}><path d="M4 9V5h4M20 9V5h-4M4 15v4h4M20 15v4h-4"/></g>',
    "contrast": '<g><circle cx="12" cy="12" r="7.5" {s}/><path d="M12 4.5a7.5 7.5 0 0 1 0 15z" fill="{c}"/></g>',
    "chevron-down": '<path d="M7 10l5 5 5-5" {s}/>',
    "chevron-right": '<path d="M10 7l5 5-5 5" {s}/>',
    "plus": '<path d="M12 6v12M6 12h12" {s}/>',
    "search": '<g {s}><circle cx="11" cy="11" r="6"/><path d="M20 20l-4.5-4.5"/></g>',
    "sprite": '<g {s}><rect x="4" y="5" width="16" height="14" rx="2"/><path d="M4 16l5-5 4 4 2-2 5 5"/><circle cx="15.5" cy="9.5" r="1.5"/></g>',
    "object": '<g {s}><path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9"/></g>',
    "room": '<g {s}><rect x="4" y="4" width="16" height="16" rx="2"/><path d="M4 10h16M10 10v10"/></g>',
    "shader": '<g {s}><circle cx="12" cy="12" r="8"/><path d="M12 4a8 8 0 0 0 0 16" fill="{c}" fill-opacity=".35"/><path d="M12 4v16"/></g>',
    "sound": '<g {s}><path d="M5 10v4h3l5 4V6L8 10z"/><path d="M16.5 9a4 4 0 0 1 0 6M19 6.5a7.5 7.5 0 0 1 0 11"/></g>',
    "folder": '<path d="M4 7a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z" {s}/>',
    "pencil": '<g {s}><path d="M15 5l4 4L9 19H5v-4z"/><path d="M13 7l4 4"/></g>',
    "eraser": '<g {s}><path d="M8 19h11M5.5 13.5l7-7a2 2 0 0 1 2.8 0l2.2 2.2a2 2 0 0 1 0 2.8L11 18H8.5z"/><path d="M9 10l5 5"/></g>',
    "bucket": '<g {s}><path d="M5 11l6-6 8 8-6 6z"/><path d="M5 11h14M20 16s1.5 2 1.5 3a1.5 1.5 0 0 1-3 0c0-1 1.5-3 1.5-3z"/></g>',
    "picker": '<g {s}><path d="M14 6l4 4M4 20l2-1 9-9-2-2-9 9z"/><path d="M13 5l2-2 6 6-2 2"/></g>',
    "game": '<g {s}><rect x="3" y="7" width="18" height="11" rx="5"/><path d="M8 11v3M6.5 12.5h3"/><circle cx="15.5" cy="11.5" r=".8" fill="{c}"/><circle cx="17.5" cy="13.5" r=".8" fill="{c}"/></g>',
    "update": '<g {s}><path d="M12 4v11M7.5 10.5L12 15l4.5-4.5M5 19h14"/></g>',
    "refresh": '<g {s}><path d="M19 12a7 7 0 1 1-2-4.9"/><path d="M19 4v4h-4"/></g>',
}


@lru_cache(maxsize=512)
def _pixmap(name: str, color: str, size: int, ratio: float) -> QPixmap:
    body = PATHS[name].replace("{s}", _STROKE).replace("{c}", QColor(color).name())
    alpha = QColor(color).alphaF()
    svg = f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" opacity="{alpha:.3f}">{body}</svg>'
    px = QPixmap(QSize(int(size * ratio), int(size * ratio)))
    px.fill(Qt.GlobalColor.transparent)
    painter = QPainter(px)
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)
    QSvgRenderer(QByteArray(svg.encode())).render(painter, QRectF(0, 0, px.width(), px.height()))
    painter.end()
    px.setDevicePixelRatio(ratio)
    return px


def icon(name: str, color: str, size: int = 16, ratio: float = 2.0) -> QIcon:
    return QIcon(_pixmap(name, color, size, ratio))


def pixmap(name: str, color: str, size: int = 16, ratio: float = 2.0) -> QPixmap:
    return _pixmap(name, color, size, ratio)


KIND_ICONS = {"sprite": "sprite", "object": "object", "room": "room", "shader": "shader", "sound": "sound"}
