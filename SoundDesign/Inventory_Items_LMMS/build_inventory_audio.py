"""用本机 LMMS 合成物品取放素材，并生成离线试听与 Unity 资源索引。"""

from __future__ import annotations

import argparse
import base64
import copy
import importlib.util
import json
import math
import shutil
import uuid
import wave
import xml.etree.ElementTree as ET
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
RATE = 48000
ASSET_ROOT = "Assets/Audio/UI/InventoryItems_LMMS"
ACTIONS = {"pickup": "拿起物品", "backpack": "放进背包", "chest": "放进箱子"}
WEIGHTS = {"light": ("轻物", 0, .62, .85), "medium": ("中等重量", -8, .82, 1),
           "heavy": ("重物", -16, 1, 1.18)}
VOLUMES = {"compact": ("小体积", 0, 1), "bulky": ("大体积", -2, 1.40)}
MATERIALS = {
    "soft": dict(name="柔软物品", description="布料、皮革、草叶等的柔软摩擦与轻闷响", key=48,
                 waves=[6, 0, 0], volumes=[66, 22, 7], coarse=[0, -12, 7], fine=[0, 0, 0],
                 decay=.058, cutoff=1150),
    "wood": dict(name="木质物品", description="木板、木柄等的干燥轻敲和短摩擦", key=64,
                 waves=[0, 0, 6], volumes=[60, 19, 16], coarse=[0, 17, 0], fine=[0, 28, 0],
                 decay=.046, cutoff=3000),
    "stone": dict(name="石质硬物", description="石头、矿石等的粗颗粒触碰和较硬的短撞击", key=61,
                  waves=[6, 0, 0], volumes=[48, 28, 11], coarse=[0, -12, 15], fine=[0, 0, 35],
                  decay=.051, cutoff=3700),
    "metal": dict(name="金属硬物", description="金属工具、矿锭等的短促清脆触碰，控制高频与余响", key=67,
                  waves=[0, 0, 0], volumes=[58, 24, 13], coarse=[0, 15, 23], fine=[0, 27, -31],
                  decay=.082, cutoff=4700),
}

spec = importlib.util.spec_from_file_location("flatworld_ui_audio", ROOT.parent / "UI_Click_LMMS/build_preview.py")
common = importlib.util.module_from_spec(spec)
spec.loader.exec_module(common)


# region LMMS 合成工程
def entries() -> list[dict]:
    output = []
    for action_index, (action, action_name) in enumerate(ACTIONS.items()):
        local_index = 0
        for material, material_params in MATERIALS.items():
            for weight, (weight_name, _, _, _) in WEIGHTS.items():
                for volume, (volume_name, _, _) in VOLUMES.items():
                    filename = f"物品交互_{material_params['name']}_{action_name}_{weight_name}_{volume_name}_01.wav"
                    output.append(dict(action=action, actionName=action_name, material=material,
                                       materialName=material_params["name"], weightBand=weight, weightName=weight_name,
                                       volumeBand=volume, volumeName=volume_name, localBar=local_index + 1,
                                       cueId=f"ui.inventory.{action}.{material}.{weight}.{volume}",
                                       file=f"WAV/{action_name}/{material_params['name']}/{filename}",
                                       unityFile=f"{ASSET_ROOT}/{action_name}/{material_params['name']}/{filename}",
                                       sliceStartSeconds=local_index * 2 + .1))
                    local_index += 1
    return output


def add_pattern(track: ET.Element, entry: dict, key: int, decay: float, velocity: int,
                position: int = 12, rebound: bool = False) -> None:
    pattern = ET.SubElement(track, "pattern", {"type": "1", "steps": "16", "len": "192",
                            "pos": str((entry["localBar"] - 1) * 192),
                            "name": f"{entry['materialName']}_{entry['weightName']}_{entry['volumeName']}"})
    length = max(8, math.ceil((decay + .035) / (2 / 192)))
    ET.SubElement(pattern, "note", {"pos": str(position), "key": str(key), "len": str(length),
                                    "vol": str(velocity), "pan": "0"})
    if rebound:
        ET.SubElement(pattern, "note", {"pos": str(position + 5), "key": str(key - 2), "len": str(length),
                                        "vol": str(round(velocity * .20)), "pan": "0"})


def make_track(template: ET.Element, name: str, params: dict) -> ET.Element:
    track = copy.deepcopy(template)
    track.set("name", name)
    for pattern in track.findall("pattern"):
        track.remove(pattern)
    common.oscillator(track.find("instrumenttrack"), params)
    return track


