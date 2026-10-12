"""编排原创 LMMS 玩法音效，整理 MCP 渲染母带并绑定现有 Unity Cue。"""

from __future__ import annotations

import argparse
import copy
import hashlib
import io
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
ASSETS = PROJECT / "Assets/Audio/Generated"
RATE = 48000
TICK_SECONDS = 2 / 192


# region 音色与分层编排
def layer(key=57, waves=(0, 0, 6), levels=(64, 20, 12), coarse=(0, 17, 0),
          fine=(0, 23, 0), attack=.002, decay=.08, release=.025,
          cutoff=3000, volume=55, delay=0, repeats=(), hold=0, sustain=0, length=None):
    return dict(key=key, waves=waves, levels=levels, coarse=coarse, fine=fine,
                attack=attack, decay=decay, release=release, cutoff=cutoff,
                volume=volume, delay=delay, repeats=repeats, hold=hold,
                sustain=sustain, length=length)


def noise(cutoff, attack, decay, volume=65, **kwargs):
    return layer(waves=(6, 0, 0), levels=(100, 0, 0), cutoff=cutoff,
                 attack=attack, decay=decay, volume=volume, **kwargs)


def swing(cutoff, attack, decay, weight):
    # 空气层与低频掠过层共同构成挥动声，不在挥空时叠加受击音。
    return [noise(cutoff, attack, decay),
            noise(450, attack * .8, decay * .8, volume=weight, delay=.008)]


def impact(kind, strong=False):
    power = 1.12 if strong else 1
    patches = {
        "default": [layer(key=27, levels=(65, 18, 12), coarse=(0, 14, 0), decay=.065),
                    noise(2900, .0015, .037, volume=50)],
        "flesh": [layer(key=22, levels=(75, 10, 10), coarse=(0, 7, 0), decay=.095, cutoff=800),
                  noise(950, .002, .075, volume=64), noise(2400, .001, .02, volume=23)],
        "wood": [layer(key=34, coarse=(0, 16, 28), levels=(64, 26, 11), decay=.09,
                       cutoff=2400, repeats=((.031, .18),)),
                 noise(3800, .001, .024, volume=53)],
        "stone": [noise(6200, .001, .027, volume=77, repeats=((.023, .33), (.057, .18))),
                  layer(key=43, coarse=(0, 19, 31), levels=(56, 27, 12), decay=.044,
                        cutoff=4100, volume=47)],
        "metal": [layer(key=66, coarse=(0, 15, 23), fine=(0, 31, -27),
                        levels=(57, 26, 14), decay=.25, volume=45, cutoff=5600),
                  noise(5500, .001, .024, volume=71)],
        "foliage": [noise(4700, .009, .1, volume=51,
                          repeats=((.047, .42), (.096, .2))),
                    noise(1200, .007, .052, volume=37)],
    }[kind]
    for patch in patches:
        patch["volume"] *= power
    return patches


