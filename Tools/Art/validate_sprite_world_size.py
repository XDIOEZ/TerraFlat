#!/usr/bin/env python3
"""只读检测 Sprite 源图可见范围、PPU 和缩放对应的 Unity 单位尺寸。"""

from __future__ import annotations

import argparse
from dataclasses import asdict, dataclass, field
import json
import math
from pathlib import Path
import re
import sys

try:
    from PIL import Image
except ImportError as exc:
    raise SystemExit("缺少 Pillow，请使用已安装 Pillow 的 Python。") from exc


# region 测量数据与输入

@dataclass
class SpriteSourceWorldSizeReport:
    path: str
    source_pixels: tuple[int, int] | None = None
    sprite_rect_pixels: tuple[int, int, int, int] | None = None
    visible_bounds_pixels: tuple[int, int, int, int] | None = None
    visible_pixels_size: tuple[int, int] | None = None
    pixels_per_unit: float | None = None
    pixels_per_unit_source: str = ""
    effective_scale: tuple[float, float] = (1.0, 1.0)
    visible_world_size: tuple[float, float] | None = None
    canvas_world_size: tuple[float, float] | None = None
    alpha_threshold: int = 1
    target_world_size: tuple[float | None, float | None] | None = None
    target_longest_world_size: float | None = None
    relative_size_error: tuple[float, ...] = ()
    suggested_pixels_per_unit: float | None = None
    size_matches_target: bool | None = None
    importer_limits: list[dict] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)

    @property
    def passed(self) -> bool:
        return not self.errors


def require_finite_positive_number(value: float, label: str) -> float:
    if not math.isfinite(value) or value <= 0:
        raise ValueError(f"{label} 必须为有限正数。")
    return value


def read_unity_texture_importer_settings(meta_path: Path) -> dict:
    # 只读取 TextureImporter 的固定标量与平台段，避免改写或重新导入资产。
    text = meta_path.read_text(encoding="utf-8-sig")
    if not re.search(r"(?m)^TextureImporter:\s*$", text):
        raise ValueError(f"不是 TextureImporter 配置：{meta_path}")
    settings: dict = {}
    for key in ("spritePixelsToUnits", "spriteMode", "textureType", "maxTextureSize", "alphaUsage", "nPOTScale"):
        match = re.search(rf"(?m)^  {key}:\s*([^\r\n]+)", text)
        if match:
            settings[key] = float(match.group(1).strip())
    platform_section = re.search(r"(?ms)^  platformSettings:\s*\n(.*?)(?=^  [A-Za-z]|\Z)", text)
    platforms: list[dict] = []
    if platform_section:
        for block in re.split(r"(?m)^  - ", platform_section.group(1))[1:]:
            values = dict(re.findall(r"(?m)^    (\w+):\s*([^\r\n]*)", block))
            name = values.get("buildTarget", "Unknown").strip()
            if name == "DefaultTexturePlatform" or values.get("overridden", "0").strip() == "1":
                raw_limit = values.get("maxTextureSize", str(settings.get("maxTextureSize", 0)))
                platforms.append({"platform": name, "max_texture_size": int(float(raw_limit))})
    if not platforms and "maxTextureSize" in settings:
        platforms.append({"platform": "DefaultTexturePlatform", "max_texture_size": int(settings["maxTextureSize"])})
    settings["platforms"] = platforms
    return settings


def merge_inherited_visual_configuration(parent: dict, child: dict) -> dict:
    result = dict(parent)
    for key, value in child.items():
        result[key] = merge_inherited_visual_configuration(result[key], value) if isinstance(value, dict) and isinstance(result.get(key), dict) else value
    return result


def read_shell_visual_scale_and_sprite_address(manifest_path: Path, item_id: str) -> tuple[tuple[float, float], str]:
    records = json.loads(manifest_path.read_text(encoding="utf-8-sig"))["items"]
    by_id = {record["id"]: record for record in records}
    if len(by_id) != len(records):
        raise ValueError("外壳清单存在重复 id。")
    visited: set[str] = set()

    def resolve_visual(current_id: str) -> dict:
        if current_id in visited:
            raise ValueError(f"外壳继承形成循环：{current_id}")
        if current_id not in by_id:
            raise ValueError(f"此清单缺少外壳或继承父项 {current_id}；请显式指定 --scale。")
        visited.add(current_id)
        record = by_id[current_id]
        parent = resolve_visual(record["parent"]) if record.get("parent") else {}
        return merge_inherited_visual_configuration(parent, record.get("visual", {}))

    visual = resolve_visual(item_id)
    scale = visual.get("rendererLocalScale", {})
    return (float(scale.get("x", 1.0)), float(scale.get("y", 1.0))), visual.get("spriteAddress", "")

