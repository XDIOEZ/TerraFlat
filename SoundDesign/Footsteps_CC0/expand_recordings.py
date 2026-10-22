"""扩增实录脚步与翻滚：只裁切、变速和混音开放授权录音，不合成声源。"""
from pathlib import Path
import array
import hashlib
import io
import json
import math
import os
import re
import shutil
import subprocess
import urllib.request
import uuid
import wave
import zipfile

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
RATE = 48000
FFMPEG = shutil.which("ffmpeg")


# region 可重建实录整理
def read_audio(path, speed=1):
    raw = subprocess.check_output([FFMPEG, "-v", "error", "-i", str(path), "-af",
        f"aresample={RATE},highpass=f=60,asetrate={RATE}*{speed},aresample={RATE}",
        "-ar", str(RATE), "-ac", "1", "-f", "f32le", "pipe:1"])
    values = array.array("f")
    values.frombytes(raw)
    return list(values)


def write_audio(path, values):
    peak = max(abs(v) for v in values)
    threshold = max(.00001, peak * .012)
    first = next(i for i, v in enumerate(values) if abs(v) >= threshold)
    last = len(values) - next(i for i, v in enumerate(reversed(values)) if abs(v) >= threshold)
    values = values[max(0, first - 384):min(len(values), last + 1680)]
    gain = 10 ** (-12 / 20) / max(abs(v) for v in values)
    pcm = array.array("h", (round(max(-1, min(1, v * gain)) * 32767 *
        min(1, i / 144, (len(values) - 1 - i) / 1200)) for i, v in enumerate(values)))
    temporary = path.with_suffix(".tmp.wav")
    with wave.open(str(temporary), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())
    os.replace(temporary, path)
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        template = (PROJECT / "Assets/Audio/Generated/player.footstep.stone__01.wav.meta").read_text("utf-8")
        meta.write_text(re.sub(r"guid: [a-f0-9]{32}", "guid: " + uuid.uuid4().hex, template, count=1), "utf-8")
    guid = re.search(r"guid: ([a-f0-9]{32})", meta.read_text("utf-8")).group(1)
    return guid, len(values) / RATE


