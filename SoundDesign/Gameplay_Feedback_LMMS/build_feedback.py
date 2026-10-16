"""用 LMMS 原生合成轨道编排玩法反馈音效，并整理 MCP 母带为 Unity Cue。"""

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
SFX_PATH = ROOT.parent / "Gameplay_SFX_LMMS" / "build_sfx.py"
loader = importlib.util.spec_from_file_location("flatworld_sfx_build", SFX_PATH)
sfx = importlib.util.module_from_spec(loader)
loader.loader.exec_module(sfx)
RATE = sfx.RATE


# region 音色方案
def specs():
    tone, dust = sfx.layer, sfx.noise
    return [
        dict(cue="building.place", name="搭建落位", seconds=.42, peakDb=-9, rmsDb=-24,
             layers=[tone(key=32, levels=(69, 23, 11), coarse=(0, 13, 26),
                          decay=.13, cutoff=1900, volume=60),
                     dust(2400, .002, .095, volume=55, repeats=((.064, .35),)),
                     tone(key=48, decay=.05, cutoff=3000, volume=23, delay=.073)]),
        dict(cue="building.reject", name="无效位置提示", seconds=.29, peakDb=-15, rmsDb=-29,
             layers=[tone(key=58, waves=(0, 1, 0), levels=(70, 18, 5),
                          decay=.065, cutoff=1850, volume=46),
                     tone(key=49, waves=(0, 1, 0), levels=(72, 18, 5),
                          decay=.065, cutoff=1550, volume=36, delay=.083)]),
        dict(cue="craft.complete", name="手工制作完成", seconds=.34, peakDb=-12, rmsDb=-27,
             layers=[tone(key=61, coarse=(0, 12, 25), levels=(75, 13, 8),
                          decay=.065, cutoff=3200, volume=42),
                     tone(key=68, coarse=(0, 12, 24), levels=(76, 13, 7),
                          decay=.09, cutoff=3700, volume=41, delay=.082),
                     dust(1850, .002, .032, volume=20)]),
        dict(cue="smelt.complete", name="熔炼出炉金属轻鸣", seconds=.53, peakDb=-11, rmsDb=-28,
             layers=[tone(key=69, coarse=(0, 19, 30), fine=(0, 22, -24),
                          levels=(66, 23, 9), decay=.28, cutoff=5300, volume=47),
                     tone(key=76, coarse=(0, 13, 31), levels=(68, 22, 10),
                          decay=.22, cutoff=4700, volume=27, delay=.094),
                     dust(3100, .002, .073, volume=30)]),
        dict(cue="fire.seed", name="引火火星", seconds=.44, peakDb=-13, rmsDb=-28,
             layers=[dust(4100, .01, .115, volume=43,
                          repeats=((.064, .44), (.135, .27))),
                     dust(1350, .02, .15, volume=33),
                     tone(key=39, levels=(54, 13, 8), decay=.085,
                          cutoff=1700, volume=23, delay=.058)]),
        dict(cue="furnace.ignite", name="炉火点燃", seconds=.69, peakDb=-10, rmsDb=-26,
             layers=[dust(780, .058, .29, volume=61),
                     dust(3500, .045, .32, volume=42, delay=.07,
                          repeats=((.16, .35),)),
                     tone(key=28, coarse=(0, 10, 19), levels=(70, 17, 8),
                          decay=.25, cutoff=1100, volume=38, delay=.037)]),
    ]
# endregion


