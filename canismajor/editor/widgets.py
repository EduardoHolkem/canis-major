"""Peças da janela no estilo do Sirius: barra de título, cápsula, console, barra de comando e tela inicial."""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path
from typing import Callable

from PySide6.QtCore import QEvent, QPoint, QSize, Qt, Signal
from PySide6.QtGui import QColor, QIcon, QTextCharFormat, QTextCursor
from PySide6.QtWidgets import (QDialog, QHBoxLayout, QLabel, QLineEdit, QListWidget, QListWidgetItem,
                               QPlainTextEdit, QPushButton, QSizePolicy, QVBoxLayout, QWidget)

from .. import __version__
from . import icons
from .theme import Palette


def set_prop(widget: QWidget, name: str, value) -> None:
    widget.setProperty(name, value)
    widget.style().unpolish(widget)
    widget.style().polish(widget)


class TitleBar(QWidget):
    open_palette = Signal()
    open_settings = Signal()
    project_menu = Signal(QPoint)

    def __init__(self, window: QWidget):
        super().__init__()
        self.win = window
        self.setFixedHeight(40)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(14, 0, 0, 0)
        layout.setSpacing(6)

        left = QHBoxLayout()
        left.setSpacing(6)
        self.logo = QLabel()
        left.addWidget(self.logo)
        self.brand = QLabel("Canis Major")
        self.brand.setProperty("role", "brand")
        left.addWidget(self.brand)
        self.project_button = QPushButton()
        self.project_button.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
        self.project_button.setIconSize(QSize(14, 14))
        self.project_button.clicked.connect(lambda: self.project_menu.emit(
            self.project_button.mapToGlobal(QPoint(0, self.project_button.height() + 2))))
        self.project_button.hide()
        left.addWidget(self.project_button)
        left_box = QWidget()
        left_box.setLayout(left)
        left.setContentsMargins(0, 0, 0, 0)

        self.pill = QPushButton("Ir para recurso ou comando")
        self.pill.setProperty("role", "pill")
        self.pill.setFixedWidth(400)
        self.pill.setIconSize(QSize(14, 14))
        self.pill.clicked.connect(self.open_palette.emit)
        pill_layout = QHBoxLayout(self.pill)
        pill_layout.setContentsMargins(0, 0, 4, 0)
        pill_layout.addStretch(1)
        self.shortcut = QLabel("Ctrl K")
        pill_layout.addWidget(self.shortcut)

        self.settings_btn = QPushButton()
        self.settings_btn.setProperty("role", "chrome")
        self.settings_btn.setToolTip("Configurações (Ctrl ,)")
        self.settings_btn.clicked.connect(self.open_settings.emit)
        self.min_btn = QPushButton()
        self.min_btn.setProperty("role", "chrome")
        self.min_btn.setToolTip("Minimizar")
        self.min_btn.clicked.connect(self.win.showMinimized)
        self.max_btn = QPushButton()
        self.max_btn.setProperty("role", "chrome")
        self.max_btn.clicked.connect(self.toggle_max)
        self.close_btn = QPushButton()
        self.close_btn.setProperty("role", "close")
        self.close_btn.setToolTip("Fechar")
        self.close_btn.clicked.connect(self.win.close)

        layout.addWidget(left_box, 1, Qt.AlignmentFlag.AlignLeft)
        layout.addWidget(self.pill, 0, Qt.AlignmentFlag.AlignCenter)
        right = QHBoxLayout()
        right.setSpacing(0)
        right.addStretch(1)
        for b in (self.settings_btn, self.min_btn, self.max_btn, self.close_btn):
            right.addWidget(b)
        right_box = QWidget()
        right.setContentsMargins(0, 0, 0, 0)
        right_box.setLayout(right)
        layout.addWidget(right_box, 1)

    def apply_palette(self, p: Palette):
        self.logo.setPixmap(icons.pixmap("star", p.ink, 16))
        self.pill.setIcon(icons.icon("search", p.muted, 14))
        self.shortcut.setStyleSheet(f"background: {p.sheet}; color: {p.muted}; border-radius: 10px; padding: 2px 8px; font-size: 11px; font-weight: 500;")
        self.project_button.setIcon(icons.icon("chevron-down", p.muted, 14))
        self.project_button.setStyleSheet(f"color: {p.muted}; padding: 0 6px;")
        self.settings_btn.setIcon(icons.icon("sliders", p.muted))
        self.min_btn.setIcon(icons.icon("minimize", p.muted))
        self.close_btn.setIcon(icons.icon("close", p.muted))
        self.update_max_icon(p)

    def update_max_icon(self, p: Palette | None = None):
        p = p or self.win.palette_
        self.max_btn.setIcon(icons.icon("restore" if self.win.isMaximized() else "maximize", p.muted))
        self.max_btn.setToolTip("Restaurar" if self.win.isMaximized() else "Maximizar")

    def set_project(self, name: str | None):
        self.project_button.setVisible(bool(name))
        self.project_button.setText(name or "")

    def toggle_max(self):
        if self.win.isMaximized():
            self.win.showNormal()
        else:
            self.win.showMaximized()

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and self.win.windowHandle():
            self.win.windowHandle().startSystemMove()

    def mouseDoubleClickEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.toggle_max()


