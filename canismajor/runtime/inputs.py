"""Teclado e mouse, consultados pelos scripts a cada quadro."""

from __future__ import annotations

import pygame

_ALIASES = {
    "enter": "return",
    "esc": "escape",
    "espaco": "space",
    "espaço": "space",
    "esquerda": "left",
    "direita": "right",
    "cima": "up",
    "baixo": "down",
    "ctrl": "left ctrl",
    "shift": "left shift",
    "alt": "left alt",
}


class Keys:
    """`keys.held("left")`, `keys.pressed("space")`, `keys.released("z")`, `keys.axis("left", "right")`."""

    def __init__(self):
        self._held: set[int] = set()
        self._pressed: set[int] = set()
        self._released: set[int] = set()
        self._codes: dict[str, int] = {}

    def _code(self, name: str) -> int:
        code = self._codes.get(name)
        if code is None:
            key = _ALIASES.get(name.lower(), name.lower())
            try:
                code = pygame.key.key_code(key)
            except ValueError:
                raise ValueError(f"Tecla desconhecida: {name!r}. Exemplos: 'left', 'space', 'a', 'return', 'escape'.") from None
            self._codes[name] = code
        return code

    def held(self, name: str) -> bool:
        return self._code(name) in self._held

    def pressed(self, name: str) -> bool:
        return self._code(name) in self._pressed

    def released(self, name: str) -> bool:
        return self._code(name) in self._released

    def axis(self, negative: str, positive: str) -> int:
        return int(self.held(positive)) - int(self.held(negative))

    def _begin_frame(self) -> None:
        self._pressed.clear()
        self._released.clear()

    def _event(self, ev) -> None:
        if ev.type == pygame.KEYDOWN:
            self._held.add(ev.key)
            self._pressed.add(ev.key)
        elif ev.type == pygame.KEYUP:
            self._held.discard(ev.key)
            self._released.add(ev.key)


class Mouse:
    """Posição no mundo do jogo (`mouse.x`, `mouse.y`) e botões: 1 esquerdo, 2 meio, 3 direito."""

    def __init__(self):
        self.x = 0.0
        self.y = 0.0
        self.screen_x = 0.0
        self.screen_y = 0.0
        self.wheel = 0
        self._held: set[int] = set()
        self._pressed: set[int] = set()
        self._released: set[int] = set()

    def held(self, button: int = 1) -> bool:
        return button in self._held

    def pressed(self, button: int = 1) -> bool:
        return button in self._pressed

    def released(self, button: int = 1) -> bool:
        return button in self._released

    def _begin_frame(self) -> None:
        self._pressed.clear()
        self._released.clear()
        self.wheel = 0

    def _event(self, ev) -> None:
        if ev.type == pygame.MOUSEBUTTONDOWN and ev.button in (1, 2, 3):
            self._held.add(ev.button)
            self._pressed.add(ev.button)
        elif ev.type == pygame.MOUSEBUTTONUP and ev.button in (1, 2, 3):
            self._held.discard(ev.button)
            self._released.add(ev.button)
        elif ev.type == pygame.MOUSEWHEEL:
            self.wheel += ev.y
