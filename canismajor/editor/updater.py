"""Aviso de versão nova e atualização pela página de Releases do GitHub."""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
from dataclasses import dataclass
from pathlib import Path

from PySide6.QtCore import QObject, QThread, Signal

from .. import __version__

ENGINE_ROOT = Path(__file__).resolve().parent.parent.parent
USER_AGENT = f"CanisMajor/{__version__}"


@dataclass
class Release:
    version: str
    notes: str
    page: str
    asset_url: str
    asset_name: str
    digest: str  # "sha256:..." que o GitHub registra para o arquivo, ou "" se não houver


def parse_version(text: str) -> tuple[int, ...]:
    nums = re.findall(r"\d+", text.split("-")[0])
    return tuple(int(n) for n in nums[:3]) or (0,)


def is_newer(remote: str, local: str = __version__) -> bool:
    return parse_version(remote) > parse_version(local)


def fetch_latest(repo: str, timeout: float = 8.0) -> Release | None:
    if not re.fullmatch(r"[\w.-]+/[\w.-]+", repo or ""):
        return None
    req = urllib.request.Request(f"https://api.github.com/repos/{repo}/releases/latest",
                                 headers={"Accept": "application/vnd.github+json", "User-Agent": USER_AGENT})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode("utf-8"))
    assets = [a for a in data.get("assets", []) if a.get("name", "").lower().endswith(".zip")]
    asset = next((a for a in assets if "canismajor" in a["name"].lower().replace("-", "").replace("_", "")),
                 assets[0] if assets else None)
    return Release(
        version=data.get("tag_name", "").lstrip("vV"),
        notes=data.get("body") or "",
        page=data.get("html_url", ""),
        asset_url=asset.get("browser_download_url", "") if asset else "",
        asset_name=asset.get("name", "") if asset else "",
        digest=(asset.get("digest") or "") if asset else "",
    )


class CheckWorker(QObject):
    finished = Signal(object, str)  # Release ou None, mensagem de erro

    def __init__(self, repo: str):
        super().__init__()
        self.repo = repo

    def run(self):
        try:
            self.finished.emit(fetch_latest(self.repo), "")
        except Exception as e:
            self.finished.emit(None, str(e))


class DownloadWorker(QObject):
    progress = Signal(int)
    finished = Signal(str, str)  # caminho do zip, erro

    def __init__(self, release: Release):
        super().__init__()
        self.release = release

    def run(self):
        try:
            folder = ENGINE_ROOT / ".update"
            folder.mkdir(exist_ok=True)
            dest = folder / (self.release.asset_name or "atualizacao.zip")
            req = urllib.request.Request(self.release.asset_url, headers={"User-Agent": USER_AGENT})
            sha = hashlib.sha256()
            with urllib.request.urlopen(req, timeout=30) as resp, open(dest, "wb") as out:
                total = int(resp.headers.get("Content-Length") or 0)
                done = 0
                while chunk := resp.read(1 << 16):
                    out.write(chunk)
                    sha.update(chunk)
                    done += len(chunk)
                    if total:
                        self.progress.emit(int(done * 100 / total))
            expected = self.release.digest.split(":", 1)[-1].lower() if self.release.digest else ""
            if not expected:
                dest.unlink(missing_ok=True)
                self.finished.emit("", "O GitHub não registrou o código de conferência deste arquivo, então ele não foi usado.")
                return
            if sha.hexdigest().lower() != expected:
                dest.unlink(missing_ok=True)
                self.finished.emit("", "O arquivo baixado não confere com o registrado no GitHub e foi apagado.")
                return
            self.finished.emit(str(dest), "")
        except Exception as e:
            self.finished.emit("", str(e))


def start_thread(worker: QObject, parent: QObject) -> QThread:
    thread = QThread(parent)
    worker.moveToThread(thread)
    thread.started.connect(worker.run)
    worker.finished.connect(thread.quit)
    thread.finished.connect(thread.deleteLater)
    thread._worker = worker  # mantém vivo
    thread.start()
    return thread


def launch_apply(zip_path: str) -> None:
    """Copia o aplicador para fora da engine e o deixa trocar os arquivos depois que o editor fechar."""
    folder = ENGINE_ROOT / ".update"
    folder.mkdir(exist_ok=True)
    script = folder / "update_apply.py"
    shutil.copy2(Path(__file__).resolve().parent.parent / "update_apply.py", script)
    python = Path(sys.executable)
    pythonw = python.with_name("pythonw.exe")
    exe = str(pythonw if pythonw.exists() else python)
    flags = 0x00000008 if os.name == "nt" else 0
    subprocess.Popen([exe, "-I", str(script), "--zip", zip_path, "--target", str(ENGINE_ROOT),
                      "--pid", str(os.getpid()), "--python", str(python)],
                     cwd=str(folder), creationflags=flags, close_fds=True)
