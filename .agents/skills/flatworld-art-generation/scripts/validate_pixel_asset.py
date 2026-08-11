#!/usr/bin/env python3
"""检查 FlatWorld 像素素材的尺寸、颜色与透明边缘是否符合运行时要求。"""

from __future__ import annotations

import argparse
import json
import sys
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Iterable

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover - 仅在环境缺少依赖时触发
    raise SystemExit("缺少 Pillow；请使用 Codex 工作区 Python，或在当前环境安装 pillow。") from exc


# region 参数与数据


@dataclass
class AssetReport:
    """记录单个图片的可复查统计与失败原因。"""

    path: str
    size: tuple[int, int] | None
    mode: str | None
    has_alpha: bool
    visible_pixels: int
    transparent_pixels: int
    partial_alpha_pixels: int
    visible_colors: int
    coverage: float
    corner_alpha: list[int]
    errors: list[str]

    @property
    def passed(self) -> bool:
        """没有错误时即通过。"""

        return not self.errors


def parse_size(value: str) -> tuple[int, int]:
    """解析 WIDTHxHEIGHT 参数。"""

    normalized = value.lower().replace("×", "x")
    try:
        width_text, height_text = normalized.split("x", maxsplit=1)
        width, height = int(width_text), int(height_text)
    except (TypeError, ValueError) as exc:
        raise argparse.ArgumentTypeError("尺寸必须写成 WIDTHxHEIGHT，例如 64x64。") from exc

    if width <= 0 or height <= 0:
        raise argparse.ArgumentTypeError("尺寸必须为正整数。")
    return width, height


def build_parser() -> argparse.ArgumentParser:
    """创建命令行参数。"""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("images", nargs="+", type=Path, help="需要检查的 PNG/WebP 图片。")
    parser.add_argument("--exact-size", type=parse_size, help="要求精确尺寸，例如 16x16。")
    parser.add_argument("--max-size", type=parse_size, help="允许的最大尺寸，例如 64x64。")
    parser.add_argument("--max-visible-colors", type=int, help="不含完全透明像素的最大颜色数。")
    parser.add_argument("--require-alpha", action="store_true", help="要求源图片实际包含 Alpha 通道。")
    parser.add_argument("--require-hard-alpha", action="store_true", help="要求 Alpha 只能为 0 或 255。")
    parser.add_argument(
        "--require-transparent-corners",
        action="store_true",
        help="要求四个角均完全透明。",
    )
    parser.add_argument("--json", action="store_true", help="输出机器可读 JSON。")
    return parser


# endregion

# region 检查逻辑


def flattened_pixels(image: Image.Image) -> Iterable[tuple[int, int, int, int]]:
    """兼容不同 Pillow 版本地读取扁平 RGBA 像素。"""

    getter = getattr(image, "get_flattened_data", None)
    return getter() if getter is not None else image.getdata()


def inspect_asset(path: Path, args: argparse.Namespace) -> AssetReport:
    """读取图片并执行所有已启用的规则。"""

    if not path.is_file():
        return AssetReport(
            path=str(path),
            size=None,
            mode=None,
            has_alpha=False,
            visible_pixels=0,
            transparent_pixels=0,
            partial_alpha_pixels=0,
            visible_colors=0,
            coverage=0.0,
            corner_alpha=[],
            errors=["文件不存在或不是普通文件。"],
        )

    with Image.open(path) as source:
        source.load()
        has_alpha = "A" in source.getbands() or "transparency" in source.info
        rgba = source.convert("RGBA")

    width, height = rgba.size
    pixels = list(flattened_pixels(rgba))
    alpha_values = [pixel[3] for pixel in pixels]
    visible_pixels = sum(alpha > 0 for alpha in alpha_values)
    transparent_pixels = sum(alpha == 0 for alpha in alpha_values)
    partial_alpha_pixels = sum(0 < alpha < 255 for alpha in alpha_values)
    visible_colors = len({pixel[:3] for pixel in pixels if pixel[3] > 0})
    corner_alpha = [
        rgba.getpixel((0, 0))[3],
        rgba.getpixel((width - 1, 0))[3],
        rgba.getpixel((0, height - 1))[3],
        rgba.getpixel((width - 1, height - 1))[3],
    ]
    errors: list[str] = []

    if visible_pixels == 0:
        errors.append("图片没有任何可见像素。")
    if args.require_alpha and not has_alpha:
        errors.append("图片不包含 Alpha 通道。")
    if args.require_hard_alpha and partial_alpha_pixels:
        errors.append(f"存在 {partial_alpha_pixels} 个半透明像素，要求 Alpha 仅为 0/255。")
    if args.require_transparent_corners and any(corner_alpha):
        errors.append(f"四角并非完全透明：{corner_alpha}。")
    if args.max_visible_colors is not None and visible_colors > args.max_visible_colors:
        errors.append(f"可见颜色为 {visible_colors}，超过上限 {args.max_visible_colors}。")
    if args.exact_size is not None and (width, height) != args.exact_size:
        errors.append(f"尺寸为 {width}x{height}，要求 {args.exact_size[0]}x{args.exact_size[1]}。")
    if args.max_size is not None and (width > args.max_size[0] or height > args.max_size[1]):
        errors.append(f"尺寸为 {width}x{height}，超过上限 {args.max_size[0]}x{args.max_size[1]}。")

    return AssetReport(
        path=str(path.resolve()),
        size=(width, height),
        mode=rgba.mode,
        has_alpha=has_alpha,
        visible_pixels=visible_pixels,
        transparent_pixels=transparent_pixels,
        partial_alpha_pixels=partial_alpha_pixels,
        visible_colors=visible_colors,
        coverage=round(visible_pixels / (width * height), 4),
        corner_alpha=corner_alpha,
        errors=errors,
    )


# endregion

# region 输出


def print_text_report(reports: list[AssetReport]) -> None:
    """输出便于人工快速浏览的结果。"""

    for report in reports:
        status = "PASS" if report.passed else "FAIL"
        size = "unknown" if report.size is None else f"{report.size[0]}x{report.size[1]}"
        print(
            f"[{status}] {report.path} | size={size} | colors={report.visible_colors} "
            f"| partial_alpha={report.partial_alpha_pixels} | coverage={report.coverage:.2%}"
        )
        for error in report.errors:
            print(f"  - {error}")


def main() -> int:
    """执行检查并以退出码表达整体结果。"""

    parser = build_parser()
    args = parser.parse_args()
    if args.max_visible_colors is not None and args.max_visible_colors <= 0:
        parser.error("--max-visible-colors 必须为正整数。")

    reports = [inspect_asset(path, args) for path in args.images]
    if args.json:
        payload = [{**asdict(report), "passed": report.passed} for report in reports]
        print(json.dumps(payload, ensure_ascii=False, indent=2))
    else:
        print_text_report(reports)

    return 0 if all(report.passed for report in reports) else 1


if __name__ == "__main__":
    sys.exit(main())


# endregion