class Capsule(QWidget):
    run_clicked = Signal()
    line_clicked = Signal()
    console_clicked = Signal()
    focus_clicked = Signal()
    theme_clicked = Signal()
    update_clicked = Signal()

    def __init__(self):
        super().__init__()
        self.setObjectName("Capsule")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setFixedHeight(40)
        layout = QHBoxLayout(self)
        layout.setContentsMargins(4, 4, 6, 4)
        layout.setSpacing(2)
        self.run_btn = QPushButton("Executar")
        self.run_btn.setProperty("role", "run")
        self.run_btn.setToolTip("Executar o jogo (F5)")
        self.run_btn.clicked.connect(self.run_clicked.emit)
        layout.addWidget(self.run_btn)
        layout.addSpacing(8)
        self.room_icon = QLabel()
        layout.addWidget(self.room_icon)
        self.room_label = QLabel("")
        self.room_label.setProperty("role", "muted")
        layout.addWidget(self.room_label)
        layout.addStretch(1)
        self.update_btn = QPushButton()
        self.update_btn.setProperty("role", "update")
        self.update_btn.clicked.connect(self.update_clicked.emit)
        self.update_btn.hide()
        layout.addWidget(self.update_btn)
        self.line_btn = QPushButton("")
        self.line_btn.setToolTip("Ir para uma linha (Ctrl G)")
        self.line_btn.clicked.connect(self.line_clicked.emit)
        layout.addWidget(self.line_btn)
        self.console_btn = QPushButton()
        self.console_btn.setToolTip("Console do jogo (Ctrl J)")
        self.console_btn.clicked.connect(self.console_clicked.emit)
        self.focus_btn = QPushButton()
        self.focus_btn.setToolTip("Modo foco (Ctrl .)")
        self.focus_btn.clicked.connect(self.focus_clicked.emit)
        self.theme_btn = QPushButton()
        self.theme_btn.setToolTip("Tema claro ou grafite")
        self.theme_btn.clicked.connect(self.theme_clicked.emit)
        for b in (self.console_btn, self.focus_btn, self.theme_btn):
            b.setFixedSize(32, 32)
            layout.addWidget(b)
        self.palette_: Palette | None = None
        self.running = False

    def apply_palette(self, p: Palette):
        self.palette_ = p
        self.set_running(self.running)
        self.room_icon.setPixmap(icons.pixmap("room", p.capsule_muted, 14))
        self.console_btn.setIcon(icons.icon("terminal", p.capsule_ink))
        self.focus_btn.setIcon(icons.icon("focus", p.capsule_ink))
        self.theme_btn.setIcon(icons.icon("contrast", p.capsule_ink))
        self.update_btn.setIcon(icons.icon("update", p.capsule_ink))

    def set_running(self, running: bool):
        self.running = running
        if self.palette_:
            self.run_btn.setIcon(icons.icon("stop" if running else "play", self.palette_.run_ink, 14))
        self.run_btn.setText("Parar" if running else "Executar")
        self.run_btn.setToolTip("Parar o jogo (F5)" if running else "Executar o jogo (F5)")

    def set_room(self, text: str):
        self.room_label.setText(text)
        self.room_icon.setVisible(bool(text))

    def set_position(self, line: int | None, col: int | None):
        self.line_btn.setVisible(line is not None)
        if line is not None:
            self.line_btn.setText(f"Ln {line}, Col {col}")

    def show_update(self, version: str | None):
        self.update_btn.setVisible(bool(version))
        if version:
            self.update_btn.setText(f"Versão {version}")
            self.update_btn.setToolTip(f"A versão {version} do Canis Major saiu. Clique para ver.")


