"""Canis Major: engine de jogos 2D em Python, com shaders e elementos 3D.

Nos scripts do jogo:

    from canismajor import GameObject, keys, mouse, game, draw, Mesh
"""

__version__ = "0.1.0"

__all__ = ["GameObject", "Mesh", "keys", "mouse", "game", "draw", "__version__"]

_RUNTIME_NAMES = {"GameObject", "Mesh", "keys", "mouse", "game", "draw"}


def __getattr__(name):
    # O editor importa só a versão; o runtime (pygame, ModernGL) carrega quando um script pede.
    if name in _RUNTIME_NAMES:
        from .runtime import api

        return getattr(api, name)
    raise AttributeError(f"module 'canismajor' has no attribute {name!r}")