def body_patch(entry: dict) -> tuple[dict, int]:
    material = MATERIALS[entry["material"]]
    _, pitch, loudness, decay_scale = WEIGHTS[entry["weightBand"]]
    _, volume_pitch, size_scale = VOLUMES[entry["volumeBand"]]
    action = entry["action"]
    action_gain = {"pickup": .75, "backpack": .79, "chest": .94}[action]
    cutoff_scale = {"pickup": .86, "backpack": .52, "chest": .93}[action]
    decay_action = {"pickup": .87, "backpack": .79, "chest": 1.05}[action]
    patch = dict(material, attack=.0028 if action == "pickup" else .0018,
                 decay=material["decay"] * decay_scale * size_scale * decay_action,
                 release=.012, cutoff=round(material["cutoff"] * cutoff_scale),
                 volume=round(62 * loudness * action_gain))
    return patch, material["key"] + pitch + volume_pitch + (-2 if action == "backpack" else 0)


def contact_patch(action: str, weight: str, volume: str) -> tuple[dict, int]:
    _, pitch, loudness, _ = WEIGHTS[weight]
    _, _, size_scale = VOLUMES[volume]
    # 背包的布料闷响与箱子的空腔木响独立合成，避免只靠音调变化区分目标容器。
    params = {
        "pickup": dict(waves=[6, 0, 0], volumes=[90, 10, 0], coarse=[0, -12, 0],
                       attack=.008, decay=.043, cutoff=1200, volume=11, key=47),
        "backpack": dict(waves=[6, 0, 0], volumes=[64, 28, 0], coarse=[0, -12, 0],
                         attack=.005, decay=.069, cutoff=760, volume=26, key=46),
        "chest": dict(waves=[0, 0, 6], volumes=[57, 22, 18], coarse=[0, 16, 0],
                      attack=.0015, decay=.066, cutoff=1900, volume=30, key=49),
    }[action]
    patch = dict(params, fine=[0, -23, 0], release=.015,
                 decay=params["decay"] * size_scale, volume=round(params["volume"] * loudness),
                 cutoff=round(params["cutoff"] * (.83 if volume == "bulky" else 1)))
    return patch, params["key"] + round(pitch * .55)


def create_projects() -> None:
    template_tree = ET.parse(ROOT.parent / "UI_Click_LMMS/FlatWorld_UI_Clicks.mmp")
    template_track = template_tree.getroot().find("song/trackcontainer/track")
    sounds = entries()
    for action, action_name in ACTIONS.items():
        root = copy.deepcopy(template_tree.getroot())
        root.find("head").set("bpm", "120")
        root.find("head").set("mastervol", "65")
        container = root.find("song/trackcontainer")
        for child in list(container):
            container.remove(child)
        contact_tracks = {}
        for weight in WEIGHTS:
            for volume in VOLUMES:
                patch, _ = contact_patch(action, weight, volume)
                contact_tracks[weight, volume] = make_track(template_track,
                    f"接触层_{action_name}_{WEIGHTS[weight][0]}_{VOLUMES[volume][0]}", patch)
        for entry in [x for x in sounds if x["action"] == action]:
            patch, key = body_patch(entry)
            track = make_track(template_track,
                f"{entry['materialName']}_{entry['weightName']}_{entry['volumeName']}", patch)
            add_pattern(track, entry, key, patch["decay"], 82)
            container.append(track)
            contact, contact_key = contact_patch(action, entry["weightBand"], entry["volumeBand"])
            add_pattern(contact_tracks[entry["weightBand"], entry["volumeBand"]], entry,
                        contact_key, contact["decay"], 80, position=13 if action == "pickup" else 12,
                        rebound=action == "chest" and entry["volumeBand"] == "bulky")
        for track in contact_tracks.values():
            container.append(track)
        root.find("song/projectnotes").text = f"FlatWorld {action_name}: 24 cues, one per bar at 120 BPM. Material: soft/wood/stone/metal. Per material: light compact, light bulky, medium compact, medium bulky, heavy compact, heavy bulky. Original TripleOscillator synthesis; no external samples."
        tree = ET.ElementTree(root)
        ET.indent(tree, space="  ")
        tree.write(ROOT / f"物品交互_{action_name}_24种_LMMS工程.mmp", encoding="utf-8", xml_declaration=True)
    manifest = dict(schemaVersion=1, generator="LMMS 1.2.2 / TripleOscillator", sampleRate=RATE,
                    channels=1, bitsPerSample=16, count=len(sounds),
                    scope="Unity audio assets and audition only; runtime routing not installed",
                    dimensions=dict(action=list(ACTIONS), material=list(MATERIALS),
                                    weightBand=list(WEIGHTS), volumeBand=list(VOLUMES)),
                    weightUnit="kg", volumeUnit="L", sounds=sounds)
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("Created 3 LMMS projects, 30 tracks and 24 cues per project")
# endregion


