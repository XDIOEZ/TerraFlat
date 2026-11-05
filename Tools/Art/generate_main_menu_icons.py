"""生成主菜单的同源 SVG 图标及供 Unity uGUI 使用的透明 PNG。"""

from __future__ import annotations

from math import cos, pi, sin
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2] / "Assets/6_Art/UI/MainMenuIcons"
SVG_SIZE = 24
PNG_SIZE = 128
RENDER_SCALE = 8
STROKE_WIDTH = 1.9


def circle(x: float, y: float, radius: float, steps: int = 64):
    return [(x + radius * cos(i * 2 * pi / steps), y + radius * sin(i * 2 * pi / steps))
            for i in range(steps + 1)]


def arc(x: float, y: float, rx: float, ry: float, start: float, end: float, steps: int = 24):
    return [(x + rx * cos(start + (end - start) * i / steps),
             y + ry * sin(start + (end - start) * i / steps))
            for i in range(steps + 1)]


def gear():
    points = []
    for i in range(8):
        angle = i * pi / 4
        for offset, radius in [(-0.29, 7.6), (-0.20, 10), (0.20, 10), (0.29, 7.6)]:
            points.append((12 + radius * cos(angle + offset),
                           12 + radius * sin(angle + offset)))
    return points + [points[0]]


def puzzle():
    points = [(4, 4), (9, 4), (9, 5.5)]
    points.extend(arc(11, 5.5, 2, 2, pi, 2 * pi)[1:])
    points.extend([(13, 4), (20, 4), (20, 10), (18.5, 10)])
    points.extend(arc(18.5, 12, 2, 2, -pi / 2, -3 * pi / 2)[1:])
    points.extend([(20, 14), (20, 20), (14, 20), (14, 18.5)])
    points.extend(arc(12, 18.5, 2, 2, 0, -pi)[1:])
    points.extend([(10, 20), (4, 20), (4, 4)])
    return points


ICONS = {
    "continue": [[(8, 5), (19, 12), (8, 19), (8, 5)]],
    "new-world": [
        circle(12, 12, 9),
        [(3, 12), (21, 12)],
        [(12 - 3.5 * sin(i * pi / 32), 12 - 9 * cos(i * pi / 32)) for i in range(33)],
        [(12 + 3.5 * sin(i * pi / 32), 12 - 9 * cos(i * pi / 32)) for i in range(33)],
    ],
    "multiplayer": [
        circle(9, 8, 3),
        circle(17, 8, 2.5),
        arc(9, 20, 5.5, 5.1, pi, 2 * pi),
        arc(17, 20, 4.5, 5, pi * 1.15, pi * 2),
    ],
    "mods": [puzzle()],
    "settings": [gear(), circle(12, 12, 3.2)],
}


def svg_content(lines):
    shapes = "\n".join(
        '  <polyline points="{}" />'.format(
            " ".join(f"{x:.3f},{y:.3f}" for x, y in points)
        )
        for points in lines
    )
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" '
        'width="24" height="24" fill="none" stroke="#ffffff" '
        'stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">\n'
        f"{shapes}\n</svg>\n"
    )


def write_png(path: Path, lines):
    image = Image.new("RGBA", (SVG_SIZE * RENDER_SCALE,) * 2, (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    width = round(STROKE_WIDTH * RENDER_SCALE)
    radius = width / 2
    for points in lines:
        scaled = [(x * RENDER_SCALE, y * RENDER_SCALE) for x, y in points]
        draw.line(scaled, fill="white", width=width, joint="curve")
        for x, y in scaled:
            draw.ellipse((round(x - radius), round(y - radius),
                          round(x + radius), round(y + radius)), fill="white")
    image.resize((PNG_SIZE, PNG_SIZE), Image.Resampling.LANCZOS).save(path, optimize=True)


def main():
    ROOT.mkdir(parents=True, exist_ok=True)
    for name, lines in ICONS.items():
        (ROOT / f"{name}.svg").write_text(svg_content(lines), encoding="utf-8")
        write_png(ROOT / f"{name}.png", lines)
        print(f"{name}: SVG + PNG")


if __name__ == "__main__":
    main()
