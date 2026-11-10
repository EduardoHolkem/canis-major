"""Inicia o editor."""

from __future__ import annotations

import os
import sys
from pathlib import Path


def run_editor(project: str | None = None, shot: str | None = None) -> int:
    if os.name == "nt":
        import ctypes
        try:  # ícone próprio na barra de tarefas, em vez do ícone do Python
            ctypes.windll.shell32.SetCurrentProcessExplicitAppUserModelID("CanisMajor.Editor")
        except (AttributeError, OSError):
            pass

    from PySide6.QtCore import Qt, QTimer
    from PySide6.QtGui import QFont, QIcon, QSurfaceFormat
    from PySide6.QtWidgets import QApplication

    fmt = QSurfaceFormat()
    fmt.setVersion(3, 3)
    fmt.setProfile(QSurfaceFormat.OpenGLContextProfile.CoreProfile)
    QSurfaceFormat.setDefaultFormat(fmt)
    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)

    app = QApplication(sys.argv[:1])
    app.setApplicationName("Canis Major")
    app.setOrganizationName("CanisMajor")
    icon = Path(__file__).resolve().parent.parent / "assets" / "icon.png"
    if icon.exists():
        app.setWindowIcon(QIcon(str(icon)))

    from .main_window import MainWindow
    from .settings import Settings
    from .theme import UI_FONT, load_fonts

    load_fonts()
    app.setFont(QFont(UI_FONT, 10))
    settings = Settings.load()
    window = MainWindow(settings)
    window.show()
    if project:
        window.open_project(project)

    if shot:
        # Uso de desenvolvimento: tira fotos da janela e fecha. CM_SHOT_TABS="object:jogador,shader:vinheta"
        # abre cada recurso e salva uma foto para cada um (shot-1.png, shot-2.png...).
        tabs = [t.split(":", 1) for t in os.environ.get("CM_SHOT_TABS", "").split(",") if ":" in t]
        base = Path(shot)

        def take(i=0):
            if i == 0:
                window.grab().save(str(base))
            if i < len(tabs):
                window.open_resource(*tabs[i])
                QTimer.singleShot(1200, lambda: (window.grab().save(str(base.with_name(f"{base.stem}-{i + 1}{base.suffix}"))),
                                                 take(i + 1)))
            else:
                app.quit()
        QTimer.singleShot(2500, take)

    return app.exec()