# region LMMS 工程编排
def build():
    tree = ET.parse(ROOT.parent / "Gameplay_SFX_LMMS" / "MCP_Base.mmp")
    template = tree.find(".//track")
    tree.getroot().set("creatorversion", "1.2.2")
    container = tree.find(".//trackcontainer")
    container.clear()
    container.set("type", "song")
    layout = []
    for index, spec in enumerate(specs()):
        for variant in range(1, 3):
            slot = index * 2 + variant - 1
            layout.append(dict(cue=spec["cue"], name=spec["name"], variant=variant,
                               seconds=spec["seconds"], peakDb=spec["peakDb"],
                               rmsDb=spec["rmsDb"], startSeconds=slot * 2 + .125,
                               file=f"Assets/Audio/Generated/{spec['cue']}__{variant:02}.wav"))
            for layer_index, original in enumerate(spec["layers"]):
                patch = copy.deepcopy(original)
                patch["key"] += variant - 1
                patch["decay"] *= 1 + (variant - 1) * .06
                patch["cutoff"] *= 1 + (variant - 1) * .05
                track = copy.deepcopy(template)
                track.set("name", f"{spec['name']} / 变体{variant} / {layer_index + 1}")
                for pattern in track.findall("pattern"):
                    track.remove(pattern)
                sfx.configure(track, patch)
                pattern = ET.SubElement(track, "pattern", type="1", steps="16", len="192",
                                        pos=str(slot * 192), name=spec["cue"])
                duration = patch["length"] or patch["attack"] + patch["hold"] + patch["decay"] + .02
                for delay, velocity in ((0, 1),) + tuple(patch["repeats"]):
                    ET.SubElement(pattern, "note",
                                  pos=str(round((.125 + patch["delay"] + delay) / sfx.TICK_SECONDS)),
                                  key=str(patch["key"]),
                                  len=str(max(3, math.ceil(duration / sfx.TICK_SECONDS))),
                                  vol=str(round(78 * velocity)), pan="0")
                container.append(track)
    tree.find(".//timeline").set("lp1pos", str(len(layout) * 192))
    ET.indent(tree, space="  ")
    tree.write(ROOT / "Gameplay_Feedback.mmp", encoding="utf-8", xml_declaration=True)
    (ROOT / "layout.json").write_text(json.dumps(layout, ensure_ascii=False, indent=2) + "\n",
                                      encoding="utf-8")
    print(f"Prepared {len(layout)} LMMS events")
# endregion


