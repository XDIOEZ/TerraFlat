"""生成与 Q 版骑乘锚点匹配的低座自行车车架、曲柄和物品图标。"""

from pathlib import Path

from PIL import Image, ImageDraw


PROJECT_ROOT = Path(__file__).resolve().parents[2]
ART = PROJECT_ROOT / "Assets/6_Art/Generated/Vehicles/Bicycle"
WIDTH, HEIGHT, PIXEL = 704, 512, 4
INK = "#263235"
SHADOW = "#315257"
FRAME = "#4b8582"
LIGHT = "#8ab0a1"
STEEL_SHADOW = "#515e61"
STEEL = "#a3aeaa"
STEEL_LIGHT = "#d4d0be"
LEATHER_DARK = "#5e3c34"
LEATHER = "#a06b4b"
LEATHER_LIGHT = "#d39a70"
ACCENT = "#bd7859"

# 以下均为 Unity 单位，以车架画布中央为 (0,0)；画布和 PPU 保持旧资源值。
REAR_HUB = (-0.625, -0.29)
FRONT_HUB = (0.625, -0.29)
CRANK = (-0.09, -0.09)
SEAT = (-0.16, 0.32)
HEAD = (0.40, 0.23)
WHEEL_VISUAL_SCALE = 0.90


def point(xy):
    x, y = xy
    return (round((WIDTH / 2 + x * 320) / PIXEL),
            round((HEIGHT / 2 - y * 320) / PIXEL))


def tube(draw, start, end, *, highlight=True):
    a, b = point(start), point(end)
    draw.line([a, b], fill=INK, width=6)
    draw.line([a, b], fill=FRAME, width=4)
    if highlight:
        draw.line([(a[0], a[1] - 1), (b[0], b[1] - 1)], fill=LIGHT, width=1)


def metal(draw, start, end, width=4):
    a, b = point(start), point(end)
    draw.line([a, b], fill=INK, width=width + 2)
    draw.line([a, b], fill=STEEL, width=width)
    draw.line([(a[0] - 1, a[1] - 1), (b[0] - 1, b[1] - 1)], fill=STEEL_LIGHT, width=1)


