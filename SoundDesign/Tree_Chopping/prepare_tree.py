"""裁切授权的真实砍木录音，整理电平并接入树木专用 Cue。"""

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
LOCAL_PACKAGES = PROJECT / ".codex-tmp/audio-python"
if LOCAL_PACKAGES.is_dir():
    sys.path.insert(0, str(LOCAL_PACKAGES))

import numpy as np
import soundfile as sf


# region 真实砍木采样整理
def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_text(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    cleaned = "\n".join(line.rstrip() for line in content.splitlines()) + "\n"
    path.write_text(cleaned, encoding="utf-8", newline="\n")


def write_wave(path, samples, rate):
    # 写完临时文件再原子替换，避免 Unity 读取半个音频。
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    sf.write(temporary, samples, rate, subtype="PCM_16", format="WAV")
    os.replace(temporary, path)


def prepare(source):
    original = ROOT / source["sourceFile"]
    assert digest(original) == source["sourceSha256"], "Source recording changed"
    settings = source["processing"]
    recorded, rate = sf.read(original, always_2d=True)
    assert rate == settings["sampleRate"] and recorded.shape[1] == settings["channels"] == 1
    clips = []
    for segment in source["segments"]:
        start = round(segment["startSeconds"] * rate)
        count = round(segment["seconds"] * rate)
        audio = recorded[start:start + count, 0].copy()
        assert len(audio) == count
        audio -= audio.mean()
        head = round(settings["headFadeSeconds"] * rate)
        tail = round(settings["tailFadeSeconds"] * rate)
        audio[:head] *= np.linspace(0, 1, head)
        audio[-tail:] *= np.linspace(1, 0, tail)
        clips.append(audio)
    gain = 10 ** (settings["commonPeakCeilingDb"] / 20) / max(float(np.max(np.abs(x))) for x in clips)
    sounds = []
    for segment, audio in zip(source["segments"], clips):
        output = PROJECT / segment["file"]
        write_wave(output, audio * gain, rate)
        audio_meta(output, segment["guid"])
        actual, _ = sf.read(output)
        sounds.append(dict(variant=segment["variant"], file=segment["file"], guid=segment["guid"],
                           sha256=digest(output), seconds=segment["seconds"],
                           sourceStartSeconds=segment["startSeconds"],
                           peakDb=round(20 * math.log10(float(np.max(np.abs(actual)))), 3),
                           rmsDb=round(20 * math.log10(float(np.sqrt(np.mean(actual ** 2)))), 3)))
    install_cue(source)
    manifest = dict(engine="Real axe chopping recording; offline trim and gain only", cueId=source["cue"]["id"],
                    sampleRate=rate, channels=1, subtype="PCM_16", sourcesFile="sources.json",
                    commonAppliedGainDb=round(20 * math.log10(gain), 6), sounds=sounds)
    write_text(ROOT / "manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    verify(source)
# endregion


# region Unity 音频资源引用
def audio_meta(output, guid):
    template = (PROJECT / "Assets/Audio/Generated/combat.impact.axe.wood__01.wav.meta").read_text(encoding="utf-8")
    template = re.sub(r"^guid: \w+$", "guid: " + guid, template, flags=re.M)
    template = re.sub(r"^  userData:.*$", "  userData: CC0 xkeril 753925; tree chopping recording", template, flags=re.M)
    write_text(Path(str(output) + ".meta"), template)


def install_cue(source):
    folder_meta = PROJECT / "Assets/Audio/Trees.meta"
    if not folder_meta.exists():
        write_text(folder_meta, "fileFormatVersion: 2\nguid: 6cb655edca7e45b3a783ec496b39fce6\nfolderAsset: yes\n"
                   "DefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    cue = source["cue"]
    template = (PROJECT / "Assets/Resources/Audio/Cues/combat.impact.axe.wood.asset").read_text(encoding="utf-8")
    template = template.replace("combat.impact.axe.wood", cue["id"])
    references = "  clips:\n" + "".join("  - {fileID: 8300000, guid: " + x["guid"] + ", type: 3}\n" for x in source["segments"])
    template = re.sub(r"  clips:\n(?:  - .*\n)+", lambda _: references, template)
    write_text(PROJECT / cue["file"], template)
    meta = (PROJECT / "Assets/Resources/Audio/Cues/combat.impact.axe.wood.asset.meta").read_text(encoding="utf-8")
    meta = re.sub(r"^guid: \w+$", "guid: " + cue["guid"], meta, flags=re.M)
    write_text(PROJECT / (cue["file"] + ".meta"), meta)
    catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    text = catalog.read_text(encoding="utf-8")
    if cue["guid"] not in text:
        write_text(catalog, text.rstrip() + "\n  - {fileID: 11400000, guid: " + cue["guid"] + ", type: 2}\n")
# endregion


# region 静态资源检查
def verify(source):
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    settings = source["processing"]
    cue = (PROJECT / source["cue"]["file"]).read_text(encoding="utf-8")
    catalog = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")
    assert catalog.count(source["cue"]["guid"]) == 1
    for segment, saved in zip(source["segments"], manifest["sounds"]):
        output = PROJECT / segment["file"]
        info = sf.info(output)
        assert info.samplerate == settings["sampleRate"] and info.channels == 1 and info.subtype == "PCM_16"
        assert info.frames == round(segment["seconds"] * settings["sampleRate"])
        assert digest(output) == saved["sha256"]
        actual, _ = sf.read(output)
        assert np.isfinite(actual).all() and float(np.max(np.abs(actual))) < 0.5
        assert abs(float(actual[0])) < 1e-4 and abs(float(actual[-1])) < 1e-4
        meta = Path(str(output) + ".meta").read_text(encoding="utf-8")
        assert "guid: " + segment["guid"] in meta and cue.count(segment["guid"]) == 1
        assert "  normalize: 0" in meta and "    preloadAudioData: 1" in meta
        print(f"{output.name}: {info.duration:.3f}s, 48000 Hz mono PCM16, peak {saved['peakDb']:.3f} dBFS, RMS {saved['rmsDb']:.3f} dBFS")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("prepare", "verify"))
    arguments = parser.parse_args()
    source = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    (prepare if arguments.command == "prepare" else verify)(source)