# region 母带拆分与音量
def export_audio(master_folder: Path) -> None:
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    clips = {}
    for action in ACTIONS:
        rate, master = common.read_wav(master_folder / f"inventory_{action}_master.wav")
        if rate != RATE:
            raise ValueError(f"LMMS render must be {RATE} Hz")
        for entry in [x for x in manifest["sounds"] if x["action"] == action]:
            start = round(entry["sliceStartSeconds"] * RATE)
            clips[entry["cueId"]] = common.trim(master[start:start + round(.65 * RATE)])
    for material in MATERIALS:
        group = [x for x in manifest["sounds"] if x["material"] == material]
        reference = next(x for x in group if x["action"] == "pickup" and x["weightBand"] == "medium" and x["volumeBand"] == "compact")
        rms = np.sqrt(np.mean(clips[reference["cueId"]] ** 2))
        gain = .065 / max(rms, 1e-6)
        max_peak = max(float(np.abs(clips[x["cueId"]]).max()) for x in group)
        gain = min(gain, .45 / max(max_peak, 1e-6))
        # 每种材质统一增益，保留重量与容器带来的相对轻重，不逐文件归一化。
        for entry in group:
            data = clips[entry["cueId"]] * gain
            clips[entry["cueId"]] = data
            common.write_wav(ROOT / entry["file"], data)
            entry["durationMs"] = round(len(data) / RATE * 1000, 1)
            entry["peakDbfs"] = round(20 * math.log10(max(float(np.abs(data).max()), 1e-9)), 1)
    demos = {}
    for action, name in ACTIONS.items():
        selected = [x for x in manifest["sounds"] if x["action"] == action and x["volumeBand"] == "compact"]
        filename = f"试听_{name}_四种材质_轻中重对比.wav"
        common.write_wav(ROOT / filename, common.sequence([clips[x["cueId"]] for x in selected], spacing=.26))
        demos[action] = filename
    selected = []
    for material in MATERIALS:
        for action in ACTIONS:
            selected.append(next(x for x in manifest["sounds"] if x["material"] == material and x["action"] == action and x["weightBand"] == "medium" and x["volumeBand"] == "bulky"))
    comparison = "试听_同一物品_拿起_放背包_放箱子_对比.wav"
    common.write_wav(ROOT / comparison, common.sequence([clips[x["cueId"]] for x in selected], spacing=.32))
    manifest["previews"] = demos
    manifest["containerComparison"] = comparison
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    make_audition(manifest)
    print(json.dumps(dict(sounds=len(clips), durationRangeMs=[min(x["durationMs"] for x in manifest["sounds"]),
                     max(x["durationMs"] for x in manifest["sounds"])], previews=4), ensure_ascii=False))
# endregion


# region Unity 素材整理
def write_meta(path: Path, kind: str, user_data: str = "") -> str:
    meta = Path(str(path) + ".meta")
    if meta.exists():
        return next(line.split(":", 1)[1].strip() for line in meta.read_text(encoding="utf-8").splitlines() if line.startswith("guid:"))
    guid = uuid.uuid4().hex
    prefix = f"fileFormatVersion: 2\nguid: {guid}\n"
    if kind == "folder":
        text = prefix + "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    elif kind == "audio":
        text = prefix + f"""AudioImporter:
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
  forceToMono: 0
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 0
  userData: {user_data}
  assetBundleName:
  assetBundleVariant:
"""
    else:
        text = prefix + "TextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    try:
        with meta.open("x", encoding="utf-8", newline="\n") as output:
            output.write(text)
    except FileExistsError:
        return write_meta(path, kind, user_data)
    return guid