_PY_LOC = re.compile(r'File "(?P<path>[^"]+)", line (?P<line>\d+)')
_GEN_LOC = re.compile(r"(?P<path>(?:[A-Za-z]:)?[^\s:\"']+\.(?:py|glsl|json)):(?P<line>\d+)")


class Console(QWidget):
    open_location = Signal(str, int)

    def __init__(self):
        super().__init__()
        self.setObjectName("Pop")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(12, 8, 8, 8)
        layout.setSpacing(4)
        head = QHBoxLayout()
        self.title = QLabel("Console do jogo")
        self.title.setProperty("role", "section")
        head.addWidget(self.title)
        head.addStretch(1)
        self.clear_btn = QPushButton("Limpar")
        self.clear_btn.setProperty("role", "segment")
        self.clear_btn.clicked.connect(lambda: self.text.clear())
        head.addWidget(self.clear_btn)
        self.close_btn = QPushButton()
        self.close_btn.setProperty("role", "tool")
        self.close_btn.clicked.connect(self.hide)
        head.addWidget(self.close_btn)
        layout.addLayout(head)
        self.text = QPlainTextEdit()
        self.text.setObjectName("Console")
        self.text.setReadOnly(True)
        self.text.setMaximumBlockCount(5000)
        self.text.viewport().installEventFilter(self)
        self.text.viewport().setMouseTracking(True)
        layout.addWidget(self.text, 1)
        self.palette_: Palette | None = None

    def apply_palette(self, p: Palette):
        self.palette_ = p
        self.close_btn.setIcon(icons.icon("x", p.muted))

    def append(self, text: str, kind: str = "out"):
        p = self.palette_
        fmt = QTextCharFormat()
        if p:
            fmt.setForeground(QColor({"err": p.error, "info": p.muted, "ok": p.added}.get(kind, p.ink)))
        cursor = self.text.textCursor()
        cursor.movePosition(QTextCursor.MoveOperation.End)
        for line in text.splitlines(keepends=True):
            line_fmt = QTextCharFormat(fmt)
            if _PY_LOC.search(line) or _GEN_LOC.search(line):
                line_fmt.setFontUnderline(True)
                if p:
                    line_fmt.setForeground(QColor(p.accent if kind != "err" else p.error))
            cursor.insertText(line, line_fmt)
        self.text.setTextCursor(cursor)
        self.text.ensureCursorVisible()

    def eventFilter(self, obj, event):
        if obj is self.text.viewport():
            if event.type() == QEvent.Type.MouseButtonRelease and event.button() == Qt.MouseButton.LeftButton:
                if not self.text.textCursor().hasSelection():
                    line = self.text.cursorForPosition(event.position().toPoint()).block().text()
                    m = _PY_LOC.search(line) or _GEN_LOC.search(line)
                    if m:
                        self.open_location.emit(m.group("path"), int(m.group("line")))
                        return True
            elif event.type() == QEvent.Type.MouseMove:
                line = self.text.cursorForPosition(event.position().toPoint()).block().text()
                linked = bool(_PY_LOC.search(line) or _GEN_LOC.search(line))
                self.text.viewport().setCursor(Qt.CursorShape.PointingHandCursor if linked else Qt.CursorShape.IBeamCursor)
        return super().eventFilter(obj, event)


@dataclass
class Command:
    label: str
    detail: str
    icon: QIcon | None
    action: Callable[[], None]


def fuzzy_score(query: str, text: str) -> int | None:
    """Subsequência com bônus para começo de palavra e letras seguidas. None se não casar."""
    if not query:
        return 0
    q, t = query.lower(), text.lower()
    if q in t:
        return 1000 - t.index(q) * 3 - len(t)
    score, ti, prev = 0, 0, -2
    for ch in q:
        found = t.find(ch, ti)
        if found < 0:
            return None
        score += 10 if found == prev + 1 else 0
        score += 8 if found == 0 or t[found - 1] in " _-/." else 0
        score -= found - ti
        prev, ti = found, found + 1
    return score


