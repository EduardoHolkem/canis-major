"""Formato do projeto de jogo: uma pasta com projeto.json e uma subpasta por tipo de recurso."""

from __future__ import annotations

import json
import keyword
import os
import re
import tempfile
from dataclasses import asdict, dataclass, fields
from pathlib import Path

from . import __version__

PROJECT_FILE = "projeto.json"

# tipo -> (pasta, extensões aceitas; a primeira é a usada ao criar)
KINDS: dict[str, tuple[str, tuple[str, ...]]] = {
    "sprite": ("sprites", (".png",)),
    "object": ("objects", (".py",)),
    "room": ("rooms", (".json",)),
    "shader": ("shaders", (".glsl",)),
    "sound": ("sounds", (".wav", ".ogg", ".mp3")),
}

KIND_LABELS = {"sprite": "Sprites", "object": "Objetos", "room": "Salas", "shader": "Shaders", "sound": "Sons"}
KIND_SINGULAR = {"sprite": "sprite", "object": "objeto", "room": "sala", "shader": "shader", "sound": "som"}

_NAME = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
_RESERVED = {"objects", "canismajor", "game", "keys", "mouse", "draw"}


class ProjectError(Exception):
    pass


def valid_name(name: str) -> bool:
    """Nomes viram nomes de módulo Python, então seguem as mesmas regras."""
    return bool(_NAME.match(name)) and not keyword.iskeyword(name) and name.lower() not in _RESERVED


def write_text_atomic(path: Path, text: str) -> None:
    """Grava num temporário ao lado e troca no fim, para nunca deixar o arquivo pela metade."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=f".{path.name}.", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        os.replace(tmp, path)
    except BaseException:
        try:
            os.unlink(tmp)
        except OSError:
            pass
        raise


def write_json(path: Path, data) -> None:
    write_text_atomic(path, json.dumps(data, indent=2, ensure_ascii=False) + "\n")


@dataclass
class Project:
    root: Path
    name: str = "Meu jogo"
    version: str = "0.1.0"
    start_room: str = "inicio"
    width: int = 480
    height: int = 270
    scale: int = 2
    fps: int = 60
    pixel_art: bool = True
    engine: str = __version__

    @staticmethod
    def is_project(root) -> bool:
        return (Path(root) / PROJECT_FILE).is_file()

    @classmethod
    def load(cls, root) -> "Project":
        root = Path(root).resolve()
        file = root / PROJECT_FILE
        try:
            data = json.loads(file.read_text("utf-8"))
        except FileNotFoundError:
            raise ProjectError(f"Esta pasta não tem um {PROJECT_FILE}: {root}") from None
        except (OSError, ValueError) as e:
            raise ProjectError(f"Não foi possível ler {file}: {e}") from None
        known = {f.name for f in fields(cls)} - {"root"}
        values = {k: v for k, v in data.items() if k in known}
        return cls(root=root, **values)

    def save(self) -> None:
        data = {k: v for k, v in asdict(self).items() if k != "root"}
        write_json(self.root / PROJECT_FILE, data)

    def folder(self, kind: str) -> Path:
        return self.root / KINDS[kind][0]

    def resources(self, kind: str) -> list[str]:
        folder = self.folder(kind)
        exts = KINDS[kind][1]
        if not folder.is_dir():
            return []
        names = {p.stem for p in folder.iterdir() if p.is_file() and p.suffix.lower() in exts}
        return sorted(names, key=str.lower)

    def path(self, kind: str, name: str) -> Path:
        folder = self.folder(kind)
        for ext in KINDS[kind][1]:
            p = folder / f"{name}{ext}"
            if p.exists():
                return p
        return folder / f"{name}{KINDS[kind][1][0]}"

    def kind_of(self, path) -> tuple[str, str] | None:
        """Diz de que tipo e nome é um arquivo do projeto, ou None se não for um recurso."""
        try:
            rel = Path(path).resolve().relative_to(self.root)
        except ValueError:
            return None
        if len(rel.parts) != 2:
            return None
        for kind, (folder, exts) in KINDS.items():
            if rel.parts[0] == folder and rel.suffix.lower() in exts:
                return kind, rel.stem
        return None


# ---------- salas ----------

def load_room(path: Path, project: Project | None = None) -> dict:
    try:
        data = json.loads(Path(path).read_text("utf-8"))
    except FileNotFoundError:
        data = {}
    except ValueError as e:
        raise ProjectError(f"A sala {Path(path).name} não é um JSON válido: {e}") from None
    room = {
        "width": project.width if project else 480,
        "height": project.height if project else 270,
        "background": "#0D0F13",
        "grid": 16,
        "post_shaders": [],
        "instances": [],
    }
    room.update(data)
    return room


def save_room(path: Path, room: dict) -> None:
    write_json(path, room)


# ---------- sprites ----------

def sprite_meta_path(png: Path) -> Path:
    return Path(png).with_suffix(".json")


def load_sprite_meta(png: Path) -> dict:
    meta = {"frames": 1, "origin": [0, 0], "fps": 8}
    try:
        meta.update(json.loads(sprite_meta_path(png).read_text("utf-8")))
    except (FileNotFoundError, ValueError):
        pass
    meta["frames"] = max(1, int(meta.get("frames", 1)))
    return meta


def save_sprite_meta(png: Path, meta: dict) -> None:
    write_json(sprite_meta_path(png), meta)