def import_unity() -> None:
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    destination = PROJECT / ASSET_ROOT
    destination.mkdir(parents=True, exist_ok=True)
    write_meta(destination, "folder")
    rows = []
    for entry in manifest["sounds"]:
        target = PROJECT / entry["unityFile"]
        for folder in [target.parent.parent, target.parent]:
            folder.mkdir(parents=True, exist_ok=True)
            write_meta(folder, "folder")
        entry["guid"] = write_meta(target, "audio", entry["cueId"])
        shutil.copyfile(ROOT / entry["file"], target)
        rows.append(f"| {entry['actionName']} | {entry['materialName']} | {entry['weightName']} | {entry['volumeName']} | {entry['durationMs']:.1f} | `{target.name}` |")
    index = destination / "InventoryAudioLibrary.json"
    write_meta(index, "text")
    index.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    readme = destination / "README_物品取放音效说明.md"
    write_meta(readme, "text")
    readme.write_text("""# 背包物品取放音效

共 72 个独立 WAV：3 种操作 × 4 种材质 × 3 档重量 × 2 档体积。每个文件都有明确名称、独立 GUID 和参数索引。

- 拿起物品：点击拿取或开始拖拽时的短触碰与摩擦。
- 放进背包：较柔软、低通后的布料与闷响。
- 放进箱子：额外叠加空腔木质碰撞，大体积有很轻的二次落稳声。
- 柔软、木质、石质、金属分别改变波形组合和材质质感；重量改变音高、力量感和衰减；体积单独改变摩擦时长、共鸣和落稳声。

命名：`物品交互_材质_操作_重量_体积_01.wav`。操作分文件夹，每个操作内再按材质分目录。此处的箱子声音按木箱设计。

| 操作 | 材质 | 重量 | 体积 | 时长 ms | 文件 |
| --- | --- | --- | --- | --- | --- |
""" + "\n".join(rows) + """

## 试听与再导出

在 Unity Project 中选中 WAV，用 Inspector 底部播放器试听。完整离线试听页和 3 个 LMMS 工程位于 `SoundDesign/Inventory_Items_LMMS/`。

所有音频是 LMMS 原创合成，没有外部音效采样。WAV 为 48 kHz、单声道、16-bit PCM；`.meta` 使用 PCM、保留采样率、加载时解压、预加载，保留相对音量。

## 后续接入参考

`InventoryAudioLibrary.json` 是素材选择索引，包含 action/material/weightBand/volumeBand、建议 Cue ID、路径、GUID 和时长，尚未安装自动播放逻辑或注册 AudioCue。

- `pickup` 对应点击拿取或拖拽开始；同一次操作只播放一次。
- `backpack` / `chest` 根据真正提交成功的目标库存选择，背包与箱子不能只靠所在 UI 面板名字判断。
- 重量与体积应按这次真正搬动的数量计算：`ItemData.Stack.Weight × movedAmount`（kg）、`ItemData.Stack.Volume × movedAmount`（L）。点击一件与拖整堆分别使用自己的数量。
- 轻／中／重、小／大只是音效档位，本次没有为游戏设置强制阈值。材质建议通过稳定标签或配置声明，并保留默认回退。
- 放回原槽、失败、取消拖拽时不播放成功放入音；普通移动、拆分、合并、交换仍沿用原有库存事务。
- 所有未来业务调用继续通过 `AudioService` 和稳定 `ui.inventory.*` Cue ID；本目录位于自动 Catalog 扫描目录之外。

导入设置参考 [Unity 2022.3 官方 Audio Clip 文档](https://docs.unity3d.com/2022.3/Documentation/Manual/class-AudioClip.html)。
""", encoding="utf-8")
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Added {len(rows)} named AudioClips to {destination}")
# endregion


# region 离线交互试听
def make_audition(manifest: dict) -> None:
    sources = {entry["cueId"]: "data:audio/wav;base64," + base64.b64encode((ROOT / entry["file"]).read_bytes()).decode()
               for entry in manifest["sounds"]}
    for name in list(manifest["previews"].values()) + [manifest["containerComparison"]]:
        sources[name] = "data:audio/wav;base64," + base64.b64encode((ROOT / name).read_bytes()).decode()
    page = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>FlatWorld · 物品取放音效试听</title><style>
