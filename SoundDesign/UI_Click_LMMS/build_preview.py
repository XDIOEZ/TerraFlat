"""生成 LMMS 音效工程，并把 LMMS 导出的母带切成独立试听音效。"""

from __future__ import annotations

import argparse
import base64
import copy
import html
import json
import math
import wave
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
RATE = 48000
EVENTS = ["click_01", "click_02", "click_03", "hover", "confirm", "cancel", "open", "close"]
LABELS = ["点击 1", "点击 2", "点击 3", "悬停", "确认", "返回", "打开", "关闭"]
STYLES = [
    dict(id="A_wood", name="A · 轻木敲击", description="短促的木质小敲击，带一点点颗粒感。", key=64,
         waves=[0, 0, 0], volumes=[67, 19, 8], coarse=[0, 19, 24], fine=[0, -18, 31],
         attack=.0015, decay=.036, release=.009, cutoff=4100, volume=65),
    dict(id="B_soft", name="B · 圆润软按键", description="圆润、低一点的软按键声，适合频繁点菜单。", key=52,
         waves=[0, 0, 1], volumes=[78, 13, 5], coarse=[0, 12, 19], fine=[0, 0, 0],
         attack=.0025, decay=.055, release=.013, cutoff=2300, volume=65),
    dict(id="C_pixel", name="C · 柔和像素音", description="清楚的短音符，留一点像素游戏的味道。", key=60,
         waves=[1, 0, 0], volumes=[66, 17, 6], coarse=[0, 0, 12], fine=[0, 0, 0],
         attack=.002, decay=.048, release=.012, cutoff=3300, volume=60),
]


# region LMMS 工程
def envelope(parent: ET.Element, attack: float, decay: float, release: float) -> None:
    # LMMS 1.2 的时间旋钮使用五秒乘以旋钮值平方，短包络让按钮声音快速收尾。
    parent.attrib.clear()
    parent.attrib.update({"pdel": "0", "att": f"{math.sqrt(attack / 5):.6f}", "hold": "0",
                          "dec": f"{math.sqrt(decay / 5):.6f}", "sustain": "0",
                          "rel": f"{math.sqrt(release / 5):.6f}", "amt": "1",
                          "lamt": "0", "lspd": "0.1", "lshp": "0", "lpdel": "0",
                          "latt": "0", "x100": "0", "ctlenvamt": "0"})


def oscillator(inst: ET.Element, style: dict) -> None:
    osc = inst.find("instrument/tripleoscillator")
    osc.attrib.clear()
    for i in range(3):
        for param, value in {"vol": style["volumes"][i], "wavetype": style["waves"][i],
                             "coarse": style["coarse"][i], "finel": style["fine"][i],
                             "finer": style["fine"][i], "pan": 0, "phoffset": 0,
                             "stphdetun": 0, "userwavefile": ""}.items():
            osc.set(f"{param}{i}", str(value))
        osc.set(f"modalgo{i + 1}", "2")
    inst.set("basenote", "57")
    inst.set("vol", str(style["volume"]))
    inst.set("fxch", "0")
    shape = inst.find("eldata")
    shape.attrib.update({"fwet": "1", "ftype": "0", "fcut": str(style["cutoff"]), "fres": "0.6"})
    envelope(shape.find("elvol"), style["attack"], style["decay"], style["release"])
    for tag in ["elcut", "elres"]:
        node = shape.find(tag)
        envelope(node, 0, .05, .01)
        node.set("amt", "0")


def event_notes(style: dict, event: str) -> list[tuple[int, int, int, int]]:
    # 音符键值沿用 LMMS 原生 C0=0，确认向上、返回向下，共享同一套音色。
    key = style["key"]
    if event == "click_01":
        return [(12, key, 9, 85)]
    if event == "click_02":
        return [(12, key + 1, 9, 81)]
    if event == "click_03":
        return [(12, key - 1, 9, 88)]
    if event == "hover":
        return [(12, key + 5, 4, 43)]
    if event == "confirm":
        return [(12, key, 8, 79), (20, key + 7, 9, 85), (27, key + 12, 8, 62)]
    if event == "cancel":
        return [(12, key + 2, 7, 68), (20, key - 5, 9, 64)]
    if event == "open":
        return [(12, key - 5, 8, 65), (19, key + 2, 8, 71)]
    return [(12, key, 7, 63), (19, key - 7, 9, 57)]


