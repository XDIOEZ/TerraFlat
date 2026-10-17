"""编排真实水声采样，只从 LMMS MCP 渲染母带裁切并绑定 Unity 资源。"""

from __future__ import annotations

import argparse
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
MASTER_FILE = ROOT / "renders/Swimming_real_raw.wav"


# region 真实水花与划水采样编排
def specs():
    # 保留实录的宽频水花与水流主体，不再用窄带白噪声冒充水声。
    return [
        dict(cue="player.swim.enter", name="实录入水水花", seconds=1.05, peakDb=-9, rmsDb=-24),
        dict(cue="player.swim.stroke", name="实录连续游泳", seconds=6, crossfadeSeconds=.5, peakDb=-11, rmsDb=-26),
        dict(cue="player.swim.exit", name="实录出水滴水", seconds=1.0, peakDb=-14, rmsDb=-29)
    ]


def atomic_text(path, text):
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(text, encoding="utf-8")
    temporary.replace(path)


def build():
    tree = ET.parse(SFX_ROOT / "MCP_Base.mmp")
    tree.getroot().set("creatorversion", "1.2.2")
    tree.find("head").set("mastervol", "75")
    container = tree.find(".//trackcontainer")
    container.clear()
    container.set("type", "song")
    layout = []
    slot = 0
    for sound in specs():
        for variant in range(1, 4):
            layout.append(dict(cue=sound["cue"], name=sound["name"], variant=variant,
                               seconds=sound["seconds"], peakDb=sound["peakDb"],
                               rmsDb=sound["rmsDb"], crossfadeSeconds=sound.get("crossfadeSeconds", 0),
                               startSeconds=slot * 2 + .125,
                               file=f"Assets/Audio/Generated/{sound['cue']}__{variant:02}.wav"))
            sample = ROOT / "Sources" / f"{sound['cue'].split('.')[-1]}_{variant:02}.wav"
            assert sample.is_file(), f"Prepare recorded sources first: {sample}"
            track = ET.SubElement(container, "track", muted="0", type="2",
                                  name=f"{sound['name']} / {variant}", solo="0")
            sample_track = ET.SubElement(track, "sampletrack", pan="0", vol="80")
            ET.SubElement(sample_track, "fxchain", numofeffects="0", enabled="0")
            ET.SubElement(track, "sampleclip", pos=str(slot * 192 + 12),
                          len=str(math.ceil(sound["seconds"] / sfx.TICK_SECONDS)),
                          src=sample.as_posix(), muted="0", off="0")
            slot += math.ceil((sound["seconds"] + .125) / 2)
    tree.find(".//timeline").set("lp1pos", str(slot * 192))
    ET.indent(tree, space="  ")
    atomic_text(PROJECT_FILE, ET.tostring(tree.getroot(), encoding="unicode", xml_declaration=True) + "\n")
    atomic_text(ROOT / "layout.json", json.dumps(layout, ensure_ascii=False, indent=2) + "\n")
    print("Prepared 9 recorded water sample clips in editable LMMS project")
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
    if cue == "player.swim.stroke":
        text = text.replace("  loop: 0", "  loop: 1").replace("  cooldown: 0.12", "  cooldown: 0")
    text = re.sub(r"  maxInstances: [^\n]+", f"  maxInstances: {2 if cue.endswith('.stroke') else 3}", text)
    atomic_text(path, text)
    meta = (PROJECT / "Assets/Resources/Audio/Cues/player.footstep.water.asset.meta").read_text(encoding="utf-8")
    atomic_text(meta_path, re.sub(r"^guid: [0-9a-f]{32}$", f"guid: {guid}", meta, flags=re.M))
    return guid


