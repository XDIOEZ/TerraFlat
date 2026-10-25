"""按来源账整理连续雨声实录，供 LMMS 采样轨重新渲染。"""

import hashlib
import json
import math
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
from scipy.signal import butter, resample_poly, sosfiltfilt

from process_weather import write_wav


# region 来源校验与实录整理
def prepare():
    source = json.loads((ROOT / "rain_sources.json").read_text(encoding="utf-8"))
    assert source["license"] == "CC0-1.0"
    cached = PROJECT / ".codex/tmp/rain-audio/167034.mp3"
    cached.parent.mkdir(parents=True, exist_ok=True)
    if not cached.exists():
        request = urllib.request.Request(source["previewUrl"], headers={"User-Agent": "Mozilla/5.0"})
        try:
            response = urllib.request.urlopen(request, timeout=8)
        except OSError:
            response = urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request, timeout=8)
        with response:
            downloaded = response.read()
        assert hashlib.sha256(downloaded).hexdigest() == source["previewSha256"]
        cached.write_bytes(downloaded)
    assert hashlib.sha256(cached.read_bytes()).hexdigest() == source["previewSha256"]
    data, rate = sf.read(cached, always_2d=True)
    settings = source["processing"]
    assert data.shape[1] == settings["channels"] and np.isfinite(data).all()
    divisor = math.gcd(rate, settings["sampleRate"])
    data = resample_poly(data, settings["sampleRate"] // divisor, rate // divisor, axis=0)
    # 只去掉低频轰鸣与过尖高频，保留原录音连续的雨幕和雨点。
    for kind, cutoff in (("highpass", settings["highpassHz"]), ("lowpass", settings["lowpassHz"])):
        data = sosfiltfilt(butter(2, cutoff, fs=settings["sampleRate"], btype=kind, output="sos"), data, axis=0)
    data -= data.mean(axis=0)
    data *= settings["sourcePeak"] / np.max(np.abs(data))
    assert len(data) / settings["sampleRate"] > settings["loopEndSeconds"]
    target = ROOT / source["file"]
    target.parent.mkdir(exist_ok=True)
    write_wav(target, data)
    print(f"Prepared {target.name}: {len(data) / settings['sampleRate']:.3f}s")
# endregion


if __name__ == "__main__":
    prepare()
