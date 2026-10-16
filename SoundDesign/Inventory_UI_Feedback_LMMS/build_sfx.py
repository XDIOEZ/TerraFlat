"""制作库存交换/穿戴/卸装的 LMMS 原创短音，并登记正式 UI Cue。"""

from __future__ import annotations

import argparse
import copy
import hashlib
import importlib.util
import json
import math
import re
import wave
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]


def load_source(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


sfx = load_source("flatworld_ui_sfx_base", ROOT.parent / "Gameplay_SFX_LMMS/build_sfx.py")
feedback = load_source("flatworld_ui_feedback_base", ROOT.parent / "Gameplay_Feedback_LMMS/build_feedback.py")
RATE = sfx.RATE


# region LMMS 音色和编排
def specs():
    tone, noise = sfx.layer, sfx.noise
    return [
        dict(cue="ui.inventory.swap", name="物品交换双段落位", seconds=.3,
             layers=[tone(key=54, levels=(72, 17, 8), coarse=(0, 12, 22),
                          decay=.045, cutoff=3200, volume=42),
                     tone(key=64, levels=(70, 16, 9), coarse=(0, 14, 25),
                          decay=.075, cutoff=3100, volume=40, delay=.07),
                     noise(1850, .002, .035, volume=24, delay=.06)]),
        dict(cue="ui.equipment.equip", name="装备扣紧与布料摩擦", seconds=.41,
             layers=[noise(1350, .013, .13, volume=38),
                     tone(key=61, coarse=(0, 20, 27), levels=(65, 19, 8),
                          decay=.09, cutoff=3300, volume=37, delay=.073),
                     tone(key=68, coarse=(0, 12, 20), levels=(72, 16, 7),
                          decay=.09, cutoff=3800, volume=30, delay=.14)]),
        dict(cue="ui.equipment.unequip", name="卸下装备的松扣收尾", seconds=.39,
             layers=[tone(key=68, coarse=(0, 16, 24), levels=(71, 18, 8),
                          decay=.055, cutoff=3200, volume=34),
                     tone(key=57, coarse=(0, 16, 24), levels=(65, 20, 8),
                          decay=.083, cutoff=2500, volume=38, delay=.08),
                     noise(1200, .01, .14, volume=32, delay=.036)])
    ]


def build():
    tree = ET.parse(ROOT.parent / "Gameplay_SFX_LMMS/MCP_Base.mmp")
    template = tree.find(".//track")
    tree.getroot().set("creatorversion", "1.2.2")
    tracks = tree.find(".//trackcontainer")
    tracks.clear()
    tracks.set("type", "song")
    layout = []
    slot = 0
    for sound in specs():
        for variant in (1, 2):
            layout.append(dict(cue=sound["cue"], name=sound["name"], variant=variant,
                               startSeconds=slot * 2 + .125, seconds=sound["seconds"],
                               file=f"Assets/Audio/Generated/{sound['cue']}__{variant:02}.wav"))
            for layer_number, original in enumerate(sound["layers"], 1):
                patch = copy.deepcopy(original)
                patch["key"] += variant - 1
                patch["decay"] *= 1 + (variant - 1) * .055
                patch["cutoff"] *= 1 + (variant - 1) * .045
                track = copy.deepcopy(template)
                track.set("name", f"{sound['name']} / {variant} / {layer_number}")
                for pattern in track.findall("pattern"):
                    track.remove(pattern)
                sfx.configure(track, patch)
                pattern = ET.SubElement(track, "pattern", type="1", steps="16",
                                        len="192", pos=str(slot * 192), name=sound["cue"])
                length = patch["length"] or patch["attack"] + patch["hold"] + patch["decay"] + .02
                for delay, velocity in ((0, 1),) + tuple(patch["repeats"]):
                    ET.SubElement(pattern, "note",
                                  pos=str(round((.125 + patch["delay"] + delay) / sfx.TICK_SECONDS)),
                                  key=str(patch["key"]),
                                  len=str(max(3, math.ceil(length / sfx.TICK_SECONDS))),
                                  vol=str(round(78 * velocity)), pan="0")
                tracks.append(track)
            slot += 1
    tree.find(".//timeline").set("lp1pos", str(slot * 192))
    ET.indent(tree, space="  ")
    tree.write(ROOT / "Inventory_UI_Feedback.mmp", encoding="utf-8", xml_declaration=True)
    (ROOT / "layout.json").write_text(json.dumps(layout, ensure_ascii=False, indent=2) + "\n",
                                      encoding="utf-8")
    print("Prepared 3 distinct UI cues and 6 LMMS event slots")
# endregion


# region LMMS 母带整理与 Unity 引用
def finalize():
    layout = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    raw = sfx.read_wave(ROOT / "renders/Inventory_UI_Feedback_raw.wav")
    assert np.max(np.abs(raw)) < .9999
    assert len(raw) >= round(max(e["startSeconds"] + e["seconds"] for e in layout) * RATE)
    outputs, guids, preview = [], {}, []
    for entry in layout:
        first, last = (round(seconds * RATE) for seconds in
                       (entry["startSeconds"], entry["startSeconds"] + entry["seconds"]))
        audio = raw[first:last].mean(axis=1)
        audio -= audio.mean()
        fade_in, fade_out = round(.002 * RATE), round(.025 * RATE)
        audio[:fade_in] *= np.linspace(0, 1, fade_in)
        audio[-fade_out:] *= np.linspace(1, 0, fade_out)
        peak = float(np.max(np.abs(audio)))
        rms = float(np.sqrt(np.mean(audio * audio)))
        assert peak > .0001 and rms > .00001 and np.isfinite(audio).all(), entry["cue"]
        audio *= min(10 ** (-13 / 20) / peak, 10 ** (-28 / 20) / rms)
        destination = PROJECT / entry["file"]
        sfx.write_wave(destination, audio)
        guid = sfx.update_meta(destination)
        guids.setdefault(entry["cue"], []).append(guid)
        outputs.append(dict(entry, guid=guid, sha256=hashlib.sha256(destination.read_bytes()).hexdigest()))
        if entry["variant"] == 1:
            preview.extend((audio, np.zeros(round(.25 * RATE))))

    catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    catalog_text = catalog.read_text(encoding="utf-8")
    for id, clips in guids.items():
        guid = feedback.add_cue(id, clips)
        cue_path = PROJECT / "Assets/Resources/Audio/Cues" / (id + ".asset")
        cue_text = cue_path.read_text(encoding="utf-8")
        for old, new in (
            ("bus: 1", "bus: 2"), ("spatialBlend: 1", "spatialBlend: 0"),
            ("priority: 128", "priority: 48"), ("cooldown: 0.065", "cooldown: 0.08")
        ):
            cue_text = cue_text.replace(old, new)
        cue_path.write_text(cue_text, encoding="utf-8")
        ref = f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"
        if ref not in catalog_text:
            catalog_text = catalog_text.rstrip() + "\n" + ref + "\n"
    catalog.write_text(catalog_text, encoding="utf-8")
    sfx.write_wave(ROOT / "Preview_Inventory_Feedback.wav", np.concatenate(preview))
    (ROOT / "manifest.json").write_text(json.dumps(dict(
        engine="LMMS 1.2.2 via MCP", sampleRate=RATE, channels=1, sounds=outputs),
        ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Finalized 6 LMMS clips and 3 UI Cues")


def verify():
    sounds = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))["sounds"]
    catalog = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")
    assert len(sounds) == 6 and len({s["cue"] for s in sounds}) == 3
    for sound in sounds:
        audio = PROJECT / sound["file"]
        with wave.open(str(audio), "rb") as reader:
            assert (reader.getnchannels(), reader.getframerate(), reader.getsampwidth()) == (1, RATE, 2)
            assert reader.getnframes() == round(sound["seconds"] * RATE)
        assert hashlib.sha256(audio.read_bytes()).hexdigest() == sound["sha256"]
        assert f"guid: {sound['guid']}" in Path(str(audio) + ".meta").read_text(encoding="utf-8")
        cue = PROJECT / "Assets/Resources/Audio/Cues" / (sound["cue"] + ".asset")
        data = cue.read_text(encoding="utf-8")
        meta = Path(str(cue) + ".meta").read_text(encoding="utf-8")
        assert f"guid: {sound['guid']}" in data and "bus: 2" in data and "spatialBlend: 0" in data
        guid = re.search(r"^guid: ([0-9a-f]{32})$", meta, re.M)[1]
        assert f"guid: {guid}, type: 2" in catalog
    print("Verified 3 UI Cues and 6 real LMMS-rendered WAVs")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=["build", "finalize", "verify"])
    args = parser.parse_args()
    {"build": build, "finalize": finalize, "verify": verify}[args.action]()
