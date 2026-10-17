"""整理狂风实录和 LMMS MCP 母带，不合成或替换其他天气音效。"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
LOCAL_PACKAGES = PROJECT / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np

from process_weather import make_loop, meta, read_wav, write_wav

RATE = 48000
PROJECT_FILE = ROOT / "Weather_Wind_Gale.mmp"
RAW_FILE = ROOT / "renders/Weather_Wind_Gale_raw.wav"
OUTPUT_FILE = PROJECT / "Assets/Audio/Generated/weather.wind.gale.loop__01.wav"


# region 录音裁切与工程路径
def prepare():
    import soundfile as sf
    from scipy.signal import resample_poly

    source = json.loads((ROOT / "gale_sources.json").read_text(encoding="utf-8"))["sources"][0]
    cache = PROJECT / ".codex-tmp/gale-wind-sources/185070.mp3"
    cache.parent.mkdir(parents=True, exist_ok=True)
    if not cache.exists():
        request = urllib.request.Request(source["previewUrl"], headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(request, timeout=30) as response:
            cache.write_bytes(response.read())
    assert source["license"] == "CC0-1.0"
    assert hashlib.sha256(cache.read_bytes()).hexdigest() == source["previewSha256"]
    data, source_rate = sf.read(cache, always_2d=True)
    assert source_rate == 44100 and data.shape[1] == 2 and len(data) > 58 * source_rate
    data = data[round(source["sourceStart"] * source_rate):round(source["sourceEnd"] * source_rate)]
    data = resample_poly(data, 160, 147)
    data -= data.mean(axis=0)
    data *= source["processing"]["peakCeiling"] / np.max(np.abs(data))
    target = ROOT / source["file"]
    target.parent.mkdir(exist_ok=True)
    # 只裁切、重采样和整理增益，呼啸与阵风由实录及 LMMS 轨道提供。
    write_wav(target, data)
    print(f"Prepared {target.name}: {len(data) / RATE:.3f}s, stereo PCM16 {RATE} Hz")


def relink():
    tree = ET.parse(PROJECT_FILE)
    clips = tree.findall(".//sampleclip")
    assert len(clips) == 1
    source = ROOT / "Sources/gale_howling_01.wav"
    assert source.is_file()
    clips[0].set("src", source.as_posix())
    ET.indent(tree, space="  ")
    tree.write(PROJECT_FILE, encoding="utf-8", xml_declaration=True)
    print(f"Relinked {PROJECT_FILE.name}")
# endregion


# region LMMS 母带与循环资源
def finalize():
    raw = read_wav(RAW_FILE)
    assert len(raw) >= 52 * RATE and raw.shape[1] == 2
    raw_peak = float(np.max(np.abs(raw)))
    assert raw_peak < .9999, "LMMS render clipped; reduce LMMS track/master gain and rerender."
    data = make_loop(raw, start=2, end=52, crossfade=1)
    data -= data.mean(axis=0)
    peak = float(np.max(np.abs(data)))
    rms = float(np.sqrt(np.mean(data ** 2)))
    assert rms > 1e-5 and np.isfinite(data).all()
    # 沿用普通风的母带目标，以音色和阵风层次体现狂风。
    gain = min(10 ** (-17 / 20) / rms, 10 ** (-5 / 20) / peak)
    data *= gain
    write_wav(OUTPUT_FILE, data)
    meta(OUTPUT_FILE)
    final = read_wav(OUTPUT_FILE)
    peak = float(np.max(np.abs(final)))
    rms = float(np.sqrt(np.mean(final ** 2)))
    steps = np.max(np.abs(np.diff(final, axis=0)), axis=1)
    manifest = dict(
        engine="LMMS 1.2.2 via MCP",
        project=PROJECT_FILE.name,
        projectSha256=hashlib.sha256(PROJECT_FILE.read_bytes()).hexdigest(),
        rawFile=str(RAW_FILE.relative_to(ROOT)).replace("\\", "/"),
        rawSha256=hashlib.sha256(RAW_FILE.read_bytes()).hexdigest(),
        rawPeak=raw_peak,
        file=str(OUTPUT_FILE.relative_to(PROJECT)).replace("\\", "/"),
        outputSha256=hashlib.sha256(OUTPUT_FILE.read_bytes()).hexdigest(),
        sampleRate=RATE,
        channels=final.shape[1],
        seconds=len(final) / RATE,
        loop=True,
        trimStart=2,
        trimEnd=52,
        crossfadeSeconds=1,
        targetRmsDb=-17,
        peakCeilingDb=-5,
        peakDb=round(20 * math.log10(peak), 3),
        rmsDb=round(20 * math.log10(rms), 3),
        seamStep=float(np.max(np.abs(final[0] - final[-1]))),
        withinFileStep99=float(np.quantile(steps, .99)),
        minimumOneSecondRmsDb=round(min(
            20 * math.log10(float(np.sqrt(np.mean(block ** 2))))
            for block in np.array_split(final, 49)), 3),
    )
    (ROOT / "gale_manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("prepare", "finalize", "relink"))
    globals()[parser.parse_args().command]()
