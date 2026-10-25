"""整理 LMMS MCP 渲染出的天气音效，保留游戏资源 GUID。"""

from __future__ import annotations

import argparse
import json
import math
import uuid
import wave
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
ASSETS = PROJECT / "Assets/Audio/Generated"
RATE = 48000


# region 母带裁切与循环接缝
def read_wav(path):
    with wave.open(str(path), "rb") as reader:
        assert reader.getframerate() == RATE and reader.getsampwidth() == 2
        channels = reader.getnchannels()
        return np.frombuffer(reader.readframes(reader.getnframes()), dtype="<i2").reshape(-1, channels).astype(np.float64) / 32768


def write_wav(path, data):
    temporary = path.with_name(f".{path.name}.{uuid.uuid4().hex}.tmp")
    # 完整写好母带后再替换，避免 Unity 导入半个 WAV。
    try:
        with wave.open(str(temporary), "wb") as writer:
            writer.setnchannels(data.shape[1])
            writer.setsampwidth(2)
            writer.setframerate(RATE)
            writer.writeframes(np.rint(np.clip(data, -1, 1) * 32767).astype("<i2").tobytes())
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


def make_loop(data, start, end, crossfade=.3):
    data = data[round(start * RATE):round(end * RATE)].copy()
    count = round(crossfade * RATE)
    assert len(data) > count * 2
    theta = np.linspace(0, np.pi / 2, count)[:, None]
    # 尾部与头部等功率重叠后旋转排列，让文件边界保持原始波形连续。
    blend = data[-count:] * np.cos(theta) + data[:count] * np.sin(theta)
    return np.concatenate((data[count:-count], blend))