# endregion

# region 生产测量与尺寸对照

def measure_sprite_source_world_size(image_path: Path, *, pixels_per_unit: float | None = None,
                                     scale: tuple[float, float] = (1.0, 1.0), alpha_threshold: int = 1,
                                     sprite_rect: tuple[int, int, int, int] | None = None) -> SpriteSourceWorldSizeReport:
    report = SpriteSourceWorldSizeReport(path=str(image_path.resolve()), alpha_threshold=alpha_threshold)
    try:
        if not 1 <= alpha_threshold <= 255:
            raise ValueError("Alpha 阈值必须在 1～255 之间。")
        if len(scale) != 2 or any(not math.isfinite(axis) or axis == 0 for axis in scale):
            raise ValueError("缩放 X/Y 必须有限且不能为零；负数镜像按绝对值测量。")
        report.effective_scale = tuple(scale)
        if not image_path.is_file():
            raise ValueError(f"源图片不存在：{image_path}")
        meta_path = Path(str(image_path) + ".meta")
        importer = read_unity_texture_importer_settings(meta_path) if meta_path.is_file() else {}
        ppu = pixels_per_unit if pixels_per_unit is not None else importer.get("spritePixelsToUnits")
        if ppu is None:
            raise ValueError("缺少相邻 .meta 的 spritePixelsToUnits，请传 --ppu。")
        report.pixels_per_unit = require_finite_positive_number(float(ppu), "PPU")
        report.pixels_per_unit_source = "explicit" if pixels_per_unit is not None else str(meta_path.resolve())
        if importer and importer.get("textureType", 8) != 8:
            raise ValueError(".meta 未配置成 Sprite 纹理，不能当作已导入 Sprite 验证。")
        if importer.get("spriteMode", 1) not in (1, 2):
            raise ValueError(".meta 未启用 Single 或 Multiple Sprite 模式。")
        if importer.get("spriteMode", 1) == 2 and sprite_rect is None:
            raise ValueError("多 Sprite 图集需要 --sprite-rect X Y W H（源图像素、Unity 左下原点）。")
        with Image.open(image_path) as image:
            rgba = image.convert("RGBA")
        width, height = rgba.size
        report.source_pixels = (width, height)
        x, y, rect_width, rect_height = sprite_rect or (0, 0, width, height)
        if min(x, y) < 0 or min(rect_width, rect_height) <= 0 or x + rect_width > width or y + rect_height > height:
            raise ValueError("Sprite rect 必须是源图内的非空矩形。")
        report.sprite_rect_pixels = (x, y, rect_width, rect_height)
        crop = rgba.crop((x, height - y - rect_height, x + rect_width, height - y))
        if importer.get("alphaUsage", 1) == 0:
            bounds = (0, 0, rect_width, rect_height)
            report.warnings.append("Importer 忽略 Alpha，按完整 Sprite 矩形计算。")
        elif importer.get("alphaUsage", 1) != 1:
            raise ValueError("Importer 从灰度生成 Alpha，此模式不能用源 Alpha 推算。")
        else:
            bounds = crop.getchannel("A").point(lambda alpha: 255 if alpha >= alpha_threshold else 0).getbbox()
        if bounds is None:
            raise ValueError("Sprite 在当前 Alpha 阈值下没有可见像素。")
        visible_width, visible_height = bounds[2] - bounds[0], bounds[3] - bounds[1]
        report.visible_bounds_pixels = bounds
        report.visible_pixels_size = (visible_width, visible_height)
        report.visible_world_size = (visible_width * abs(scale[0]) / ppu, visible_height * abs(scale[1]) / ppu)
        report.canvas_world_size = (rect_width * abs(scale[0]) / ppu, rect_height * abs(scale[1]) / ppu)
        report.importer_limits = importer.get("platforms", [])
        for limit in report.importer_limits:
            if limit["max_texture_size"] < max(width, height):
                report.warnings.append(f"{limit['platform']} maxTextureSize={limit['max_texture_size']} 会缩小 {width}×{height} 源图；这里按源像素/配置 PPU 预测，导入后边缘可能量化。")
        if importer.get("nPOTScale", 0) != 0:
            report.warnings.append("启用了 NPOT 尺寸调整，请另外检查实际导入结果。")
    except (OSError, ValueError, TypeError, OverflowError) as exc:
        report.errors.append(str(exc))
    return report


