#!/usr/bin/env python3
"""调用正式尺寸检测器检查 PPU、透明留白、缩放、拒绝路径与当前钻木取火器。"""

from __future__ import annotations

import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from PIL import Image, ImageDraw

PROJECT_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(PROJECT_ROOT))
from Tools.Art import validate_sprite_world_size as validator


# region 正式检测器回归

class SpriteWorldSizeValidatorRegressionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory(prefix="flatworld_sprite_size_")
        self.root = Path(self.directory.name)

    def tearDown(self) -> None:
        self.directory.cleanup()

    def create_test_sprite(self, filename: str, size: tuple[int, int], bounds: tuple[int, int, int, int] | None,
                           ppu: float, extra_meta: str = "") -> Path:
        # 合成图仅作为测量夹具，所有断言直接调用正式检测器。
        path = self.root / filename
        image = Image.new("RGBA", size)
        if bounds:
            left, top, right, bottom = bounds
            ImageDraw.Draw(image).rectangle((left, top, right - 1, bottom - 1), fill=(120, 80, 40, 255))
        image.save(path)
        Path(str(path) + ".meta").write_text(
            "fileFormatVersion: 2\nTextureImporter:\n  textureType: 8\n  spriteMode: 1\n"
            f"  spritePixelsToUnits: {ppu}\n  maxTextureSize: 4096\n  alphaUsage: 1\n{extra_meta}", encoding="utf-8")
        return path

    def run_validator_cli(self, *arguments: object) -> subprocess.CompletedProcess:
        return subprocess.run([sys.executable, str(PROJECT_ROOT / "Tools/Art/validate_sprite_world_size.py"),
                               *[str(argument) for argument in arguments], "--json"],
                              cwd=PROJECT_ROOT, capture_output=True, encoding="utf-8", check=False,
                              env={**__import__("os").environ, "PYTHONIOENCODING": "utf-8", "PYTHONDONTWRITEBYTECODE": "1"})

    def test_different_source_resolution_and_ppu_keep_equal_world_size(self) -> None:
        small = self.create_test_sprite("small.png", (8, 8), (2, 2, 6, 6), 8)
        large = self.create_test_sprite("large.png", (64, 64), (16, 16, 48, 48), 64)
        result = self.run_validator_cli(small, large, "--target-longest", 0.5)
        self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
        reports = json.loads(result.stdout)["reports"]
        self.assertEqual([entry["visible_world_size"] for entry in reports], [[0.5, 0.5], [0.5, 0.5]])

    def test_transparent_padding_does_not_inflate_visible_size(self) -> None:
        path = self.create_test_sprite("padded.png", (100, 80), (10, 20, 30, 50), 40)
        report = validator.measure_sprite_source_world_size(path)
        self.assertEqual(report.visible_pixels_size, (20, 30))
        self.assertEqual(report.visible_world_size, (0.5, 0.75))
        self.assertEqual(report.canvas_world_size, (2.5, 2.0))

    def test_nonuniform_negative_scale_uses_absolute_axes(self) -> None:
        path = self.create_test_sprite("mirror.png", (20, 30), (0, 0, 20, 30), 40)
        report = validator.measure_sprite_source_world_size(path, scale=(-2, 0.5))
        self.assertEqual(report.visible_world_size, (1.0, 0.375))

    def test_wrong_size_fails_and_suggests_ppu_without_writing_asset(self) -> None:
        path = self.create_test_sprite("oversized.png", (70, 35), (0, 0, 70, 35), 32)
        original = path.read_bytes(), Path(str(path) + ".meta").read_bytes()
        report = validator.measure_sprite_source_world_size(path)
        validator.compare_sprite_world_size_with_target(report, target_longest=0.7)
        self.assertFalse(report.passed)
        self.assertAlmostEqual(report.suggested_pixels_per_unit, 100)
        self.assertEqual((path.read_bytes(), Path(str(path) + ".meta").read_bytes()), original)

    def test_reference_compares_width_and_height(self) -> None:
        reference = self.create_test_sprite("reference.png", (10, 20), (0, 0, 10, 20), 20)
        candidate = self.create_test_sprite("candidate.png", (20, 40), (0, 0, 20, 40), 40)
        self.assertEqual(self.run_validator_cli(candidate, "--reference", reference).returncode, 0)
        different = self.create_test_sprite("different.png", (20, 20), (0, 0, 20, 20), 20)
        result = self.run_validator_cli(different, "--reference", reference)
        self.assertEqual(result.returncode, 1)
        self.assertFalse(json.loads(result.stdout)["passed"])

    def test_alpha_threshold_excludes_faint_outer_pixels(self) -> None:
        path = self.create_test_sprite("faint.png", (10, 10), (3, 3, 7, 7), 10)
        with Image.open(path) as source:
            image = source.convert("RGBA")
        image.putpixel((0, 0), (255, 255, 255, 1))
        image.save(path)
        self.assertEqual(validator.measure_sprite_source_world_size(path).visible_pixels_size, (7, 7))
        self.assertEqual(validator.measure_sprite_source_world_size(path, alpha_threshold=128).visible_pixels_size, (4, 4))

    def test_empty_image_and_invalid_ppu_are_reported(self) -> None:
        empty = self.create_test_sprite("empty.png", (8, 8), None, 8)
        self.assertFalse(validator.measure_sprite_source_world_size(empty).passed)
        valid = self.create_test_sprite("valid.png", (8, 8), (0, 0, 8, 8), 8)
        for ppu in (0, -1, float("nan"), float("inf")):
            with self.subTest(ppu=ppu):
                self.assertFalse(validator.measure_sprite_source_world_size(valid, pixels_per_unit=ppu).passed)

    def test_invalid_scale_and_reference_scale_return_structured_json(self) -> None:
        path = self.create_test_sprite("valid.png", (8, 8), (0, 0, 8, 8), 8)
        for values in (("--scale", "nan", "1"), ("--scale", "inf", "1"), ("--scale", "0", "1"),
                       ("--reference", path, "--reference-scale", "nan", "1")):
            with self.subTest(values=values):
                result = self.run_validator_cli(path, *values)
                self.assertNotEqual(result.returncode, 0)
                self.assertFalse(json.loads(result.stdout)["passed"])
                self.assertNotIn("Traceback", result.stderr)

    def test_bad_meta_and_out_of_range_rect_return_structured_json(self) -> None:
        path = self.create_test_sprite("valid.png", (8, 8), (0, 0, 8, 8), 8)
        result = self.run_validator_cli(path, "--sprite-rect", 6, 0, 4, 4)
        self.assertEqual(result.returncode, 1)
        self.assertFalse(json.loads(result.stdout)["passed"])
        meta = Path(str(path) + ".meta")
        meta.write_text(meta.read_text().replace("spritePixelsToUnits: 8", "spritePixelsToUnits: broken"))
        result = self.run_validator_cli(path)
        self.assertEqual(result.returncode, 1)
        self.assertFalse(json.loads(result.stdout)["passed"])

    def test_sprite_sheet_requires_explicit_source_rect(self) -> None:
        path = self.create_test_sprite("sheet.png", (20, 20), (5, 10, 15, 20), 10)
        meta = Path(str(path) + ".meta")
        meta.write_text(meta.read_text().replace("spriteMode: 1", "spriteMode: 2"))
        self.assertFalse(validator.measure_sprite_source_world_size(path).passed)
        report = validator.measure_sprite_source_world_size(path, sprite_rect=(5, 0, 10, 10))
        self.assertEqual(report.visible_world_size, (1, 1))

    def test_active_platform_downsample_risk_is_reported(self) -> None:
        path = self.create_test_sprite("large.png", (100, 80), (0, 0, 100, 80), 100,
            "  platformSettings:\n  - serializedVersion: 3\n    buildTarget: DefaultTexturePlatform\n    maxTextureSize: 4096\n    overridden: 0\n"
            "  - serializedVersion: 3\n    buildTarget: Android\n    maxTextureSize: 32\n    overridden: 1\n"
            "  - serializedVersion: 3\n    buildTarget: Standalone\n    maxTextureSize: 16\n    overridden: 0\n  spriteSheet:\n    sprites: []\n")
        report = validator.measure_sprite_source_world_size(path)
        self.assertTrue(report.passed)
        self.assertEqual(len(report.warnings), 1)
        self.assertIn("Android", report.warnings[0])
        self.assertNotIn("Standalone", str(report.importer_limits))

    def test_shell_manifest_inherits_visual_scale_and_validates_sprite_address(self) -> None:
        path = self.create_test_sprite("shell.png", (10, 10), (0, 0, 10, 10), 10)
        manifest = self.root / "shells.json"
        manifest.write_text(json.dumps({"items": [
            {"id": "base", "visual": {"rendererLocalScale": {"x": -0.5, "y": 0.5}}},
            {"id": "tool", "parent": "base", "visual": {"spriteAddress": path.name}}
        ]}), encoding="utf-8")
        result = self.run_validator_cli(path, "--shell-manifest", manifest, "--item-id", "tool", "--project-root", self.root, "--target-longest", 0.5)
        self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
        self.assertEqual(json.loads(result.stdout)["reports"][0]["visible_world_size"], [0.5, 0.5])
        result = self.run_validator_cli(path, "--shell-manifest", manifest, "--item-id", "tool", "--project-root", self.root / "wrong")
        self.assertEqual(result.returncode, 2)

    def test_current_fire_drill_source_with_actual_shell_scale_matches_point_seven_units(self) -> None:
        path = PROJECT_ROOT / "Assets/6_Art/Generated/Tools/PrimitiveSet_20261023/SparkMaker_Summoner/SparkMaker_Summoner_Icon.png"
        for filename, item_id in (("building_summoners.json", "SparkMaker_Summoner"), ("building_bodies.json", "SparkMaker")):
            with self.subTest(item_id=item_id):
                result = self.run_validator_cli(path, "--shell-manifest", PROJECT_ROOT / "Assets/StreamingAssets/GameConfig/Items/shells" / filename,
                                                "--item-id", item_id, "--target-longest", 0.7)
                self.assertEqual(result.returncode, 0, result.stderr + result.stdout)
                report = json.loads(result.stdout)["reports"][0]
                self.assertTrue(report["size_matches_target"])
                self.assertFalse(report["warnings"], report["warnings"])

# endregion

if __name__ == "__main__":
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    unittest.main(verbosity=2)
