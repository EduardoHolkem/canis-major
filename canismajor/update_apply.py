"""Aplica uma atualização baixada. Roda de uma cópia temporária, com o editor já fechado.

Só usa a biblioteca padrão, porque os arquivos da engine estão sendo trocados enquanto ele roda.

    python update_apply.py --zip pacote.zip --target PASTA_DA_ENGINE --pid 1234 --python python.exe
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path, PurePosixPath

SKIP = {".venv", ".git", ".update"}


def _wait_for(pid: int, timeout: float = 60.0) -> None:
    if pid <= 0:
        return
    deadline = time.time() + timeout
    if os.name == "nt":
        import ctypes

        SYNCHRONIZE = 0x00100000
        handle = ctypes.windll.kernel32.OpenProcess(SYNCHRONIZE, False, pid)
        if handle:
            ctypes.windll.kernel32.WaitForSingleObject(handle, int(timeout * 1000))
            ctypes.windll.kernel32.CloseHandle(handle)
        return
    while time.time() < deadline:
        try:
            os.kill(pid, 0)
        except OSError:
            return
        time.sleep(0.2)


def _members(zf: zipfile.ZipFile):
    """Tira a pasta de cima do zip (o GitHub empacota tudo em Repo-versao/) e barra caminhos perigosos."""
    names = [n for n in zf.namelist() if not n.endswith("/")]
    tops = {PurePosixPath(n).parts[0] for n in names}
    strip = 1 if len(tops) == 1 and all(len(PurePosixPath(n).parts) > 1 for n in names) else 0
    for name in names:
        parts = PurePosixPath(name).parts[strip:]
        if not parts or parts[0] in SKIP or any(p in ("..", "") or ":" in p for p in parts):
            continue
        yield name, Path(*parts)


def apply(zip_path: Path, target: Path) -> None:
    with zipfile.ZipFile(zip_path) as zf:
        for name, rel in _members(zf):
            dest = target / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            tmp = dest.with_name(dest.name + ".novo")
            with zf.open(name) as src, open(tmp, "wb") as out:
                shutil.copyfileobj(src, out)
            os.replace(tmp, dest)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--zip", required=True)
    parser.add_argument("--target", required=True)
    parser.add_argument("--pid", type=int, default=0)
    parser.add_argument("--python", default=sys.executable)
    args = parser.parse_args()

    target = Path(args.target)
    log = target / ".update" / "ultima-atualizacao.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    with open(log, "w", encoding="utf-8") as out:
        try:
            _wait_for(args.pid)
            time.sleep(0.5)
            apply(Path(args.zip), target)
            out.write("Arquivos trocados.\n")
            req = subprocess.run([args.python, "-m", "pip", "install", "-q", "-e", str(target)],
                                 capture_output=True, text=True)
            out.write(req.stdout + req.stderr)
        except Exception as e:  # o log fica para a próxima abertura mostrar
            out.write(f"Falhou: {e!r}\n")
    pythonw = Path(args.python).with_name("pythonw.exe")
    exe = str(pythonw if pythonw.exists() else args.python)
    flags = 0x00000008 if os.name == "nt" else 0  # DETACHED_PROCESS
    subprocess.Popen([exe, "-m", "canismajor"], cwd=str(target), creationflags=flags, close_fds=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