class CommandPalette(QDialog):
    def __init__(self, parent: QWidget, commands: list[Command], placeholder: str = "Ir para recurso ou comando"):
        super().__init__(parent, Qt.WindowType.Popup | Qt.WindowType.FramelessWindowHint)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.commands = commands
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        box = QWidget()
        box.setObjectName("Pop")
        box.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        outer.addWidget(box)
        layout = QVBoxLayout(box)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)
        self.input = QLineEdit()
        self.input.setPlaceholderText(placeholder)
        self.input.textChanged.connect(self._filter)
        self.input.installEventFilter(self)
        layout.addWidget(self.input)
        self.list = QListWidget()
        self.list.setIconSize(QSize(16, 16))
        self.list.itemActivated.connect(self._run)
        self.list.itemClicked.connect(self._run)
        layout.addWidget(self.list)
        self.resize(560, 380)
        self._filter("")

    def _filter(self, text: str):
        self.list.clear()
        scored = []
        for i, cmd in enumerate(self.commands):
            s = fuzzy_score(text.strip(), f"{cmd.label} {cmd.detail}")
            if s is not None:
                scored.append((-s, i, cmd))
        scored.sort(key=lambda x: (x[0], x[1]) if text.strip() else (0, x[1]))
        for _, _, cmd in scored[:80]:
            item = QListWidgetItem(cmd.icon or QIcon(), f"{cmd.label}    {cmd.detail}" if cmd.detail else cmd.label)
            item.setData(Qt.ItemDataRole.UserRole, cmd)
            self.list.addItem(item)
        if self.list.count():
            self.list.setCurrentRow(0)

    def _run(self, item: QListWidgetItem):
        cmd: Command = item.data(Qt.ItemDataRole.UserRole)
        self.accept()
        cmd.action()

    def eventFilter(self, obj, event):
        if obj is self.input and event.type() == QEvent.Type.KeyPress:
            key = event.key()
            if key in (Qt.Key.Key_Down, Qt.Key.Key_Up):
                row = self.list.currentRow() + (1 if key == Qt.Key.Key_Down else -1)
                self.list.setCurrentRow(max(0, min(self.list.count() - 1, row)))
                return True
            if key in (Qt.Key.Key_Return, Qt.Key.Key_Enter):
                if self.list.currentItem():
                    self._run(self.list.currentItem())
                return True
            if key == Qt.Key.Key_Escape:
                self.reject()
                return True
        return super().eventFilter(obj, event)


class StartPage(QWidget):
    new_project = Signal()
    open_project = Signal()
    open_recent = Signal(str)

    def __init__(self):
        super().__init__()
        self.setObjectName("Page")
        outer = QVBoxLayout(self)
        outer.addStretch(1)
        center = QVBoxLayout()
        center.setSpacing(10)
        self.logo = QLabel()
        self.logo.setAlignment(Qt.AlignmentFlag.AlignCenter)
        center.addWidget(self.logo)
        title = QLabel("Canis Major")
        title.setProperty("role", "title")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        center.addWidget(title)
        sub = QLabel(f"Engine de jogos 2D em Python, com shaders e elementos 3D · versão {__version__}")
        sub.setProperty("role", "muted")
        sub.setAlignment(Qt.AlignmentFlag.AlignCenter)
        center.addWidget(sub)
        center.addSpacing(10)
        buttons = QHBoxLayout()
        buttons.addStretch(1)
        new = QPushButton("Novo projeto")
        new.setProperty("role", "primary")
        new.clicked.connect(self.new_project.emit)
        open_ = QPushButton("Abrir projeto")
        open_.setProperty("role", "secondary")
        open_.clicked.connect(self.open_project.emit)
        buttons.addWidget(new)
        buttons.addWidget(open_)
        buttons.addStretch(1)
        center.addLayout(buttons)
        center.addSpacing(18)
        self.recent_title = QLabel("Recentes")
        self.recent_title.setProperty("role", "section")
        self.recent_title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        center.addWidget(self.recent_title)
        self.recent_box = QVBoxLayout()
        self.recent_box.setSpacing(2)
        center.addLayout(self.recent_box)
        wrap = QHBoxLayout()
        wrap.addStretch(1)
        holder = QWidget()
        holder.setLayout(center)
        holder.setFixedWidth(520)
        wrap.addWidget(holder)
        wrap.addStretch(1)
        outer.addLayout(wrap)
        outer.addStretch(2)

    def apply_palette(self, p: Palette):
        self.logo.setPixmap(icons.pixmap("star", p.accent, 44))

    def set_recent(self, paths: list[str]):
        while self.recent_box.count():
            item = self.recent_box.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        existing = [p for p in paths if (Path(p) / "projeto.json").exists()]
        self.recent_title.setVisible(bool(existing))
        for path in existing[:6]:
            b = QPushButton(f"{Path(path).name}    {path}")
            b.setProperty("role", "section")
            b.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
            b.clicked.connect(lambda _=False, p=path: self.open_recent.emit(p))
            self.recent_box.addWidget(b)
