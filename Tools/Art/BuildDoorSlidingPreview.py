from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


# 仅生成缩放展示拼图，不修改或重新编码游戏素材。
ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / "Tools/Art/DoorSliding_20261025_Preview.png"
FONT = Path("C:/Windows/Fonts/msyh.ttc")
CANVAS = Image.new("RGB", (1120, 900), "#191d22")
DRAW = ImageDraw.Draw(CANVAS)


def label(x, y, text, size=22, fill="#e1e3df"):
    DRAW.text((x, y), text, font=ImageFont.truetype(str(FONT), size), fill=fill)


def checker(x, y, width, height):
    for row in range(0, height, 16):
        for col in range(0, width, 16):
            shade = "#30363c" if (row // 16 + col // 16) % 2 else "#383e44"
            DRAW.rectangle((x + col, y + row, min(x + col + 15, x + width - 1),
                            min(y + row + 15, y + height - 1)), fill=shade)


label(36, 22, "木门、石门 · 当前方形双扇素材", 30)
label(36, 70, "右侧为切半滑移示意；并非 Unity 运行截图。原图保留 1254×1254 和原 Alpha。", 19, "#aeb8bd")
label(100, 112, "闭合 / 单格", 22)
label(590, 112, "开启示意 / 沿局部 X 向两侧滑动", 22)

for material, name, y in [("Wood", "木门", 178), ("Stone", "石门", 514)]:
    src = Image.open(ROOT / f"Assets/6_Art/Building/Door_{material}_Close.png").convert("RGBA")
    size = 256
    split = src.width // 2
    label(36, y - 12, name, 24)
    checker(100, y + 28, size, size)
    shown = src.resize((size, size), Image.Resampling.NEAREST)
    CANVAS.paste(shown, (100, y + 28), shown)

    # 与正式算法一致：完整框随半图各向外移动 0.55 格。
    open_x, open_y, open_width = 466, y + 28, 544
    checker(open_x, open_y, open_width, size)
    travel = round(size * .55)
    closed_left = open_x + (open_width - size) // 2
    left = src.crop((0, 0, split, src.height)).resize((size // 2, size), Image.Resampling.NEAREST)
    right = src.crop((split, 0, src.width, src.height)).resize((size - size // 2, size), Image.Resampling.NEAREST)
    CANVAS.paste(left, (closed_left - travel, open_y), left)
    CANVAS.paste(right, (closed_left + size // 2 + travel, open_y), right)
    label(1034, y + 125, "← →", 23, "#abb9be")

label(36, 851, "两种材质共用飞船门的方形结构；实际游戏效果由玩家在之后重新载入后查看。", 19, "#aeb8bd")
CANVAS.save(OUTPUT)
print(OUTPUT)
