"""Ponto de entrada: `python -m canismajor` abre o editor, `--run` roda um jogo."""

from __future__ import annotations

import argparse
import sys


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="canismajor", description="Canis Major: engine de jogos 2D em Python.")
    parser.add_argument("project", nargs="?", help="pasta do projeto")
    parser.add_argument("--run", action="store_true", help="roda o jogo em vez de abrir o editor")
    parser.add_argument("--room", help="sala para começar (com --run)")
    parser.add_argument("--frames", type=int, default=0, help=argparse.SUPPRESS)
    parser.add_argument("--screenshot", help=argparse.SUPPRESS)
    parser.add_argument("--shot", help=argparse.SUPPRESS)
    args = parser.parse_args(argv)

    if args.run:
        if not args.project:
            parser.error("--run precisa da pasta do projeto")
        from .runtime.engine import run_project

        return run_project(args.project, room=args.room, max_frames=args.frames, screenshot=args.screenshot)

    from .editor.app import run_editor

    return run_editor(args.project, shot=args.shot)


if __name__ == "__main__":
    sys.exit(main())