def specs():
    output = []

    def add(cue, name, group, patches, seconds, peak_db=-7, rms_db=-22, loop=False):
        output.append(dict(cue=cue, name=name, group=group, layers=patches, seconds=seconds,
                           peakDb=peak_db, rmsDb=rms_db, loop=loop))

    for weapon, name, cutoff, attack, decay, weight in [
        ("generic", "通用挥动", 3800, .047, .081, 27),
        ("knife", "短刀快速划过", 6700, .026, .052, 14),
        ("axe", "斧头沉重挥动", 2150, .087, .103, 54),
        ("pickaxe", "镐头挥动", 3300, .065, .084, 38),
        ("spear", "长矛刺出", 5200, .024, .101, 24),
        ("blunt", "钝器厚重挥动", 1500, .098, .116, 68),
    ]:
        add(f"combat.weapon.{weapon}.attack", name, "Attacks",
            swing(cutoff, attack, decay, weight), .36, peak_db=-9, rms_db=-25)

    add("combat.hit", "通用伤害反馈", "Impacts", impact("flesh"), .25)
    for material, name, seconds in [("default", "通用碰撞", .2), ("foliage", "草叶割裂", .3),
                                    ("wood", "木头敲裂", .24), ("stone", "石头碎屑", .25),
                                    ("metal", "金属撞击", .43), ("flesh", "肉体闷击", .25)]:
        add(f"combat.impact.{material}", name, "Impacts", impact(material), seconds)
    for weapon, material, name, seconds in [
        ("knife", "foliage", "刀割草叶", .24), ("knife", "stone", "刀碰石头", .23),
        ("axe", "wood", "斧头砍木头", .3), ("pickaxe", "stone", "镐凿石头", .3),
    ]:
        patches = impact(material, strong=True)
        if weapon == "knife":
            for patch in patches:
                patch["decay"] *= .7
        if weapon == "pickaxe":
            patches.append(layer(key=71, coarse=(0, 13, 25), decay=.13, volume=22,
                                 levels=(62, 24, 9), cutoff=6400, delay=.012))
        add(f"combat.impact.{weapon}.{material}", name, "Impacts", patches, seconds)

    add("item.pickup", "拿起物品的轻触与衣料声", "Interactions",
        [layer(key=50, decay=.052, cutoff=1800, volume=35),
         noise(1700, .012, .065, volume=31, delay=.012)], .22, -12, -27)
    add("item.drop", "放下物品的短接触声", "Interactions",
        [layer(key=37, decay=.082, cutoff=2100, repeats=((.043, .18),)),
         noise(2200, .002, .036, volume=41)], .26, -10, -25)
    add("item.hoe.till", "锄土与土粒散落", "Interactions",
        [layer(key=29, decay=.062, cutoff=1600, volume=44),
         noise(2300, .005, .15, volume=61, repeats=((.07, .2),))], .36, -9, -24)
    add("door.open", "木门摩擦后打开", "Interactions",
        [noise(900, .042, .18, volume=47),
         layer(key=35, coarse=(0, 13, 0), decay=.05, volume=40, delay=.18)], .43, -11, -26)
    add("door.close", "木门合上的闷响", "Interactions",
        [noise(1100, .027, .09, volume=39),
         layer(key=28, coarse=(0, 16, 0), decay=.13, cutoff=1400, volume=62, delay=.07)], .35, -9, -24)
    add("food.eat", "柔软咀嚼", "Interactions",
        [noise(1150, .005, .055, volume=47, repeats=((.08, .75), (.16, .54))),
         layer(key=24, decay=.035, cutoff=700, volume=24)], .34, -13, -28)
    add("food.crunch", "脆食碎裂", "Interactions",
        [noise(4800, .001, .035, volume=58, repeats=((.037, .6), (.085, .35))),
         layer(key=40, decay=.043, volume=32)], .27, -11, -26)
    add("food.drink", "短促喝水与吞咽", "Interactions",
        [layer(key=30, coarse=(0, 7, 19), fine=(0, -30, 24), decay=.065,
               cutoff=750, volume=38, repeats=((.09, .55), (.2, .29))),
         noise(1250, .018, .16, volume=30)], .38, -13, -28)
    for action, name, key, delays in [("hover", "轻柔悬停", 66, ()), ("click", "柔和点击", 58, ()),
                                      ("confirm", "确认上行短音", 58, ((.065, 7), (.13, 12))),
                                      ("cancel", "返回下行短音", 61, ((.072, -7),))]:
        patches = [layer(key=key, waves=(0, 0, 1), levels=(76, 14, 7),
                         coarse=(0, 12, 19), fine=(0, 0, 0), decay=.046,
                         cutoff=2600, volume=48)]
        for delay, interval in delays:
            patches.append(dict(patches[0], key=key + interval, delay=delay, volume=42))
        add(f"ui.{action}", name, "Interactions", patches, .29 if delays else .15, -13, -28)
    patches = [noise(850, .18, .2, volume=30, hold=.2, sustain=.6, length=5.5),
               noise(1900, .026, .1, volume=21, repeats=tuple((i * .43, .5 + (i % 3) * .15)
                                                           for i in range(1, 12)))]
    add("player.wet_clothes.move.loop", "湿衣服轻柔摩擦循环", "Interactions", patches,
        4.4, -16, -31, loop=True)
    return output


def envelope(node, patch, enabled=True):
    node.attrib.clear()
    node.attrib.update({"pdel": "0", "hold": str(math.sqrt(patch.get("hold", 0) / 5)),
                        "att": str(math.sqrt(patch["attack"] / 5)),
                        "dec": str(math.sqrt(patch["decay"] / 5)),
                        "rel": str(math.sqrt(patch["release"] / 5)),
                        "sustain": str(patch.get("sustain", 0)), "amt": "1" if enabled else "0",
                        "lamt": "0", "lspd": "0.1", "lshp": "0", "lpdel": "0",
                        "latt": "0", "x100": "0", "ctlenvamt": "0"})


