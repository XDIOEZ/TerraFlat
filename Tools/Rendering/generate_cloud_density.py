"""生成写实二维云数据图：R 云团场、G 团内起伏、B 细节侵蚀、A 大尺度天气场。"""
import argparse
from pathlib import Path

import numpy as np
from PIL import Image


#region 周期分形噪声
def perlin(x: np.ndarray, y: np.ndarray, period: tuple[int, int], seed: int) -> np.ndarray:
    # 五次插值与周期梯度保证跨贴图边界的形状和斜率连续。
    rng = np.random.default_rng(seed)
    px, py = period
    angles = rng.uniform(0, np.pi * 2, (py, px))
    gx, gy = np.cos(angles), np.sin(angles)
    sx, sy = x * px, y * py
    ix, iy = np.floor(sx).astype(int), np.floor(sy).astype(int)
    tx, ty = sx - ix, sy - iy
    ux = tx ** 3 * (tx * (tx * 6 - 15) + 10)
    uy = ty ** 3 * (ty * (ty * 6 - 15) + 10)

    def corner(dx: int, dy: int) -> np.ndarray:
        return gx[(iy + dy) % py, (ix + dx) % px] * (tx - dx) + gy[
            (iy + dy) % py, (ix + dx) % px
        ] * (ty - dy)

    lower = corner(0, 0) * (1 - ux) + corner(1, 0) * ux
    upper = corner(0, 1) * (1 - ux) + corner(1, 1) * ux
    return lower * (1 - uy) + upper * uy


def fbm(x: np.ndarray, y: np.ndarray, period: tuple[int, int], seed: int, octaves: int = 6) -> np.ndarray:
    value = np.zeros_like(x)
    amplitude, total = 1.0, 0.0
    for octave in range(octaves):
        value += amplitude * perlin(x, y, (period[0] * 2 ** octave, period[1] * 2 ** octave), seed + octave * 7919)
        total += amplitude
        amplitude *= 0.52
    return value / total


