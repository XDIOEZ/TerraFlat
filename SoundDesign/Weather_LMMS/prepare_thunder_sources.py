"""将已记录来源和许可的公共雷声预览整理成 LMMS 单声道采样。"""

import argparse
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parent
CACHE = ROOT.parents[1] / ".codex-tmp/thunder-sources"
LOCAL_PACKAGES = ROOT.parents[1] / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np
import soundfile as sf
from scipy.signal import butter, resample_poly, sosfilt


def prepare(only=None):
    CACHE.mkdir(parents=True, exist_ok=True)
    sources = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))["sources"]
    for source in sources:
        filename = Path(source["file"]).stem
        if only and filename != only:
            continue
        assert source["license"] == "CC0-1.0"
        cached = CACHE / f'{source["soundId"]}.mp3'
        if not cached.exists():
            request = urllib.request.Request(source["previewUrl"], headers={"User-Agent": "Mozilla/5.0"})
            with urllib.request.urlopen(request, timeout=30) as response:
                cached.write_bytes(response.read())
        assert hashlib.sha256(cached.read_bytes()).hexdigest() == source["previewSha256"], cached
        data, rate = sf.read(cached)
        data = data[round(source["sourceStart"] * rate):round(source["sourceEnd"] * rate)].mean(axis=1)
        settings = source["processing"]
        assert rate in (44100, 48000) and settings["sampleRate"] == 48000
        if rate == 44100:
            data = resample_poly(data, 160, 147)
        data = sosfilt(butter(2, settings["highpassHz"], fs=48000, btype="highpass", output="sos"), data)
        data = sosfilt(butter(2, settings["lowpassHz"], fs=48000, btype="lowpass", output="sos"), data)
        # 仅淡化底噪尾部，保留真实雷声的炸响和自然回响。
        fade = round(settings["tailFadeSeconds"] * 48000)
        data[-fade:] *= np.linspace(1, 0, fade) ** 1.5
        fade = round((.004 if filename.startswith("near") else .15) * 48000)
        data[:fade] *= np.linspace(0, 1, fade)
        data -= data.mean()
        data *= .7 / np.max(np.abs(data))
        target = ROOT / source["file"]
        assert target.resolve().parent == (ROOT / "Sources").resolve()
        target.parent.mkdir(exist_ok=True)
        sf.write(target, data, 48000, subtype="PCM_16")
        print(f"Prepared {filename}: {len(data) / 48000:.3f}s")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--only")
    args = parser.parse_args()
    prepare(args.only)
