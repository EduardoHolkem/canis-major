"""Testes da lógica sem interface. Rode com: python -m unittest discover -s tests"""

from __future__ import annotations

import io
import os
import shutil
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT))
os.environ.setdefault("PYGAME_HIDE_SUPPORT_PROMPT", "1")

from canismajor import shaderlib  # noqa: E402
from canismajor.project import Project, load_room, load_sprite_meta, valid_name  # noqa: E402
from canismajor.runtime.mathutil import parse_color  # noqa: E402
from canismajor.runtime.mesh import Mesh  # noqa: E402
from canismajor.templates import create_project  # noqa: E402
from canismajor.update_apply import _members, apply  # noqa: E402


class TempDir(unittest.TestCase):
    def setUp(self):
        base = os.environ.get("CM_TEST_TMP")
        self.tmp = Path(tempfile.mkdtemp(prefix="cm-", dir=base))

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)


class ProjectTests(TempDir):
    def test_valid_names(self):
        for ok in ("jogador", "Inimigo_2", "_x"):
            self.assertTrue(valid_name(ok), ok)
        for bad in ("", "2x", "class", "com espaco", "ação", "objects", "game"):
            self.assertFalse(valid_name(bad), bad)

    def test_example_project_round_trip(self):
        project = create_project(self.tmp / "Jogo", "Jogo")
        again = Project.load(self.tmp / "Jogo")
        self.assertEqual(again.name, "Jogo")
        self.assertEqual(again.resources("object"), ["cubo", "estrela", "jogador", "parede", "placar"])
        self.assertEqual(again.resources("sprite"), ["estrela", "jogador", "parede"])  # .json de meta não conta
        self.assertIn("coleta", again.resources("sound"))
        self.assertEqual(again.kind_of(project.path("object", "jogador")), ("object", "jogador"))
        self.assertIsNone(again.kind_of(project.root / "projeto.json"))
        room = load_room(project.path("room", "inicio"), project)
        self.assertEqual(room["post_shaders"], ["vinheta"])
        self.assertTrue(any(i["object"] == "jogador" for i in room["instances"]))
        self.assertEqual(load_sprite_meta(project.path("sprite", "estrela"))["frames"], 4)

    def test_refuses_non_empty_folder(self):
        (self.tmp / "ocupada").mkdir()
        (self.tmp / "ocupada" / "arquivo.txt").write_text("x")
        with self.assertRaises(FileExistsError):
            create_project(self.tmp / "ocupada", "X")


class ShaderTests(unittest.TestCase):
    def test_wrap_keeps_user_line_numbers(self):
        full = shaderlib.wrap_effect("#version 330\nvec4 effect(sampler2D t, vec2 uv, vec4 c) { return c; }\n")
        self.assertEqual(full.count("#version"), 1)
        self.assertIn("#line 1\n", full)
        self.assertIn("f_color = effect(", full)

    def test_parse_driver_messages(self):
        amd = "ERROR: 0:5: 'cor' : undeclared identifier\nERROR: 0:100002: 'effect' : no matching function"
        self.assertEqual(shaderlib.parse_errors(amd)[0], (5, "'cor' : undeclared identifier"))
        self.assertEqual(shaderlib.parse_errors(amd)[1][0], 0)  # erro no rodapé da engine não aponta linha do usuário
        nvidia = "0(7) : error C1008: undefined variable \"x\""
        self.assertEqual(shaderlib.parse_errors(nvidia)[0][0], 7)


class UpdateTests(TempDir):
    def test_versions(self):
        from canismajor.editor.updater import is_newer, parse_version
        self.assertEqual(parse_version("v1.10.2"), (1, 10, 2))
        self.assertTrue(is_newer("0.2.0", "0.1.9"))
        self.assertFalse(is_newer("0.1.0", "0.1.0"))
        self.assertTrue(is_newer("1.0.0", "0.99.99"))

    def test_zip_strips_top_folder_and_blocks_escape(self):
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w") as zf:
            zf.writestr("CanisMajor-0.2.0/canismajor/__init__.py", "__version__ = '0.2.0'\n")
            zf.writestr("CanisMajor-0.2.0/README.md", "novo")
            zf.writestr("CanisMajor-0.2.0/.venv/lixo.txt", "não deve entrar")
            zf.writestr("CanisMajor-0.2.0/../fora.txt", "não deve sair da pasta")
        path = self.tmp / "u.zip"
        path.write_bytes(buf.getvalue())
        with zipfile.ZipFile(path) as zf:
            rels = sorted(str(rel).replace("\\", "/") for _, rel in _members(zf))
        self.assertEqual(rels, ["README.md", "canismajor/__init__.py"])
        target = self.tmp / "engine"
        target.mkdir()
        apply(path, target)
        self.assertEqual((target / "README.md").read_text(), "novo")
        self.assertFalse((self.tmp / "fora.txt").exists())


