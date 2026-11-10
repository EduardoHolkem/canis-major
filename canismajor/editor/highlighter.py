"""Sintaxe colorida para Python e GLSL, com as cores do tema."""

from __future__ import annotations

import keyword
import re

from PySide6.QtGui import QColor, QFont, QSyntaxHighlighter, QTextCharFormat

from .theme import ITALIC_FONT, Palette

PY_KEYWORDS = set(keyword.kwlist) | {"match", "case", "self"}
PY_CONSTANTS = {"True", "False", "None"}
PY_BUILTINS = {
    "print", "len", "range", "int", "float", "str", "list", "dict", "set", "tuple", "min", "max", "abs", "round",
    "isinstance", "super", "enumerate", "zip", "sorted", "any", "all", "open", "sum", "map", "filter", "getattr",
    "setattr", "hasattr", "type", "bool", "reversed", "divmod", "pow", "repr", "id", "iter", "next",
}
ENGINE_NAMES = {"GameObject", "Mesh", "keys", "mouse", "game", "draw"}

GLSL_KEYWORDS = {
    "if", "else", "for", "while", "do", "return", "break", "continue", "discard", "in", "out", "inout", "uniform",
    "const", "struct", "layout", "true", "false", "precision", "highp", "mediump", "lowp", "switch", "case", "default",
}
GLSL_TYPES = {
    "void", "bool", "int", "uint", "float", "vec2", "vec3", "vec4", "ivec2", "ivec3", "ivec4", "bvec2", "bvec3",
    "bvec4", "mat2", "mat3", "mat4", "sampler2D",
}
GLSL_BUILTINS = {
    "texture", "mix", "smoothstep", "step", "clamp", "sin", "cos", "tan", "asin", "acos", "atan", "length",
    "normalize", "dot", "cross", "fract", "floor", "ceil", "abs", "pow", "exp", "log", "sqrt", "min", "max", "mod",
    "reflect", "distance", "sign", "radians", "degrees", "exp2", "log2", "inversesqrt", "effect",
}
GLSL_UNIFORMS = {"u_texture", "u_time", "u_resolution", "uv", "color", "tex"}

PY_TOKEN = re.compile(
    r"(?P<comment>#.*)"
    r"|(?P<tstr>[rRbBfFuU]{0,2}(?:'''|\"\"\"))"
    r"|(?P<str>[rRbBfFuU]{0,2}(?:'(?:[^'\\\n]|\\.)*'?|\"(?:[^\"\\\n]|\\.)*\"?))"
    r"|(?P<num>\b(?:0[xXbBoO][0-9a-fA-F_]+|\d[\d_]*\.?\d*(?:[eE][+-]?\d+)?j?)\b)"
    r"|(?P<deco>@[\w.]+)"
    r"|(?P<word>[A-Za-z_]\w*)"
)

GLSL_TOKEN = re.compile(
    r"(?P<comment>//.*)"
    r"|(?P<block>/\*)"
    r"|(?P<pre>^\s*#.*)"
    r"|(?P<num>\b\d+\.?\d*(?:[eE][+-]?\d+)?[fu]?\b|\.\d+\b)"
    r"|(?P<word>[A-Za-z_]\w*)"
)