def main():
    if not FFMPEG:
        raise RuntimeError("Install FFmpeg before rebuilding recorded sounds")
    original = json.loads((ROOT / "sources.json").read_text("utf-8"))
    archives = {}
    for group in ("kenney", "dirt"):
        url = original["recordings"][group]["downloadUrl"]
        archives[group] = zipfile.ZipFile(io.BytesIO(urllib.request.urlopen(url).read()))
    downloaded = {}
    wanted = {"dirt": {"leaves01.ogg", "leaves02.ogg", "stone01.ogg", "wood01.ogg", "wood02.ogg", "wood03.ogg"},
        "kenney": {f"footstep_{surface}_{n:03}.ogg" for surface in ("grass", "concrete", "wood") for n in (3, 4)} |
            {f"footstep_snow_{n:03}.ogg" for n in range(5)} |
            {f"footstep_carpet_{n:03}.ogg" for n in range(3)} |
            {f"impactSoft_medium_{n:03}.ogg" for n in range(3)}}
    for group, archive in archives.items():
        for member in archive.namelist():
            if not member.lower().endswith(".ogg"):
                continue
            name = Path(member).name
            if name not in wanted[group]:
                continue
            path = ROOT / "Sources" / (group + "_" + name)
            if not path.exists():
                path.write_bytes(archive.read(member))
            downloaded[group + ":" + name] = path
    sounds, segments = [], []
    for material in ("grass", "stone", "wood", "sand", "dirt", "snow", "water"):
        cue = "player.footstep." + material
        old = [s for s in original["segments"] if s["cue"] == cue]
        if material in ("grass", "stone", "wood"):
            key = {"grass": "grass", "stone": "concrete", "wood": "wood"}[material]
            candidates = [(downloaded[f"kenney:footstep_{key}_{n:03}.ogg"], 1, "kenney") for n in (3, 4)]
            extra = {"grass": ["leaves01.ogg", "leaves02.ogg"], "stone": ["stone01.ogg"],
                     "wood": ["wood01.ogg", "wood02.ogg", "wood03.ogg"]}[material]
            candidates += [(downloaded["dirt:" + name], 1, "dirt") for name in extra]
        elif material == "snow":
            candidates = [(downloaded[f"kenney:footstep_snow_{n:03}.ogg"], 1, "kenney") for n in range(5)]
        elif material == "dirt":
            candidates = [(downloaded["dirt:leaves02.ogg"], 1, "dirt")]
        else:
            candidates = []
        for n in range(5 - len(candidates)):
            segment = old[n % len(old)]
            candidates.append((ROOT / segment["source"], .91 if n % 2 == 0 else 1.09, segment["recording"]))
        clip_guids = []
        cue_path = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
        config = cue_path.read_text("utf-8")
        clip_guids.extend(re.findall(r"fileID: 8300000, guid: ([a-f0-9]{32})", config)[:3])
        for variant, (source, speed, recording) in enumerate(candidates[:5], 4):
            path = PROJECT / "Assets/Audio/Generated" / f"{cue}__{variant:02}.wav"
            guid, seconds = write_audio(path, read_audio(source, speed))
            clip_guids.append(guid)
            segments.append(dict(cue=cue, variant=variant, recording=recording,
                source=str(source.relative_to(PROJECT)).replace("\\", "/"),
                sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(), speed=speed,
                file=str(path.relative_to(PROJECT)).replace("\\", "/")))
            sounds.append(dict(cue=cue, variant=variant, guid=guid, seconds=seconds,
                file=str(path.relative_to(PROJECT)).replace("\\", "/"), sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        clips = "  clips:\n" + "".join(f"  - {{fileID: 8300000, guid: {guid}, type: 3}}\n" for guid in clip_guids)
        config = re.sub(r"  clips:\n(?:  - [^\n]*\n)+", clips, config)
        config = re.sub(r"  noRepeatHistory: \d+\n", "", config)
        config = re.sub(r"  selection: \d+\n", "  selection: 0\n  noRepeatHistory: 3\n", config)
        config = re.sub(r"  pitch:\n    Min: [^\n]+\n    Max: [^\n]+", "  pitch:\n    Min: 0.94\n    Max: 1.06", config)
        cue_path.write_text(config, "utf-8")
    roll_guids = []
    for variant in range(1, 4):
        body = downloaded[f"kenney:impactSoft_medium_{variant - 1:03}.ogg"]
        cloth = downloaded[f"kenney:footstep_carpet_{variant - 1:03}.ogg"]
        a, b = read_audio(body, 1), read_audio(cloth, 1.06)
        offset = round(.09 * RATE)
        mixed = [0.] * max(len(a), len(b) + offset)
        for n, value in enumerate(a): mixed[n] += value * .7
        for n, value in enumerate(b): mixed[n + offset] += value * .55
        path = PROJECT / "Assets/Audio/Generated" / f"player.roll__{variant:02}.wav"
        guid, seconds = write_audio(path, mixed)
        roll_guids.append(guid)
        sounds.append(dict(cue="player.roll", variant=variant, guid=guid, seconds=seconds,
            file=str(path.relative_to(PROJECT)).replace("\\", "/"), sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        segments.append(dict(cue="player.roll", variant=variant, recording="kenney", sources=[str(p.relative_to(PROJECT)).replace("\\", "/") for p in (body, cloth)],
            processing="Soft body impact + recorded carpet friction delayed 90ms; gain, mono, peak -12dB"))
    cue_path = PROJECT / "Assets/Resources/Audio/Cues/player.roll.asset"
    template = (PROJECT / "Assets/Resources/Audio/Cues/player.footstep.stone.asset").read_text("utf-8")
    template = template.replace("player.footstep.stone", "player.roll")
    template = re.sub(r"  clips:\n(?:  - [^\n]*\n)+", "  clips:\n" + "".join(
        f"  - {{fileID: 8300000, guid: {g}, type: 3}}\n" for g in roll_guids), template)
    template = template.replace("  noRepeatHistory: 3", "  noRepeatHistory: 1")
    cue_path.write_text(template, "utf-8")
    meta = Path(str(cue_path) + ".meta")
    if not meta.exists():
        meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n", "utf-8")
    cue_guid = re.search(r"guid: ([a-f0-9]{32})", meta.read_text("utf-8")).group(1)
    catalog_path = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    catalog = catalog_path.read_text("utf-8")
    line = f"  - {{fileID: 11400000, guid: {cue_guid}, type: 2}}"
    if line not in catalog:
        catalog_path.write_text(catalog.rstrip() + "\n" + line + "\n", "utf-8")
    (ROOT / "sources-expanded.json").write_text(json.dumps(dict(verifiedOn="2026-10-22", recordings=original["recordings"],
        processing="Existing first 3 preserved; 5 extra recorded or tempo-varied footsteps each; last 3 clips excluded from next random choice", segments=segments), ensure_ascii=False, indent=2) + "\n", "utf-8")
    (ROOT / "manifest-expanded.json").write_text(json.dumps(dict(sampleRate=RATE, channels=1, sounds=sounds), indent=2) + "\n", "utf-8")
    print(f"Prepared {len(sounds)} extra recorded clips and player.roll Cue")
# endregion


if __name__ == "__main__":
    main()
