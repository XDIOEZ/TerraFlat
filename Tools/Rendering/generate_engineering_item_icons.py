"""生成工程护目镜和隐藏电线的硬边像素图标。"""
from pathlib import Path
import struct
import zlib

ROOT = Path(__file__).resolve().parents[2]
SIZE = 32
OUTLINE = (39, 48, 52, 255)


def rect(pixels, x0, y0, x1, y1, color):
    for y in range(max(0, y0), min(SIZE, y1 + 1)):
        for x in range(max(0, x0), min(SIZE, x1 + 1)):
            pixels[y][x] = color


def ellipse(pixels, x0, y0, x1, y1, color):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    rx, ry = (x1 - x0 + 1) / 2, (y1 - y0 + 1) / 2
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1:
                pixels[y][x] = color


def write_png(name, pixels):
    # 每个逻辑像素扩大两倍，保留清楚的像素簇和透明背景。
    rows = []
    for row in pixels:
        data = b''.join(bytes(color) * 2 for color in row)
        rows.extend([b'\0' + data] * 2)
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))
    path = ROOT / 'Assets/6_Art/Generated/Electrical' / name / f'{name}_Icon.png'
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 64, 64, 8, 6, 0, 0, 0)) +
                     chunk(b'IDAT', zlib.compress(b''.join(rows), 9)) + chunk(b'IEND', b''))


def goggles():
    p = [[(0, 0, 0, 0)] * SIZE for _ in range(SIZE)]
    rect(p, 1, 12, 30, 19, OUTLINE)
    rect(p, 2, 13, 29, 17, (111, 80, 57, 255))
    rect(p, 2, 13, 29, 14, (177, 135, 82, 255))
    for left in (4, 18):
        rect(p, left + 1, 9, left + 8, 22, OUTLINE)
        rect(p, left, 10, left + 9, 21, OUTLINE)
        rect(p, left + 1, 10, left + 8, 20, (174, 130, 73, 255))
        rect(p, left + 2, 10, left + 7, 11, (231, 192, 113, 255))
        rect(p, left + 2, 12, left + 7, 19, (45, 103, 114, 255))
        rect(p, left + 2, 12, left + 7, 15, (73, 153, 164, 255))
        rect(p, left + 2, 12, left + 3, 15, (174, 229, 224, 255))
        rect(p, left + 3, 12, left + 5, 13, (174, 229, 224, 255))
        rect(p, left + 2, 20, left + 7, 20, (111, 79, 49, 255))
    rect(p, 14, 13, 17, 16, OUTLINE)
    rect(p, 14, 13, 17, 14, (198, 156, 91, 255))
    return p


def hidden_wire():
    p = [[(0, 0, 0, 0)] * SIZE for _ in range(SIZE)]
    ellipse(p, 4, 5, 26, 26, OUTLINE)
    ellipse(p, 6, 6, 24, 24, (80, 93, 98, 255))
    ellipse(p, 8, 8, 22, 22, (40, 51, 58, 255))
    ellipse(p, 10, 10, 20, 20, (100, 119, 125, 255))
    ellipse(p, 12, 12, 18, 18, OUTLINE)
    ellipse(p, 13, 13, 17, 17, (0, 0, 0, 0))
    rect(p, 7, 7, 12, 9, (159, 179, 179, 255))
    rect(p, 6, 10, 8, 14, (159, 179, 179, 255))
    rect(p, 23, 19, 26, 24, OUTLINE)
    rect(p, 25, 23, 29, 26, OUTLINE)
    rect(p, 26, 24, 29, 25, (187, 120, 65, 255))
    rect(p, 13, 5, 17, 8, (74, 178, 187, 255))
    rect(p, 13, 5, 17, 5, (162, 234, 228, 255))
    return p


if __name__ == '__main__':
    write_png('Engineering_Goggles', goggles())
    write_png('Wire_Hidden', hidden_wire())