class Highlighter(QSyntaxHighlighter):
    IN_SINGLE, IN_DOUBLE, IN_BLOCK = 1, 2, 3

    def __init__(self, document, language: str, palette: Palette):
        super().__init__(document)
        self.language = language
        self.set_palette(palette)

    def set_palette(self, p: Palette) -> None:
        def fmt(color: str, bold=False, italic=False) -> QTextCharFormat:
            f = QTextCharFormat()
            f.setForeground(QColor(color))
            if bold:
                f.setFontWeight(QFont.Weight.DemiBold)
            if italic:
                f.setFontFamilies([ITALIC_FONT])
                f.setFontItalic(True)
            return f

        self.f_keyword = fmt(p.keyword, bold=True)
        self.f_type = fmt(p.type)
        self.f_function = fmt(p.function)
        self.f_string = fmt(p.string)
        self.f_number = fmt(p.number)
        self.f_comment = fmt(p.faint, italic=True)
        self.f_deco = fmt(p.keyword)
        self.f_engine = fmt(p.type, bold=True)
        self.f_muted = fmt(p.muted)
        self.rehighlight()

    def highlightBlock(self, text: str) -> None:
        if self.language == "python":
            self._python(text)
        elif self.language == "glsl":
            self._glsl(text)
        else:
            self.setCurrentBlockState(0)

    def _python(self, text: str) -> None:
        state = self.previousBlockState()
        pos = 0
        if state in (self.IN_SINGLE, self.IN_DOUBLE):
            delim = "'''" if state == self.IN_SINGLE else '"""'
            end = text.find(delim)
            if end < 0:
                self.setFormat(0, len(text), self.f_string)
                self.setCurrentBlockState(state)
                return
            self.setFormat(0, end + 3, self.f_string)
            pos = end + 3
        self.setCurrentBlockState(0)
        prev_word = ""
        while True:
            m = PY_TOKEN.search(text, pos)
            if not m:
                break
            start, end = m.span()
            kind = m.lastgroup
            if kind == "comment":
                self.setFormat(start, end - start, self.f_comment)
            elif kind == "tstr":
                delim = m.group()[-3:]
                close = text.find(delim, end)
                if close < 0:
                    self.setFormat(start, len(text) - start, self.f_string)
                    self.setCurrentBlockState(self.IN_SINGLE if delim == "'''" else self.IN_DOUBLE)
                    return
                end = close + 3
                self.setFormat(start, end - start, self.f_string)
            elif kind == "str":
                self.setFormat(start, end - start, self.f_string)
            elif kind == "num":
                self.setFormat(start, end - start, self.f_number)
            elif kind == "deco":
                self.setFormat(start, end - start, self.f_deco)
            elif kind == "word":
                word = m.group()
                if prev_word == "def":
                    self.setFormat(start, end - start, self.f_function)
                elif prev_word == "class":
                    self.setFormat(start, end - start, self.f_type)
                elif word in PY_CONSTANTS:
                    self.setFormat(start, end - start, self.f_number)
                elif word == "self":
                    self.setFormat(start, end - start, self.f_muted)
                elif word in PY_KEYWORDS:
                    self.setFormat(start, end - start, self.f_keyword)
                elif word in ENGINE_NAMES:
                    self.setFormat(start, end - start, self.f_engine)
                elif word in PY_BUILTINS or text[end:end + 1] == "(":
                    self.setFormat(start, end - start, self.f_function)
                elif word[:1].isupper():
                    self.setFormat(start, end - start, self.f_type)
                prev_word = word
            pos = max(end, start + 1)

    def _glsl(self, text: str) -> None:
        pos = 0
        if self.previousBlockState() == self.IN_BLOCK:
            end = text.find("*/")
            if end < 0:
                self.setFormat(0, len(text), self.f_comment)
                self.setCurrentBlockState(self.IN_BLOCK)
                return
            self.setFormat(0, end + 2, self.f_comment)
            pos = end + 2
        self.setCurrentBlockState(0)
        while True:
            m = GLSL_TOKEN.search(text, pos)
            if not m:
                break
            start, end = m.span()
            kind = m.lastgroup
            if kind == "comment":
                self.setFormat(start, end - start, self.f_comment)
            elif kind == "block":
                close = text.find("*/", end)
                if close < 0:
                    self.setFormat(start, len(text) - start, self.f_comment)
                    self.setCurrentBlockState(self.IN_BLOCK)
                    return
                end = close + 2
                self.setFormat(start, end - start, self.f_comment)
            elif kind == "pre":
                self.setFormat(start, end - start, self.f_deco)
            elif kind == "num":
                self.setFormat(start, end - start, self.f_number)
            elif kind == "word":
                word = m.group()
                if word in GLSL_KEYWORDS:
                    self.setFormat(start, end - start, self.f_keyword)
                elif word in GLSL_TYPES:
                    self.setFormat(start, end - start, self.f_type)
                elif word in GLSL_BUILTINS or text[end:end + 1] == "(":
                    self.setFormat(start, end - start, self.f_function)
                elif word in GLSL_UNIFORMS or word.startswith("u_"):
                    self.setFormat(start, end - start, self.f_number)
            pos = max(end, start + 1)


def completion_words(language: str) -> list[str]:
    if language == "python":
        api = [
            "create", "step", "draw", "on_destroy", "draw_self", "destroy", "collide", "collides", "move", "bbox",
            "distance_to", "direction_to", "sprite", "layer", "scale_x", "scale_y", "rotation", "alpha", "frame",
            "animation_speed", "visible", "shader", "held", "pressed", "released", "axis", "goto", "restart", "quit",
            "spawn", "instances", "first", "count", "follow", "set_uniform", "play_sound", "stop_sounds", "camera_x",
            "camera_y", "room_width", "room_height", "post_shaders", "background", "data", "time", "fps", "width",
            "height", "text", "rect", "line", "circle", "mesh", "cube", "sphere", "plane", "load", "screen",
        ]
        return sorted(set(keyword.kwlist) | PY_BUILTINS | ENGINE_NAMES | set(api) | {"self"})
    if language == "glsl":
        return sorted(GLSL_KEYWORDS | GLSL_TYPES | GLSL_BUILTINS | {"u_texture", "u_time", "u_resolution"})
    return []