def meta(path, mono=False):
    target = Path(str(path) + ".meta")
    if target.exists():
        return
    target.write_text(f"""fileFormatVersion: 2
guid: {uuid.uuid4().hex}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 1
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: 1
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: {int(mono)}
  normalize: 0
  loadInBackground: 1
  ambisonic: 0
  3D: 0
  userData: LMMS_MCP_Weather
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8")


def finalize(only_thunder=False, only_rain=False):
    manifest = ROOT / "manifest.json"
    selected = {"Rain"} if only_rain else {"Thunder", "Thunder_Far"} if only_thunder else None
    entries = json.loads(manifest.read_text(encoding="utf-8"))["sounds"] if selected else []
    inputs = [
        ("Rain", "weather.rain.loop__01.wav", .5, 25.5, True, -20, -6),
        ("Wind", "weather.wind.loop__01.wav", 4, 48, True, -17, -5),
        ("Thunder", "weather.thunder__01.wav", 0, 17, False, -24, -3),
        ("Thunder", "weather.thunder__02.wav", 24, 41, False, -24, -3),
        ("Thunder", "weather.thunder__03.wav", 48, 71, False, -24, -3),
        ("Thunder_Far", "weather.thunder.far__01.wav", 0, 18.2, False, -28, -6),
        ("Thunder_Far", "weather.thunder.far__02.wav", 28, 51, False, -28, -6),
        ("Thunder_Far", "weather.thunder.far__03.wav", 56, 79, False, -28, -6),
    ]
    cache = {}
    for project, filename, start, end, looping, rms_db, peak_db in inputs:
        if selected and project not in selected:
            continue
        if project not in cache:
            cache[project] = read_wav(ROOT / "renders" / f"Weather_{project}_raw.wav")
        raw = cache[project]
        assert len(raw) >= round(end * RATE), f"Render too short: {project}"
        data = make_loop(raw, start, end, crossfade=1 if project == "Rain" else .3) if looping else raw[round(start*RATE):round(end*RATE)].copy()
        raw_peak = float(np.max(np.abs(raw)))
        assert raw_peak < .9999, f"LMMS render clipped: {project}"
        # 雨和雷声由 LMMS 编排真实录音，普通风仍为合成；此处仅整理母带。
        if not looping:
            data = data.mean(axis=1, keepdims=True)
        data -= data.mean(axis=0)
        if not looping:
            n = round(.003 * RATE)
            data[:n] *= np.linspace(0, 1, n)[:, None]
            n = round(.55 * RATE)
            data[-n:] *= np.linspace(1, 0, n)[:, None]
        peak = float(np.max(np.abs(data)))
        rms = float(np.sqrt(np.mean(data**2)))
        assert rms > 1e-5 and np.isfinite(data).all()
        gain = min(10**(rms_db/20) / rms, 10**(peak_db/20) / peak)
        data *= gain
        output = ASSETS / filename
        write_wav(output, data)
        meta(output, mono=not looping)
        if not looping:
            target = Path(str(output) + ".meta")
            text = target.read_text(encoding="utf-8").replace("forceToMono: 0", "forceToMono: 1")
            with target.open("r+", encoding="utf-8") as stream:
                stream.write(text)
                stream.truncate()
        if filename == "weather.rain.loop__01.wav":
            target = Path(str(output) + ".meta")
            text = target.read_text(encoding="utf-8")
            # 持续雨声预先解码为 PCM，循环时不再依赖实时压缩解码。
            text = text.replace("loadType: 1", "loadType: 0").replace("compressionFormat: 1", "compressionFormat: 0")
            text = text.replace("sampleRateOverride: 44100", "sampleRateOverride: 48000")
            text = text.replace("preloadAudioData: 0", "preloadAudioData: 1").replace("normalize: 1", "normalize: 0").replace("loadInBackground: 1", "loadInBackground: 0")
            # 原资源的 meta 在 Windows 上为隐藏文件，原位写入以保留 GUID 和文件属性。
            with target.open("r+", encoding="utf-8") as stream:
                stream.write("\n".join(line.rstrip() for line in text.splitlines())+"\n")
                stream.truncate()
        peak = float(np.max(np.abs(data)))
        rms = float(np.sqrt(np.mean(data**2)))
        seam = float(np.max(np.abs(data[0] - data[-1]))) if looping else None
        relative_path = str(output.relative_to(PROJECT)).replace("\\", "/")
        entries = [entry for entry in entries if entry["file"] != relative_path]
        entries.append(dict(file=relative_path,
                            project=f"Weather_{project}.mmp", seconds=round(len(data)/RATE, 3),
                            sampleRate=RATE, channels=data.shape[1], loop=looping,
                            peakDb=round(20*math.log10(peak), 2), rmsDb=round(20*math.log10(rms), 2),
                            seamStep=seam, rawPeak=raw_peak))
    order = {entry[1]: index for index, entry in enumerate(inputs)}
    entries.sort(key=lambda entry: order[Path(entry["file"]).name])
    (ROOT / "manifest.json").write_text(json.dumps(dict(engine="LMMS 1.2.2 via MCP", sounds=entries), ensure_ascii=False, indent=2)+"\n", encoding="utf-8")
    print(json.dumps(entries, indent=2))


def relink():
    # 移动项目目录后，只更新 LMMS 的采样绝对路径。
    for filename in ("Weather_Rain.mmp", "Weather_Thunder.mmp", "Weather_Thunder_Far.mmp"):
        path = ROOT / filename
        tree = ET.parse(path)
        for clip in tree.findall(".//sampleclip"):
            source = ROOT / "Sources" / Path(clip.get("src")).name
            assert source.is_file(), source
            clip.set("src", source.as_posix())
        ET.indent(tree, space="  ")
        tree.write(path, encoding="utf-8", xml_declaration=True)
        print(f"Relinked {filename}")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["finalize", "relink"])
    selection = parser.add_mutually_exclusive_group()
    selection.add_argument("--only-thunder", action="store_true")
    selection.add_argument("--only-rain", action="store_true")
    args = parser.parse_args()
    relink() if args.command == "relink" else finalize(args.only_thunder, args.only_rain)
