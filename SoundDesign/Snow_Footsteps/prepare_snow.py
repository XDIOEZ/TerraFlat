"""整理 CC0 雪地实录并保留已有 Unity GUID，不生成合成声。"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
PLAYER_ROOT = ROOT.parent / "Player_Fishing_SFX_LMMS"
LOCAL_PACKAGES = PROJECT / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np
import soundfile as sf
from scipy.ndimage import gaussian_filter1d, maximum_filter1d
from scipy.signal import butter, resample_poly, sosfilt

CUE = "player.footstep.snow"
RATE = 48000


# region 雪地实录与原子写入
def write_wave(path, audio):
    # 完整写好后再替换，避免 Unity 导入尚未写完的文件。
    temporary = path.with_suffix(path.suffix + ".tmp")
    sf.write(temporary, audio, RATE, subtype="PCM_16", format="WAV")
    os.replace(temporary, path)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare():
    source = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    assert source["license"] == "CC0-1.0"
    settings = source["processing"]
    assert settings["sampleRate"] == RATE
    sounds = []
    preview = []
    for segment in source["segments"]:
        original = ROOT / segment["sourceFile"]
        assert sha256(original) == segment["sourceSha256"], original
        audio, rate = sf.read(original, always_2d=True)
        assert rate == 44100 and audio.shape[1] == 2, original
        start = round(segment["startSeconds"] * rate)
        audio = audio[start:].mean(axis=1)
        audio = resample_poly(audio, RATE // math.gcd(rate, RATE), rate // math.gcd(rate, RATE))
        audio = sosfilt(butter(2, settings["highpassHz"], fs=RATE,
                               btype="highpass", output="sos"), audio)
        audio -= audio.mean()
        # 轻压孤立尖峰并保留压雪摩擦，共同增益保留三次步伐的力度差别。
        threshold = max(float(np.percentile(np.abs(audio), settings["transientPercentile"])), 1e-7)
        envelope = maximum_filter1d(np.abs(audio), size=round(settings["transientWindowSeconds"] * RATE))
        gain = np.minimum(1, (np.maximum(envelope, threshold) / threshold) **
                          (1 / settings["transientRatio"] - 1))
        audio *= gaussian_filter1d(gain, sigma=settings["transientSmoothSeconds"] * RATE)
        audio *= 10 ** (settings["commonGainDb"] / 20)
        head, tail = (round(settings[key] * RATE) for key in ("headFadeSeconds", "tailFadeSeconds"))
        audio[:head] *= np.linspace(0, 1, head)
        audio[-tail:] *= np.linspace(1, 0, tail)
        count = round(segment["seconds"] * RATE)
        assert len(audio) <= count, "Choose a duration that preserves the complete recorded step"
        audio = np.pad(audio, (0, count - len(audio)))
        audio *= min(1, 10 ** (settings["peakCeilingDb"] / 20) / float(np.max(np.abs(audio))))
        assert np.isfinite(audio).all() and float(np.max(np.abs(audio))) > .001
        asset = PROJECT / segment["file"]
        meta = Path(str(asset) + ".meta").read_text(encoding="utf-8")
        assert re.search(r"^guid: " + segment["guid"] + r"$", meta, re.M), asset
        write_wave(asset, audio)
        actual, _ = sf.read(asset)
        actual_peak = float(np.max(np.abs(actual)))
        actual_rms = float(np.sqrt(np.mean(actual ** 2)))
        sounds.append(dict(cue=CUE, name="雪地脚步", variant=segment["variant"],
                           seconds=segment["seconds"], file=segment["file"],
                           guid=segment["guid"], sha256=sha256(asset),
                           peakDb=round(20 * math.log10(actual_peak), 2),
                           rmsDb=round(20 * math.log10(actual_rms), 2),
                           sourceFile=segment["sourceFile"],
                           externalManifest="SoundDesign/Snow_Footsteps/manifest.json"))
        preview.extend((actual, np.zeros(round(.18 * RATE))))
    manifest = dict(engine="CC0 field recording; offline trim, gain and format conversion",
                    sampleRate=RATE, channels=1, sounds=sounds)
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n",
                                        encoding="utf-8")
    write_wave(ROOT / "Preview_Snow.wav", np.concatenate(preview * 2))
    sync_player_manifest(sounds)
    verify()


def sync_player_manifest(sounds):
    path = PLAYER_ROOT / "manifest.json"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    replacements = {entry["variant"]: entry for entry in sounds}
    manifest["sounds"] = [dict(entry, **replacements[entry["variant"]])
                          if entry["cue"] == CUE else entry for entry in manifest["sounds"]]
    manifest["engine"] = "LMMS 1.2.2 via MCP; snow: CC0 field recording"
    path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    preview = []
    for entry in manifest["sounds"]:
        if entry["variant"] == 1:
            audio, rate = sf.read(PROJECT / entry["file"], always_2d=True)
            assert rate == RATE
            preview.extend((audio.mean(axis=1), np.zeros(round(.24 * RATE))))
    write_wave(PLAYER_ROOT / "Preview_Player_Fishing.wav", np.concatenate(preview))
# endregion


# region 规格与来源一致性检查
def verify():
    source = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    assert len(manifest["sounds"]) == len(source["segments"]) == 3
    for segment, entry in zip(source["segments"], manifest["sounds"]):
        assert sha256(ROOT / segment["sourceFile"]) == segment["sourceSha256"]
        asset = PROJECT / entry["file"]
        info = sf.info(asset)
        assert (info.samplerate, info.channels, info.subtype) == (RATE, 1, "PCM_16"), asset
        assert info.frames == round(entry["seconds"] * RATE)
        assert sha256(asset) == entry["sha256"]
        audio, _ = sf.read(asset)
        assert np.isfinite(audio).all() and .01 < float(np.max(np.abs(audio))) < .5
        assert float(np.sqrt(np.mean(audio ** 2))) > .01
        assert float(np.max(np.abs(audio[[0, -1]]))) < 1 / 32768
        meta = Path(str(asset) + ".meta").read_text(encoding="utf-8")
        assert f"guid: {entry['guid']}" in meta
        assert "preloadAudioData: 1" in meta and "compressionFormat: 0" in meta and "normalize: 0" in meta
        print(f"{asset.name}: {info.duration:.2f}s, 48 kHz mono PCM16, "
              f"peak {entry['peakDb']:.2f} dBFS, RMS {entry['rmsDb']:.2f} dBFS, GUID preserved")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("prepare", "verify"))
    args = parser.parse_args()
    {"prepare": prepare, "verify": verify}[args.command]()