class RuntimeLogicTests(unittest.TestCase):
    def test_colors(self):
        self.assertEqual(parse_color("#FF0000"), (1.0, 0.0, 0.0, 1.0))
        self.assertEqual(parse_color((255, 255, 255), alpha=0.5), (1.0, 1.0, 1.0, 0.5))
        self.assertAlmostEqual(parse_color("#00000080")[3], 128 / 255)
        with self.assertRaises(ValueError):
            parse_color("#12")

    def test_meshes(self):
        self.assertEqual(len(Mesh.cube().vertices), 36)
        self.assertEqual(len(Mesh.plane().vertices), 6)
        obj = "v 0 0 0\nv 2 0 0\nv 0 2 0\nvn 0 0 1\nf 1//1 2//1 3//1\n"
        with tempfile.NamedTemporaryFile("w", suffix=".obj", delete=False, dir=os.environ.get("CM_TEST_TMP")) as f:
            f.write(obj)
        try:
            mesh = Mesh.load(f.name)
            self.assertEqual(len(mesh.vertices), 3)
            self.assertLessEqual(float(abs(mesh.vertices[:, :3]).max()), 0.5 + 1e-6)
        finally:
            os.unlink(f.name)

    def test_move_stops_at_wall(self):
        from canismajor.runtime import api
        from canismajor.runtime.engine import Sprite

        class FakeEngine:
            def __init__(self):
                self.objs = []

            def sprite_info(self, name):
                return Sprite(name, None, 16, 16, 1, (0.0, 0.0), 8.0) if name else None

            def instances(self, kind=None):
                return [o for o in self.objs if o.alive and (kind is None or o.kind == kind)]

        class Box(api.GameObject):
            sprite = "caixa"

        eng = FakeEngine()
        api._engine = eng
        try:
            player, wall = Box(), Box()
            player.kind, wall.kind = "jogador", "parede"
            player.x, wall.x = 0, 40
            eng.objs = [player, wall]
            hit_x, hit_y = player.move(30, 0, solid="parede")
            self.assertTrue(hit_x)
            self.assertFalse(hit_y)
            self.assertEqual(player.x, 24)  # encostou: 24 + 16 = 40
            self.assertIs(player.collide("parede", dx=1), wall)
        finally:
            api._engine = None


class FuzzyTests(unittest.TestCase):
    def test_order(self):
        from canismajor.editor.widgets import fuzzy_score
        self.assertIsNone(fuzzy_score("xyz", "jogador"))
        self.assertGreater(fuzzy_score("jog", "jogador objeto"), fuzzy_score("jog", "novo objeto de jogo"))
        self.assertIsNotNone(fuzzy_score("nsl", "Nova sala"))


@unittest.skipIf(os.environ.get("CM_SKIP_GL"), "sem janela OpenGL neste ambiente")
class RuntimeSmokeTest(TempDir):
    def test_example_runs_and_draws(self):
        from canismajor.runtime.engine import run_project
        create_project(self.tmp / "Jogo", "Jogo")
        shot = self.tmp / "quadro.png"
        self.assertEqual(run_project(self.tmp / "Jogo", max_frames=10, screenshot=str(shot)), 0)
        from PIL import Image
        img = Image.open(shot)
        self.assertEqual(img.size, (480, 270))
        colors = img.convert("RGB").getcolors(maxcolors=1 << 20) or []
        self.assertGreater(len(colors), 20)  # desenhou algo além do fundo


if __name__ == "__main__":
    unittest.main()