def build_project(base_path: Path) -> None:
    tree = ET.parse(base_path)
    root = tree.getroot()
    root.set("creator", "LMMS")
    root.set("creatorversion", "1.2.2")
    root.find("head").set("bpm", "120")
    root.find("head").set("mastervol", "65")
    container = root.find("song/trackcontainer")
    tracks = container.findall("track")[:3]
    for extra in container.findall("track")[3:]:
        container.remove(extra)
    wood_texture = copy.deepcopy(tracks[0])
    for pattern in wood_texture.findall("pattern"):
        wood_texture.remove(pattern)
    wood_texture.set("name", "A - Wood Texture")
    texture_style = dict(STYLES[0], volumes=[100, 0, 0], waves=[6, 0, 0],
                         coarse=[0, 0, 0], fine=[0, 0, 0], attack=.0008,
                         decay=.011, release=.004, cutoff=2600, volume=12)
    oscillator(wood_texture.find("instrumenttrack"), texture_style)
    container.append(wood_texture)
    manifest = {"engine": "LMMS 1.2.2 TripleOscillator", "sample_rate": RATE,
                "format": "mono PCM 16-bit WAV", "integration": "preview only, outside Assets",
                "source_project": "FlatWorld_UI_Clicks.mmp", "events": []}
    for i, (track, style) in enumerate(zip(tracks, STYLES)):
        oscillator(track.find("instrumenttrack"), style)
        for child in track.findall("pattern"):
            track.remove(child)
        for j, event in enumerate(EVENTS):
            bar = i * len(EVENTS) + j
            notes = event_notes(style, event)
            pattern = ET.SubElement(track, "pattern", {"type": "1", "steps": "16", "len": "192",
                                    "pos": str(bar * 192), "name": f"{style['id']} / {event}"})
            for pos, key, length, volume in notes:
                ET.SubElement(pattern, "note", {"pos": str(pos), "key": str(key), "len": str(length),
                                                "vol": str(volume), "pan": "0"})
            if i == 0:
                texture_pattern = ET.SubElement(wood_texture, "pattern", dict(pattern.attrib))
                for pos, key, _, volume in notes:
                    ET.SubElement(texture_pattern, "note", {"pos": str(pos), "key": str(key),
                                  "len": "3", "vol": str(volume), "pan": "0"})
            manifest["events"].append({"style": style["id"], "event": event,
                                       "label": LABELS[j], "bar": bar + 1,
                                       "slice_start_seconds": bar * 2 + .100,
                                       "file": f"{style['id']}/ui.{event.split('_')[0]}__{event[-2:] if event.startswith('click') else '01'}.wav"})
    root.find("song/trackcontainer").set("width", "1100")
    root.find("song/trackcontainer").set("height", "440")
    notes = root.find("song/projectnotes")
    notes.text = "FlatWorld UI candidates: A wood / B soft / C pixel. One cue per bar at 120 BPM. Each group: click 1, click 2, click 3, hover, confirm, cancel, open, close. Original TripleOscillator patches; preview only."
    ET.indent(tree, space="  ")
    tree.write(ROOT / "FlatWorld_UI_Clicks.mmp", encoding="utf-8", xml_declaration=True)
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Created LMMS project: 4 tracks, 24 cues, 48 seconds")
# endregion


# region 音频拆分与试听
def read_wav(path: Path) -> tuple[int, np.ndarray]:
    with wave.open(str(path), "rb") as wav:
        rate, channels, width = wav.getframerate(), wav.getnchannels(), wav.getsampwidth()
        if width != 2:
            raise ValueError(f"Expected PCM16 render, got {width * 8}-bit: {path}")
        data = np.frombuffer(wav.readframes(wav.getnframes()), dtype="<i2").astype(np.float64) / 32768
    return rate, data.reshape(-1, channels).mean(axis=1)


def write_wav(path: Path, data: np.ndarray) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as wav:
        wav.setparams((1, 2, RATE, 0, "NONE", "not compressed"))
        wav.writeframes(np.rint(np.clip(data, -1, 1) * 32767).astype("<i2").tobytes())