def worley(x: np.ndarray, y: np.ndarray, period: tuple[int, int], seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    px, py = period
    features = rng.uniform(0.05, 0.95, (py, px, 2))
    sx, sy = x * px, y * py
    ix, iy = np.floor(sx).astype(int), np.floor(sy).astype(int)
    tx, ty = sx - ix, sy - iy
    nearest = np.full_like(x, 10.0)
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            feature = features[(iy + dy) % py, (ix + dx) % px]
            distance = (dx + feature[..., 0] - tx) ** 2 + (dy + feature[..., 1] - ty) ** 2
            nearest = np.minimum(nearest, distance)
    return np.clip(1.0 - np.sqrt(nearest), 0, 1)
#endregion


#region 云团场生成
def build_density(size: int = 1024) -> np.ndarray:
    yy, xx = (np.mgrid[:size, :size] + 0.5) / size
    # 连续域扭曲改变整个云团轮廓，不给每朵云套圆形或椭圆外框。
    warp_x = fbm(xx, yy, (3, 3), 1171, 4)
    warp_y = fbm(xx, yy, (3, 3), 8053, 4)
    x, y = xx + warp_x * 0.19, yy + warp_y * 0.19
    macro = fbm(x, y, (5, 7), 20261013)
    weather = fbm(xx, yy, (2, 3), 31277, 4)
    billows = worley(x, y, (23, 29), 71527)
    detail = fbm(x, y, (27, 39), 92003, 4)
    # 大尺度空域切开云片，细胞起伏形成积云的小团块，细噪声修饰薄边。
    shape = np.clip(0.5 + macro * 1.1 + weather * 0.45 + (billows - 0.52) * 0.11, 0, 1)
    relief = np.clip(billows * 0.62 + 0.22 + detail * 0.25, 0, 1)
    erosion = np.clip(detail * 1.2 + 0.5, 0, 1)
    weather = np.clip(weather * 1.2 + 0.5, 0, 1)
    packed = np.stack((shape, relief, erosion, weather), axis=-1)
    return np.rint(packed * 255).astype(np.uint8)
#endregion


#region 离线材质预览
def preview_density(packed: np.ndarray, destination: Path) -> None:
    # 按生产 Shader 的密度和透光公式生成材质草图，便于作者观察数据图而不启动游戏。
    height, width = 540, 960
    yy, xx = np.mgrid[:height, :width]
    world_x = (xx / width - 0.5) * 213.333
    world_y = (0.5 - yy / height) * 120
    texture = packed.astype(np.float64) / 255

    def sample(u: np.ndarray, v: np.ndarray) -> np.ndarray:
        tx, ty = u * packed.shape[1] - 0.5, v * packed.shape[0] - 0.5
        ix, iy = np.floor(tx).astype(int), np.floor(ty).astype(int)
        fx, fy = (tx - ix)[..., None], (ty - iy)[..., None]
        def tex(dx: int, dy: int) -> np.ndarray:
            return texture[(iy + dy) % packed.shape[0], (ix + dx) % packed.shape[1]]
        lower = tex(0, 0) * (1 - fx) + tex(1, 0) * fx
        upper = tex(0, 1) * (1 - fx) + tex(1, 1) * fx
        return lower * (1 - fy) + upper * fy

    def density(data: np.ndarray, detail: np.ndarray) -> np.ndarray:
        body = np.clip((data[..., 0] - 0.49) * 4.2, 0, 1)
        return np.maximum(0, body - np.maximum(0, detail - 0.38) * 0.17 * (1 - body))

    u, v = world_x * 0.055 * 0.07, world_y * 0.055 * 0.085
    data = sample(u, v)
    detail = sample(world_x * 0.055 * 0.157 + 0.173, world_y * 0.055 * 0.183 + 0.619)[..., 2]
    thick = density(data, detail)
    near = sample(u - 0.006, v + 0.009)
    far = sample(u - 0.006 * 2.7, v + 0.009 * 2.7)
    light_depth = np.maximum(0, density(near, detail) - thick * 0.55) * 2.2 + density(far, detail) * 0.65 + thick * 0.25
    illumination = 0.28 + 0.72 * np.exp2(-light_depth * 0.55 * 4.5)
    relief = np.clip(0.72 + (data[..., 1] - near[..., 1]) * 1.4, 0, 1)
    bright = np.array([0.98, 0.99, 1.0])
    shadow = bright * np.array([0.55, 0.64, 0.75])
    color = (shadow + (bright - shadow) * illumination[..., None]) * (0.91 + relief[..., None] * 0.09)
    rim = np.clip(thick / 0.12, 0, 1)
    rim = rim * rim * (3 - 2 * rim)
    color += ((1 - thick) ** 2 * rim * 0.06)[..., None]
    radial = np.clip((np.hypot(world_x, world_y) - 8) / 20, 0, 1)
    radial = radial * radial * (3 - 2 * radial)
    alpha = radial * (1 - np.exp2(-thick * (1.5 + data[..., 1] * 2) * 2))
    background = np.full((height, width, 3), [0.16, 0.34, 0.47])
    composite = background * (1 - alpha[..., None]) + color * alpha[..., None]
    # 数据及光照在线性空间计算，显示图最后才转换为 sRGB。
    composite = np.where(composite <= 0.0031308, composite * 12.92, 1.055 * np.maximum(composite, 0) ** (1 / 2.4) - 0.055)
    Image.fromarray(np.rint(np.clip(composite, 0, 1) * 255).astype(np.uint8)).save(destination)
    print(f"离线材质预览: {destination}")
#endregion


#region 资源输出
if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--preview", type=Path, help="额外输出离线材质草图 PNG")
    args = parser.parse_args()
    project = Path(__file__).resolve().parents[2]
    destination = project / "Assets/9_Shaders/Resources/Shaders/CloudDensity_RGBA.png"
    packed = build_density()
    Image.fromarray(np.flipud(packed)).save(destination, optimize=True)
    print(destination)
    if args.preview:
        preview_density(packed, args.preview)
#endregion
