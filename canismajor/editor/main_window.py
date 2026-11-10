"""Janela principal do editor."""

from __future__ import annotations

import ctypes
import os
import shutil
import sys
import time
from pathlib import Path

from PySide6.QtCore import QFileSystemWatcher, QPoint, QProcess, QProcessEnvironment, QSize, Qt, QTimer
from PySide6.QtGui import QAction, QIcon, QImage, QKeySequence, QShortcut
from PySide6.QtWidgets import (QApplication, QFileDialog, QHBoxLayout, QInputDialog, QLabel, QMainWindow, QMenu,
                               QMessageBox, QPushButton, QStackedWidget, QTabBar, QTreeWidget, QTreeWidgetItem,
                               QVBoxLayout, QWidget)

from .. import __version__
from ..project import KIND_LABELS, KIND_SINGULAR, KINDS, Project, ProjectError, write_text_atomic
from ..templates import NEW_OBJECT, NEW_SHADER, class_name, create_project
from . import icons
from .code_editor import CodeEditor, CodePage
from .dialogs import (NameDialog, NewProjectDialog, NewSpriteDialog, ProjectSettingsDialog, SettingsDialog,
                      UpdateDialog)
from .resources import object_pixmap, thumbnail
from .room_editor import RoomPage
from .settings import Settings
from .shader_editor import ShaderPage
from .sprite_editor import SpritePage
from .theme import PALETTES, Palette, stylesheet
from .updater import CheckWorker, DownloadWorker, ENGINE_ROOT, is_newer, launch_apply, start_thread
from .widgets import Capsule, Command, CommandPalette, Console, StartPage, TitleBar

EDGE = 5


def send_to_recycle_bin(path: Path) -> bool:
    """Manda para a Lixeira do Windows. Devolve False se não conseguir (aí nada é apagado)."""
    if os.name != "nt":
        return False

    class SHFILEOPSTRUCTW(ctypes.Structure):
        _fields_ = [("hwnd", ctypes.c_void_p), ("wFunc", ctypes.c_uint), ("pFrom", ctypes.c_wchar_p),
                    ("pTo", ctypes.c_wchar_p), ("fFlags", ctypes.c_ushort), ("fAnyOperationsAborted", ctypes.c_int),
                    ("hNameMappings", ctypes.c_void_p), ("lpszProgressTitle", ctypes.c_wchar_p)]

    FO_DELETE, FOF_ALLOWUNDO, FOF_NOCONFIRMATION, FOF_SILENT, FOF_NOERRORUI = 3, 0x40, 0x10, 0x4, 0x400
    op = SHFILEOPSTRUCTW(None, FO_DELETE, str(path) + "\0", None,
                         FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI, 0, None, None)
    return ctypes.windll.shell32.SHFileOperationW(ctypes.byref(op)) == 0 and not op.fAnyOperationsAborted


class Root(QWidget):
    """Fundo da janela. Sua borda fina é onde o mouse redimensiona a janela sem moldura."""

    def __init__(self, win: "MainWindow"):
        super().__init__()
        self.win = win
        self.setObjectName("Root")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)
        self.setMouseTracking(True)

    def _edges(self, pos: QPoint) -> Qt.Edge:
        if self.win.isMaximized():
            return Qt.Edge(0)
        edges = Qt.Edge(0)
        if pos.x() <= EDGE:
            edges |= Qt.Edge.LeftEdge
        if pos.x() >= self.width() - EDGE:
            edges |= Qt.Edge.RightEdge
        if pos.y() <= EDGE:
            edges |= Qt.Edge.TopEdge
        if pos.y() >= self.height() - EDGE:
            edges |= Qt.Edge.BottomEdge
        return edges

    def mouseMoveEvent(self, event):
        e = self._edges(event.position().toPoint())
        diag1 = Qt.Edge.LeftEdge | Qt.Edge.TopEdge, Qt.Edge.RightEdge | Qt.Edge.BottomEdge
        diag2 = Qt.Edge.RightEdge | Qt.Edge.TopEdge, Qt.Edge.LeftEdge | Qt.Edge.BottomEdge
        if e in diag1:
            self.setCursor(Qt.CursorShape.SizeFDiagCursor)
        elif e in diag2:
            self.setCursor(Qt.CursorShape.SizeBDiagCursor)
        elif e & (Qt.Edge.LeftEdge | Qt.Edge.RightEdge):
            self.setCursor(Qt.CursorShape.SizeHorCursor)
        elif e & (Qt.Edge.TopEdge | Qt.Edge.BottomEdge):
            self.setCursor(Qt.CursorShape.SizeVerCursor)
        else:
            self.unsetCursor()

    def mousePressEvent(self, event):
        e = self._edges(event.position().toPoint())
        if e and event.button() == Qt.MouseButton.LeftButton and self.win.windowHandle():
            self.win.windowHandle().startSystemResize(e)


class SheetHost(QWidget):
    """A folha arredondada com as abas. A cápsula e o console flutuam por cima dela."""

    def __init__(self, win: "MainWindow"):
        super().__init__()
        self.win = win
        self.setObjectName("Sheet")
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, True)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self.win.place_overlays()


class SoundPage(QWidget):
    def __init__(self, project: Project, name: str):
        super().__init__()
        self.setObjectName("Page")
        self.path = project.path("sound", name)
        self.dirty = False
        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 24, 24, 24)
        title = QLabel(name)
        title.setProperty("role", "title")
        layout.addWidget(title)
        size = self.path.stat().st_size if self.path.exists() else 0
        info = QLabel(f"{self.path.name} · {size / 1024:.1f} KB\nNo jogo: game.play_sound(\"{name}\")")
        info.setProperty("role", "muted")
        info.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        layout.addWidget(info)
        play = QPushButton("Tocar")
        play.setProperty("role", "primary")
        play.clicked.connect(self.play)
        row = QHBoxLayout()
        row.addWidget(play)
        row.addStretch(1)
        layout.addLayout(row)
        layout.addStretch(1)

    def play(self):
        try:
            os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")
            import pygame
            if not pygame.mixer.get_init():
                pygame.mixer.init()
            pygame.mixer.Sound(str(self.path)).play()
        except Exception as e:
            QMessageBox.warning(self, "Som", f"Não foi possível tocar: {e}")

    def save(self):
        pass