def frame():
    image = Image.new("RGBA", (WIDTH // PIXEL, HEIGHT // PIXEL))
    d = ImageDraw.Draw(image)

    # 低跨梁与短轴距的实用小车；先画后方三角架，再画前叉和车把。
    tube(d, REAR_HUB, (-0.19, 0.20))
    tube(d, REAR_HUB, CRANK, highlight=False)
    tube(d, (-0.19, 0.20), CRANK)
    tube(d, (-0.19, 0.20), HEAD)
    tube(d, CRANK, HEAD)
    tube(d, HEAD, (0.55, -0.05))
    tube(d, (0.55, -0.05), FRONT_HUB)
    # 两端小银色轮轴、下部链条护罩以及车架上的一抹暖红点缀。
    for hub in (REAR_HUB, FRONT_HUB):
        x, y = point(hub)
        d.ellipse((x - 3, y - 3, x + 3, y + 3), fill=INK)
        d.rectangle((x - 1, y - 1, x + 1, y + 1), fill=STEEL_LIGHT)
    a, b = point((-0.51, -0.245)), point((-0.09, -0.12))
    d.line((a, b), fill=STEEL_SHADOW, width=2)
    metal(d, (-0.18, 0.20), (-0.16, 0.32), 3)
    metal(d, (0.40, 0.23), (0.36, 0.40), 3)
    metal(d, (0.36, 0.40), (0.56, 0.46), 3)

    # 坐垫压低到 Q 版角色髋部，轮廓宽厚且后缘略翘。
    sx, sy = point(SEAT)
    d.polygon([(sx - 14, sy - 1), (sx - 11, sy - 5), (sx + 7, sy - 5),
               (sx + 12, sy - 2), (sx + 8, sy + 3), (sx - 12, sy + 3)],
              fill=LEATHER_DARK)
    d.polygon([(sx - 12, sy - 3), (sx - 8, sy - 6), (sx + 7, sy - 6),
               (sx + 10, sy - 3), (sx + 4, sy), (sx - 10, sy)], fill=LEATHER)
    d.line((sx - 8, sy - 5, sx + 4, sy - 5), fill=LEATHER_LIGHT, width=2)

    # 小而上扬的握把，保证玩家手能靠近；不用竞技公路车的高车架。
    hx, hy = point((0.56, 0.46))
    d.line((hx - 4, hy - 2, hx + 5, hy - 2), fill=INK, width=5)
    d.line((hx - 3, hy - 3, hx + 4, hy - 3), fill=LEATHER_DARK, width=3)
    d.line((hx - 3, hy - 4, hx + 3, hy - 4), fill=LEATHER_LIGHT, width=1)

    # 主梁局部斜置彩漆；轴心固定、踏板臂留给独立旋转层。
    a, b = point((0.08, 0.18)), point((0.20, 0.19))
    d.line((a, b), fill=ACCENT, width=2)
    cx, cy = point(CRANK)
    d.ellipse((cx - 7, cy - 7, cx + 7, cy + 7), fill=INK)
    d.ellipse((cx - 5, cy - 5, cx + 5, cy + 5), fill=STEEL_SHADOW)
    d.ellipse((cx - 3, cy - 3, cx + 3, cy + 3), outline=STEEL, width=2)
    return image.resize((WIDTH, HEIGHT), Image.Resampling.NEAREST)


def crank():
    # 中心在曲柄轴：两个踏板朝相反方向，随真实车轮位移同步转动。
    image = Image.new("RGBA", (160 // PIXEL, 160 // PIXEL))
    d = ImageDraw.Draw(image)
    center = (20, 20)
    tips = ((13, 9), (27, 31))
    for tip in tips:
        d.line((center, tip), fill=INK, width=5)
        d.line((center, tip), fill=STEEL, width=3)
        x, y = tip
        d.rectangle((x - 6, y - 2, x + 6, y + 2), fill=INK)
        d.rectangle((x - 4, y - 2, x + 4, y), fill=LEATHER_DARK)
        d.line((x - 3, y - 2, x + 3, y - 2), fill=LEATHER_LIGHT, width=1)
    d.ellipse((17, 17, 23, 23), fill=INK)
    d.ellipse((18, 18, 22, 22), fill=STEEL_LIGHT)
    return image.resize((160, 160), Image.Resampling.NEAREST)


def icon(frame_sprite, crank_sprite):
    canvas = Image.new("RGBA", (WIDTH, HEIGHT))
    wheel = Image.open(ART / "Bicycle_Wheel.png").convert("RGBA")
    diameter = round(wheel.width * 320 / 500 * WHEEL_VISUAL_SCALE)
    wheel = wheel.resize((diameter, diameter), Image.Resampling.NEAREST)
    for center in (REAR_HUB, FRONT_HUB):
        cx, cy = point(center)
        cx, cy = cx * PIXEL, cy * PIXEL
        canvas.alpha_composite(wheel, (cx - diameter // 2, cy - diameter // 2))
    canvas.alpha_composite(frame_sprite)
    cx, cy = point(CRANK)
    canvas.alpha_composite(crank_sprite, (cx * PIXEL - 80, cy * PIXEL - 80))
    return canvas


def main():
    frame_sprite, crank_sprite = frame(), crank()
    frame_sprite.save(ART / "Bicycle_Frame.png")
    crank_sprite.save(ART / "Bicycle_Crank.png")
    icon(frame_sprite, crank_sprite).save(ART / "Bicycle_Icon.png")
    print("Bicycle: lowered seat, compact frame, independent crank; artwork exported.")


if __name__ == "__main__":
    main()
