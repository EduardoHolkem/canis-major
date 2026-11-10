"""Malhas 3D que entram na cena 2D: cubo, esfera, plano ou um arquivo .obj."""

from __future__ import annotations

import math
from pathlib import Path

import numpy as np


class Mesh:
    """Vértices em 8 floats: posição (3), normal (3) e uv (2). O tamanho padrão cabe num cubo de 1."""

    def __init__(self, vertices: np.ndarray):
        vertices = np.asarray(vertices, dtype="f4").reshape(-1, 8)
        if len(vertices) == 0:
            raise ValueError("A malha não tem vértices.")
        self.vertices = vertices
        self._gpu = None  # (contexto, buffer, vao)

    # ---------- formas prontas ----------

    @classmethod
    def cube(cls) -> "Mesh":
        faces = [
            ((0, 0, 1), (1, 0, 0), (0, 1, 0)),
            ((0, 0, -1), (-1, 0, 0), (0, 1, 0)),
            ((1, 0, 0), (0, 0, -1), (0, 1, 0)),
            ((-1, 0, 0), (0, 0, 1), (0, 1, 0)),
            ((0, 1, 0), (1, 0, 0), (0, 0, -1)),
            ((0, -1, 0), (1, 0, 0), (0, 0, 1)),
        ]
        out = []
        for n, u, v in faces:
            n, u, v = np.array(n, "f4"), np.array(u, "f4"), np.array(v, "f4")
            c = n * 0.5
            corners = [
                (c - u * 0.5 - v * 0.5, (0, 1)),
                (c + u * 0.5 - v * 0.5, (1, 1)),
                (c + u * 0.5 + v * 0.5, (1, 0)),
                (c - u * 0.5 + v * 0.5, (0, 0)),
            ]
            for i in (0, 1, 2, 0, 2, 3):
                p, uv = corners[i]
                out.append([*p, *n, *uv])
        return cls(np.array(out, "f4"))

    @classmethod
    def sphere(cls, segments: int = 24, rings: int = 16) -> "Mesh":
        out = []

        def vert(ring, seg):
            theta = math.pi * ring / rings
            phi = 2 * math.pi * seg / segments
            n = (math.sin(theta) * math.cos(phi), math.cos(theta), math.sin(theta) * math.sin(phi))
            return [n[0] * 0.5, n[1] * 0.5, n[2] * 0.5, *n, seg / segments, ring / rings]

        for r in range(rings):
            for s in range(segments):
                a, b = vert(r, s), vert(r + 1, s)
                c, d = vert(r + 1, s + 1), vert(r, s + 1)
                out += [a, b, c, a, c, d]
        return cls(np.array(out, "f4"))

    @classmethod
    def plane(cls) -> "Mesh":
        n = (0.0, 0.0, 1.0)
        quad = [(-0.5, -0.5, 0, 0), (0.5, -0.5, 1, 0), (0.5, 0.5, 1, 1), (-0.5, 0.5, 0, 1)]
        out = [[x, y, 0.0, *n, u, v] for i in (0, 1, 2, 0, 2, 3) for x, y, u, v in [quad[i]]]
        return cls(np.array(out, "f4"))

    @classmethod
    def load(cls, path) -> "Mesh":
        """Lê um .obj simples (v, vt, vn, f), centraliza e ajusta para caber num cubo de 1."""
        positions, uvs, normals, out = [], [], [], []
        for line in Path(path).read_text("utf-8", errors="replace").splitlines():
            parts = line.split()
            if not parts:
                continue
            if parts[0] == "v":
                positions.append([float(x) for x in parts[1:4]])
            elif parts[0] == "vt":
                uvs.append([float(x) for x in parts[1:3]])
            elif parts[0] == "vn":
                normals.append([float(x) for x in parts[1:4]])
            elif parts[0] == "f":
                idx = [p.split("/") for p in parts[1:]]
                for i in range(1, len(idx) - 1):
                    for corner in (idx[0], idx[i], idx[i + 1]):
                        p = positions[int(corner[0]) - 1]
                        uv = uvs[int(corner[1]) - 1] if len(corner) > 1 and corner[1] else [0.0, 0.0]
                        n = normals[int(corner[2]) - 1] if len(corner) > 2 and corner[2] else [0.0, 0.0, 1.0]
                        out.append([*p, *n, uv[0], 1.0 - uv[1]])
        if not out:
            raise ValueError(f"Nenhuma face em {path}")
        data = np.array(out, "f4")
        pos = data[:, :3]
        center = (pos.max(axis=0) + pos.min(axis=0)) / 2
        size = float((pos.max(axis=0) - pos.min(axis=0)).max()) or 1.0
        data[:, :3] = (pos - center) / size
        return cls(data)

    # ---------- GPU ----------

    def vao(self, ctx, program):
        if self._gpu is None or self._gpu[0] is not ctx:
            buf = ctx.buffer(self.vertices.tobytes())
            vao = ctx.vertex_array(program, [(buf, "3f 3f 2f", "in_pos", "in_normal", "in_uv")], skip_errors=True)
            self._gpu = (ctx, buf, vao)
        return self._gpu[2]