:root{font-family:"Segoe UI","Microsoft YaHei",sans-serif;color-scheme:dark;background:#191919;color:#eee}body{max-width:1120px;margin:40px auto;padding:0 24px}h1{font-size:29px}h2{font-size:20px}p{color:#bbb;line-height:1.8}.panel{background:#252525;border:1px solid #484848;border-radius:9px;padding:24px;margin:20px 0}.settings,.actions,.demos{display:flex;gap:14px;flex-wrap:wrap;align-items:center}.settings label{display:grid;gap:9px;flex:1;min-width:180px}select,button{font:inherit;color:#eee;background:#363636;border:1px solid #555;border-radius:5px;padding:12px 16px}button{cursor:pointer}button:hover{background:#444;border-color:#bfa45c}button:active{background:#655b3c}button:focus-visible,select:focus-visible{outline:2px solid #d9c17b;outline-offset:2px}input{accent-color:#d9c17b}.drag{display:grid;grid-template-columns:1fr 1fr 1fr;gap:18px;margin:22px 0}.item,.target{border:1px solid #666;border-radius:7px;padding:30px 12px;text-align:center;min-height:68px;display:grid;align-content:center;gap:9px}.item{background:#36322a;cursor:grab}.item:active{cursor:grabbing}.target{background:#2c2c2c}.target.over{border-color:#d9c17b;background:#393527}.small{font-size:13px;color:#aaa}.status{min-height:28px;color:#ddc580}.list{width:100%;border-collapse:collapse;font-size:14px}td,th{text-align:left;padding:11px 8px;border-bottom:1px solid #414141}th{color:#bbb;font-weight:normal}.demos button{font-size:14px}.volume{display:flex;gap:12px;align-items:center;margin-top:16px}.hint{font-size:14px;color:#aaa}@media(max-width:680px){.drag{grid-template-columns:1fr}.list td:nth-child(2),.list th:nth-child(2){display:none}}
</style><h1>背包物品取放音效</h1><p>72 个声音：拿起、放背包、放箱子 × 四种材质 × 轻中重 × 小大体积。先选物品特征，再点按钮，或把物品卡片拖到背包／箱子上听。</p>
<section class="panel"><div class="settings"><label>物品材质<select id="material"><option value="soft">柔软物品 · 布料／草叶</option><option value="wood" selected>木质物品 · 木板／木柄</option><option value="stone">石质硬物 · 石头／矿石</option><option value="metal">金属硬物 · 工具／矿锭</option></select></label><label>这次搬动的重量<select id="weight"><option value="light">轻物</option><option value="medium" selected>中等重量</option><option value="heavy">重物</option></select></label><label>这次搬动的体积<select id="size"><option value="compact" selected>小体积</option><option value="bulky">大体积</option></select></label></div>
<div class="drag"><div class="item" id="item" draggable="true" role="button" tabindex="0"><b id="item-title">木质物品</b><span class="small">点击拿起，或拖拽这张卡片</span></div><div class="target" data-action="backpack"><b>背包</b><span class="small">柔软布料与轻闷响</span></div><div class="target" data-action="chest"><b>木箱</b><span class="small">空腔木响与轻落稳声</span></div></div>
<div class="actions"><button data-play="pickup">拿起物品</button><button data-play="backpack">放进背包</button><button data-play="chest">放进箱子</button><button id="compare">同一物品：拿起 → 背包 → 箱子</button><button id="repeat">连续取放 5 次</button><button id="stop">停止</button></div><div class="volume"><label>试听音量 <input id="volume" type="range" min="0" max="100" value="75"></label><span id="volume-text">75%</span></div><p class="status" id="status" aria-live="polite">等待试听</p></section>
<section class="panel"><h2>批量对比</h2><p class="hint">每段按柔软 → 木质 → 石质 → 金属排列；每种材质依次轻 → 中 → 重，使用小体积音效。</p><div class="demos"><button data-demo="pickup">拿起 · 四种材质与重量</button><button data-demo="backpack">背包 · 四种材质与重量</button><button data-demo="chest">箱子 · 四种材质与重量</button><button id="all-containers">四种材质：拿起／背包／箱子</button></div></section>
<section class="panel"><h2>当前材质的全部声音</h2><table class="list"><thead><tr><th>操作</th><th>重量</th><th>体积</th><th>时长</th><th>试听</th></tr></thead><tbody id="rows"></tbody></table></section><p class="hint">本页完全离线，所有音频已嵌入。LMMS 原创合成；已整理为 Unity 素材，自动播放逻辑尚未接入。箱子声音按木箱设计。</p>
<script>
const sources=__SOURCES__,manifest=__MANIFEST__,active=new Set();let timers=[],token=0;
const material=document.querySelector('#material'),weight=document.querySelector('#weight'),size=document.querySelector('#size'),volume=document.querySelector('#volume'),status=document.querySelector('#status');
const actionNames={pickup:'拿起物品',backpack:'放进背包',chest:'放进箱子'};
function stop(){token++;timers.forEach(clearTimeout);timers=[];active.forEach(a=>a.pause());active.clear()}
function sound(key){const a=new Audio(sources[key]);a.volume=volume.value/100;active.add(a);a.onended=()=>active.delete(a);a.play().catch(e=>{active.delete(a);status.textContent='播放失败：'+e.message})}
function selected(action){return manifest.sounds.find(s=>s.action===action&&s.material===material.value&&s.weightBand===weight.value&&s.volumeBand===size.value)}
function play(action){stop();const s=selected(action);sound(s.cueId);status.textContent=s.materialName+' · '+s.weightName+' · '+s.volumeName+' · '+s.actionName}
function series(actions,gap){stop();const current=token,keys=actions.map(a=>selected(a).cueId);keys.forEach((key,i)=>timers.push(setTimeout(()=>{if(token===current)sound(key)},i*gap)));status.textContent='正在对比：'+material.options[material.selectedIndex].text+' / '+weight.options[weight.selectedIndex].text+' / '+size.options[size.selectedIndex].text}
document.querySelectorAll('[data-play]').forEach(b=>b.onclick=()=>play(b.dataset.play));document.querySelector('#compare').onclick=()=>series(['pickup','backpack','chest'],520);document.querySelector('#repeat').onclick=()=>series(Array.from({length:10},(_,i)=>i%2?'backpack':'pickup'),210);document.querySelector('#stop').onclick=()=>{stop();status.textContent='已停止'};
const item=document.querySelector('#item');item.onclick=()=>play('pickup');item.onkeydown=e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();play('pickup')}};item.ondragstart=e=>{e.dataTransfer.setData('text/plain','inventory-audio-item');e.dataTransfer.effectAllowed='move';play('pickup')};document.querySelectorAll('.target').forEach(t=>{t.ondragover=e=>{e.preventDefault();t.classList.add('over')};t.ondragleave=()=>t.classList.remove('over');t.ondrop=e=>{e.preventDefault();t.classList.remove('over');if(e.dataTransfer.getData('text/plain')==='inventory-audio-item')play(t.dataset.action)};t.onclick=()=>play(t.dataset.action)});
document.querySelectorAll('[data-demo]').forEach(b=>b.onclick=()=>{stop();sound(manifest.previews[b.dataset.demo]);status.textContent='正在播放：'+b.textContent});document.querySelector('#all-containers').onclick=()=>{stop();sound(manifest.containerComparison);status.textContent='四种材质：拿起 → 背包 → 箱子'};
volume.oninput=()=>{document.querySelector('#volume-text').textContent=volume.value+'%';active.forEach(a=>a.volume=volume.value/100)};
function update(){stop();document.querySelector('#item-title').textContent=selected('pickup').materialName+' · '+selected('pickup').weightName+' · '+selected('pickup').volumeName;const rows=document.querySelector('#rows');rows.replaceChildren();manifest.sounds.filter(s=>s.material===material.value).forEach(s=>{const row=document.createElement('tr');[s.actionName,s.weightName,s.volumeName,s.durationMs+' ms'].forEach(value=>{const cell=document.createElement('td');cell.textContent=value;row.append(cell)});const cell=document.createElement('td'),button=document.createElement('button');button.textContent='播放';button.onclick=()=>{stop();sound(s.cueId);status.textContent=s.actionName+' · '+s.weightName+' · '+s.volumeName};cell.append(button);row.append(cell);rows.append(row)});status.textContent='已切换物品特征'};[material,weight,size].forEach(s=>s.onchange=update);update();window.addEventListener('pagehide',stop);
</script></html>'''
    (ROOT / "物品取放音效试听.html").write_text(page.replace("__SOURCES__", json.dumps(sources)).replace("__MANIFEST__", json.dumps(manifest, ensure_ascii=False)), encoding="utf-8")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--create", action="store_true")
    parser.add_argument("--masters", type=Path)
    parser.add_argument("--unity", action="store_true")
    args = parser.parse_args()
    if args.create:
        create_projects()
    if args.masters:
        export_audio(args.masters)
    if args.unity:
        import_unity()
    if not (args.create or args.masters or args.unity):
        parser.error("Use --create, --masters MASTER_FOLDER, or --unity")
