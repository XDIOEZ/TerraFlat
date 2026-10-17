"""编排 LMMS 原生游泳音色，只从 MCP 渲染母带裁切并绑定 Unity 资源。"""

from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.util
import json
import math
import re
import uuid
import wave
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
SFX_ROOT = ROOT.parent / "Gameplay_SFX_LMMS"
loader = importlib.util.spec_from_file_location("flatworld_swimming_sfx", SFX_ROOT / "build_sfx.py")
sfx = importlib.util.module_from_spec(loader)
loader.loader.exec_module(sfx)
RATE = sfx.RATE
PROJECT_FILE = ROOT / "Swimming.mmp"
MASTER_FILE = ROOT / "renders/Swimming_raw.wav"


# region 原生水花音色与非等距水滴
def water(cutoff, attack, decay, volume, delay=0, repeats=(), resonance=.8, bandpass=False):
    return dict(sfx.noise(cutoff, attack, decay, volume=volume, delay=delay, repeats=repeats),
                resonance=resonance, filterType=2 if bandpass else 0)


def specs():
    # 短水膜与稀疏共鸣水滴分层，避免持续白噪声和低频鼓点。
    return [
        dict(cue="player.swim.enter", name="入水水花", seconds=.65, peakDb=-13, rmsDb=-29,
             layers=[water(2050, .014, .16, 45),
                     water(790, .028, .20, 26, delay=.025, resonance=1.2, bandpass=True),
                     water(2750, .002, .025, 29, delay=.055, resonance=2.2, bandpass=True,
                           repeats=((.093, .62), (.233, .35))),
                     water(1080, .003, .032, 34, delay=.092, resonance=4.4, bandpass=True,
                           repeats=((.119, .67), (.265, .38)))]),
        dict(cue="player.swim.stroke", name="游泳划水", seconds=.60, peakDb=-17, rmsDb=-31,
             layers=[water(1480, .050, .165, 42),
                     water(720, .024, .13, 29, delay=.045, resonance=1.4, bandpass=True),
                     water(2250, .005, .038, 27, delay=.116, resonance=2.1, bandpass=True,
                           repeats=((.128, .44),)),
                     water(980, .004, .027, 31, delay=.156, resonance=4.1, bandpass=True,
                           repeats=((.151, .51),))]),
        dict(cue="player.swim.exit", name="出水滴水", seconds=.55, peakDb=-18, rmsDb=-32,
             layers=[water(1580, .025, .115, 26),
                     water(2380, .002, .024, 25, delay=.053, resonance=2.4, bandpass=True,
                           repeats=((.149, .61), (.293, .35))),
                     water(1120, .003, .031, 32, delay=.100, resonance=4.6, bandpass=True,
                           repeats=((.173, .57), (.291, .29)))])
    ]


def atomic_text(path, text):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(text, encoding="utf-8")
    temporary.replace(path)


