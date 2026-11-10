"""Janelas de diálogo: novo projeto, novo recurso, configurações do editor e do jogo, atualização."""

from __future__ import annotations

from pathlib import Path

from PySide6.QtCore import Qt, Signal
from PySide6.QtWidgets import (QCheckBox, QComboBox, QDialog, QDoubleSpinBox, QFileDialog, QFormLayout,
                               QHBoxLayout, QLabel, QLineEdit, QProgressBar, QPushButton, QSpinBox,
                               QStackedWidget, QTextBrowser, QVBoxLayout, QWidget)

from .. import __version__
from ..project import KIND_SINGULAR, Project, valid_name
from .settings import Settings


def _muted(text: str) -> QLabel:
    label = QLabel(text)
    label.setProperty("role", "muted")
    label.setWordWrap(True)
    return label


def _buttons(dialog: QDialog, ok_text: str) -> tuple[QHBoxLayout, QPushButton]:
    row = QHBoxLayout()
    row.addStretch(1)
    cancel = QPushButton("Cancelar")
    cancel.setProperty("role", "secondary")
    cancel.clicked.connect(dialog.reject)
    ok = QPushButton(ok_text)
    ok.setProperty("role", "primary")
    ok.setDefault(True)
    ok.clicked.connect(dialog.accept)
    row.addWidget(cancel)
    row.addWidget(ok)
    return row, ok


class NewProjectDialog(QDialog):
    def __init__(self, parent, default_dir: str):
        super().__init__(parent)
        self.setWindowTitle("Novo projeto")
        self.setMinimumWidth(480)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 18, 20, 18)
        layout.setSpacing(10)
        title = QLabel("Novo projeto")
        title.setProperty("role", "title")
        layout.addWidget(title)
        form = QFormLayout()
        form.setVerticalSpacing(8)
        self.name = QLineEdit("Meu jogo")
        self.folder = QLineEdit(default_dir)
        pick = QPushButton("Escolher…")
        pick.clicked.connect(self._pick)
        folder_row = QHBoxLayout()
        folder_row.addWidget(self.folder, 1)
        folder_row.addWidget(pick)
        self.template = QComboBox()
        self.template.addItem("Exemplo: jogador, estrelas, cubo 3D e shaders", "exemplo")
        self.template.addItem("Vazio: só uma sala", "vazio")
        form.addRow(_muted("Nome"), self.name)
        form.addRow(_muted("Pasta"), folder_row)
        form.addRow(_muted("Modelo"), self.template)
        layout.addLayout(form)
        self.where = _muted("")
        layout.addWidget(self.where)
        self.error = QLabel()
        self.error.setProperty("role", "error")
        layout.addWidget(self.error)
        row, self.ok = _buttons(self, "Criar projeto")
        layout.addLayout(row)
        self.name.textChanged.connect(self._update)
        self.folder.textChanged.connect(self._update)
        self._update()

    def _pick(self):
        chosen = QFileDialog.getExistingDirectory(self, "Pasta onde o projeto vai ficar", self.folder.text())
        if chosen:
            self.folder.setText(chosen)

    def target(self) -> Path:
        safe = "".join(c for c in self.name.text().strip() if c not in '<>:"/\\|?*').strip(" .")
        return Path(self.folder.text().strip()) / (safe or "Meu jogo")

    def _update(self):
        target = self.target()
        self.where.setText(f"Será criado em {target}")
        problem = ""
        if not self.name.text().strip():
            problem = "Dê um nome ao projeto."
        elif target.exists() and any(target.iterdir()):
            problem = "Já existe uma pasta com esse nome e ela não está vazia."
        self.error.setText(problem)
        self.ok.setEnabled(not problem)


class NameDialog(QDialog):
    """Pede o nome de um recurso novo ou renomeado, conferindo as regras de nome."""

    def __init__(self, parent, kind: str, project: Project, initial: str = "", title: str | None = None):
        super().__init__(parent)
        self.kind = kind
        self.project = project
        self.initial = initial
        self.setWindowTitle(title or f"Novo {KIND_SINGULAR[kind]}")
        self.setMinimumWidth(380)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 18, 20, 18)
        layout.setSpacing(10)
        head = QLabel(title or f"Novo {KIND_SINGULAR[kind]}")
        head.setProperty("role", "title")
        layout.addWidget(head)
        self.input = QLineEdit(initial)
        self.input.selectAll()
        layout.addWidget(self.input)
        layout.addWidget(_muted("Letras sem acento, números e _, começando por letra. Ex.: inimigo_voador"))
        self.error = QLabel()
        self.error.setProperty("role", "error")
        layout.addWidget(self.error)
        self.extra = QVBoxLayout()
        layout.addLayout(self.extra)
        row, self.ok = _buttons(self, "Criar" if not initial else "Renomear")
        layout.addLayout(row)
        self.input.textChanged.connect(self._check)
        self._check()

    def name(self) -> str:
        return self.input.text().strip()

    def _check(self):
        name = self.name()
        problem = ""
        if not name:
            problem = " "
        elif not valid_name(name):
            problem = "Esse nome não serve para um recurso."
        elif name != self.initial and name.lower() in {n.lower() for n in self.project.resources(self.kind)}:
            problem = f"Já existe um {KIND_SINGULAR[self.kind]} com esse nome."
        self.error.setText(problem.strip())
        self.ok.setEnabled(not problem)


