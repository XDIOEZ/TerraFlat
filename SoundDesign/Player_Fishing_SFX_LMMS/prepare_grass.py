"""整理 CC0 草地实录为 LMMS 采样，声音由 LMMS MCP 编排渲染。"""

from __future__ import annotations

import hashlib
import json
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
LOCAL_PACKAGES = PROJECT / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np
import soundfile as sf
from scipy.ndimage import gaussian_filter1d, maximum_filter1d
from scipy.signal import butter, sosfilt


# region 草地实录裁切与增益整理
def prepare():
    source = json.loads((ROOT / "grass_sources.json").read_text(encoding="utf-8"))
    assert source["license"] == "CC0-1.0"
    settings = source["processing"]
    cache = PROJECT / ".codex-tmp/grass-source.mp3"
    if not cache.exists():
        cache.parent.mkdir(parents=True, exist_ok=True)
        request = urllib.request.Request(source["downloadUrl"], headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(request, timeout=30) as response:
            cache.write_bytes(response.read())
    assert hashlib.sha256(cache.read_bytes()).hexdigest() == source["sha256"]
    raw, rate = sf.read(cache, always_2d=True)
    assert rate == settings["sampleRate"]
    raw = raw.mean(axis=1)
    (ROOT / "Sources").mkdir(exist_ok=True)
    for segment in source["segments"]:
        start = round(segment["startSeconds"] * rate)
        audio = raw[start:start + round(segment["seconds"] * rate)].copy()
        assert len(audio) == round(segment["seconds"] * rate)
        audio = sosfilt(butter(2, settings["highpassHz"], fs=rate, btype="highpass", output="sos"), audio)
        audio = sosfilt(butter(2, settings["lowpassHz"], fs=rate, btype="lowpass", output="sos"), audio)
        audio -= audio.mean()
        # 轻压鞋底的孤立重击，让草叶摩擦在较小音量下仍有细节。
        threshold = max(float(np.percentile(np.abs(audio), settings["transientPercentile"])), 1e-6)
        envelope = maximum_filter1d(np.abs(audio), size=round(.004 * rate))
        gain = np.minimum(1, (np.maximum(envelope, threshold) / threshold) **
                          (1 / settings["transientRatio"] - 1))
        audio *= gaussian_filter1d(gain, sigma=.002 * rate)
        head, tail = (round(settings[key] * rate) for key in ("headFadeSeconds", "tailFadeSeconds"))
        audio[:head] *= np.linspace(0, 1, head)
        audio[-tail:] *= np.linspace(1, 0, tail)
        assert np.isfinite(audio).all() and np.max(np.abs(audio)) > .0001
        audio *= settings["peak"] / np.max(np.abs(audio))
        path = ROOT / "Sources" / segment["file"]
        sf.write(path, audio, rate, subtype="PCM_16")
        print(f"Prepared {path.name}: {len(audio) / rate:.2f}s")
# endregion


if __name__ == "__main__":
    prepare()