def trim(data: np.ndarray) -> np.ndarray:
    significant = np.flatnonzero(np.abs(data) > 10 ** (-68 / 20))
    if significant.size == 0:
        raise ValueError("LMMS rendered an empty cue")
    # 保留两毫秒起音，并让尾部自然落到零，避免剪辑引入爆音。
    begin = max(0, int(significant[0]) - int(.002 * RATE))
    end = min(len(data), int(significant[-1]) + int(.009 * RATE))
    data = data[begin:end].copy()
    data -= data.mean()
    fade_in = min(int(.0007 * RATE), len(data))
    fade_out = min(int(.006 * RATE), len(data))
    data[:fade_in] *= np.linspace(0, 1, fade_in)
    data[-fade_out:] *= np.linspace(1, 0, fade_out)
    return data


def sequence(clips: list[np.ndarray], spacing: float = .42) -> np.ndarray:
    pieces = [np.zeros(int(.2 * RATE))]
    for clip in clips:
        pieces.extend([clip, np.zeros(int(spacing * RATE))])
    return np.concatenate(pieces)


def export(master_path: Path) -> None:
    rate, master = read_wav(master_path)
    if rate != RATE:
        raise ValueError(f"Render at {RATE} Hz; got {rate}")
    manifest_path = ROOT / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    clips = {}
    for entry in manifest["events"]:
        start = round(entry["slice_start_seconds"] * RATE)
        clips[entry["file"]] = trim(master[start:start + int(.7 * RATE)])
    for style in STYLES:
        entries = [x for x in manifest["events"] if x["style"] == style["id"]]
        reference = clips[entries[0]["file"]]
        rms = np.sqrt(np.mean(reference ** 2))
        gain = .10 / max(rms, 1e-6)
        peak = max(float(np.abs(clips[x["file"]]).max()) for x in entries)
        gain = min(gain, .42 / max(peak, 1e-6))
        for entry in entries:
            data = clips[entry["file"]] * gain
            clips[entry["file"]] = data
            write_wav(ROOT / entry["file"], data)
            entry["duration_ms"] = round(len(data) / RATE * 1000, 1)
            entry["peak_dbfs"] = round(20 * math.log10(max(np.abs(data).max(), 1e-9)), 1)
        clicks = [clips[entries[j]["file"]] for j in range(3)]
        full = sequence(clicks * 2 + [clips[x["file"]] for x in entries[3:]])
        write_wav(ROOT / f"preview_{style['id']}.wav", full)
    compare = []
    for style in STYLES:
        entries = [x for x in manifest["events"] if x["style"] == style["id"]]
        compare.extend([clips[entries[j % 3]["file"]] for j in range(6)])
        compare.append(np.zeros(int(.8 * RATE)))
    write_wav(ROOT / "ABC_click_comparison.wav", sequence(compare, spacing=.32))
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    make_html(manifest)
    print(json.dumps({"files": len(clips), "style_previews": 3, "comparison": 1,
                      "click_duration_ms": [x["duration_ms"] for x in manifest["events"] if x["event"] == "click_01"],
                      "peak_max_dbfs": max(x["peak_dbfs"] for x in manifest["events"])}, ensure_ascii=False))