def compare_sprite_world_size_with_target(report: SpriteSourceWorldSizeReport, *, target_longest: float | None = None,
                                          target_width: float | None = None, target_height: float | None = None,
                                          tolerance: float = 0.05) -> None:
    if report.errors:
        return
    if not math.isfinite(tolerance) or tolerance < 0:
        raise ValueError("容差必须为有限非负数。")
    if target_longest is not None and (target_width is not None or target_height is not None):
        raise ValueError("最长边目标与宽/高目标不能同时指定。")
    width, height = report.visible_world_size
    comparisons: list[tuple[float, float]] = []
    if target_longest is not None:
        require_finite_positive_number(target_longest, "最长边目标")
        report.target_longest_world_size = target_longest
        comparisons = [(max(width, height), target_longest)]
    elif target_width is not None or target_height is not None:
        report.target_world_size = (target_width, target_height)
        for actual, target in ((width, target_width), (height, target_height)):
            if target is not None:
                comparisons.append((actual, require_finite_positive_number(target, "目标尺寸")))
    if not comparisons:
        return
    ratios = tuple(actual / target for actual, target in comparisons)
    report.relative_size_error = tuple(abs(ratio - 1.0) for ratio in ratios)
    report.suggested_pixels_per_unit = report.pixels_per_unit * max(ratios)
    report.size_matches_target = all(error <= tolerance + 1e-9 for error in report.relative_size_error)
    if not report.size_matches_target:
        report.errors.append(f"可见尺寸与目标不符，最大偏差 {max(report.relative_size_error):.2%}，容差 {tolerance:.2%}。")

# endregion

# region 命令行与报告

def create_sprite_world_size_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", type=Path, nargs="+", help="PNG 等源图片；可一次对照多张。")
    parser.add_argument("--ppu", type=float, help="显式 PPU；默认读取相邻 .meta。")
    parser.add_argument("--scale", type=float, nargs=2, default=(1.0, 1.0), metavar=("X", "Y"), help="额外世界缩放；负数镜像允许。")
    parser.add_argument("--alpha-threshold", type=int, default=1)
    parser.add_argument("--sprite-rect", type=int, nargs=4, metavar=("X", "Y", "W", "H"), help="源图像素、Unity 左下原点；用于显式选择图集切片。")
    parser.add_argument("--target-longest", type=float, help="目标最长可见边，单位 Unity units。")
    parser.add_argument("--target-width", type=float)
    parser.add_argument("--target-height", type=float)
    parser.add_argument("--tolerance", type=float, default=0.05, help="相对容差，0.05 表示 ±5%%。")
    parser.add_argument("--reference", type=Path, help="参考素材；按其可见宽高对照，读取参考自身 .meta PPU。")
    parser.add_argument("--reference-scale", type=float, nargs=2, default=(1.0, 1.0), metavar=("X", "Y"))
    parser.add_argument("--shell-manifest", type=Path, help="读取物品清单中 rendererLocalScale；与 --item-id 同用，仅支持单张图片。")
    parser.add_argument("--item-id")
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--json", action="store_true", help="输出 JSON；返回码 0=通过/测量成功，1=失败，2=参数错误。")
    return parser


