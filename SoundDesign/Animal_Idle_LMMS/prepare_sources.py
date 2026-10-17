"""把动物实录裁成不同叫声片段，脚本不合成或改音高。"""

from __future__ import annotations

import hashlib
import json
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
LOCAL_PACKAGES = PROJECT / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np
import soundfile as sf
from scipy.signal import butter, resample_poly, sosfilt

RATE = 48000


# region 实录选段与自然增益整理
def prepare():
    path = ROOT / "sources.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    decoded = {}
    for name, source in manifest["recordings"].items():
        original = ROOT / source["sourceFile"]
        assert hashlib.sha256(original.read_bytes()).hexdigest() == source["sha256"], original
        data, rate = sf.read(original, always_2d=True)
        decoded[name] = (data.mean(axis=1), rate)
    for segment in manifest["segments"]:
        data, rate = decoded[segment["recording"]]
        start = round(segment["startSeconds"] * rate)
        count = round(segment["seconds"] * rate)
        data = data[start:start + count].copy()
        assert len(data) == count, segment
        if rate != RATE:
            divisor = math.gcd(rate, RATE)
            data = resample_poly(data, RATE // divisor, rate // divisor)
        target_count = round(segment["seconds"] * RATE)
        data = data[:target_count]
        if len(data) < target_count:
            data = np.pad(data, (0, target_count - len(data)))
        data = sosfilt(butter(2, segment.get("highpassHz", 90), fs=RATE, btype="highpass", output="sos"), data)
        data = sosfilt(butter(2, segment.get("lowpassHz", 12000), fs=RATE, btype="lowpass", output="sos"), data)
        data -= data.mean()
        # 只淡化截取的边缘，保留不同次叫声原有的节奏和音色。
        head = round(segment.get("headFadeSeconds", .008) * RATE)
        tail = round(segment.get("tailFadeSeconds", .08) * RATE)
        data[:head] *= np.linspace(0, 1, head)
        data[-tail:] *= np.linspace(1, 0, tail)
        peak = np.max(np.abs(data))
        assert peak > .0001 and np.isfinite(data).all(), segment
        data *= .6 / peak
        destination = ROOT / segment["file"]
        assert destination.resolve().parent == (ROOT / "Sources").resolve()
        destination.parent.mkdir(exist_ok=True)
        sf.write(destination, data, RATE, subtype="PCM_16")
        segment["sourceSha256"] = hashlib.sha256(destination.read_bytes()).hexdigest()
        print(f"Prepared {destination.name}: {len(data) / RATE:.3f}s from {segment['recording']}")
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
# endregion


if __name__ == "__main__":
    prepare()
