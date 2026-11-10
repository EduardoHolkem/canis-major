"""Shaders da engine. Os do usuário só escrevem a função `effect`, e a engine completa o resto.

Um shader do projeto (shaders/nome.glsl) tem esta forma:

    vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
        return texture(tex, uv) * color;
    }

Ele serve tanto para um sprite (`self.shader = "nome"`) quanto para a tela inteira
(pós-processamento, na lista "post_shaders" da sala). Ficam disponíveis u_time (segundos),
u_resolution (tamanho do jogo em pixels) e qualquer `uniform` extra que você declarar,
preenchido com `game.set_uniform("nome", "u_x", valor)`.
"""

from __future__ import annotations

import re

HEADER = """#version 330
uniform sampler2D u_texture;
uniform float u_time;
uniform vec2 u_resolution;
in vec2 v_uv;
in vec4 v_color;
out vec4 f_color;
"""

FOOTER = """
void main() {
    f_color = effect(u_texture, v_uv, v_color);
}
"""

DEFAULT_EFFECT = """vec4 effect(sampler2D tex, vec2 uv, vec4 color) {
    return texture(tex, uv) * color;
}
"""

SPRITE_VS = """#version 330
uniform mat4 u_proj;
in vec2 in_corner;
in vec4 i_rect;        // x, y, largura, altura
in vec3 i_origin_rot;  // origem x, origem y, rotação em radianos
in vec4 i_uv;          // u0, v0, u1, v1
in vec4 i_color;
out vec2 v_uv;
out vec4 v_color;
void main() {
    vec2 local = in_corner * i_rect.zw - i_origin_rot.xy;
    float c = cos(i_origin_rot.z);
    float s = sin(i_origin_rot.z);
    vec2 world = i_rect.xy + vec2(local.x * c - local.y * s, local.x * s + local.y * c);
    gl_Position = u_proj * vec4(world, 0.0, 1.0);
    v_uv = mix(i_uv.xy, i_uv.zw, in_corner);
    v_color = i_color;
}
"""

FULLSCREEN_VS = """#version 330
in vec2 in_corner;
out vec2 v_uv;
out vec4 v_color;
void main() {
    gl_Position = vec4(in_corner * 2.0 - 1.0, 0.0, 1.0);
    v_uv = in_corner;
    v_color = vec4(1.0);
}
"""

MESH_VS = """#version 330
uniform mat4 u_proj;
uniform mat4 u_model;
uniform mat3 u_normal_mat;
in vec3 in_pos;
in vec3 in_normal;
in vec2 in_uv;
out vec3 v_normal;
out vec2 v_uv;
void main() {
    gl_Position = u_proj * u_model * vec4(in_pos, 1.0);
    v_normal = normalize(u_normal_mat * in_normal);
    v_uv = in_uv;
}
"""

MESH_FS = """#version 330
uniform vec4 u_color;
uniform vec3 u_light;
uniform float u_ambient;
uniform sampler2D u_texture;
uniform bool u_textured;
in vec3 v_normal;
in vec2 v_uv;
out vec4 f_color;
void main() {
    vec4 base = u_color;
    if (u_textured) base *= texture(u_texture, v_uv);
    float diffuse = max(dot(normalize(v_normal), normalize(u_light)), 0.0);
    vec3 lit = base.rgb * (u_ambient + (1.0 - u_ambient) * diffuse);
    f_color = vec4(lit, base.a);
}
"""

_VERSION_LINE = re.compile(r"^\s*#version[^\n]*\n?", re.MULTILINE)


def wrap_effect(source: str) -> str:
    """Monta o fragment shader completo. O `#line 1` faz os erros apontarem a linha do arquivo do usuário."""
    body = _VERSION_LINE.sub("", source, count=1) if source.lstrip().startswith("#version") else source
    if "effect" not in body:
        body += "\n" + DEFAULT_EFFECT
    return HEADER + "#line 1\n" + body + "\n#line 100000\n" + FOOTER


_ERROR_LINE = re.compile(r"(?:^|\s)\d+[:(](\d+)\)?\s*:?\s*(?:error|ERROR)?:?\s*(.*)")


def parse_errors(message: str) -> list[tuple[int, str]]:
    """Transforma a mensagem do driver em (linha, texto). Linhas fora do arquivo do usuário viram 0."""
    found: list[tuple[int, str]] = []
    for raw in message.splitlines():
        raw = raw.strip()
        if not raw:
            continue
        m = _ERROR_LINE.search(raw)
        if m:
            line = int(m.group(1))
            text = m.group(2).strip() or raw
            found.append((line if line < 100000 else 0, text))
        elif "error" in raw.lower() or "erro" in raw.lower():
            found.append((0, raw))
    return found


class ShaderError(Exception):
    def __init__(self, name: str, path: str, message: str):
        self.name = name
        self.path = path
        self.message = message
        self.errors = parse_errors(message)
        lines = [f"{path}:{line}: {text}" if line else f"{path}: {text}" for line, text in self.errors] or [f"{path}: {message.strip()}"]
        super().__init__(f"Erro no shader '{name}':\n" + "\n".join(lines))
