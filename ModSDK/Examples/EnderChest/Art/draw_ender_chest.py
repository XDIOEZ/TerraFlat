"""把手绘的 16×16 像素矩阵导出为示例 MOD 的正式贴图。"""

from pathlib import Path
from PIL import Image


PALETTE = {
    ".": (0, 0, 0, 0),
    "K": (31, 25, 42, 255),   # 深色外轮廓
    "D": (45, 35, 63, 255),   # 背光紫
    "V": (58, 43, 78, 255),   # 正面暗面
    "M": (77, 56, 99, 255),   # 主体紫
    "L": (105, 77, 132, 255), # 顶盖亮面
    "H": (142, 110, 166, 255),# 左上高光
    "S": (29, 74, 82, 255),   # 铜绿暗边
    "T": (41, 119, 124, 255), # 铜绿包角
    "B": (70, 170, 168, 255), # 铜绿亮边
    "C": (120, 219, 201, 255),# 锁眼
}

PIXELS = [
    "................",
    "...KKKKKKKKKK...",
    "..KHHLLLLLLLDK..",
    ".KHHLLLLLLLLDDK.",
    ".KHLMMMMMMMMVDK.",
    ".KLLMMMMMMMMDVK.",
    ".KTTKKKKKKKKTTK.",
    ".KBTMMMMMMMMSBK.",
    ".KTVMMLMMMLMVSK.",
    ".KTVMMLKCKLMVSK.",
    ".KTVMMKBCCKMVSK.",
    ".KBTMMKSBSKMVTK.",
    ".KSVVVKKKKVVVSK.",
    "..KDDDDDDDDDDK..",
    "...KKKKKKKKKK...",
    "................",
]


def main():
    if len(PIXELS) != 16 or any(len(row) != 16 for row in PIXELS):
        raise ValueError("末影箱贴图必须是 16×16 像素")
    image = Image.new("RGBA", (16, 16))
    image.putdata([PALETTE[pixel] for row in PIXELS for pixel in row])
    repo = Path(__file__).resolve().parents[4]
    target = repo / "Assets/6_Art/Generated/Building/EnderChest/EnderChest_Closed.png"
    target.parent.mkdir(parents=True, exist_ok=True)
    image.save(target)
    image.resize((128, 128), Image.Resampling.NEAREST).save(
        Path(__file__).with_name("EnderChest_Preview_8x.png")
    )
    print(target)


if __name__ == "__main__":
    main()