def configure(track, patch):
    inst = track.find("instrumenttrack")
    inst.set("vol", str(round(patch["volume"])))
    inst.set("basenote", "57")
    inst.set("fxch", "0")
    osc = inst.find("instrument/tripleoscillator")
    osc.attrib.clear()
    for i in range(3):
        for param, value in {"vol": patch["levels"][i], "wavetype": patch["waves"][i],
                             "coarse": patch["coarse"][i], "finel": patch["fine"][i],
                             "finer": patch["fine"][i], "pan": 0, "phoffset": 0,
                             "stphdetun": 0, "userwavefile": ""}.items():
            osc.set(f"{param}{i}", str(value))
        osc.set(f"modalgo{i + 1}", "2")
    shape = inst.find("eldata")
    shape.attrib.update(fwet="1", ftype="0", fres="0.6", fcut=str(patch["cutoff"]))
    envelope(shape.find("elvol"), patch)
    for tag in ("elcut", "elres"):
        envelope(shape.find(tag), patch, enabled=False)


def build():
    base = ET.parse(ROOT / "MCP_Base.mmp")
    template = base.find(".//track")
    clips = []
    for group in ("Attacks", "Impacts", "Interactions"):
        tree = copy.deepcopy(base)
        tree.getroot().set("creatorversion", "1.2.2")
        container = tree.find(".//trackcontainer")
        container.clear()
        container.set("type", "song")
        slot = 0
        for spec in [s for s in specs() if s["group"] == group]:
            for variant in range(1, 2 if spec["loop"] else 4):
                entry = {k: v for k, v in spec.items() if k != "layers"}
                entry.update(variant=variant, project=f"Gameplay_{group}.mmp",
                             startSeconds=slot * 2 + .125,
                             file=f"Assets/Audio/Generated/{spec['cue']}__{variant:02}.wav")
                clips.append(entry)
                for index, original in enumerate(spec["layers"]):
                    patch = copy.deepcopy(original)
                    shift = (0, .035, -.03)[variant - 1]
                    patch["decay"] *= 1 + shift
                    patch["cutoff"] *= 1 + shift * 1.5
                    patch["key"] += (0, 1, -1)[variant - 1]
                    track = copy.deepcopy(template)
                    track.set("name", f"{spec['name']} / {variant} / layer {index + 1}")
                    for pattern in track.findall("pattern"):
                        track.remove(pattern)
                    configure(track, patch)
                    bars = 4 if spec["loop"] else 1
                    pattern = ET.SubElement(track, "pattern", type="1", steps="16", len=str(192 * bars),
                                            pos=str(slot * 192), name=spec["cue"])
                    duration = patch["length"] or patch["attack"] + patch["hold"] + patch["decay"] + .02
                    for delay, velocity in ((0, 1),) + tuple(patch["repeats"]):
                        ET.SubElement(pattern, "note", pos=str(round((.125 + patch["delay"] + delay) / TICK_SECONDS)),
                                      key=str(patch["key"]), len=str(max(3, math.ceil(duration / TICK_SECONDS))),
                                      vol=str(round(78 * velocity)), pan="0")
                    container.append(track)
                slot += 4 if spec["loop"] else 1
        tree.find(".//timeline").set("lp1pos", str(slot * 192))
        ET.indent(tree, space="  ")
        tree.write(ROOT / f"Gameplay_{group}.mmp", encoding="utf-8", xml_declaration=True)
    (ROOT / "layout.json").write_text(json.dumps(clips, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {len(clips)} clips across 3 editable LMMS projects")
# endregion


# region 母带裁切与 Unity 绑定
def read_wave(path):
    with wave.open(str(path), "rb") as reader:
        assert reader.getframerate() == RATE and reader.getsampwidth() == 2, path
        return np.frombuffer(reader.readframes(reader.getnframes()), dtype="<i2").reshape(
            -1, reader.getnchannels()).astype(np.float64) / 32768


def write_wave(path, data):
    # 先写完整临时文件再替换，避免 Unity 导入半个 WAV 或 Windows 映射文件不能截断。
    stream = io.BytesIO()
    with wave.open(stream, "wb") as writer:
        writer.setnchannels(1)
        writer.setsampwidth(2)
        writer.setframerate(RATE)
        writer.writeframes(np.rint(np.clip(data, -1, 1) * 32767).astype("<i2").tobytes())
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_bytes(stream.getvalue())
    temporary.replace(path)


def update_meta(path):
    target = Path(str(path) + ".meta")
    existing = target.read_text(encoding="utf-8") if target.exists() else ""
    found = re.search(r"^guid: ([0-9a-f]+)$", existing, re.MULTILINE)
    guid = found[1] if found else uuid.uuid4().hex
    text = f"""fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: 0
    quality: 1
    conversionMode: 0
    preloadAudioData: 1
  platformSettingOverrides: {{}}
  forceToMono: 1
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData: LMMS_MCP_Gameplay
  assetBundleName:
  assetBundleVariant:
"""
    with target.open("r+" if target.exists() else "w", encoding="utf-8") as writer:
        writer.write(text)
        writer.truncate()
    return guid


def finalize():
    layout = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    raw = {}
    # 所有母带齐全且有效后才改游戏资源，避免漏渲一组时只更新半套素材。
    for project in dict.fromkeys(entry["project"] for entry in layout):
        raw[project] = read_wave(ROOT / "renders" / (Path(project).stem + "_raw.wav"))
        assert np.max(np.abs(raw[project])) < .9999, f"Clipped LMMS master: {project}"
        required = max(entry["startSeconds"] + entry["seconds"] + (.5 if entry["loop"] else 0)
                       for entry in layout if entry["project"] == project)
        assert len(raw[project]) >= round(required * RATE), f"Short LMMS master: {project}"
    manifest = []
    groups = {}
    bindings = {}
    for entry in layout:
        project = entry["project"]
        offset = entry["startSeconds"] + (.5 if entry["loop"] else 0)
        data = raw[project][round(offset * RATE):round((offset + entry["seconds"]) * RATE)].mean(axis=1)
        assert len(data) == round(entry["seconds"] * RATE), entry["file"]
        data -= data.mean()
        if entry["loop"]:
            n = round(.12 * RATE)
            theta = np.linspace(0, np.pi / 2, n)
            blend = data[-n:] * np.cos(theta) + data[:n] * np.sin(theta)
            data = np.concatenate((data[n:-n], blend))
        else:
            n = round(.0015 * RATE)
            data[:n] *= np.linspace(0, 1, n)
            n = round(.025 * RATE)
            data[-n:] *= np.linspace(1, 0, n)
        peak, rms = float(np.max(np.abs(data))), float(np.sqrt(np.mean(data ** 2)))
        assert rms > 1e-5 and np.isfinite(data).all(), entry["file"]
        data *= min(10 ** (entry["peakDb"] / 20) / peak, 10 ** (entry["rmsDb"] / 20) / rms)
        output = PROJECT / entry["file"]
        try:
            write_wave(output, data)
        except OSError as error:
            raise OSError(f"Unable to write WAV: {output}") from error
        guid = update_meta(output)
        bindings.setdefault(entry["cue"], []).append(guid)
        manifest.append(dict(entry, guid=guid, seconds=round(len(data) / RATE, 3),
                             peakDb=round(20 * math.log10(np.max(np.abs(data))), 2),
                             rmsDb=round(20 * math.log10(np.sqrt(np.mean(data ** 2))), 2),
                             sha256=hashlib.sha256(output.read_bytes()).hexdigest(),
                             seamStep=float(abs(data[0] - data[-1])) if entry["loop"] else None))
        if entry["variant"] == 1 and not entry["loop"]:
            groups.setdefault(entry["group"], []).extend((data, np.zeros(round(.28 * RATE))))
    for cue, guids in bindings.items():
        path = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
        text = path.read_text(encoding="utf-8")
        refs = "  clips:\n" + "".join(f"  - {{fileID: 8300000, guid: {guid}, type: 3}}\n" for guid in guids)
        text, count = re.subn(r"  clips:\n(?:  - [^\n]*\n)+", refs, text)
        assert count == 1, path
        with path.open("r+", encoding="utf-8") as writer:
            writer.write(text)
            writer.truncate()
    for group, parts in groups.items():
        write_wave(ROOT / f"Preview_{group}.wav", np.concatenate(parts))
    (ROOT / "manifest.json").write_text(json.dumps(dict(engine="LMMS 1.2.2 via MCP",
                                                         sampleRate=RATE, channels=1, sounds=manifest),
                                                   ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Finalized {len(manifest)} mono clips; bound {len(bindings)} existing cues")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("build", "finalize"))
    args = parser.parse_args()
    build() if args.command == "build" else finalize()
