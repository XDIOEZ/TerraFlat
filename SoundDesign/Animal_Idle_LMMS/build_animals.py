"""通过 LMMS MCP 编排实录，再裁切母带并绑定空间音效资源。"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import math
import os
import re
import sys
import uuid
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
PROJECT_FILE = ROOT / "Animal_Idle.mmp"
MASTER_FILE = ROOT / "renders/Animal_Idle_raw.wav"
RATE = 48000


# region LMMS MCP 真实采样编排
def atomic_text(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    if path.suffix == ".meta":
        text = "\n".join(line.rstrip() for line in text.splitlines()) + "\n"
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(text, encoding="utf-8")
    temporary.replace(path)


def layout():
    sources = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    result = []
    slot = 0
    for entry in sources["segments"]:
        start_ticks = slot * 192 + 12
        length_ticks = math.ceil(entry["seconds"] * 96)
        result.append(dict(entry, startSeconds=start_ticks / 96, posBars=start_ticks / 192,
                           lengthBars=length_ticks / 192,
                           asset=f"Assets/Audio/Animals/{entry['cue']}__{entry['variant']:02}.wav"))
        slot += math.ceil((entry["seconds"] + .35) / 2)
    atomic_text(ROOT / "layout.json", json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    return result


async def build_mcp(render=True):
    from mcp import ClientSession, StdioServerParameters
    from mcp.client.stdio import stdio_client
    entries = layout()
    server = StdioServerParameters(command=sys.executable, args=["-m", "lmms_mcp"],
        env=dict(os.environ, LMMS_EXECUTABLE=os.environ.get("LMMS_EXECUTABLE", r"G:\LMMS\lmms.exe"),
                 LMMS_PROJECTS_DIR=str(ROOT)))
    MASTER_FILE.parent.mkdir(exist_ok=True)
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            async def call(name, arguments):
                result = await session.call_tool(name, arguments)
                messages = [getattr(part, "text", "") for part in result.content]
                assert not result.is_error, (name, messages)
                payload = json.loads("".join(messages))
                assert "error" not in payload, (name, payload)
                return payload
            await call("create_project", {"bpm": 120})
            await call("set_master_volume", {"volume": 75})
            for index, entry in enumerate(entries):
                result = await call("add_sample_track", {"name": f"{entry['cue']} / {entry['variant']}", "volume": 80})
                assert result["track_index"] == index
                await call("place_sample_clip", {"track_index": index, "file_path": (ROOT / entry["file"]).as_posix(),
                                                "pos_bars": entry["posBars"], "length_bars": entry["lengthBars"]})
            await call("save_project", {"path": str(PROJECT_FILE), "compressed": False})
            if render:
                await call("render_project", {"output_path": str(MASTER_FILE), "file_format": "wav", "samplerate": RATE})
    print(f"LMMS MCP arranged {len(entries)} independent recorded sample clips" + (" and rendered master" if render else ""))
# endregion


# region 单声道母带整理与 Unity Cue 绑定
def audio_dependencies():
    packages = PROJECT / ".codex-tmp/audio-python"
    if packages.is_dir():
        sys.path.insert(0, str(packages))
    import numpy as np
    import soundfile as sf
    return np, sf


def guid_for(path):
    if path.is_file():
        return re.search(r"^guid: ([0-9a-f]{32})$", path.read_text(encoding="utf-8"), re.M)[1]
    return uuid.uuid4().hex


def bind_cue(cue, entries):
    destination = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
    meta = Path(str(destination) + ".meta")
    guid = guid_for(meta)
    text = (PROJECT / "Assets/Resources/Audio/Cues/player.swim.enter.asset").read_text(encoding="utf-8")
    text = text.replace("player.swim.enter", cue)
    clips = "  clips:\n" + "".join(f"  - {{fileID: 8300000, guid: {entry['guid']}, type: 3}}\n" for entry in entries)
    text, count = re.subn(r"  clips:\n(?:  - [^\n]*\n)+", lambda _: clips, text)
    assert count == 1
    for field, value in {"bus": 1, "loop": 0, "spatialBlend": 1, "minDistance": 1.5,
                         "maxDistance": entries[0]["maxDistance"], "priority": 170,
                         "cooldown": .2, "maxInstances": 3, "concurrencyPolicy": 0, "rolloffMode": 1}.items():
        text = re.sub(rf"^  {field}: [^\n]+", f"  {field}: {value}", text, flags=re.M)
    # 主要差异来自真实不同叫声，保留极轻的随机音量和音高浮动。
    text = re.sub(r"  volume:\n    Min: [^\n]+\n    Max: [^\n]+", "  volume:\n    Min: 0.9\n    Max: 1", text)
    text = re.sub(r"  pitch:\n    Min: [^\n]+\n    Max: [^\n]+", "  pitch:\n    Min: 0.99\n    Max: 1.01", text)
    atomic_text(destination, text)
    template = (PROJECT / "Assets/Resources/Audio/Cues/player.swim.enter.asset.meta").read_text(encoding="utf-8")
    atomic_text(meta, re.sub(r"^guid: [0-9a-f]{32}$", f"guid: {guid}", template, flags=re.M))
    return guid


def finalize():
    np, sf = audio_dependencies()
    entries = json.loads((ROOT / "layout.json").read_text(encoding="utf-8"))
    inputs = [PROJECT_FILE, *(ROOT / entry["file"] for entry in entries)]
    assert MASTER_FILE.stat().st_mtime >= max(path.stat().st_mtime for path in inputs), "LMMS master is stale"
    master, rate = sf.read(MASTER_FILE, always_2d=True)
    assert rate == RATE and np.max(np.abs(master)) < .9999
    groups, sounds, preview = {}, [], []
    folder_meta = PROJECT / "Assets/Audio/Animals.meta"
    if not folder_meta.is_file():
        atomic_text(folder_meta, f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    for entry in entries:
        start = round(entry["startSeconds"] * RATE)
        count = round(entry["seconds"] * RATE)
        data = master[start:start + count].mean(axis=1)
        assert len(data) == count
        data -= data.mean()
        # 母带仅做末端淡入淡出和音量上限，避免静音、爆音和重复波形。
        head, tail = round(.004 * RATE), round(.04 * RATE)
        data[:head] *= np.linspace(0, 1, head)
        data[-tail:] *= np.linspace(1, 0, tail)
        peak, rms = float(np.max(np.abs(data))), float(np.sqrt(np.mean(data ** 2)))
        assert peak > .0001 and rms > .00001 and np.isfinite(data).all(), entry
        data *= min(10 ** (entry["peakDb"] / 20) / peak, 10 ** (entry["rmsDb"] / 20) / rms)
        destination = PROJECT / entry["asset"]
        destination.parent.mkdir(exist_ok=True)
        temporary = destination.with_suffix(".wav.tmp")
        sf.write(temporary, data, RATE, subtype="PCM_16", format="WAV")
        temporary.replace(destination)
        meta = Path(str(destination) + ".meta")
        guid = guid_for(meta)
        template = (PROJECT / "Assets/Audio/Generated/player.footstep.water__01.wav.meta").read_text(encoding="utf-8")
        template = re.sub(r"^guid: [0-9a-f]{32}$", f"guid: {guid}", template, flags=re.M)
        atomic_text(meta, template.replace("LMMS_MCP_Gameplay", "Recorded_Animal_Idle_LMMS_MCP"))
        sound = dict(entry, guid=guid, sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
                     peakDb=round(20 * math.log10(np.max(np.abs(data))), 2),
                     rmsDb=round(20 * math.log10(np.sqrt(np.mean(data ** 2))), 2))
        groups.setdefault(entry["cue"], []).append(sound)
        sounds.append(sound)
        preview.extend((data, np.zeros(round(.35 * RATE))))
    cue_guids = {cue: bind_cue(cue, values) for cue, values in groups.items()}
    catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    text = catalog.read_text(encoding="utf-8")
    for guid in cue_guids.values():
        line = f"  - {{fileID: 11400000, guid: {guid}, type: 2}}"
        if line not in text:
            text = text.rstrip() + "\n" + line + "\n"
    atomic_text(catalog, text)
    sf.write(ROOT / "renders/Preview_Animal_Idle.wav", np.concatenate(preview), RATE, subtype="PCM_16")
    atomic_text(ROOT / "manifest.json", json.dumps(dict(engine="LMMS 1.2.2 via MCP", kind="Recorded natural audio sample arrangement",
        sampleRate=RATE, channels=1, masterSha256=hashlib.sha256(MASTER_FILE.read_bytes()).hexdigest(),
        sourcesFile="sources.json", cueGuids=cue_guids, sounds=sounds), ensure_ascii=False, indent=2) + "\n")
    print(f"Bound {len(groups)} spatial animal Cues and {len(sounds)} unique mono recordings")


def verify():
    np, sf = audio_dependencies()
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    sources = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    catalog = (PROJECT / "Assets/Resources/Audio/AudioCatalog.asset").read_text(encoding="utf-8")
    guids, hashes, counts = set(), set(), {}
    for entry in manifest["sounds"]:
        destination = PROJECT / entry["asset"]
        with wave.open(str(destination), "rb") as reader:
            assert (reader.getframerate(), reader.getnchannels(), reader.getsampwidth()) == (RATE, 1, 2)
            assert reader.getnframes() == round(entry["seconds"] * RATE)
        data, rate = sf.read(destination)
        assert np.isfinite(data).all() and 1e-5 < np.sqrt(np.mean(data ** 2)) and np.max(np.abs(data)) < .9999
        assert abs(data[0]) <= 1 / 32768 and abs(data[-1]) <= 1 / 32768
        digest = hashlib.sha256(destination.read_bytes()).hexdigest()
        assert digest == entry["sha256"] and digest not in hashes and entry["guid"] not in guids
        assert guid_for(Path(str(destination) + ".meta")) == entry["guid"]
        assert hashlib.sha256((ROOT / entry["file"]).read_bytes()).hexdigest() == entry["sourceSha256"]
        cue = PROJECT / "Assets/Resources/Audio/Cues" / (entry["cue"] + ".asset")
        config = cue.read_text(encoding="utf-8")
        for expected in [f"  id: {entry['cue']}\n", "  bus: 1\n", "  loop: 0\n", "  spatialBlend: 1\n", "  rolloffMode: 1\n",
                         "  cooldown: 0.2\n", "  maxInstances: 3\n", "  concurrencyPolicy: 0\n", f"guid: {entry['guid']}, type: 3"]:
            assert expected in config, (cue, expected)
        assert f"guid: {guid_for(Path(str(cue) + '.meta'))}, type: 2" in catalog
        hashes.add(digest)
        guids.add(entry["guid"])
        counts[entry["cue"]] = counts.get(entry["cue"], 0) + 1
    for name, source in sources["recordings"].items():
        assert hashlib.sha256((ROOT / source["sourceFile"]).read_bytes()).hexdigest() == source["sha256"], name
        assert source["licenseEvidence"] in (ROOT / source["licenseProof"]).read_text(encoding="utf-8"), name
    assert all(value >= 2 for value in counts.values())
    print(f"Verified {len(counts)} spatial Cues and {len(hashes)} unique, non-silent, unclipped 48 kHz mono PCM16 clips, sources, licenses, GUIDs and Catalog")
# endregion


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=("layout", "build", "finalize", "verify"))
    arguments = parser.parse_args()
    if arguments.command == "build":
        asyncio.run(build_mcp())
    else:
        {"layout": layout, "finalize": finalize, "verify": verify}[arguments.command]()
