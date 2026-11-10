"""Preferências do editor, em %APPDATA%\\CanisMajor\\settings.json."""

from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass, field, fields
from pathlib import Path

from ..project import write_json


def settings_dir() -> Path:
    base = os.environ.get("APPDATA") or str(Path.home() / ".config")
    return Path(base) / "CanisMajor"


@dataclass
class Settings:
    theme: str = "grafite"
    ui_size: int = 13
    code_size: float = 13.5
    auto_save: bool = False
    word_wrap: bool = False
    auto_pairs: bool = True
    recent: list[str] = field(default_factory=list)
    projects_dir: str = ""
    check_updates: bool = True
    update_repo: str = ""  # "dono/repositorio" no GitHub, onde saem as versões
    last_update_check: float = 0.0
    window: list[int] = field(default_factory=list)
    sidebar_width: int = 232

    @classmethod
    def load(cls) -> "Settings":
        try:
            data = json.loads((settings_dir() / "settings.json").read_text("utf-8"))
        except (FileNotFoundError, ValueError, OSError):
            return cls()
        known = {f.name for f in fields(cls)}
        return cls(**{k: v for k, v in data.items() if k in known})

    def save(self) -> None:
        try:
            write_json(settings_dir() / "settings.json", asdict(self))
        except OSError:
            pass  # disco cheio ou sem permissão: as preferências valem só nesta sessão

    def add_recent(self, path: str) -> None:
        path = str(Path(path))
        self.recent = [path] + [p for p in self.recent if Path(p) != Path(path)]
        self.recent = self.recent[:10]
        self.save()
