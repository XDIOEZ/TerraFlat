"""编排脚步和钓鱼的 LMMS 工程，并把 MCP 真正渲染的母带接入 AudioCue。"""

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
SFX_ROOT = ROOT.parent / "Gameplay_SFX_LMMS"
FEEDBACK_ROOT = ROOT.parent / "Gameplay_Feedback_LMMS"


def load_module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


sfx = load_module("flatworld_audio_sfx", SFX_ROOT / "build_sfx.py")
feedback = load_module("flatworld_audio_feedback", FEEDBACK_ROOT / "build_feedback.py")
RATE = sfx.RATE


# region 脚步与钓具音色
def specs():
    tone, noise = sfx.layer, sfx.noise
    return [
        dict(cue="player.footstep.dirt", name="泥土脚步", seconds=.23, variants=3, peakDb=-17, rmsDb=-32,
             layers=[tone(key=27, levels=(63, 18, 9), coarse=(0, 11, 24),
                          decay=.065, cutoff=1350, volume=36),
                     noise(1250, .006, .07, volume=47),
                     noise(3300, .002, .025, volume=29, delay=.019)]),
        dict(cue="player.footstep.grass", name="草地脚步", seconds=.42, variants=3,
             peakDb=-17, rmsDb=-31, recording="grass"),
        dict(cue="player.footstep.sand", name="沙地脚步", seconds=.3, variants=3, peakDb=-17, rmsDb=-32,
             layers=[noise(3600, .009, .105, volume=43,
                           repeats=((.052, .21),)),
                     noise(950, .015, .095, volume=44),
                     tone(key=25, levels=(57, 10, 6), decay=.065, cutoff=900, volume=25)]),
        dict(cue="player.footstep.stone", name="石地脚步", seconds=.21, variants=3, peakDb=-16, rmsDb=-31,
             layers=[tone(key=41, levels=(66, 23, 8), coarse=(0, 17, 28),
                          decay=.051, cutoff=3100, volume=43),
                     noise(3900, .001, .038, volume=45),
                     tone(key=52, levels=(41, 17, 6), decay=.032,
                          cutoff=3600, volume=19, delay=.025)]),
        dict(cue="player.footstep.snow", name="雪地脚步", seconds=.51, variants=3, peakDb=-7, rmsDb=-22,
             externalManifest="SoundDesign/Snow_Footsteps/manifest.json"),
        dict(cue="player.footstep.water", name="浅水脚步", seconds=.37, variants=3, peakDb=-15, rmsDb=-30,
             layers=[tone(key=38, levels=(62, 14, 8), coarse=(0, 8, 17),
                          fine=(0, -17, 27), decay=.092, cutoff=1150, volume=34),
                     noise(2350, .01, .135, volume=47),
                     noise(750, .026, .15, volume=36, delay=.04,
                           repeats=((.082, .32),))]),
        dict(cue="fishing.cast", name="抛竿甩线", seconds=.45, variants=2, peakDb=-10, rmsDb=-26,
             layers=[noise(3700, .037, .16, volume=57),
                     noise(1000, .02, .14, volume=27, delay=.048),
                     tone(key=66, levels=(54, 12, 5), decay=.12, cutoff=3000,
                          volume=22, delay=.125)]),
        dict(cue="fishing.hook", name="鱼儿咬钩", seconds=.49, variants=2, peakDb=-11, rmsDb=-27,
             layers=[noise(3600, .007, .13, volume=51,
                           repeats=((.078, .3),)),
                     tone(key=68, waves=(0, 1, 0), levels=(70, 16, 6),
                          decay=.11, cutoff=3500, volume=39, delay=.081),
                     tone(key=75, waves=(0, 1, 0), levels=(69, 16, 6),
                          decay=.13, cutoff=3950, volume=33, delay=.156)]),
        dict(cue="fishing.reel", name="钓线收轮咔嗒", seconds=.35, variants=2, peakDb=-14, rmsDb=-29,
             layers=[tone(key=47, levels=(63, 19, 11), coarse=(0, 16, 26),
                          decay=.042, cutoff=2500, volume=40,
                          repeats=((.082, .56), (.156, .26))),
                     noise(2100, .003, .032, volume=37,
                           repeats=((.08, .5), (.15, .25)))]),
    ]
# endregion


# region LMMS 原生编排
def external_sound(cue, variant):
    sound = next((entry for entry in specs() if entry["cue"] == cue), {})
    if "externalManifest" not in sound:
        return None
    path = PROJECT / sound["externalManifest"]
    assert path.is_file(), f"Run SoundDesign/Snow_Footsteps/prepare_snow.py prepare first: {path}"
    manifest = json.loads(path.read_text(encoding="utf-8"))
    entry = next(entry for entry in manifest["sounds"] if entry["cue"] == cue and entry["variant"] == variant)
    asset = PROJECT / entry["file"]
    assert asset.is_file() and hashlib.sha256(asset.read_bytes()).hexdigest() == entry["sha256"], \
        f"Rebuild external recorded sound before finalizing: {asset}"
    return entry


