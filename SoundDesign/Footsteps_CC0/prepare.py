"""将开放授权的真实脚步录音整理为正式 Cue 素材，不合成替代声源。"""

from __future__ import annotations

import hashlib
import json
import math
import re
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]

import numpy as np
import soundfile as sf
from scipy.signal import butter, resample_poly, sosfilt

RATE = 48000


# region 实录整理与绑定
def prepare():
    sources = json.loads((ROOT / "sources.json").read_text(encoding="utf-8"))
    entries = []
    for segment in sources["segments"]:
        source = ROOT / segment["source"]
        assert hashlib.sha256(source.read_bytes()).hexdigest() == segment["sourceSha256"]
        audio, rate = sf.read(source, always_2d=True)
        audio = audio.mean(axis=1)
        if rate != RATE:
            divisor = math.gcd(rate, RATE)
            audio = resample_poly(audio, RATE // divisor, rate // divisor)
        audio = sosfilt(butter(2, 60, fs=RATE, btype="highpass", output="sos"), audio)
        audio -= audio.mean()
        # 裁去录音头尾静音，完整保留脚跟落地和地表摩擦的自然尾音。
        threshold = max(float(np.max(np.abs(audio))) * .012, .00001)
        audible = np.flatnonzero(np.abs(audio) >= threshold)
        assert len(audible) > 0
        start = max(0, int(audible[0]) - round(.008 * RATE))
        end = min(len(audio), int(audible[-1]) + round(.035 * RATE))
        if "maxSeconds" in segment:
            end = min(end, start + round(segment["maxSeconds"] * RATE))
        audio = audio[start:end].copy()
        fade_in = min(round(.003 * RATE), len(audio))
        fade_out = min(round(.025 * RATE), len(audio))
        audio[:fade_in] *= np.linspace(0, 1, fade_in)
        audio[-fade_out:] *= np.linspace(1, 0, fade_out)
        audio *= 10 ** (-12 / 20) / np.max(np.abs(audio))
        asset = PROJECT / segment["file"]
        asset.parent.mkdir(parents=True, exist_ok=True)
        temporary = asset.with_suffix(".tmp.wav")
        sf.write(temporary, audio, RATE, subtype="PCM_16")
        temporary.replace(asset)
        meta = Path(str(asset) + ".meta")
        if not meta.exists():
            template = (PROJECT / "Assets/Audio/Generated/player.footstep.stone__01.wav.meta").read_text(encoding="utf-8")
            template = re.sub(r"guid: [a-f0-9]{32}", "guid: " + uuid.uuid4().hex, template, count=1)
            meta.write_text(template, encoding="utf-8")
        guid = re.search(r"guid: ([a-f0-9]{32})", meta.read_text(encoding="utf-8")).group(1)
        decoded, _ = sf.read(asset)
        entries.append(dict(cue=segment["cue"], variant=segment["variant"], file=segment["file"],
                            seconds=len(audio) / RATE, guid=guid,
                            peakDb=round(20 * math.log10(np.max(np.abs(decoded))), 2),
                            rmsDb=round(20 * math.log10(np.sqrt(np.mean(decoded ** 2))), 2),
                            sha256=hashlib.sha256(asset.read_bytes()).hexdigest(),
                            externalManifest="SoundDesign/Footsteps_CC0/manifest.json"))
    for cue in dict.fromkeys(entry["cue"] for entry in entries):
        cue_path = PROJECT / "Assets/Resources/Audio/Cues" / (cue + ".asset")
        if not cue_path.exists():
            template = (PROJECT / "Assets/Resources/Audio/Cues/player.footstep.stone.asset").read_text(encoding="utf-8")
            cue_path.write_text(template.replace("player.footstep.stone", cue), encoding="utf-8")
            Path(str(cue_path) + ".meta").write_text(
                "fileFormatVersion: 2\nguid: " + uuid.uuid4().hex +
                "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                encoding="utf-8")
        config = cue_path.read_text(encoding="utf-8")
        clips = "  clips:\n" + "".join(
            f"  - {{fileID: 8300000, guid: {entry['guid']}, type: 3}}\n"
            for entry in entries if entry["cue"] == cue)
        config = re.sub(r"  clips:\n(?:  - [^\n]*\n)+", clips, config)
        cue_path.write_text(config, encoding="utf-8")
        cue_guid = re.search(r"guid: ([a-f0-9]{32})", Path(str(cue_path) + ".meta").read_text(encoding="utf-8")).group(1)
        catalog = PROJECT / "Assets/Resources/Audio/AudioCatalog.asset"
        text = catalog.read_text(encoding="utf-8")
        line = f"  - {{fileID: 11400000, guid: {cue_guid}, type: 2}}"
        if line not in text:
            catalog.write_text(text.rstrip() + "\n" + line + "\n", encoding="utf-8")
    manifest = dict(engine="CC0 recordings; trim, mono conversion, resampling and gain only",
                    sampleRate=RATE, channels=1, sounds=entries)
    (ROOT / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    old_path = PROJECT / "SoundDesign/Player_Fishing_SFX_LMMS/manifest.json"
    previous = json.loads(old_path.read_text(encoding="utf-8"))
    replacements = {entry["file"]: entry for entry in entries}
    previous["sounds"] = [dict(entry, **replacements.get(entry["file"], {})) for entry in previous["sounds"]]
    previous["engine"] = "LMMS fishing; CC0 recorded footsteps"
    old_path.write_text(json.dumps(previous, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Prepared {len(entries)} recorded footsteps; kept existing asset GUIDs")
# endregion


if __name__ == "__main__":
    prepare()