def build():
    tree = ET.parse(SFX_ROOT / "MCP_Base.mmp")
    template = tree.find(".//track")
    tree.getroot().set("creatorversion", "1.2.2")
    tree.find("head").set("mastervol", "42")
    container = tree.find(".//trackcontainer")
    container.clear()
    container.set("type", "song")
    layout = []
    slot = 0
    for sound in specs():
        for variant in range(1, 4):
            layout.append(dict(cue=sound["cue"], name=sound["name"], variant=variant,
                               seconds=sound["seconds"], peakDb=sound["peakDb"],
                               rmsDb=sound["rmsDb"], startSeconds=slot * 2 + .125,
                               file=f"Assets/Audio/Generated/{sound['cue']}__{variant:02}.wav"))
            for index, original in enumerate(sound["layers"]):
                patch = copy.deepcopy(original)
                shift = (0, .075, -.065)[variant - 1]
                patch["decay"] *= 1 + shift
                patch["cutoff"] *= 1 + shift
                patch["delay"] *= (1, 1.08, .94)[variant - 1]
                track = copy.deepcopy(template)
                track.set("name", f"{sound['name']} / {variant} / water layer {index + 1}")
                for pattern in track.findall("pattern"):
                    track.remove(pattern)
                sfx.configure(track, patch)
                track.find("instrumenttrack/eldata").attrib.update(
                    ftype=str(patch["filterType"]), fres=str(patch["resonance"]))
                pattern = ET.SubElement(track, "pattern", type="1", steps="16", len="192",
                                        pos=str(slot * 192), name=sound["cue"])
                duration = patch["attack"] + patch["decay"] + .02
                for delay, velocity in ((0, 1),) + tuple(patch["repeats"]):
                    ET.SubElement(pattern, "note",
                                  pos=str(round((.125 + patch["delay"] + delay) / sfx.TICK_SECONDS)),
                                  key=str(patch["key"]),
                                  len=str(max(3, math.ceil(duration / sfx.TICK_SECONDS))),
                                  vol=str(round(78 * velocity)), pan="0")
                container.append(track)
            slot += 1
    tree.find(".//timeline").set("lp1pos", str(slot * 192))
    ET.indent(tree, space="  ")
    atomic_text(PROJECT_FILE, ET.tostring(tree.getroot(), encoding="unicode", xml_declaration=True) + "\n")
    atomic_text(ROOT / "layout.json", json.dumps(layout, ensure_ascii=False, indent=2) + "\n")
    print("Prepared 9 water events in editable LMMS project; no waveform synthesized in Python")
# endregion


# region MCP 母带整理与 Unity 资源绑定
def guid_for(path):
    if path.exists():
        match = re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.M)
        assert match, path
        return match[1]
    return uuid.uuid4().hex


def bind_cue(cue, clip_guids):
    path = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
    meta_path = Path(str(path) + ".meta")
    guid = guid_for(meta_path)
    text = (PROJECT / "Assets/Resources/Audio/Cues/player.footstep.water.asset").read_text(encoding="utf-8")
    text = text.replace("player.footstep.water", cue)
    clips = "  clips:\n" + "".join(f"  - {{fileID: 8300000, guid: {g}, type: 3}}\n" for g in clip_guids)
    text, count = re.subn(r"  clips:\n(?:  - [^\n]*\n)+", lambda _: clips, text)
    assert count == 1
    text = re.sub(r"  cooldown: [^\n]+", "  cooldown: 0.12", text)
    text = re.sub(r"  maxInstances: [^\n]+", f"  maxInstances: {2 if cue.endswith('.stroke') else 3}", text)
    atomic_text(path, text)
    meta = (PROJECT / "Assets/Resources/Audio/Cues/player.footstep.water.asset.meta").read_text(encoding="utf-8")
    atomic_text(meta_path, re.sub(r"^guid: [0-9a-f]{32}$", f"guid: {guid}", meta, flags=re.M))
    return guid


