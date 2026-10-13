"""生成 Unity 风格水的可平铺数据贴图；全部图案由本脚本生成。"""
from pathlib import Path
import argparse
import json

import numpy as np
from PIL import Image


def periodic_field(size, seed, minimum_frequency, maximum_frequency, components):
    rng = np.random.default_rng(seed)
    axis = np.arange(size, dtype=np.float64) / size
    x, y = np.meshgrid(axis, axis)
    field = np.zeros((size, size), dtype=np.float64)
    for _ in range(components):
        frequency_x, frequency_y = rng.integers(-maximum_frequency, maximum_frequency + 1, 2)
        if abs(frequency_x) + abs(frequency_y) < minimum_frequency:
            frequency_x += minimum_frequency
        amplitude = rng.uniform(0.5, 1.0) / max(np.hypot(frequency_x, frequency_y), 1.0)
        field += amplitude * np.sin(2 * np.pi * (frequency_x * x + frequency_y * y) + rng.uniform(0, 2 * np.pi))
    field -= field.mean()
    return field / max(field.std(), 1e-8)


def smoothstep(low, high, value):
    t = np.clip((value - low) / (high - low), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def encode_normals(height, strength):
    # 周期中心差分与 Shader 的世界 XY 方向一致。
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5
    gradient_rms = np.sqrt(np.mean(dx * dx + dy * dy))
    nx, ny = -dx * strength / gradient_rms, -dy * strength / gradient_rms
    inverse_length = 1.0 / np.sqrt(nx * nx + ny * ny + 1.0)
    return nx * inverse_length * 0.5 + 0.5, ny * inverse_length * 0.5 + 0.5


def generate_pattern(size=256):
    broad = periodic_field(size, 831701, 2, 9, 36)
    detail = periodic_field(size, 250313, 8, 22, 40)
    height = broad * 0.78 + detail * 0.22
    red, green = encode_normals(height, 0.68)
    foam_field = periodic_field(size, 139613, 2, 11, 34)
    foam = smoothstep(-0.2, 1.1, foam_field + detail * 0.12)
    shallow_light = smoothstep(-1.6, 1.7, broad * 0.6 + detail * 0.4)
    return np.stack((red, green, foam, shallow_light), axis=-1)


def generate_detail(size=128):
    height = periodic_field(size, 719017, 3, 16, 56)
    red, green = encode_normals(height, 1.25)
    return np.stack((red, green, np.ones_like(red), np.ones_like(red)), axis=-1)


def save_pattern(path, pattern):
    path.parent.mkdir(parents=True, exist_ok=True)
    # PNG 从上向下排列，翻转后保持法线方向与 Unity UV 一致。
    pixels = np.uint8(np.rint(np.clip(np.flipud(pattern), 0, 1) * 255))
    Image.fromarray(pixels).save(path)
    return {"path": str(path), "size": list(pixels.shape[:2]), "channelMean": pattern.mean(axis=(0, 1)).tolist()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "Assets/9_Shaders/Textures")
    args = parser.parse_args()
    reports = [save_pattern(args.output / "UnityStylizedWaterPattern_RGBA.png", generate_pattern()),
               save_pattern(args.output / "UnityStylizedWaterDetail_RGBA.png", generate_detail())]
    print(json.dumps(reports, ensure_ascii=True, indent=2))


if __name__ == "__main__":
    main()