def build():
    tree = ET.parse(SFX_ROOT / "MCP_Base.mmp")
    template = tree.find(".//track")
    tree.getroot().set("creatorversion", "1.2.2")
    container = tree.find(".//trackcontainer")
    container.clear()
    container.set("type", "song")
    layout = []
    slot = 0
    for sound in specs():
        for variant in range(1, sound["variants"] + 1):
            entry = dict(cue=sound["cue"], name=sound["name"], variant=variant,
                         seconds=sound["seconds"], peakDb=sound["peakDb"],
                         rmsDb=sound["rmsDb"], startSeconds=slot * 2 + .125,
                         file=f"Assets/Audio/Generated/{sound['cue']}__{variant:02}.wav")
            external = external_sound(sound["cue"], variant)
            if external:
                # 实录成品独立重建，保留槽位防止后续钓鱼片段偏移。
                entry.update({key: external[key] for key in ("seconds", "peakDb", "rmsDb", "externalManifest")})
                layout.append(entry)
                slot += 1
                continue
            layout.append(entry)
            # 草地使用实录鞋底压草和草叶摩擦，重建工程时也保留真实采样。
            if "recording" in sound:
                sample = ROOT / "Sources" / f"{sound['recording']}_{variant:02}.wav"
                assert sample.is_file(), f"Run prepare_grass.py first: {sample}"
                track = ET.SubElement(container, "track", muted="0", type="2",
                                      name=f"{sound['name']} / {variant}", solo="0")
                sample_track = ET.SubElement(track, "sampletrack", pan="0", vol="80")
                ET.SubElement(sample_track, "fxchain", numofeffects="0", enabled="0")
                ET.SubElement(track, "sampleclip", pos=str(slot * 192 + 12),
                              len=str(math.ceil(sound["seconds"] / sfx.TICK_SECONDS)),
                              src=sample.as_posix(), muted="0", off="0")
                slot += 1
                continue
            for layer_index, original in enumerate(sound["layers"]):
                patch = copy.deepcopy(original)
                shift = (0, .055, -.045)[variant - 1]
                patch["key"] += (0, 1, -1)[variant - 1]
                patch["decay"] *= 1 + shift
                patch["cutoff"] *= 1 + shift
                track = copy.deepcopy(template)
                track.set("name", f"{sound['name']} / {variant} / layer {layer_index + 1}")
                for pattern in track.findall("pattern"):
                    track.remove(pattern)
                sfx.configure(track, patch)
                pattern = ET.SubElement(track, "pattern", type="1", steps="16",
                                        len="192", pos=str(slot * 192), name=sound["cue"])
                duration = patch["length"] or patch["attack"] + patch["hold"] + patch["decay"] + .02
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
    tree.write(ROOT / "Player_Fishing.mmp", encoding="utf-8", xml_declaration=True)
    (ROOT / "layout.json").write_text(json.dumps(layout, ensure_ascii=False, indent=2) + "\n",
                                      encoding="utf-8")
    external_count = sum("externalManifest" in entry for entry in layout)
    print(f"Prepared {len(layout) - external_count} LMMS events and {external_count} external recordings "
          f"across {len(specs())} Cues; all {len(layout)} slots retained")
# endregion


