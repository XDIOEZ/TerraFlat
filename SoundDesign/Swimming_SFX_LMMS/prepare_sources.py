"""把已记录许可的真实水声裁成 LMMS 采样，不合成替代水声。"""

from __future__ import annotations

import hashlib
import json
import math
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
CACHE = PROJECT / ".codex-tmp/swimming-sources"
LOCAL_PACKAGES = PROJECT / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np
import soundfile as sf
from scipy.ndimage import gaussian_filter1d, maximum_filter1d
from scipy.signal import butter, resample_poly, sosfilt


# region 真实录音来源与采样整理
def prepare():
    manifest = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    assert manifest["license"] == "CC0-1.0"
    settings = manifest["processing"]
    target_rate = settings["sampleRate"]
    CACHE.mkdir(parents=True, exist_ok=True)
    decoded = {}
    for name, source in manifest["recordings"].items():
        cached = CACHE / f"{source['soundId']}.mp3"
        if not cached.exists():
            request = urllib.request.Request(source["previewUrl"], headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(request, timeout=30) as response:
                cached.write_bytes(response.read())
        assert hashlib.sha256(cached.read_bytes()).hexdigest() == source["previewSha256"], cached
        data, rate = sf.read(cached, always_2d=True)
        decoded[name] = (data.mean(axis=1), rate)

    for segment in manifest["segments"]:
        raw, rate = decoded[segment["recording"]]
        start = round(segment["startSeconds"] * rate)
        data = raw[start:start + round(segment["seconds"] * rate)].copy()
        assert len(data) == round(segment["seconds"] * rate)
        if rate != target_rate:
            divisor = math.gcd(rate, target_rate)
            data = resample_poly(data, target_rate // divisor, rate // divisor)
        data = data[:round(segment["seconds"] * target_rate)]
        data = sosfilt(butter(2, settings["highpassHz"], fs=target_rate, btype="highpass", output="sos"), data)
        data = sosfilt(butter(2, settings["lowpassHz"], fs=target_rate, btype="lowpass", output="sos"), data)
        data -= data.mean()
        # 只整理孤立尖峰的增益，避免整段水流被几毫秒的冲击压到听不清。
        threshold = max(float(np.percentile(np.abs(data), settings["transientPercentile"])), 1e-6)
        envelope = maximum_filter1d(np.abs(data), size=max(3, round(settings["lookaheadMilliseconds"] * target_rate / 1000)))
        gain = np.minimum(1, (np.maximum(envelope, threshold) / threshold) ** (1 / settings["transientRatio"] - 1))
        gain = gaussian_filter1d(gain, sigma=max(1, settings["gainSmoothingMilliseconds"] * target_rate / 1000))
        data *= gain
        # 只淡化裁切边缘，保留实录水花的宽频冲击、水流主体和自然水滴。
        head = round(settings["headFadeSeconds"] * target_rate)
        tail = round(settings["tailFadeSeconds"] * target_rate)
        data[:head] *= np.linspace(0, 1, head)
        data[-tail:] *= np.linspace(1, 0, tail)
        assert np.isfinite(data).all() and np.max(np.abs(data)) > .0001
        data *= settings["peak"] / np.max(np.abs(data))
        path = ROOT / segment["file"]
        assert path.resolve().parent == (ROOT / "Sources").resolve()
        path.parent.mkdir(exist_ok=True)
        sf.write(path, data, target_rate, subtype="PCM_16")
        print(f"Prepared {path.name}: {len(data) / target_rate:.3f}s from {segment['recording']}")
# endregion


if __name__ == "__main__":
    prepare()