class NewSpriteDialog(NameDialog):
    def __init__(self, parent, project: Project):
        super().__init__(parent, "sprite", project)
        form = QFormLayout()
        self.w = QSpinBox()
        self.w.setRange(1, 1024)
        self.w.setValue(16)
        self.h = QSpinBox()
        self.h.setRange(1, 1024)
        self.h.setValue(16)
        self.frames = QSpinBox()
        self.frames.setRange(1, 64)
        self.frames.setValue(1)
        form.addRow(_muted("Largura do quadro"), self.w)
        form.addRow(_muted("Altura"), self.h)
        form.addRow(_muted("Quadros"), self.frames)
        self.extra.addLayout(form)


class SettingsDialog(QDialog):
    changed = Signal()
    check_updates_now = Signal()

    def __init__(self, parent, settings: Settings):
        super().__init__(parent)
        self.settings = settings
        self.setWindowTitle("Configurações")
        self.resize(640, 420)
        root = QHBoxLayout(self)
        root.setContentsMargins(14, 14, 18, 14)
        root.setSpacing(14)
        nav = QVBoxLayout()
        nav.setSpacing(2)
        self.pages = QStackedWidget()
        self.nav_buttons = []
        for i, (label, page) in enumerate((("Aparência", self._appearance()), ("Editor", self._editor()),
                                            ("Atualizações", self._updates()))):
            b = QPushButton(label)
            b.setProperty("role", "section")
            b.clicked.connect(lambda _=False, n=i: self._show(n))
            nav.addWidget(b)
            self.nav_buttons.append(b)
            self.pages.addWidget(page)
        nav.addStretch(1)
        side = QWidget()
        side.setFixedWidth(150)
        side.setLayout(nav)
        root.addWidget(side)
        root.addWidget(self.pages, 1)
        self._show(0)

    def _show(self, index: int):
        self.pages.setCurrentIndex(index)
        for i, b in enumerate(self.nav_buttons):
            b.setProperty("on", "true" if i == index else "false")
            b.style().unpolish(b)
            b.style().polish(b)

    def _page(self, title: str) -> tuple[QWidget, QVBoxLayout]:
        page = QWidget()
        layout = QVBoxLayout(page)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(10)
        head = QLabel(title)
        head.setProperty("role", "title")
        layout.addWidget(head)
        return page, layout

    def _apply(self):
        self.settings.save()
        self.changed.emit()

    def _appearance(self):
        page, layout = self._page("Aparência")
        form = QFormLayout()
        theme = QComboBox()
        theme.addItem("Grafite", "grafite")
        theme.addItem("Claro", "claro")
        theme.setCurrentIndex(0 if self.settings.theme == "grafite" else 1)
        theme.currentIndexChanged.connect(lambda: (setattr(self.settings, "theme", theme.currentData()), self._apply()))
        ui = QSpinBox()
        ui.setRange(11, 18)
        ui.setValue(self.settings.ui_size)
        ui.valueChanged.connect(lambda v: (setattr(self.settings, "ui_size", v), self._apply()))
        code = QDoubleSpinBox()
        code.setRange(9, 28)
        code.setSingleStep(0.5)
        code.setValue(self.settings.code_size)
        code.valueChanged.connect(lambda v: (setattr(self.settings, "code_size", v), self._apply()))
        form.addRow(_muted("Tema"), theme)
        form.addRow(_muted("Letra da interface"), ui)
        form.addRow(_muted("Letra do código"), code)
        layout.addLayout(form)
        layout.addStretch(1)
        return page

    def _editor(self):
        page, layout = self._page("Editor")
        for attr, text in (("auto_save", "Salvar sozinho um segundo depois de parar de digitar"),
                           ("auto_pairs", "Fechar parênteses, colchetes, chaves e aspas"),
                           ("word_wrap", "Quebrar linhas longas")):
            box = QCheckBox(text)
            box.setChecked(getattr(self.settings, attr))
            box.toggled.connect(lambda on, a=attr: (setattr(self.settings, a, on), self._apply()))
            layout.addWidget(box)
        layout.addWidget(_muted("O Executar sempre salva tudo antes de abrir o jogo."))
        layout.addStretch(1)
        return page

    def _updates(self):
        page, layout = self._page("Atualizações")
        layout.addWidget(_muted(f"Versão instalada: {__version__}"))
        box = QCheckBox("Avisar quando sair uma versão nova")
        box.setChecked(self.settings.check_updates)
        box.toggled.connect(lambda on: (setattr(self.settings, "check_updates", on), self._apply()))
        layout.addWidget(box)
        form = QFormLayout()
        repo = QLineEdit(self.settings.update_repo)
        repo.setPlaceholderText("dono/repositorio")
        repo.editingFinished.connect(lambda: (setattr(self.settings, "update_repo", repo.text().strip()), self._apply()))
        form.addRow(_muted("Repositório no GitHub"), repo)
        layout.addLayout(form)
        layout.addWidget(_muted(
            "O editor pergunta à API pública do GitHub qual é a última versão publicada em Releases, "
            "no máximo uma vez por hora, sem enviar nada do seu projeto. Nada é baixado sem o seu clique, "
            "e o pacote só é usado se conferir com o código SHA-256 que o GitHub registrou."))
        now = QPushButton("Verificar agora")
        now.setProperty("role", "secondary")
        now.clicked.connect(lambda: (setattr(self.settings, "update_repo", repo.text().strip()), self._apply(),
                                     self.check_updates_now.emit()))
        row = QHBoxLayout()
        row.addWidget(now)
        row.addStretch(1)
        layout.addLayout(row)
        layout.addStretch(1)
        return page