# region LMMS 母带分切与 Unity 绑定
def finalize(only_grass=False):
    layout = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    if only_grass:
        layout = [entry for entry in layout if entry["cue"] == "player.footstep.grass"]
    raw = sfx.read_wave(ROOT / "renders" / "Player_Fishing_raw.wav")
    assert np.max(np.abs(raw)) < .9999, "LMMS mother track clipped"
    assert len(raw) >= round(max(x["startSeconds"] + x["seconds"] for x in layout) * RATE)
    manifest, clips = [], {}
    for entry in layout:
        external = external_sound(entry["cue"], entry["variant"])
        if external:
            # 连旧母带也只复用已核验的实录雪声，不覆盖正式素材。
            manifest.append(dict(entry, **external))
            continue
        start, end = (round(time * RATE) for time in
                      (entry["startSeconds"], entry["startSeconds"] + entry["seconds"]))
        audio = raw[start:end].mean(axis=1)
        assert len(audio) == round(entry["seconds"] * RATE)
        audio -= audio.mean()
        fade_in, fade_out = round(.002 * RATE), round(.025 * RATE)
        audio[:fade_in] *= np.linspace(0, 1, fade_in)
        audio[-fade_out:] *= np.linspace(1, 0, fade_out)
        peak = float(np.max(np.abs(audio)))
        rms = float(np.sqrt(np.mean(audio ** 2)))
        assert peak > .0001 and rms > .00001 and np.isfinite(audio).all(), entry["cue"]
        audio *= min(10 ** (entry["peakDb"] / 20) / peak, 10 ** (entry["rmsDb"] / 20) / rms)
        asset = PROJECT / entry["file"]
        sfx.write_wave(asset, audio)
        guid = sfx.update_meta(asset)
        clips.setdefault(entry["cue"], []).append(guid)
        manifest.append(dict(entry, guid=guid,
                             sha256=hashlib.sha256(asset.read_bytes()).hexdigest(),
                             peakDb=round(20 * math.log10(np.max(np.abs(audio))), 2),
                             rmsDb=round(20 * math.log10(np.sqrt(np.mean(audio ** 2))), 2)))
    cue_guids = {id: feedback.add_cue(id, variants) for id, variants in clips.items()}
    # 高频脚步仅保留两个重叠音源，所有新 Cue 仍由正式 Catalog 和总线统一管理。
    for id in cue_guids:
        path = PROJECT / "Assets/Resources/Audio/Cues" / (id + ".asset")
        config = path.read_text(encoding="utf-8")
        footstep = id.startswith("player.footstep.")
        cooldown = .08 if footstep else .10 if id == "fishing.reel" else .08
        config = re.sub(r"  cooldown: [^\n]+", f"  cooldown: {cooldown}", config)
        config = re.sub(r"  maxInstances: [^\n]+", f"  maxInstances: {2 if footstep else 3}", config)
        path.write_text(config, encoding="utf-8")
    catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    text = catalog.read_text(encoding="utf-8")
    for guid in cue_guids.values():
        line = f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"
        if line not in text:
            text = text.rstrip() + "\n" + line + "\n"
    catalog.write_text(text, encoding="utf-8")
    if only_grass:
        previous = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
        replacements = {entry["file"]: entry for entry in manifest}
        manifest = [replacements.get(entry["file"], entry) for entry in previous["sounds"]]
    preview = []
    for entry in manifest:
        if entry["variant"] == 1:
            preview.extend((sfx.read_wave(PROJECT / entry["file"]).mean(axis=1),
                            np.zeros(round(.24 * RATE))))
    sfx.write_wave(ROOT / "Preview_Player_Fishing.wav", np.concatenate(preview))
    grass_preview = []
    for entry in manifest:
        if entry["cue"] == "player.footstep.grass":
            grass_preview.extend((sfx.read_wave(PROJECT / entry["file"]).mean(axis=1),
                                  np.zeros(round(.16 * RATE))))
    sfx.write_wave(ROOT / "Preview_Grass.wav", np.concatenate(grass_preview * 2))
    (ROOT / "manifest.json").write_text(json.dumps(dict(
        engine="LMMS 1.2.2 via MCP; snow: CC0 field recording", sampleRate=RATE, channels=1, sounds=manifest),
        ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Finalized {len(layout)} clips; updated {len(cue_guids)} LMMS Cues and retained external recordings")


def verify():
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    catalog = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")
    sounds = manifest["sounds"]
    guids = set()
    for entry in sounds:
        asset = PROJECT / entry["file"]
        with wave.open(str(asset), "rb") as reader:
            assert (reader.getframerate(), reader.getnchannels(), reader.getsampwidth()) == (RATE, 1, 2), asset
            assert reader.getnframes() == round(entry["seconds"] * RATE), asset
        assert hashlib.sha256(asset.read_bytes()).hexdigest() == entry["sha256"]
        assert f"guid: {entry['guid']}" in Path(str(asset) + ".meta").read_text(encoding="utf-8")
        cue_path = PROJECT / "Assets/Resources/Audio/Cues" / (entry["cue"] + ".asset")
        cue_text = cue_path.read_text(encoding="utf-8")
        assert f"id: {entry['cue']}" in cue_text and f"guid: {entry['guid']}" in cue_text
        cue_meta = Path(str(cue_path) + ".meta").read_text(encoding="utf-8")
        cue_guid = re.search(r"^guid: ([0-9a-f]{32})$", cue_meta, re.M)[1]
        assert f"guid: {cue_guid}, type: 2" in catalog
        assert "spatialBlend: 1" in cue_text and "bus: 1" in cue_text
        assert entry["guid"] not in guids
        guids.add(entry["guid"])
    assert len(sounds) == 24 and len(set(entry["cue"] for entry in sounds)) == 9
    print("Verified 9 spatial Cues and 24 mono PCM clips, GUIDs, hashes and Catalog")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("build", "finalize", "verify"))
    parser.add_argument("--only-grass", action="store_true", help="仅替换草地脚步，保留其他音效")
    args = parser.parse_args()
    if args.command == "finalize":
        finalize(only_grass=args.only_grass)
    else:
        {"build": build, "verify": verify}[args.command]()
