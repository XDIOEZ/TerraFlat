"""生成河面线性 RGBA 数据图：RG 斜率、B 柔和碎光、A 稀疏顺流泡沫。"""
from pathlib import Path

import numpy as np
from PIL import Image


#region 水面数据图
def build_pattern(size: int = 256) -> np.ndarray:
    # 整数周期保证边缘无缝，短波形状直接画亮片，不对噪声等值线描边。
    rng = np.random.default_rng(20261013)
    yy, xx = np.mgrid[:size, :size] / size
    height = np.zeros((size, size), dtype=np.float64)
    slope_x = np.zeros_like(height)
    slope_y = np.zeros_like(height)
    for frequency_x, frequency_y, amplitude in (
        (5, 1, 0.48), (9, -2, 0.27), (13, 3, 0.16), (21, -4, 0.09)
    ):
        phase = 2 * np.pi * (frequency_x * xx + frequency_y * yy) + rng.uniform(0, 2 * np.pi)
        height += np.sin(phase) * amplitude
        slope_x += np.cos(phase) * amplitude * frequency_x
        slope_y += np.cos(phase) * amplitude * frequency_y
    peak_slope = max(np.max(np.abs(slope_x)), np.max(np.abs(slope_y)))
    slope_x *= 0.72 / peak_slope
    slope_y *= 0.72 / peak_slope
    glints = np.zeros_like(height)
    foam = np.zeros_like(height)
    # 亮纹和泡沫沿局部 X 方向拉长，包裹距离让跨贴图边界的笔触连续。
    for index in range(60):
        center_x, center_y = rng.uniform(0, 1, size=2)
        width = rng.uniform(0.035, 0.095)
        thickness = rng.uniform(0.005, 0.017)
        dx = (xx - center_x + 0.5) % 1.0 - 0.5
        dy = (yy - center_y + 0.5) % 1.0 - 0.5
        ellipse = (dx / width) ** 2 + (dy / thickness) ** 2
        stroke = np.exp(-ellipse * 2.5)
        glints = np.maximum(glints, stroke * rng.uniform(0.65, 1.0))
        if index % 5 == 0:
            foam = np.maximum(foam, np.exp(-ellipse * 4.0))
    glints = np.clip(0.22 + height * 0.08 + glints * 0.72, 0, 1)
    packed = np.stack((slope_x * 0.5 + 0.5, slope_y * 0.5 + 0.5, glints, foam), axis=-1)
    return np.rint(np.clip(packed, 0, 1) * 255).astype(np.uint8)
#endregion


#region 资源输出
if __name__ == "__main__":
    project = Path(__file__).resolve().parents[2]
    destination = project / "Assets/9_Shaders/Textures/RiverPattern_RGBA.png"
    # PNG 行坐标朝下，翻转后 RG 斜率与 Shader 的 UV 朝向保持一致。
    Image.fromarray(np.flipud(build_pattern()), "RGBA").save(destination, optimize=True)
    print(destination)
#endregion