class ProjectSettingsDialog(QDialog):
    def __init__(self, parent, project: Project):
        super().__init__(parent)
        self.project = project
        self.setWindowTitle("Configurações do jogo")
        self.setMinimumWidth(420)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 18, 20, 18)
        layout.setSpacing(10)
        head = QLabel("Configurações do jogo")
        head.setProperty("role", "title")
        layout.addWidget(head)
        form = QFormLayout()
        form.setVerticalSpacing(8)
        self.name = QLineEdit(project.name)
        self.version = QLineEdit(project.version)
        self.w = QSpinBox()
        self.w.setRange(64, 7680)
        self.w.setValue(project.width)
        self.h = QSpinBox()
        self.h.setRange(64, 4320)
        self.h.setValue(project.height)
        size_row = QHBoxLayout()
        size_row.addWidget(self.w)
        size_row.addWidget(QLabel("×"))
        size_row.addWidget(self.h)
        self.scale = QSpinBox()
        self.scale.setRange(1, 8)
        self.scale.setValue(project.scale)
        self.fps = QSpinBox()
        self.fps.setRange(10, 240)
        self.fps.setValue(project.fps)
        self.pixel = QCheckBox("Pixel art: sem suavizar e com zoom em números inteiros")
        self.pixel.setChecked(project.pixel_art)
        self.start = QComboBox()
        for room in project.resources("room"):
            self.start.addItem(room)
        self.start.setCurrentText(project.start_room)
        form.addRow(_muted("Nome"), self.name)
        form.addRow(_muted("Versão"), self.version)
        form.addRow(_muted("Resolução"), size_row)
        form.addRow(_muted("Escala da janela"), self.scale)
        form.addRow(_muted("Quadros por segundo"), self.fps)
        form.addRow(_muted("Sala inicial"), self.start)
        layout.addLayout(form)
        layout.addWidget(self.pixel)
        row, _ = _buttons(self, "Salvar")
        layout.addLayout(row)

    def accept(self):
        p = self.project
        p.name = self.name.text().strip() or p.name
        p.version = self.version.text().strip() or p.version
        p.width, p.height = self.w.value(), self.h.value()
        p.scale, p.fps = self.scale.value(), self.fps.value()
        p.pixel_art = self.pixel.isChecked()
        if self.start.currentText():
            p.start_room = self.start.currentText()
        p.save()
        super().accept()


class UpdateDialog(QDialog):
    update_now = Signal()

    def __init__(self, parent, release):
        super().__init__(parent)
        self.setWindowTitle("Versão nova")
        self.resize(520, 420)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(20, 18, 20, 18)
        layout.setSpacing(10)
        head = QLabel(f"Canis Major {release.version}")
        head.setProperty("role", "title")
        layout.addWidget(head)
        layout.addWidget(_muted(f"Você está na versão {__version__}."))
        notes = QTextBrowser()
        notes.setOpenExternalLinks(True)
        notes.setMarkdown(release.notes or "Sem notas para esta versão.")
        notes.setStyleSheet("QTextBrowser { background: transparent; border: none; }")
        layout.addWidget(notes, 1)
        self.progress = QProgressBar()
        self.progress.setTextVisible(False)
        self.progress.hide()
        layout.addWidget(self.progress)
        self.status = _muted("")
        layout.addWidget(self.status)
        row = QHBoxLayout()
        row.addStretch(1)
        later = QPushButton("Depois")
        later.setProperty("role", "secondary")
        later.clicked.connect(self.reject)
        self.go = QPushButton("Atualizar agora")
        self.go.setProperty("role", "primary")
        self.go.clicked.connect(self._go)
        if not release.asset_url:
            self.go.setEnabled(False)
            self.status.setText("Esta versão não tem um pacote .zip anexado. Baixe pela página do GitHub.")
        row.addWidget(later)
        row.addWidget(self.go)
        layout.addLayout(row)

    def _go(self):
        self.go.setEnabled(False)
        self.progress.show()
        self.status.setText("Baixando…")
        self.update_now.emit()

    def set_progress(self, value: int):
        self.progress.setValue(value)

    def set_error(self, message: str):
        self.progress.hide()
        self.status.setProperty("role", "error")
        self.status.style().unpolish(self.status)
        self.status.style().polish(self.status)
        self.status.setText(message)
        self.go.setEnabled(True)