def finalize(only_stroke=False):
    layout = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    samples = [ROOT / "Sources" / f"{entry['cue'].split('.')[-1]}_{entry['variant']:02}.wav" for entry in layout]
    latest_input = max(path.stat().st_mtime for path in [PROJECT_FILE, ROOT / "sources.json", *samples])
    assert MASTER_FILE.stat().st_mtime >= latest_input, "LMMS master is stale; reload and render the project before finalize"
    raw = sfx.read_wave(MASTER_FILE)
    assert np.max(np.abs(raw)) < .9999, "Clipped LMMS master"
    assert len(raw) >= round(max(x["startSeconds"] + x["seconds"] for x in layout) * RATE)
    manifest, cue_clips, preview = [], {}, []
    previous = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8")) if only_stroke else None
    for entry in layout:
        if only_stroke and entry["cue"] != "player.swim.stroke":
            kept = next(x for x in previous["sounds"] if x["file"] == entry["file"])
            manifest.append(dict(kept, masterSha256=kept.get("masterSha256", previous["masterSha256"])))
            if entry["variant"] == 1:
                preview.extend((sfx.read_wave(PROJECT / entry["file"]).mean(axis=1), np.zeros(round(.35 * RATE))))
            continue
        start = round(entry["startSeconds"] * RATE)
        data = raw[start:start + round(entry["seconds"] * RATE)].mean(axis=1)
        data -= data.mean()
        crossfade = round(entry.get("crossfadeSeconds", 0) * RATE)
        if crossfade:
            # 把尾部与头部等功率交叠，循环边界落在实录内部，不在每轮末尾淡到静音。
            angle = np.linspace(0, np.pi / 2, crossfade)
            seam = data[-crossfade:] * np.cos(angle) + data[:crossfade] * np.sin(angle)
            data = np.concatenate((data[crossfade:-crossfade], seam))
            data -= data.mean()
        else:
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
        sample = ROOT / "Sources" / f"{entry['cue'].split('.')[-1]}_{entry['variant']:02}.wav"
        manifest.append(dict(entry, seconds=len(data) / RATE, guid=guid, sha256=hashlib.sha256(asset.read_bytes()).hexdigest(),
                             sourceSha256=hashlib.sha256(sample.read_bytes()).hexdigest(),
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
    sfx.write_wave(ROOT / "Preview_Swimming_Recorded.wav", np.concatenate(preview))
    atomic_text(ROOT / "manifest.json", json.dumps(dict(
        engine="LMMS 1.2.2 via MCP", instrument="Recorded swimming, splashes and trickles through LMMS sample tracks",
        masterSha256=hashlib.sha256(MASTER_FILE.read_bytes()).hexdigest(),
        sourcesFile="sources.json",
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
        assert 20 * math.log10(peak / rms) < 22, f"Water body hidden by isolated peaks: {asset}"
        if entry.get("crossfadeSeconds", 0):
            assert abs(data[0, 0] - data[-1, 0]) <= np.max(np.abs(np.diff(data[:, 0]))) + 1 / 32768, asset
        else:
            assert abs(data[0, 0]) <= 1 / 32768 and abs(data[-1, 0]) <= 1 / 32768, asset
        assert hashlib.sha256(asset.read_bytes()).hexdigest() == entry["sha256"]
        sample = ROOT / "Sources" / f"{entry['cue'].split('.')[-1]}_{entry['variant']:02}.wav"
        assert hashlib.sha256(sample.read_bytes()).hexdigest() == entry["sourceSha256"]
        assert f"guid: {entry['guid']}" in Path(str(asset) + ".meta").read_text(encoding="utf-8")
        cue = PROJECT / "Assets/Resources/Audio/Cues" / (entry["cue"] + ".asset")
        config = cue.read_text(encoding="utf-8")
        assert f"id: {entry['cue']}" in config and f"guid: {entry['guid']}" in config
        assert "spatialBlend: 1" in config and "bus: 1" in config and "rolloffMode: 1" in config
        assert f"  loop: {1 if entry['cue'] == 'player.swim.stroke' else 0}\n" in config
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
    parser.add_argument("--only-stroke", action="store_true", help="仅替换连续游泳素材，保留现有入出水成品")
    args = parser.parse_args()
    if args.command == "finalize":
        finalize(args.only_stroke)
    else:
        {"build": build, "verify": verify}[args.command]()