def finalize():
    layout = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    raw = sfx.read_wave(MASTER_FILE)
    assert np.max(np.abs(raw)) < .9999, "Clipped LMMS master"
    assert len(raw) >= round(max(x["startSeconds"] + x["seconds"] for x in layout) * RATE)
    manifest, cue_clips, preview = [], {}, []
    for entry in layout:
        start = round(entry["startSeconds"] * RATE)
        data = raw[start:start + round(entry["seconds"] * RATE)].mean(axis=1)
        data -= data.mean()
        fade_in, fade_out = round(.002 * RATE), round(.035 * RATE)
        data[:fade_in] *= np.linspace(0, 1, fade_in)
        data[-fade_out:] *= np.linspace(1, 0, fade_out)
        peak, rms = float(np.max(np.abs(data))), float(np.sqrt(np.mean(data ** 2)))
        assert peak > .0001 and rms > .00001 and np.isfinite(data).all(), entry["cue"]
        data *= min(10 ** (entry["peakDb"] / 20) / peak, 10 ** (entry["rmsDb"] / 20) / rms)
        asset = PROJECT / entry["file"]
        sfx.write_wave(asset, data)
        meta_path = Path(str(asset) + ".meta")
        guid = guid_for(meta_path)
        template = (PROJECT / "Assets/Audio/Generated/player.footstep.water__01.wav.meta").read_text(encoding="utf-8")
        template = re.sub(r"^guid: [0-9a-f]{32}$", f"guid: {guid}", template, flags=re.M)
        atomic_text(meta_path, template.replace("LMMS_MCP_Gameplay", "LMMS_MCP_Swimming"))
        cue_clips.setdefault(entry["cue"], []).append(guid)
        manifest.append(dict(entry, guid=guid, sha256=hashlib.sha256(asset.read_bytes()).hexdigest(),
                             peakDb=round(20 * math.log10(np.max(np.abs(data))), 2),
                             rmsDb=round(20 * math.log10(np.sqrt(np.mean(data ** 2))), 2)))
        if entry["variant"] == 1:
            preview.extend((data, np.zeros(round(.35 * RATE))))
    cue_guids = {cue: bind_cue(cue, clips) for cue, clips in cue_clips.items()}
    catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    text = catalog.read_text(encoding="utf-8")
    for guid in cue_guids.values():
        line = f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"
        if line not in text:
            text = text.rstrip() + "\n" + line + "\n"
    atomic_text(catalog, text)
    sfx.write_wave(ROOT / "Preview_Swimming.wav", np.concatenate(preview))
    atomic_text(ROOT / "manifest.json", json.dumps(dict(
        engine="LMMS 1.2.2 via MCP", instrument="TripleOscillator noise with native filters/envelopes",
        masterSha256=hashlib.sha256(MASTER_FILE.read_bytes()).hexdigest(),
        sampleRate=RATE, channels=1, sounds=manifest), ensure_ascii=False, indent=2) + "\n")
    print("Bound 3 spatial world SFX Cues and 9 mono PCM clips")


def verify():
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    catalog = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")
    guids, hashes = set(), set()
    for entry in manifest["sounds"]:
        asset = PROJECT / entry["file"]
        with wave.open(str(asset), "rb") as reader:
            assert (reader.getframerate(), reader.getnchannels(), reader.getsampwidth()) == (RATE, 1, 2), asset
            assert reader.getnframes() == round(entry["seconds"] * RATE), asset
        data = sfx.read_wave(asset)
        peak, rms = np.max(np.abs(data)), np.sqrt(np.mean(data ** 2))
        assert .0001 < peak < .9999 and rms > .00001 and np.isfinite(data).all(), asset
        assert abs(data[0, 0]) <= 1 / 32768 and abs(data[-1, 0]) <= 1 / 32768, asset
        assert hashlib.sha256(asset.read_bytes()).hexdigest() == entry["sha256"]
        assert f"guid: {entry['guid']}" in Path(str(asset) + ".meta").read_text(encoding="utf-8")
        cue = PROJECT / "Assets/Resources/Audio/Cues" / (entry["cue"] + ".asset")
        config = cue.read_text(encoding="utf-8")
        assert f"id: {entry['cue']}" in config and f"guid: {entry['guid']}" in config
        assert "spatialBlend: 1" in config and "bus: 1" in config and "rolloffMode: 1" in config
        assert float(re.search(r"  cooldown: ([^\n]+)", config)[1]) <= .2
        cue_guid = guid_for(Path(str(cue) + ".meta"))
        assert f"guid: {cue_guid}, type: 2" in catalog
        assert entry["guid"] not in guids and entry["sha256"] not in hashes
        guids.add(entry["guid"])
        hashes.add(entry["sha256"])
    assert len(manifest["sounds"]) == 9 and len({x["cue"] for x in manifest["sounds"]}) == 3
    print("Verified 3 spatial Cues, 9 non-silent/unclipped unique 48 kHz mono PCM WAVs, GUIDs and Catalog")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("build", "finalize", "verify"))
    args = parser.parse_args()
    {"build": build, "finalize": finalize, "verify": verify}[args.command]()