def execute_sprite_world_size_validation(arguments: argparse.Namespace) -> dict:
    if bool(arguments.shell_manifest) != bool(arguments.item_id):
        raise ValueError("--shell-manifest 与 --item-id 必须一起指定。")
    if arguments.reference and any(value is not None for value in (arguments.target_longest, arguments.target_width, arguments.target_height)):
        raise ValueError("--reference 与显式目标不能同时指定。")
    if not math.isfinite(arguments.tolerance) or arguments.tolerance < 0:
        raise ValueError("容差必须为有限非负数。")
    for label, value in (("最长边", arguments.target_longest), ("宽", arguments.target_width), ("高", arguments.target_height)):
        if value is not None:
            require_finite_positive_number(value, label)
    if arguments.target_longest is not None and (arguments.target_width is not None or arguments.target_height is not None):
        raise ValueError("最长边目标与宽/高目标不能同时指定。")
    scale = tuple(arguments.scale)
    if arguments.shell_manifest:
        if len(arguments.images) != 1:
            raise ValueError("物品外壳模式一次只检测一张图片。")
        visual_scale, sprite_address = read_shell_visual_scale_and_sprite_address(arguments.shell_manifest, arguments.item_id)
        source_address = re.sub(r"\[[^\]]+\]$", "", sprite_address)
        expected = (arguments.project_root / source_address).resolve()
        if not source_address or str(expected).casefold() != str(arguments.images[0].resolve()).casefold():
            raise ValueError("物品 visual.spriteAddress 与待测源图不一致。")
        scale = tuple(manual * local for manual, local in zip(scale, visual_scale))
    reference = None
    target_width, target_height = arguments.target_width, arguments.target_height
    if arguments.reference:
        reference = measure_sprite_source_world_size(arguments.reference, scale=tuple(arguments.reference_scale), alpha_threshold=arguments.alpha_threshold)
        if reference.errors:
            raise ValueError("参考素材无效：" + "；".join(reference.errors))
        target_width, target_height = reference.visible_world_size
    reports = []
    for path in arguments.images:
        report = measure_sprite_source_world_size(path, pixels_per_unit=arguments.ppu, scale=scale,
                                                  alpha_threshold=arguments.alpha_threshold,
                                                  sprite_rect=tuple(arguments.sprite_rect) if arguments.sprite_rect else None)
        compare_sprite_world_size_with_target(report, target_longest=arguments.target_longest,
                                               target_width=target_width, target_height=target_height, tolerance=arguments.tolerance)
        reports.append({**asdict(report), "passed": report.passed})
    return {"schema_version": 1, "measurement_scope": "source_asset_prediction",
            "scope_note": "源图/导入配置预测；不读取当前已加载 Sprite，不包含父级缩放、旋转、动画、Shader、相机或 UI 拉伸。",
            "pixel_coordinates_note": "sprite_rect_pixels 为源图左下原点 X/Y/W/H；visible_bounds_pixels 为切片内左上原点 left/top/right/bottom（右下不含）。",
            "passed": all(report["passed"] for report in reports), "tolerance": arguments.tolerance,
            "reference": asdict(reference) if reference else None, "reports": reports}


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = create_sprite_world_size_argument_parser()
    arguments = parser.parse_args(argv)
    try:
        payload = execute_sprite_world_size_validation(arguments)
    except (OSError, ValueError, KeyError, TypeError) as exc:
        if arguments.json:
            print(json.dumps({"schema_version": 1, "passed": False, "errors": [str(exc)], "reports": []}, ensure_ascii=False, indent=2))
        else:
            print(f"[ERROR] {exc}", file=sys.stderr)
        return 2
    if arguments.json:
        print(json.dumps(payload, ensure_ascii=False, indent=2, allow_nan=False))
    else:
        print(payload["scope_note"])
        for report in payload["reports"]:
            status = "FAIL" if not report["passed"] else "PASS" if report["size_matches_target"] is not None else "MEASURED"
            size = report["visible_world_size"]
            rendered = "未知" if size is None else f"{size[0]:.4f} × {size[1]:.4f} Unity units"
            print(f"[{status}] {report['path']}\n  源图 {report['source_pixels']} | 可见像素 {report['visible_pixels_size']} | PPU {report['pixels_per_unit']} | 缩放 {report['effective_scale']} | {rendered}")
            if report["suggested_pixels_per_unit"] is not None:
                print(f"  建议 PPU={report['suggested_pixels_per_unit']:.4f}（保持原图；多目标按最大边界适配，宽高比不同时不能同时精确匹配）")
            for message in report["warnings"]:
                print(f"  WARN: {message}")
            for message in report["errors"]:
                print(f"  ERROR: {message}")
    return 0 if payload["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())

# endregion
