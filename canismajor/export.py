"""Exporta um jogo para uma pasta que roda em qualquer Windows 64 bits, sem instalar nada.

A pasta leva um Python próprio, só as bibliotecas do runtime (sem o editor), a engine e o jogo:

    Meu jogo/
      Meu jogo.cmd      <- dois cliques para jogar
      jogo/             <- projeto
      engine/canismajor <- runtime
      python/           <- Python com pygame-ce, ModernGL, NumPy e Pillow
"""

from __future__ import annotations

import shutil
import sys
import sysconfig
from pathlib import Path
from typing import Callable

ENGINE_PKG = Path(__file__).resolve().parent
RUNTIME_PACKAGES = ["pygame", "moderngl", "glcontext", "numpy", "numpy.libs", "PIL", "pillow.libs", "_moderngl.py"]
SKIP_PYTHON = {"test", "tests", "idlelib", "tkinter", "turtledemo", "ensurepip", "lib2to3", "venv", "pydoc_data",
               "__pycache__", "site-packages", "Doc", "include", "libs", "tcl", "Tools", "Scripts"}
SKIP_GAME = {"__pycache__", ".git", ".venv"}

LAUNCHER = """@echo off
rem Abre o jogo com o Python que vem junto nesta pasta.
set "PYTHONPATH=%~dp0engine"
set "PYTHONDONTWRITEBYTECODE=1"
start "" "%~dp0python\\pythonw.exe" -m canismajor "%~dp0jogo" --run
"""


def _copytree(src: Path, dst: Path, skip: set[str], progress: Callable[[str], None] | None = None) -> None:
    def ignore(folder, names):
        return [n for n in names if n in skip or n.endswith((".pyc", ".pdb"))]

    if progress:
        progress(f"Copiando {src.name}…")
    shutil.copytree(src, dst, ignore=ignore, dirs_exist_ok=True)


def site_packages() -> Path:
    return Path(sysconfig.get_paths()["purelib"])


def export_game(project, dest: Path, progress: Callable[[str], None] | None = None) -> Path:
    dest = Path(dest)
    if dest.exists() and any(dest.iterdir()):
        raise FileExistsError(f"A pasta {dest} já existe e não está vazia.")
    dest.mkdir(parents=True, exist_ok=True)

    base = Path(sys.base_prefix)
    _copytree(base, dest / "python", SKIP_PYTHON, progress)

    target_site = dest / "python" / "Lib" / "site-packages"
    target_site.mkdir(parents=True, exist_ok=True)
    src_site = site_packages()
    for name in RUNTIME_PACKAGES:
        src = src_site / name
        if src.is_dir():
            _copytree(src, target_site / name, {"__pycache__", "tests", "test", "examples", "docs"}, progress)
        elif src.is_file():
            shutil.copy2(src, target_site / name)

    if progress:
        progress("Copiando a engine…")
    eng = dest / "engine" / "canismajor"
    (eng / "runtime").mkdir(parents=True, exist_ok=True)
    for f in ("__init__.py", "__main__.py", "project.py", "shaderlib.py"):
        shutil.copy2(ENGINE_PKG / f, eng / f)
    for f in (ENGINE_PKG / "runtime").glob("*.py"):
        shutil.copy2(f, eng / "runtime" / f.name)
    (eng / "assets" / "fonts").mkdir(parents=True, exist_ok=True)
    for f in ("Geist-Medium.ttf", "OFL-Geist.txt"):
        shutil.copy2(ENGINE_PKG / "assets" / "fonts" / f, eng / "assets" / "fonts" / f)
    shutil.copy2(ENGINE_PKG / "assets" / "icon.png", eng / "assets" / "icon.png")
    # O __main__ importa o editor só quando não é --run, então ele não precisa ir junto.

    _copytree(Path(project.root), dest / "jogo", SKIP_GAME, progress)
    safe = "".join(c for c in project.name if c not in '<>:"/\\|?*').strip(" .") or "Jogo"
    (dest / f"{safe}.cmd").write_text(LAUNCHER, encoding="ascii", errors="replace")
    if progress:
        progress("Pronto.")
    return dest


def folder_size(path: Path) -> int:
    return sum(f.stat().st_size for f in Path(path).rglob("*") if f.is_file())
