"""Matrizes 4x4 para o ModernGL (escritas em linha, enviadas transpostas)."""

from __future__ import annotations

import math

import numpy as np


def ortho(left: float, right: float, bottom: float, top: float, near: float = -1000.0, far: float = 1000.0) -> np.ndarray:
    m = np.identity(4, dtype="f4")
    m[0, 0] = 2.0 / (right - left)
    m[1, 1] = 2.0 / (top - bottom)
    m[2, 2] = -2.0 / (far - near)
    m[0, 3] = -(right + left) / (right - left)
    m[1, 3] = -(top + bottom) / (top - bottom)
    m[2, 3] = -(far + near) / (far - near)
    return m


def translate(x: float, y: float, z: float) -> np.ndarray:
    m = np.identity(4, dtype="f4")
    m[:3, 3] = (x, y, z)
    return m


def scale(sx: float, sy: float, sz: float) -> np.ndarray:
    return np.diag([sx, sy, sz, 1.0]).astype("f4")


def rotate_xyz(rx: float, ry: float, rz: float) -> np.ndarray:
    """Rotação em graus, aplicada em X, depois Y, depois Z."""
    ax, ay, az = (math.radians(a) for a in (rx, ry, rz))
    cx, sx = math.cos(ax), math.sin(ax)
    cy, sy = math.cos(ay), math.sin(ay)
    cz, sz = math.cos(az), math.sin(az)
    mx = np.array([[1, 0, 0, 0], [0, cx, -sx, 0], [0, sx, cx, 0], [0, 0, 0, 1]], dtype="f4")
    my = np.array([[cy, 0, sy, 0], [0, 1, 0, 0], [-sy, 0, cy, 0], [0, 0, 0, 1]], dtype="f4")
    mz = np.array([[cz, -sz, 0, 0], [sz, cz, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]], dtype="f4")
    return mz @ my @ mx


def gl_bytes(m: np.ndarray) -> bytes:
    return np.ascontiguousarray(m.T, dtype="f4").tobytes()


def normal_matrix(model: np.ndarray) -> np.ndarray:
    m3 = model[:3, :3].astype("f8")
    try:
        return np.linalg.inv(m3).T.astype("f4")
    except np.linalg.LinAlgError:
        return np.identity(3, dtype="f4")


def parse_color(value, alpha: float | None = None) -> tuple[float, float, float, float]:
    """Aceita '#RRGGBB', '#RRGGBBAA', (r, g, b) ou (r, g, b, a) de 0 a 1 ou de 0 a 255."""
    if isinstance(value, str):
        h = value.strip().lstrip("#")
        if len(h) == 3:
            h = "".join(c * 2 for c in h)
        if len(h) not in (6, 8):
            raise ValueError(f"Cor inválida: {value!r}")
        parts = [int(h[i:i + 2], 16) / 255.0 for i in range(0, len(h), 2)]
        if len(parts) == 3:
            parts.append(1.0)
    else:
        parts = [float(v) for v in value]
        if any(v > 1.0 for v in parts):
            parts = [v / 255.0 for v in parts]
        if len(parts) == 3:
            parts.append(1.0)
    if alpha is not None:
        parts[3] = parts[3] * alpha
    return parts[0], parts[1], parts[2], parts[3]
