"""将 CC0 冰雪硬壳踩踏实录整理为专用冰面脚步 Cue。"""

from __future__ import annotations

import hashlib
import io
import json
import subprocess

import numpy as np
import soundfile as sf

from prepare_metal import PROJECT, RATE, ROOT, SOURCES, ensure_meta, install_cue, save_sample

ARCHIVE = ROOT / "Sources/qubodup-wet_snow_steps.7z"
CUE_ID = "player.footstep.ice"


# region 冰面脚步实录整理
def read_crunch_recording(member: str) -> np.ndarray:
    raw = subprocess.check_output(["tar", "-xOf", str(ARCHIVE), member])
    samples, rate = sf.read(io.BytesIO(raw), always_2d=True, dtype="float64")
    if rate != 96000:
        raise ValueError(f"冰面实录的采样率与来源清单不符：{member}")

    # 先低通再二倍抽样，避免 96kHz 实录降至 48kHz 时出现高频混叠。
    centered = np.arange(127, dtype=np.float64) - 63
    kernel = .47 * np.sinc(.47 * centered) * np.kaiser(127, 8.6)
    kernel /= kernel.sum()
    mono = samples.mean(axis=1)
    return np.convolve(mono, kernel, mode="same")[::2]


def prepare() -> None:
    data = json.loads(SOURCES.read_text(encoding="utf-8"))
    source = data["recordings"]["ice"]
    if hashlib.sha256(ARCHIVE.read_bytes()).hexdigest() != source["sourceSha256"]:
        raise ValueError("冰面实录的原始压缩包散列不符")

    clips = []
    for entry in data["iceSegments"]:
        member = entry["archiveFile"]
        asset = PROJECT / f"Assets/Audio/FootstepsCC0/{CUE_ID}__{entry['variant']:02}.wav"
        seconds = save_sample(asset, read_crunch_recording(member))
        guid = ensure_meta(asset, PROJECT / "Assets/Audio/FootstepsCC0/player.footstep.wood__01.wav.meta", "CC0_qubodup")
        clips.append({"cue": CUE_ID, "variant": entry["variant"], "source": member,
                      "file": str(asset.relative_to(PROJECT)).replace("\\", "/"),
                      "guid": guid, "seconds": seconds,
                      "sha256": hashlib.sha256(asset.read_bytes()).hexdigest()})

    install_cue(CUE_ID, clips)
    manifest = {"sourcePage": source["sourcePage"], "license": source["license"],
                "archiveSha256": source["sourceSha256"], "sampleRate": RATE, "channels": 1,
                "clips": clips}
    (ROOT / "manifest-ice.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"已接入 {len(clips)} 个 CC0 冰面脚步变体：{CUE_ID}")


if __name__ == "__main__":
    prepare()
# endregion