class MainWindow(QMainWindow):
    def __init__(self, settings: Settings):
        super().__init__()
        self.settings = settings
        self.palette_: Palette = PALETTES.get(settings.theme, PALETTES["grafite"])
        self.project: Project | None = None
        self.pages: dict[tuple[str, str], QWidget] = {}
        self.process: QProcess | None = None
        self.release = None
        self._threads = []
        self.focus_mode = False

        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.Window)
        self.setWindowTitle("Canis Major")
        self.setMinimumSize(820, 520)
        app_icon = Path(__file__).resolve().parent.parent / "assets" / "icon.png"
        if app_icon.exists():
            self.setWindowIcon(QIcon(str(app_icon)))

        self.root = Root(self)
        self.setCentralWidget(self.root)
        outer = QVBoxLayout(self.root)
        outer.setContentsMargins(EDGE, 0, EDGE, EDGE)
        outer.setSpacing(0)

        self.title = TitleBar(self)
        self.title.open_palette.connect(self.show_palette)
        self.title.open_settings.connect(self.show_settings)
        self.title.project_menu.connect(self.show_project_menu)
        outer.addWidget(self.title)

        body = QHBoxLayout()
        body.setSpacing(8)
        body.setContentsMargins(3, 0, 0, 0)
        outer.addLayout(body, 1)

        # ---------- barra lateral ----------
        self.sidebar = QWidget()
        self.sidebar.setFixedWidth(max(180, settings.sidebar_width))
        side = QVBoxLayout(self.sidebar)
        side.setContentsMargins(0, 0, 0, 0)
        side.setSpacing(2)
        head = QHBoxLayout()
        head.setContentsMargins(6, 0, 0, 0)
        self.side_title = QLabel("Recursos")
        self.side_title.setProperty("role", "section")
        head.addWidget(self.side_title)
        head.addStretch(1)
        self.add_btn = QPushButton()
        self.add_btn.setProperty("role", "tool")
        self.add_btn.setToolTip("Novo recurso")
        self.add_btn.clicked.connect(lambda: self.new_menu().exec(self.add_btn.mapToGlobal(QPoint(0, 26))))
        head.addWidget(self.add_btn)
        self.folder_btn = QPushButton()
        self.folder_btn.setProperty("role", "tool")
        self.folder_btn.setToolTip("Abrir a pasta do projeto no Explorer")
        self.folder_btn.clicked.connect(self.reveal_project)
        head.addWidget(self.folder_btn)
        side.addLayout(head)
        self.tree = QTreeWidget()
        self.tree.setHeaderHidden(True)
        self.tree.setIndentation(14)
        self.tree.setIconSize(QSize(16, 16))
        self.tree.setContextMenuPolicy(Qt.ContextMenuPolicy.CustomContextMenu)
        self.tree.customContextMenuRequested.connect(self._tree_menu)
        self.tree.itemActivated.connect(self._tree_open)
        self.tree.itemClicked.connect(self._tree_open)
        side.addWidget(self.tree, 1)
        body.addWidget(self.sidebar)

        # ---------- folha ----------
        self.sheet = SheetHost(self)
        sheet_l = QVBoxLayout(self.sheet)
        sheet_l.setContentsMargins(0, 0, 0, 0)
        sheet_l.setSpacing(0)
        tab_row = QHBoxLayout()
        tab_row.setContentsMargins(10, 0, 14, 0)
        self.tabs = QTabBar()
        self.tabs.setExpanding(False)
        self.tabs.setMovable(True)
        self.tabs.setDocumentMode(True)
        self.tabs.setDrawBase(False)
        self.tabs.setIconSize(QSize(14, 14))
        self.tabs.setElideMode(Qt.TextElideMode.ElideNone)
        self.tabs.setUsesScrollButtons(True)
        self.tabs.currentChanged.connect(self._tab_changed)
        self.tabs.tabMoved.connect(lambda *_: None)
        tab_row.addWidget(self.tabs, 1)
        self.breadcrumb = QLabel()
        self.breadcrumb.setProperty("role", "muted")
        tab_row.addWidget(self.breadcrumb)
        self.tab_strip = QWidget()
        self.tab_strip.setLayout(tab_row)
        self.tab_strip.setFixedHeight(38)
        sheet_l.addWidget(self.tab_strip)
        self.stack = QStackedWidget()
        self.stack.setObjectName("Pages")
        self.start_page = StartPage()
        self.start_page.new_project.connect(self.new_project)
        self.start_page.open_project.connect(self.open_project_dialog)
        self.start_page.open_recent.connect(self.open_project)
        self.stack.addWidget(self.start_page)
        sheet_l.addWidget(self.stack, 1)
        sheet_l.addSpacing(56)  # espaço da cápsula
        body.addWidget(self.sheet, 1)

        self.console = Console()
        self.console.setParent(self.sheet)
        self.console.hide()
        self.console.open_location.connect(self.open_location)
        self.capsule = Capsule()
        self.capsule.setParent(self.sheet)
        self.capsule.run_clicked.connect(self.toggle_run)
        self.capsule.console_clicked.connect(self.toggle_console)
        self.capsule.focus_clicked.connect(self.toggle_focus)
        self.capsule.theme_clicked.connect(self.toggle_theme)
        self.capsule.line_clicked.connect(self.go_to_line)
        self.capsule.update_clicked.connect(self.show_update)

        self.watcher = QFileSystemWatcher(self)
        self.watcher.directoryChanged.connect(lambda *_: self._refresh_timer.start())
        self._refresh_timer = QTimer(self)
        self._refresh_timer.setSingleShot(True)
        self._refresh_timer.setInterval(250)
        self._refresh_timer.timeout.connect(self.refresh_tree)
        self._autosave = QTimer(self)
        self._autosave.setSingleShot(True)
        self._autosave.setInterval(1000)
        self._autosave.timeout.connect(self.save_all)

        self._shortcuts()
        self.apply_settings()
        self._show_project_ui(False)
        self.start_page.set_recent(settings.recent)
        if len(settings.window) == 4:
            self.setGeometry(*settings.window)
        else:
            self.resize(1280, 800)
        QTimer.singleShot(1500, lambda: self.check_updates(manual=False))

    # ---------- aparência ----------

    def apply_settings(self):
        self.palette_ = PALETTES.get(self.settings.theme, PALETTES["grafite"])
        p = self.palette_
        QApplication.instance().setStyleSheet(stylesheet(p, self.settings.ui_size))
        self.title.apply_palette(p)
        self.capsule.apply_palette(p)
        self.console.apply_palette(p)
        self.start_page.apply_palette(p)
        self.add_btn.setIcon(icons.icon("plus", p.muted))
        self.folder_btn.setIcon(icons.icon("folder", p.muted))
        for page in self.pages.values():
            if isinstance(page, CodePage):
                page.editor.apply_palette(p)
                page.editor.set_code_size(self.settings.code_size)
                page.editor.set_word_wrap(self.settings.word_wrap)
                page.editor.auto_pairs = self.settings.auto_pairs
            elif isinstance(page, ShaderPage):
                page.apply_palette(p)
                page.editor.set_code_size(self.settings.code_size)
            elif hasattr(page, "apply_palette"):
                page.apply_palette(p)
        self.refresh_tree()
        self._refresh_tab_icons()
        self.place_overlays()

    def toggle_theme(self):
        self.settings.theme = "claro" if self.settings.theme == "grafite" else "grafite"
        self.settings.save()
        self.apply_settings()

    def toggle_focus(self):
        self.focus_mode = not self.focus_mode
        self.sidebar.setVisible(not self.focus_mode and self.project is not None)

    def changeEvent(self, event):
        super().changeEvent(event)
        if event.type() == event.Type.WindowStateChange:
            self.title.update_max_icon()
            m = 0 if self.isMaximized() else EDGE
            self.root.layout().setContentsMargins(m, 0, m, m)

    def place_overlays(self):
        if not hasattr(self, "capsule"):
            return
        w, h = self.sheet.width(), self.sheet.height()
        cw = min(600, max(360, w - 40))
        self.capsule.setGeometry((w - cw) // 2, h - 52, cw, 40)
        self.capsule.raise_()
        ch = min(280, max(140, h // 3))
        self.console.setGeometry(16, h - 60 - ch, w - 32, ch)
        self.console.raise_()
        self.capsule.raise_()

    # ---------- atalhos ----------

    def _shortcuts(self):
        def sc(keys, fn):
            s = QShortcut(QKeySequence(keys), self)
            s.setContext(Qt.ShortcutContext.WindowShortcut)
            s.activated.connect(fn)
            return s

        sc("Ctrl+K", self.show_palette)
        sc("Ctrl+S", self.save_current)
        sc("Ctrl+Shift+S", self.save_all)
        sc("Ctrl+W", lambda: self.close_tab(self.tabs.currentIndex()))
        sc("Ctrl+O", self.open_project_dialog)
        sc("F5", self.toggle_run)
        sc("Ctrl+J", self.toggle_console)
        sc("Ctrl+,", self.show_settings)
        sc("Ctrl+.", self.toggle_focus)
        sc("Ctrl+G", self.go_to_line)
        sc("Ctrl+F", self.find_in_file)
        sc("Ctrl+Z", lambda: self._page_call("undo"))
        sc("Ctrl+Y", lambda: self._page_call("redo"))
        sc("Ctrl+Shift+Z", lambda: self._page_call("redo"))
        sc("Ctrl+=", lambda: self.zoom_code(0.5))
        sc("Ctrl++", lambda: self.zoom_code(0.5))
        sc("Ctrl+-", lambda: self.zoom_code(-0.5))
        sc("Ctrl+0", lambda: self.zoom_code(None))
        sc("Ctrl+Tab", lambda: self.tabs.setCurrentIndex((self.tabs.currentIndex() + 1) % max(1, self.tabs.count())))

    def _page_call(self, method: str):
        page = self.current_page()
        if isinstance(page, (CodePage, ShaderPage)):
            editor = page.editor
            getattr(editor, method)()
        elif page is not None and hasattr(page, method):
            getattr(page, method)()

    def zoom_code(self, delta):
        self.settings.code_size = 13.5 if delta is None else max(9.0, min(28.0, self.settings.code_size + delta))
        self.settings.save()
        for page in self.pages.values():
            if isinstance(page, (CodePage, ShaderPage)):
                page.editor.set_code_size(self.settings.code_size)

    # ---------- projeto ----------

    def _show_project_ui(self, has_project: bool):
        self.sidebar.setVisible(has_project and not self.focus_mode)
        self.tab_strip.setVisible(has_project)
        self.capsule.setVisible(has_project)
        if not has_project:
            self.stack.setCurrentWidget(self.start_page)
            self.capsule.set_room("")
        self.title.set_project(self.project.name if self.project else None)

    def default_projects_dir(self) -> str:
        if self.settings.projects_dir:
            return self.settings.projects_dir
        if self.settings.recent:
            return str(Path(self.settings.recent[0]).parent)
        return str(Path.home() / "Documents" / "Canis Major")

    def new_project(self):
        dialog = NewProjectDialog(self, self.default_projects_dir())
        if dialog.exec():
            target = dialog.target()
            try:
                create_project(target, dialog.name.text().strip(), dialog.template.currentData())
            except (OSError, FileExistsError) as e:
                QMessageBox.warning(self, "Novo projeto", f"Não foi possível criar o projeto:\n{e}")
                return
            self.settings.projects_dir = str(target.parent)
            self.open_project(str(target))

    def open_project_dialog(self):
        folder = QFileDialog.getExistingDirectory(self, "Abrir projeto do Canis Major", self.default_projects_dir())
        if folder:
            self.open_project(folder)

    def open_project(self, folder: str):
        if not self.close_all_tabs():
            return
        try:
            project = Project.load(folder)
        except ProjectError as e:
            QMessageBox.warning(self, "Abrir projeto", str(e))
            return
        self.stop_game()
        self.project = project
        self.settings.add_recent(str(project.root))
        self.setWindowTitle(f"{project.name} · Canis Major")
        if self.watcher.directories():
            self.watcher.removePaths(self.watcher.directories())
        dirs = [str(project.root)] + [str(project.folder(k)) for k in KINDS if project.folder(k).is_dir()]
        self.watcher.addPaths(dirs)
        self._show_project_ui(True)
        self.refresh_tree()
        self.console.text.clear()
        start = project.start_room if project.start_room in project.resources("room") else None
        if start:
            self.open_resource("room", start)
        self.capsule.set_room(project.start_room)

    def close_project(self):
        if not self.close_all_tabs():
            return
        self.stop_game()
        if self.watcher.directories():
            self.watcher.removePaths(self.watcher.directories())
        self.project = None
        self.tree.clear()
        self.setWindowTitle("Canis Major")
        self.start_page.set_recent(self.settings.recent)
        self._show_project_ui(False)

    def reveal_project(self):
        if self.project:
            os.startfile(str(self.project.root))  # noqa: S606 - abre o Explorer na pasta do projeto

    def show_project_menu(self, pos: QPoint):
        menu = QMenu(self)
        menu.addAction("Configurações do jogo…", self.project_settings)
        menu.addAction("Exportar jogo para Windows…", self.export_game)
        menu.addAction("Abrir a pasta no Explorer", self.reveal_project)
        menu.addSeparator()
        menu.addAction("Novo projeto…", self.new_project)
        menu.addAction("Abrir projeto…", self.open_project_dialog)
        recent = [p for p in self.settings.recent if self.project is None or Path(p) != self.project.root]
        if recent:
            sub = menu.addMenu("Recentes")
            for path in recent[:8]:
                sub.addAction(f"{Path(path).name}  ·  {path}", lambda p=path: self.open_project(p))
        menu.addSeparator()
        menu.addAction("Fechar projeto", self.close_project)
        menu.exec(pos)

    def project_settings(self):
        if self.project and ProjectSettingsDialog(self, self.project).exec():
            self.title.set_project(self.project.name)
            self.setWindowTitle(f"{self.project.name} · Canis Major")
            self.capsule.set_room(self.project.start_room)
            for page in self.pages.values():
                if isinstance(page, RoomPage):
                    page.start_check.blockSignals(True)
                    page.start_check.setChecked(self.project.start_room == page.name)
                    page.start_check.blockSignals(False)
                    page.view.viewport().update()

    # ---------- árvore ----------

    def refresh_tree(self):
        if not hasattr(self, "tree"):
            return
        if self.project is None:
            self.tree.clear()
            return
        expanded = {self.tree.topLevelItem(i).data(0, Qt.ItemDataRole.UserRole)[0]
                    for i in range(self.tree.topLevelItemCount()) if self.tree.topLevelItem(i).isExpanded()}
        first = self.tree.topLevelItemCount() == 0
        current = self.current_key()
        self.tree.clear()
        p = self.palette_
        for kind in KINDS:
            cat = QTreeWidgetItem([KIND_LABELS[kind]])
            cat.setData(0, Qt.ItemDataRole.UserRole, (kind, None))
            cat.setIcon(0, icons.icon("folder", p.muted))
            cat.setForeground(0, self.tree.palette().text())
            self.tree.addTopLevelItem(cat)
            for name in self.project.resources(kind):
                item = QTreeWidgetItem([name])
                item.setData(0, Qt.ItemDataRole.UserRole, (kind, name))
                if kind == "sprite":
                    from .resources import sprite_frame
                    found = sprite_frame(self.project, name)
                    item.setIcon(0, QIcon(thumbnail(found[0], 16)) if found else icons.icon("sprite", p.accent))
                elif kind == "object":
                    pix, _ = object_pixmap(self.project, name, p.accent)
                    item.setIcon(0, QIcon(thumbnail(pix, 16)))
                else:
                    item.setIcon(0, icons.icon(icons.KIND_ICONS[kind], p.accent))
                if kind == "room" and name == self.project.start_room:
                    item.setToolTip(0, "Sala inicial do jogo")
                    item.setText(0, f"{name}  ★")
                cat.addChild(item)
                if (kind, name) == current:
                    self.tree.setCurrentItem(item)
            cat.setExpanded(first or kind in expanded)
        for page in self.pages.values():
            if isinstance(page, RoomPage):
                page.refresh_lists()

    def _tree_open(self, item: QTreeWidgetItem, _col=0):
        kind, name = item.data(0, Qt.ItemDataRole.UserRole)
        if name:
            self.open_resource(kind, name)

    def _tree_menu(self, pos: QPoint):
        item = self.tree.itemAt(pos)
        menu = QMenu(self)
        if item:
            kind, name = item.data(0, Qt.ItemDataRole.UserRole)
            if name:
                menu.addAction("Abrir", lambda: self.open_resource(kind, name))
                menu.addAction("Renomear…", lambda: self.rename_resource(kind, name))
                menu.addAction("Duplicar", lambda: self.duplicate_resource(kind, name))
                if kind == "room":
                    menu.addAction("Executar a partir desta sala", lambda: self.run_game(room=name))
                    menu.addAction("Tornar sala inicial", lambda: self.set_start_room(name))
                menu.addSeparator()
                menu.addAction("Mandar para a Lixeira", lambda: self.delete_resource(kind, name))
            else:
                menu.addAction(f"Novo {KIND_SINGULAR[kind]}…", lambda: self.new_resource(kind))
                if kind in ("sprite", "sound"):
                    menu.addAction("Importar arquivo…", lambda: self.import_files(kind))
        else:
            menu = self.new_menu()
        menu.exec(self.tree.viewport().mapToGlobal(pos))

    def new_menu(self) -> QMenu:
        menu = QMenu(self)
        p = self.palette_
        for kind in KINDS:
            if kind == "sound":
                continue
            menu.addAction(icons.icon(icons.KIND_ICONS[kind], p.accent), f"Novo {KIND_SINGULAR[kind]}…",
                           lambda k=kind: self.new_resource(k))
        menu.addSeparator()
        menu.addAction(icons.icon("sprite", p.muted), "Importar imagens…", lambda: self.import_files("sprite"))
        menu.addAction(icons.icon("sound", p.muted), "Importar sons…", lambda: self.import_files("sound"))
        return menu

    def set_start_room(self, name: str):
        if self.project:
            self.project.start_room = name
            self.project.save()
            self.capsule.set_room(name)
            self.refresh_tree()

    # ---------- recursos ----------

    def new_resource(self, kind: str):
        if not self.project:
            return
        if kind == "sound":
            self.import_files("sound")
            return
        dialog = NewSpriteDialog(self, self.project) if kind == "sprite" else NameDialog(self, kind, self.project)
        if not dialog.exec():
            return
        name = dialog.name()
        path = self.project.path(kind, name)
        path.parent.mkdir(parents=True, exist_ok=True)
        if kind == "object":
            write_text_atomic(path, NEW_OBJECT.format(cls=class_name(name)))
        elif kind == "shader":
            write_text_atomic(path, NEW_SHADER)
        elif kind == "room":
            from ..project import save_room
            save_room(path, {"width": self.project.width, "height": self.project.height, "background": "#16181D",
                             "grid": 16, "post_shaders": [], "instances": []})
        elif kind == "sprite":
            from ..project import save_sprite_meta
            frames = dialog.frames.value()
            img = QImage(dialog.w.value() * frames, dialog.h.value(), QImage.Format.Format_ARGB32)
            img.fill(Qt.GlobalColor.transparent)
            img.save(str(path), "PNG")
            save_sprite_meta(path, {"frames": frames, "origin": [0, 0], "fps": 8})
        self.refresh_tree()
        self.open_resource(kind, name)

    def import_files(self, kind: str):
        if not self.project:
            return
        filters = {"sprite": "Imagens PNG (*.png)", "sound": "Sons (*.wav *.ogg *.mp3)"}
        files, _ = QFileDialog.getOpenFileNames(self, "Importar", str(Path.home()), filters[kind])
        for f in files:
            src = Path(f)
            stem = "".join(c if c.isalnum() or c == "_" else "_" for c in src.stem).strip("_") or "recurso"
            if stem[0].isdigit():
                stem = "_" + stem
            name, n = stem, 2
            while name.lower() in {r.lower() for r in self.project.resources(kind)}:
                name, n = f"{stem}_{n}", n + 1
            dest = self.project.folder(kind) / f"{name}{src.suffix.lower()}"
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(src, dest)
        self.refresh_tree()

    def _files_of(self, kind: str, name: str) -> list[Path]:
        main = self.project.path(kind, name)
        files = [main]
        if kind == "sprite":
            meta = main.with_suffix(".json")
            if meta.exists():
                files.append(meta)
        return [f for f in files if f.exists()]

    def rename_resource(self, kind: str, name: str):
        dialog = NameDialog(self, kind, self.project, initial=name, title=f"Renomear {KIND_SINGULAR[kind]}")
        if not dialog.exec() or dialog.name() == name:
            return
        new = dialog.name()
        key = (kind, name)
        if key in self.pages and getattr(self.pages[key], "dirty", False):
            self.pages[key].save()
        for f in self._files_of(kind, name):
            f.rename(f.with_name(new + f.suffix))
        if key in self.pages:
            self._close_key(key, ask=False)
            self.open_resource(kind, new)
        if kind == "room" and self.project.start_room == name:
            self.set_start_room(new)
        self.refresh_tree()
        if kind in ("object", "sprite", "shader"):
            self.console.append(f"'{name}' agora é '{new}'. Confira os scripts e salas que usavam o nome antigo.\n", "info")

    def duplicate_resource(self, kind: str, name: str):
        base, n = f"{name}_copia", 2
        new = base
        while new in self.project.resources(kind):
            new, n = f"{base}{n}", n + 1
        for f in self._files_of(kind, name):
            shutil.copy2(f, f.with_name(new + f.suffix))
        if kind == "object":
            path = self.project.path("object", new)
            text = path.read_text("utf-8")
            write_text_atomic(path, text.replace(f"class {class_name(name)}(", f"class {class_name(new)}(", 1))
        self.refresh_tree()

    def delete_resource(self, kind: str, name: str):
        answer = QMessageBox.question(self, "Mandar para a Lixeira",
                                      f"Mandar o {KIND_SINGULAR[kind]} '{name}' para a Lixeira?")
        if answer != QMessageBox.StandardButton.Yes:
            return
        key = (kind, name)
        if key in self.pages:
            self._close_key(key, ask=False)
        failed = [f for f in self._files_of(kind, name) if not send_to_recycle_bin(f)]
        if failed:
            QMessageBox.warning(self, "Lixeira", "Não foi possível mandar para a Lixeira:\n" + "\n".join(map(str, failed)))
        self.refresh_tree()

    # ---------- abas ----------

    def current_page(self):
        w = self.stack.currentWidget()
        return None if w is self.start_page else w

    def current_key(self):
        page = self.current_page()
        for key, p in self.pages.items():
            if p is page:
                return key
        return None

    def open_resource(self, kind: str, name: str, line: int | None = None):
        if not self.project:
            return
        key = (kind, name)
        page = self.pages.get(key)
        if page is None:
            page = self._make_page(kind, name)
            if page is None:
                return
            self.pages[key] = page
            self.stack.addWidget(page)
            index = self.tabs.addTab(name)
            self.tabs.setTabData(index, key)
            close = QPushButton()
            close.setProperty("role", "tool")
            close.setFixedSize(18, 18)
            close.setIcon(icons.icon("x", self.palette_.muted, 12))
            close.setToolTip("Fechar (Ctrl W)")
            close.clicked.connect(lambda _=False, k=key: self._close_key(k))
            self.tabs.setTabButton(index, QTabBar.ButtonPosition.RightSide, close)
            self._refresh_tab(key)
        self.tabs.setCurrentIndex(self._tab_index(key))
        self.stack.setCurrentWidget(page)
        if line is not None and isinstance(page, (CodePage, ShaderPage)):
            QTimer.singleShot(0, lambda: page.editor.go_to_line(line))

    def _make_page(self, kind: str, name: str):
        p, s = self.palette_, self.settings
        if kind == "object":
            path = self.project.path(kind, name)
            editor = CodeEditor("python", p, s.code_size, s.auto_pairs)
            editor.set_word_wrap(s.word_wrap)
            try:
                editor.setPlainText(path.read_text("utf-8"))
            except OSError as e:
                QMessageBox.warning(self, "Abrir", f"Não foi possível abrir {path}:\n{e}")
                return None
            editor.document().setModified(False)
            page = CodePage(editor, p)
            page.path = path
            page.dirty = False
            page.save = lambda pg=page: self._save_code(pg)
            editor.document().modificationChanged.connect(lambda m, k=(kind, name), pg=page: self._code_modified(pg, k, m))
            editor.cursor_moved.connect(lambda l, c: self.capsule.set_position(l, c))
            return page
        if kind == "room":
            try:
                page = RoomPage(self.project, name, p)
            except ProjectError as e:
                QMessageBox.warning(self, "Abrir sala", str(e))
                return None
        elif kind == "sprite":
            page = SpritePage(self.project, name, p)
        elif kind == "shader":
            page = ShaderPage(self.project, name, p, s.code_size, s.auto_pairs)
            page.editor.cursor_moved.connect(lambda l, c: self.capsule.set_position(l, c))
        else:
            page = SoundPage(self.project, name)
            return page
        page.modified.connect(lambda k=(kind, name): self._refresh_tab(k))
        page.modified.connect(self._maybe_autosave)
        return page

    def _code_modified(self, page, key, modified: bool):
        page.dirty = modified
        self._refresh_tab(key)
        if modified:
            self._maybe_autosave()

    def _maybe_autosave(self):
        if self.settings.auto_save:
            self._autosave.start()

    def _save_code(self, page: CodePage):
        write_text_atomic(page.path, page.editor.toPlainText())
        page.editor.document().setModified(False)
        page.dirty = False
        if page.path.parent.name == "objects":
            self.refresh_tree()  # o sprite do objeto pode ter mudado

    def _tab_index(self, key) -> int:
        for i in range(self.tabs.count()):
            if self.tabs.tabData(i) == key:
                return i
        return -1

    def _refresh_tab(self, key):
        i = self._tab_index(key)
        page = self.pages.get(key)
        if i < 0 or page is None:
            return
        kind, name = key
        dirty = getattr(page, "dirty", False)
        self.tabs.setTabText(i, f"{name} •" if dirty else name)
        self.tabs.setTabIcon(i, icons.icon(icons.KIND_ICONS[kind], self.palette_.accent, 14))
        self.tabs.setTabToolTip(i, str(self.project.path(kind, name)) if self.project else name)

    def _refresh_tab_icons(self):
        for i in range(self.tabs.count()):
            key = self.tabs.tabData(i)
            self._refresh_tab(key)
            btn = self.tabs.tabButton(i, QTabBar.ButtonPosition.RightSide)
            if btn:
                btn.setIcon(icons.icon("x", self.palette_.muted, 12))

    def _tab_changed(self, index: int):
        if index < 0:
            self.stack.setCurrentWidget(self.start_page if self.project is None else self.stack.currentWidget())
            self.breadcrumb.setText("")
            self.capsule.set_position(None, None)
            if self.project and not self.pages:
                self.stack.setCurrentWidget(self.start_page)
            return
        key = self.tabs.tabData(index)
        page = self.pages.get(key)
        if page is None:
            return
        self.stack.setCurrentWidget(page)
        kind, name = key
        path = self.project.path(kind, name)
        self.breadcrumb.setText(f"{path.parent.name}/{path.name}")
        if isinstance(page, (CodePage, ShaderPage)):
            c = page.editor.textCursor()
            self.capsule.set_position(c.blockNumber() + 1, c.positionInBlock() + 1)
            page.editor.setFocus()
        else:
            self.capsule.set_position(None, None)
        if kind == "room":
            self.capsule.set_room(name)
        elif self.project:
            self.capsule.set_room(self.project.start_room)

    def _close_key(self, key, ask=True) -> bool:
        page = self.pages.get(key)
        if page is None:
            return True
        if ask and getattr(page, "dirty", False):
            answer = QMessageBox.question(
                self, "Salvar?", f"'{key[1]}' tem mudanças não salvas. Salvar antes de fechar?",
                QMessageBox.StandardButton.Save | QMessageBox.StandardButton.Discard | QMessageBox.StandardButton.Cancel)
            if answer == QMessageBox.StandardButton.Cancel:
                return False
            if answer == QMessageBox.StandardButton.Save:
                page.save()
        i = self._tab_index(key)
        del self.pages[key]
        self.stack.removeWidget(page)
        page.deleteLater()
        if i >= 0:
            self.tabs.removeTab(i)
        if not self.pages:
            self.stack.setCurrentWidget(self.start_page)
            self.breadcrumb.setText("")
            self.capsule.set_position(None, None)
            if self.project:
                self._show_empty_sheet()
        return True

    def _show_empty_sheet(self):
        # Com o projeto aberto e nenhuma aba, a tela inicial vira um lembrete curto.
        self.stack.setCurrentWidget(self.start_page)

    def close_tab(self, index: int):
        if index >= 0:
            self._close_key(self.tabs.tabData(index))

    def close_all_tabs(self) -> bool:
        for key in list(self.pages):
            if not self._close_key(key):
                return False
        return True

    def save_current(self):
        page = self.current_page()
        if page is not None and getattr(page, "dirty", False):
            page.save()
            key = self.current_key()
            if key:
                self._refresh_tab(key)

    def save_all(self):
        for key, page in list(self.pages.items()):
            if getattr(page, "dirty", False):
                try:
                    page.save()
                except OSError as e:
                    QMessageBox.warning(self, "Salvar", f"Não foi possível salvar '{key[1]}':\n{e}")
                self._refresh_tab(key)

    def go_to_line(self):
        page = self.current_page()
        if not isinstance(page, (CodePage, ShaderPage)):
            return
        editor = page.editor
        line, ok = QInputDialog.getInt(self, "Ir para a linha", f"Linha (1 a {editor.blockCount()}):",
                                       editor.textCursor().blockNumber() + 1, 1, editor.blockCount())
        if ok:
            editor.go_to_line(line)

    def find_in_file(self):
        page = self.current_page()
        if isinstance(page, CodePage):
            page.show_find()
        elif isinstance(page, ShaderPage):
            page.code_page.show_find()

    def open_location(self, path: str, line: int):
        if not self.project:
            return
        found = self.project.kind_of(path)
        if found is None:
            candidate = self.project.root / path
            found = self.project.kind_of(candidate) if candidate.exists() else None
        if found:
            self.open_resource(found[0], found[1], line)

    # ---------- executar ----------

    def toggle_run(self):
        if self.process and self.process.state() != QProcess.ProcessState.NotRunning:
            self.stop_game()
        else:
            self.run_game()

    def run_game(self, room: str | None = None):
        if not self.project:
            return
        self.save_all()
        self.stop_game()
        python = Path(sys.executable)
        pythonw = python.with_name("pythonw.exe")
        exe = str(pythonw if pythonw.exists() else python)
        args = ["-u", "-m", "canismajor", str(self.project.root), "--run"]
        if room:
            args += ["--room", room]
        proc = QProcess(self)
        env = QProcessEnvironment.systemEnvironment()
        env.insert("PYTHONPATH", str(ENGINE_ROOT) + os.pathsep + env.value("PYTHONPATH", ""))
        env.insert("PYTHONUNBUFFERED", "1")
        env.insert("PYTHONIOENCODING", "utf-8")
        env.insert("PYTHONDONTWRITEBYTECODE", "1")
        proc.setProcessEnvironment(env)
        proc.setWorkingDirectory(str(self.project.root))
        proc.readyReadStandardOutput.connect(lambda: self.console.append(
            bytes(proc.readAllStandardOutput()).decode("utf-8", "replace"), "out"))
        proc.readyReadStandardError.connect(lambda: self.console.append(
            bytes(proc.readAllStandardError()).decode("utf-8", "replace"), "err"))
        proc.finished.connect(self._game_finished)
        proc.errorOccurred.connect(lambda err: self.console.append(f"Não foi possível abrir o jogo ({err.name}).\n", "err"))
        self.process = proc
        self._started = time.perf_counter()
        self.console.append(f"▶ {self.project.name}" + (f" · sala {room}" if room else "") + "\n", "info")
        proc.start(exe, args)
        self.capsule.set_running(True)

    def stop_game(self):
        proc = self.process
        if proc and proc.state() != QProcess.ProcessState.NotRunning:
            proc.kill()
            proc.waitForFinished(2000)

    def _game_finished(self, code: int, status):
        self.capsule.set_running(False)
        if self.process is None:
            return
        seconds = time.perf_counter() - getattr(self, "_started", time.perf_counter())
        if status == QProcess.ExitStatus.CrashExit:
            self.console.append(f"■ O jogo foi parado depois de {seconds:.0f} s.\n", "info")
        elif code == 0:
            self.console.append(f"■ O jogo fechou depois de {seconds:.0f} s.\n", "info")
        else:
            self.console.append(f"■ O jogo parou com um erro (código {code}). Clique na linha sublinhada para ir até ela.\n", "err")
            self.console.show()
            self.place_overlays()

    def toggle_console(self):
        self.console.setVisible(not self.console.isVisible())
        self.place_overlays()

    # ---------- exportar ----------

    def export_game(self):
        if not self.project:
            return
        parent = QFileDialog.getExistingDirectory(self, "Onde a pasta do jogo exportado vai ficar",
                                                  str(self.project.root.parent))
        if not parent:
            return
        safe = "".join(c for c in self.project.name if c not in '<>:"/\\|?*').strip(" .") or "Jogo"
        dest = Path(parent) / f"{safe} (exportado)"
        if dest.exists() and any(dest.iterdir()):
            QMessageBox.warning(self, "Exportar", f"Já existe uma pasta não vazia em:\n{dest}")
            return
        free = shutil.disk_usage(parent).free
        if free < 300 * 1024 * 1024:
            QMessageBox.warning(self, "Exportar", f"O disco tem só {free / 1024 ** 2:.0f} MB livres. "
                                                   "A exportação precisa de uns 150 MB.")
            return
        self.save_all()
        from PySide6.QtCore import QObject, Signal as _Signal

        from ..export import export_game, folder_size

        class Worker(QObject):
            finished = _Signal(str, str)
            progress = _Signal(str)

            def run(w):
                try:
                    out = export_game(self.project, dest, w.progress.emit)
                    w.finished.emit(str(out), "")
                except Exception as e:
                    w.finished.emit("", str(e))

        box = QMessageBox(QMessageBox.Icon.NoIcon, "Exportar", "Preparando…", QMessageBox.StandardButton.NoButton, self)
        box.setStandardButtons(QMessageBox.StandardButton.NoButton)
        worker = Worker()
        worker.progress.connect(box.setText)

        def done(path, err):
            box.done(0)
            if err:
                QMessageBox.warning(self, "Exportar", f"A exportação falhou:\n{err}")
                return
            size = folder_size(Path(path)) / 1024 ** 2
            self.console.append(f"Jogo exportado em {path} ({size:.0f} MB).\n", "ok")
            answer = QMessageBox.question(self, "Exportar", f"Pronto. O jogo está em:\n{path}\n\n"
                                          f"São {size:.0f} MB. Para jogar, abra o arquivo .cmd da pasta.\n\nAbrir a pasta?")
            if answer == QMessageBox.StandardButton.Yes:
                os.startfile(path)  # noqa: S606

        worker.finished.connect(done)
        self._threads.append(start_thread(worker, self))
        box.exec()

    # ---------- barra de comando ----------

    def show_palette(self):
        p = self.palette_
        cmds: list[Command] = []
        if self.project:
            for kind in KINDS:
                for name in self.project.resources(kind):
                    cmds.append(Command(name, KIND_SINGULAR[kind], icons.icon(icons.KIND_ICONS[kind], p.accent),
                                        lambda k=kind, n=name: self.open_resource(k, n)))
            cmds += [
                Command("Executar o jogo", "F5", icons.icon("play", p.ink), self.run_game),
                Command("Parar o jogo", "F5", icons.icon("stop", p.ink), self.stop_game),
                Command("Salvar tudo", "Ctrl Shift S", None, self.save_all),
                Command("Configurações do jogo", "resolução, fps, sala inicial", icons.icon("game", p.ink), self.project_settings),
                Command("Novo objeto", "", icons.icon("object", p.ink), lambda: self.new_resource("object")),
                Command("Novo sprite", "", icons.icon("sprite", p.ink), lambda: self.new_resource("sprite")),
                Command("Nova sala", "", icons.icon("room", p.ink), lambda: self.new_resource("room")),
                Command("Novo shader", "", icons.icon("shader", p.ink), lambda: self.new_resource("shader")),
                Command("Importar imagens", "", icons.icon("sprite", p.muted), lambda: self.import_files("sprite")),
                Command("Importar sons", "", icons.icon("sound", p.muted), lambda: self.import_files("sound")),
                Command("Exportar jogo para Windows", "pasta que roda sem instalar nada", icons.icon("game", p.ink),
                        self.export_game),
                Command("Abrir a pasta do projeto", "Explorer", icons.icon("folder", p.ink), self.reveal_project),
                Command("Console do jogo", "Ctrl J", icons.icon("terminal", p.ink), self.toggle_console),
                Command("Fechar projeto", "", None, self.close_project),
            ]
        cmds += [
            Command("Novo projeto", "", icons.icon("plus", p.ink), self.new_project),
            Command("Abrir projeto", "Ctrl O", icons.icon("folder", p.ink), self.open_project_dialog),
            Command("Configurações do editor", "Ctrl ,", icons.icon("sliders", p.ink), self.show_settings),
            Command("Trocar tema", "claro ou grafite", icons.icon("contrast", p.ink), self.toggle_theme),
            Command("Modo foco", "Ctrl .", icons.icon("focus", p.ink), self.toggle_focus),
            Command("Verificar atualizações", f"versão {__version__}", icons.icon("update", p.ink),
                    lambda: self.check_updates(manual=True)),
        ]
        palette = CommandPalette(self, cmds)
        top = self.title.pill.mapToGlobal(QPoint(0, 0))
        palette.move(top.x() + (self.title.pill.width() - palette.width()) // 2, top.y() - 4)
        palette.input.setFocus()
        palette.exec()

    def show_settings(self):
        dialog = SettingsDialog(self, self.settings)
        dialog.changed.connect(self.apply_settings)
        dialog.check_updates_now.connect(lambda: self.check_updates(manual=True))
        dialog.exec()

    # ---------- atualizações ----------

    def check_updates(self, manual: bool):
        repo = self.settings.update_repo.strip()
        if not repo:
            if manual:
                QMessageBox.information(self, "Atualizações",
                                        "Informe o repositório do Canis Major no GitHub em Configurações, Atualizações.")
            return
        if not manual and (not self.settings.check_updates or time.time() - self.settings.last_update_check < 3600):
            return
        self.settings.last_update_check = time.time()
        self.settings.save()
        worker = CheckWorker(repo)
        worker.finished.connect(lambda rel, err: self._update_checked(rel, err, manual))
        self._threads.append(start_thread(worker, self))

    def _update_checked(self, release, error: str, manual: bool):
        if error or release is None:
            if manual:
                QMessageBox.warning(self, "Atualizações", f"Não foi possível consultar o GitHub.\n{error}")
            return
        if is_newer(release.version):
            self.release = release
            self.capsule.show_update(release.version)
            if manual:
                self.show_update()
        elif manual:
            QMessageBox.information(self, "Atualizações", f"Você já está na versão mais recente ({__version__}).")

    def show_update(self):
        if not self.release:
            return
        dialog = UpdateDialog(self, self.release)

        def go():
            worker = DownloadWorker(self.release)
            worker.progress.connect(dialog.set_progress)
            worker.finished.connect(lambda path, err: self._downloaded(dialog, path, err))
            self._threads.append(start_thread(worker, self))

        dialog.update_now.connect(go)
        dialog.exec()

    def _downloaded(self, dialog, path: str, error: str):
        if error:
            dialog.set_error(error)
            return
        if not self.close_all_tabs():
            dialog.set_error("Atualização adiada: há arquivos não salvos.")
            return
        launch_apply(path)
        self.close()

    # ---------- janela ----------

    def closeEvent(self, event):
        if not self.close_all_tabs():
            event.ignore()
            return
        self.stop_game()
        if not self.isMaximized():
            g = self.geometry()
            self.settings.window = [g.x(), g.y(), g.width(), g.height()]
        self.settings.save()
        super().closeEvent(event)
