"""把 CC0 金属脚步实录裁切为八声，复用项目现有音频 Cue 与 Unity 导入规则。"""

from __future__ import annotations

import hashlib
import io
import json
import os
import re
import subprocess
import uuid
import wave
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
RATE = 48000
CUE_ID = "player.footstep.metal"
ARCHIVE = ROOT / "Sources/metal_steps_48k24b.7z"
SOURCES = ROOT / "sources.json"


# region CC0 金属脚步裁切与 Unity 资源绑定
def read_metal_recording(member: str) -> np.ndarray:
    raw = subprocess.check_output(["tar", "-xOf", str(ARCHIVE), member])
    with wave.open(io.BytesIO(raw), "rb") as source:
        if (source.getframerate(), source.getnchannels(), source.getsampwidth()) != (RATE, 2, 3):
            raise ValueError(f"不支持的金属脚步格式：{member}")
        data = source.readframes(source.getnframes())

    # 仅把原始 24 位立体声录音转为 48kHz 单声道，不生成任何合成音色。
    samples = np.frombuffer(data, dtype=np.uint8).reshape(-1, 2, 3).astype(np.int32)
    unsigned = samples[:, :, 0] | (samples[:, :, 1] << 8) | (samples[:, :, 2] << 16)
    signed = (unsigned ^ 0x800000) - 0x800000
    mono = signed.mean(axis=1).astype(np.float64) / 8388608.0
    return mono - mono.mean()


def save_sample(path: Path, samples: np.ndarray) -> float:
    peak = float(np.max(np.abs(samples)))
    if peak <= 0:
        raise ValueError(f"无有效实录音量：{path}")
    audible = np.flatnonzero(np.abs(samples) >= max(peak * .012, .00001))
    if len(audible) == 0:
        raise ValueError(f"录音全静音：{path}")
    start = max(0, int(audible[0]) - round(.008 * RATE))
    end = min(len(samples), int(audible[-1]) + 1 + round(.035 * RATE))
    clip = samples[start:end].copy()
    fade_in = min(round(.003 * RATE), len(clip) // 2)
    fade_out = min(round(.025 * RATE), len(clip) // 2)
    clip[:fade_in] *= np.linspace(0, 1, fade_in)
    clip[-fade_out:] *= np.linspace(1, 0, fade_out)
    clip *= (10 ** (-12 / 20)) / float(np.max(np.abs(clip)))
    pcm = np.round(np.clip(clip, -1, 1) * 32767).astype("<i2")

    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp.wav")
    with wave.open(str(temporary), "wb") as output:
        output.setnchannels(1)
        output.setsampwidth(2)
        output.setframerate(RATE)
        output.writeframes(pcm.tobytes())
    os.replace(temporary, path)
    return len(clip) / RATE


def ensure_meta(asset: Path, template: Path) -> str:
    meta = Path(str(asset) + ".meta")
    if not meta.exists():
        original = template.read_text(encoding="utf-8")
        original = re.sub(r"guid: [a-f0-9]{32}", "guid: " + uuid.uuid4().hex, original, count=1)
        original = original.replace("LMMS_MCP_Gameplay", "CC0_Thimras")
        meta.write_text("\n".join(line.rstrip() for line in original.splitlines()) + "\n", encoding="utf-8")
    return re.search(r"guid: ([a-f0-9]{32})", meta.read_text(encoding="utf-8")).group(1)


def prepare() -> None:
    data = json.loads(SOURCES.read_text(encoding="utf-8"))
    source = data["recordings"]["metal"]
    if hashlib.sha256(ARCHIVE.read_bytes()).hexdigest() != source["sourceSha256"]:
        raise ValueError("金属脚步原始录音散列不符")

    clips = []
    for entry in data["metalSegments"]:
        member = entry["archiveFile"]
        asset = PROJECT / f"Assets/Audio/FootstepsCC0/{CUE_ID}__{entry['variant']:02}.wav"
        seconds = save_sample(asset, read_metal_recording(member))
        guid = ensure_meta(asset, PROJECT / "Assets/Audio/FootstepsCC0/player.footstep.wood__01.wav.meta")
        clips.append({"cue": CUE_ID, "variant": entry["variant"], "source": member,
                      "file": str(asset.relative_to(PROJECT)).replace("\\", "/"),
                      "guid": guid, "seconds": seconds,
                      "sha256": hashlib.sha256(asset.read_bytes()).hexdigest()})

    cue_path = PROJECT / f"Assets/Resources/Audio/Cues/{CUE_ID}.asset"
    template = PROJECT / "Assets/Resources/Audio/Cues/player.footstep.stone.asset"
    config = cue_path.read_text(encoding="utf-8") if cue_path.exists() else template.read_text(encoding="utf-8").replace("player.footstep.stone", CUE_ID)
    entries = "  clips:\n" + "".join(f"  - {{fileID: 8300000, guid: {clip['guid']}, type: 3}}\n" for clip in clips)
    config, changes = re.subn(r"  clips:\n(?:  - [^\n]*\n)+", entries, config)
    if changes != 1:
        raise ValueError("音频 Cue 未找到唯一 clips 列表")
    cue_path.write_text(config, encoding="utf-8")
    cue_guid = ensure_meta(cue_path, PROJECT / "Assets/Resources/Audio/Cues/player.footstep.wood.asset.meta")

    catalog_path = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
    catalog = catalog_path.read_text(encoding="utf-8")
    line = f"  - {{fileID: 11400000, guid: {cue_guid}, type: 2}}"
    if line not in catalog:
        catalog_path.write_text(catalog.rstrip() + "\n" + line + "\n", encoding="utf-8")

    manifest = {"sourcePage": source["sourcePage"], "license": source["license"],
                "archiveSha256": source["sourceSha256"], "sampleRate": RATE, "channels": 1,
                "clips": clips}
    (ROOT / "manifest-metal.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"已接入 {len(clips)} 个 CC0 金属脚步变体：{CUE_ID}")


if __name__ == "__main__":
    prepare()
# endregion