# region Unity 素材与 Cue
def add_cue(cue, clip_guids):
    cue_path = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
    clips = "  clips:\n" + "".join(
        f"  - {{fileID: 8300000, guid: {guid}, type: 3}}\n" for guid in clip_guids)
    if cue_path.exists():
        existing = cue_path.read_text(encoding="utf-8")
        result, count = re.subn(r"  clips:\n(?:  - [^\n]*\n)+", lambda _: clips, existing)
        assert count == 1, cue_path
        cue_path.write_text(result, encoding="utf-8")
        return re.search(r"^guid: ([0-9a-f]+)$",
                         cue_path.with_suffix(".asset.meta").read_text(encoding="utf-8"), re.M)[1]

    guid = uuid.uuid4().hex
    cooldown = .18 if cue == "building.reject" else .09 if cue == "craft.complete" else .065
    body = f"""%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: c0c19eb3306146649bcc5130f92e0433, type: 3}}
  m_Name: {cue}
  m_EditorClassIdentifier:
  id: {cue}
  bus: 1
{clips}  selection: 0
  loop: 0
  volume:
    Min: 0.85
    Max: 0.98
  pitch:
    Min: 0.98
    Max: 1.02
  spatialBlend: 1
  minDistance: 1.5
  maxDistance: 14
  priority: 128
  cooldown: {cooldown}
  maxInstances: 3
  concurrencyPolicy: 1
  rolloffMode: 1
  outputOverride: {{fileID: 0}}
"""
    cue_path.write_text(body, encoding="utf-8")
    cue_path.with_suffix(".asset.meta").write_text(f"""fileFormatVersion: 2
guid: {guid}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData:
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8")
    return guid


def finalize():
    layout = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    master = sfx.read_wave(ROOT / "renders" / "Gameplay_Feedback_raw.wav")
    assert np.max(np.abs(master)) < .9999, "LMMS master clipped"
    required = max(entry["startSeconds"] + entry["seconds"] for entry in layout)
    assert len(master) >= round(required * RATE), "LMMS master too short"
    manifest, cue_clips, preview = [], {}, []
    for entry in layout:
        offset = entry["startSeconds"]
        data = master[round(offset * RATE):round((offset + entry["seconds"]) * RATE)].mean(axis=1)
        data -= data.mean()
        n = round(.002 * RATE)
        data[:n] *= np.linspace(0, 1, n)
        n = round(.025 * RATE)
        data[-n:] *= np.linspace(1, 0, n)
        peak = float(np.max(np.abs(data)))
        rms = float(np.sqrt(np.mean(data ** 2)))
        assert peak > 1e-4 and rms > 1e-5 and np.isfinite(data).all(), entry["cue"]
        data *= min(10 ** (entry["peakDb"] / 20) / peak,
                    10 ** (entry["rmsDb"] / 20) / rms)
        dest = PROJECT / entry["file"]
        sfx.write_wave(dest, data)
        guid = sfx.update_meta(dest)
        cue_clips.setdefault(entry["cue"], []).append(guid)
        manifest.append(dict(entry, guid=guid,
                             sha256=hashlib.sha256(dest.read_bytes()).hexdigest(),
                             peakDb=round(20 * math.log10(np.max(np.abs(data))), 2),
                             rmsDb=round(20 * math.log10(np.sqrt(np.mean(data ** 2))), 2)))
        if entry["variant"] == 1:
            preview.extend([data, np.zeros(round(.22 * RATE))])

    cue_guids = {cue: add_cue(cue, clips) for cue, clips in cue_clips.items()}
    catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    current = catalog.read_text(encoding="utf-8")
    for guid in cue_guids.values():
        reference = f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"
        if reference not in current:
            current = current.rstrip() + "\n" + reference + "\n"
    catalog.write_text(current, encoding="utf-8")
    sfx.write_wave(ROOT / "Preview_Feedback.wav", np.concatenate(preview))
    (ROOT / "manifest.json").write_text(json.dumps(
        dict(engine="LMMS 1.2.2 via MCP", sampleRate=RATE,
             channels=1, sounds=manifest), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Bound {len(cue_guids)} new AudioCues and {len(manifest)} LMMS clips")


def verify():
    sounds = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))["sounds"]
    catalog = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")
    seen = set()
    for entry in sounds:
        path = PROJECT / entry["file"]
        with wave.open(str(path), "rb") as clip:
            assert (clip.getframerate(), clip.getnchannels(), clip.getsampwidth()) == (RATE, 1, 2), path
            assert clip.getnframes() == round(entry["seconds"] * RATE), path
        assert hashlib.sha256(path.read_bytes()).hexdigest() == entry["sha256"], path
        clip_meta = Path(str(path) + ".meta").read_text(encoding="utf-8")
        assert f"guid: {entry['guid']}" in clip_meta, path
        cue = entry["cue"]
        cue_path = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
        cue_text = cue_path.read_text(encoding="utf-8")
        assert f"id: {cue}" in cue_text and f"guid: {entry['guid']}" in cue_text, cue
        cue_guid = re.search(r"^guid: ([0-9a-f]+)$",
                             Path(str(cue_path) + ".meta").read_text(encoding="utf-8"), re.M)[1]
        assert f"guid: {cue_guid}, type: 2" in catalog, cue
        assert entry["guid"] not in seen, entry["guid"]
        seen.add(entry["guid"])
    assert len(sounds) == 12 and len({entry["cue"] for entry in sounds}) == 6
    print("Verified 6 AudioCues, 12 unique PCM WAVs, metadata and catalog references")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("build", "finalize", "verify"))
    args = parser.parse_args()
    if args.command == "build":
        build()
    elif args.command == "finalize":
        finalize()
    else:
        verify()