def make_html(manifest: dict) -> None:
    audio = {}
    for entry in manifest["events"]:
        audio[entry["file"]] = "data:audio/wav;base64," + base64.b64encode((ROOT / entry["file"]).read_bytes()).decode()
    for style in STYLES:
        key = f"preview_{style['id']}.wav"
        audio[key] = "data:audio/wav;base64," + base64.b64encode((ROOT / key).read_bytes()).decode()
    comparison = "ABC_click_comparison.wav"
    audio[comparison] = "data:audio/wav;base64," + base64.b64encode((ROOT / comparison).read_bytes()).decode()
    cards = []
    for style in STYLES:
        entries = [x for x in manifest["events"] if x["style"] == style["id"]]
        buttons = "".join(f'<button data-file="{html.escape(x["file"])}">{x["label"]}<small>{x["duration_ms"]:.0f} ms</small></button>' for x in entries)
        cards.append(f'<article><h2>{style["name"]}</h2><p>{style["description"]}</p><div class="buttons">{buttons}</div><div class="actions"><button data-file="preview_{style["id"]}.wav">整套试听</button><button data-repeat="{entries[0]["file"]}">连续点 10 次</button></div></article>')
    template = '''<!doctype html>
<html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>FlatWorld · UI 点击音效试听</title>
<style>
:root{color-scheme:dark;font-family:"Segoe UI","Microsoft YaHei",sans-serif;background:#181818;color:#eee}
body{max-width:1120px;margin:48px auto;padding:0 24px}h1{font-size:30px;margin-bottom:12px}h2{font-size:21px;margin:0 0 14px}
p{line-height:1.8;color:#bbb}header{border-bottom:1px solid #444;padding-bottom:26px}.controls{display:flex;gap:18px;align-items:center;flex-wrap:wrap;margin-top:22px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(285px,1fr));gap:18px;margin-top:26px}article{background:#242424;border:1px solid #454545;padding:24px;border-radius:10px}
.buttons{display:grid;grid-template-columns:1fr 1fr;gap:9px}button{font:inherit;background:#363636;color:#eee;border:1px solid #555;padding:12px 15px;border-radius:5px;cursor:pointer}
button:hover{background:#454545;border-color:#a99354}button:active{background:#635636}button:focus-visible{outline:2px solid #d5bc70;outline-offset:2px}small{display:block;font-size:12px;color:#aaa;margin-top:5px}
.actions{display:flex;gap:9px;flex-wrap:wrap;border-top:1px solid #444;margin-top:20px;padding-top:18px}.actions button{font-size:14px;padding:10px}.accent{color:#e1ca85}input{accent-color:#d5bc70}footer{margin-top:28px;color:#aaa;font-size:14px}#status{min-height:24px;color:#d5bc70}
</style>
<header><h1>UI 点击音效试听</h1><p>三种声音方向，每套都有点击、悬停、确认、返回、打开和关闭。点击下面的按钮就能听。</p>
<div class="controls"><label>试听音量 <input type="range" id="volume" min="0" max="100" value="70" aria-label="试听音量"><span id="volume-label">70%</span></label><button data-file="ABC_click_comparison.wav">A → B → C 点击对比</button><button id="stop">停止播放</button></div></header>
<main class="grid">__CARDS__</main><p id="status" aria-live="polite">等待试听</p>
<footer>整套试听顺序：点击 1 / 2 / 3 各两轮 → 悬停 → 确认 → 返回 → 打开 → 关闭。<br>LMMS 原创合成 · 游戏音效尚未替换 · 所有音频都已嵌入此页面，可离线试听。</footer>
<script>
const sources=__AUDIO__;
const volume=document.querySelector('#volume'),status=document.querySelector('#status');
const active=new Set();let pending=[],generation=0;
function stop(){generation++;pending.forEach(clearTimeout);pending=[];active.forEach(a=>{a.pause();a.currentTime=0});active.clear()}
function play(file){const a=new Audio(sources[file]);a.volume=volume.value/100;active.add(a);a.onended=()=>active.delete(a);a.onerror=()=>{active.delete(a);status.textContent='播放失败，请用浏览器打开页面'};a.play().catch(e=>{active.delete(a);status.textContent='播放失败：'+e.message})}
document.querySelectorAll('[data-file]').forEach(b=>b.onclick=()=>{stop();play(b.dataset.file);status.textContent='正在播放：'+b.textContent});
document.querySelectorAll('[data-repeat]').forEach(b=>b.onclick=()=>{stop();const token=generation;for(let i=0;i<10;i++)pending.push(setTimeout(()=>{if(token===generation)play(b.dataset.repeat)},i*130));status.textContent='正在连续点击 10 次'});
volume.oninput=()=>{document.querySelector('#volume-label').textContent=volume.value+'%';active.forEach(a=>a.volume=volume.value/100)};
document.querySelector('#stop').onclick=()=>{stop();status.textContent='已停止'};
window.addEventListener('pagehide',stop);
</script></html>'''
    (ROOT / "试听.html").write_text(template.replace("__CARDS__", "".join(cards)).replace("__AUDIO__", json.dumps(audio)), encoding="utf-8")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", type=Path)
    parser.add_argument("--master", type=Path)
    args = parser.parse_args()
    if args.base:
        build_project(args.base)
    if args.master:
        export(args.master)
    if not (args.base or args.master):
        parser.error("Supply --base LMMS_MCP_project.mmp or --master LMMS_export.wav")
