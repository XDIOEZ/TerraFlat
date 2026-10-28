"""从保留的开放授权自然实录整理循环 WAV 与 Unity 音频资源。"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import shutil
import struct
import subprocess
import tempfile
import wave
from pathlib import Path


ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
SOURCES = ROOT / "sources.json"
AUDIO_BUS_ENUM_VALUES = {"Music": 0, "Sfx": 1, "UI": 2, "Ambient": 3, "Voice": 4}


# region 开放授权原声与处理工具
def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def write_text(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    cleaned = "\n".join(line.rstrip() for line in content.splitlines()) + "\n"
    if path.exists() and path.read_text(encoding="utf-8") == cleaned:
        return
    path.write_text(cleaned, encoding="utf-8", newline="\n")


def resolve_audio_tool(name: str) -> str:
    executable = shutil.which(name)
    if executable:
        return executable
    ffmpeg = shutil.which("ffmpeg")
    if ffmpeg:
        adjacent = Path(ffmpeg).with_name(name + (".exe" if os.name == "nt" else ""))
        if adjacent.is_file():
            return str(adjacent)
    raise RuntimeError(f"找不到 {name}，请安装 FFmpeg 并把 bin 目录加入 PATH")


def run_audio_command(arguments: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(arguments, check=True, text=True, capture_output=True)


def probe_audio(path: Path, ffprobe: str) -> dict:
    result = run_audio_command([
        ffprobe, "-v", "error", "-select_streams", "a:0", "-show_streams", "-show_format",
        "-of", "json", str(path),
    ])
    document = json.loads(result.stdout)
    if not document.get("streams"):
        raise RuntimeError(f"找不到音频流：{path}")
    return {"stream": document["streams"][0], "format": document.get("format", {})}


def measure_volume(path: Path, ffmpeg: str) -> dict[str, float]:
    result = run_audio_command([
        ffmpeg, "-hide_banner", "-nostats", "-i", str(path), "-vn", "-af", "volumedetect", "-f", "null", "-",
    ])
    peak = re.search(r"max_volume:\s*(-?inf|-?[0-9.]+) dB", result.stderr)
    rms = re.search(r"mean_volume:\s*(-?inf|-?[0-9.]+) dB", result.stderr)
    if peak is None or rms is None or peak.group(1) == "-inf" or rms.group(1) == "-inf":
        raise RuntimeError(f"无法测量音频电平：{path}")
    return {"peakDbfs": float(peak.group(1)), "rmsDbfs": float(rms.group(1))}


def measure_pcm_loop_boundary_delta(path: Path) -> float:
    with wave.open(str(path), "rb") as source:
        if source.getsampwidth() != 2 or source.getnchannels() != 1:
            raise RuntimeError(f"循环接缝检查要求单声道 PCM16：{path}")
        first = struct.unpack("<h", source.readframes(1))[0]
        source.setpos(source.getnframes() - 1)
        last = struct.unpack("<h", source.readframes(1))[0]
    return abs(first - last) / 32768.0


def measure_pcm_windowed_rms(path: Path, window_seconds: float) -> dict:
    with wave.open(str(path), "rb") as source:
        sample_rate = source.getframerate()
        if source.getsampwidth() != 2 or source.getnchannels() != 1:
            raise RuntimeError(f"RMS 采样要求单声道 PCM16：{path}")
        samples = struct.unpack(f"<{source.getnframes()}h", source.readframes(source.getnframes()))
    window_frames = round(sample_rate * window_seconds)
    if not samples or window_frames <= 0 or len(samples) % window_frames:
        raise RuntimeError(f"RMS 窗口无法完整覆盖音频：{path}")
    windows = [
        math.sqrt(sum(value * value for value in samples[offset:offset + window_frames]) / window_frames) / 32768.0
        for offset in range(0, len(samples), window_frames)
    ]
    global_rms = math.sqrt(sum(value * value for value in samples) / len(samples)) / 32768.0
    minimum = min(windows)
    ratio = minimum / global_rms if global_rms else 0.0
    seam_windows = windows[-round(1.0 / window_seconds):]
    return {
        "windowRmsSeconds": window_seconds,
        "windowRmsDbfs": [round(20 * math.log10(max(value, 1e-12)), 3) for value in windows],
        "globalRmsDbfs": round(20 * math.log10(max(global_rms, 1e-12)), 3),
        "minWindowRmsDbfs": round(20 * math.log10(max(minimum, 1e-12)), 3),
        "minWindowRmsToGlobalRmsRatio": round(ratio, 6),
        "loopSeamMinWindowRmsToGlobalRmsRatio": round(min(seam_windows) / global_rms, 6) if global_rms else 0.0,
    }


def render_continuous_nature_loop_from_recording(item: dict, settings: dict, ffmpeg: str, ffprobe: str) -> dict:
    source = ROOT / item["sourceFile"]
    if sha256(source) != item["sourceSha256"]:
        raise RuntimeError(f"原始音源哈希不匹配：{source}")
    source_info = probe_audio(source, ffprobe)["stream"]
    if int(source_info["channels"]) != 2:
        raise RuntimeError(f"原始录音应为双声道：{source}")

    output = PROJECT / item["outputFile"]
    output.parent.mkdir(parents=True, exist_ok=True)
    sample_rate = int(settings["sampleRate"])
    loop_seconds = float(item["loopSeconds"])
    seam_seconds = float(settings["crossfadeSeconds"])
    curve = settings["crossfadeCurve"]
    if curve != "qsin":
        raise RuntimeError("自然循环接缝必须使用 qsin 等功率曲线")
    source_seconds = loop_seconds + seam_seconds
    start = float(item["sourceStartSeconds"])
    if start + source_seconds > float(source_info["duration"]):
        raise RuntimeError(f"原始录音过短，无法裁切循环：{source}")

    # 循环从相同原始采样点开始和结束，尾部以等功率交叠接回开头。
    graph = (
        f"[0:a]aresample={sample_rate},pan=mono|c0=0.5*c0+0.5*c1,asetpts=PTS-STARTPTS,"
        f"atrim=start={start}:duration={source_seconds},asetpts=PTS-STARTPTS,asplit=3[h0][b0][t0];"
        f"[h0]atrim=start=0:duration={seam_seconds},asetpts=PTS-STARTPTS[head];"
        f"[b0]atrim=start={seam_seconds}:duration={loop_seconds - seam_seconds},asetpts=PTS-STARTPTS[body];"
        f"[t0]atrim=start={loop_seconds}:duration={seam_seconds},asetpts=PTS-STARTPTS[tail];"
        f"[tail][head]acrossfade=d={seam_seconds}:c1={curve}:c2={curve}[seam];"
        "[body][seam]concat=n=2:v=0:a=1[out]"
    )

    with tempfile.TemporaryDirectory(prefix="nature-ambience-", dir=ROOT) as temporary_folder:
        temporary = Path(temporary_folder)
        raw = temporary / "raw.wav"
        prepared = temporary / "prepared.wav"
        run_audio_command([
            ffmpeg, "-hide_banner", "-nostats", "-y", "-i", str(source), "-filter_complex", graph,
            "-map", "[out]", "-t", str(loop_seconds), "-ar", str(sample_rate), "-ac", "1",
            "-c:a", "pcm_s16le", str(raw),
        ])
        raw_levels = measure_volume(raw, ffmpeg)
        gain_db = float(settings["peakTargetDbfs"]) - raw_levels["peakDbfs"]
        run_audio_command([
            ffmpeg, "-hide_banner", "-nostats", "-y", "-i", str(raw), "-af", f"volume={gain_db:.8f}dB",
            "-ar", str(sample_rate), "-ac", "1", "-c:a", "pcm_s16le", str(prepared),
        ])
        os.replace(prepared, output)

    output_info = probe_audio(output, ffprobe)["stream"]
    levels = measure_volume(output, ffmpeg)
    expected_frames = round(loop_seconds * sample_rate)
    if int(output_info["sample_rate"]) != sample_rate or int(output_info["channels"]) != 1:
        raise RuntimeError(f"成品采样规格错误：{output}")
    if int(output_info["bits_per_sample"]) != 16 or int(output_info["duration_ts"]) != expected_frames:
        raise RuntimeError(f"成品位深或长度错误：{output}")

    preview = PROJECT / item["previewFile"]
    preview.parent.mkdir(parents=True, exist_ok=True)
    preview_temp = preview.with_suffix(".partial.mp3")
    run_audio_command([
        ffmpeg, "-hide_banner", "-nostats", "-y", "-i", str(source), "-ss", str(start),
        "-t", str(loop_seconds), "-vn", "-codec:a", "libmp3lame", "-q:a", "4", str(preview_temp),
    ])
    os.replace(preview_temp, preview)

    create_unity_audio_importer_meta(output, item, settings)
    rms_metrics = measure_pcm_windowed_rms(output, 0.1)
    return {
        "cueId": item["cueId"], "sourceSha256": sha256(source), "outputFile": item["outputFile"],
        "cueBus": item["cue"]["bus"], "cueBusEnum": AUDIO_BUS_ENUM_VALUES[item["cue"]["bus"]],
        "outputSha256": sha256(output), "previewFile": item["previewFile"],
        "previewSha256": sha256(preview), "seconds": loop_seconds, "sampleRate": sample_rate,
        "channels": 1, "format": "PCM16 WAV", "crossfadeSeconds": seam_seconds,
        "sourceStartSeconds": start, "appliedGainDb": round(gain_db, 6),
        "rawPeakDbfs": raw_levels["peakDbfs"], "peakDbfs": levels["peakDbfs"], "rmsDbfs": levels["rmsDbfs"],
        "crossfadeCurve": f"{curve}/{curve}", **rms_metrics,
    }
# endregion


# region Unity 音频资源
def create_unity_audio_importer_meta(output: Path, item: dict, settings: dict) -> None:
    meta = (
        "fileFormatVersion: 2\n"
        f"guid: {item['clipGuid']}\n"
        "AudioImporter:\n"
        "  externalObjects: {}\n"
        "  serializedVersion: 7\n"
        "  defaultSettings:\n"
        "    serializedVersion: 2\n"
        "    loadType: 0\n"
        "    sampleRateSetting: 0\n"
        f"    sampleRateOverride: {settings['sampleRate']}\n"
        "    compressionFormat: 0\n"
        "    quality: 1\n"
        "    conversionMode: 0\n"
        "    preloadAudioData: 1\n"
        "  platformSettingOverrides: {}\n"
        "  forceToMono: 1\n"
        "  normalize: 0\n"
        "  loadInBackground: 0\n"
        "  ambisonic: 0\n"
        "  3D: 1\n"
        f"  userData: CC0 {item['author']}; nature field recording\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )
    write_text(Path(str(output) + ".meta"), meta)


def create_unity_audio_cue_asset(item: dict) -> None:
    cue = item["cue"]
    cue_path = PROJECT / item["cueFile"]
    content = (
        "%YAML 1.1\n"
        "%TAG !u! tag:unity3d.com,2011:\n"
        "--- !u!114 &11400000\n"
        "MonoBehaviour:\n"
        "  m_ObjectHideFlags: 0\n"
        "  m_CorrespondingSourceObject: {fileID: 0}\n"
        "  m_PrefabInstance: {fileID: 0}\n"
        "  m_PrefabAsset: {fileID: 0}\n"
        "  m_GameObject: {fileID: 0}\n"
        "  m_Enabled: 1\n"
        "  m_EditorHideFlags: 0\n"
        "  m_Script: {fileID: 11500000, guid: c0c19eb3306146649bcc5130f92e0433, type: 3}\n"
        f"  m_Name: {item['cueId']}\n"
        "  m_EditorClassIdentifier:\n"
        f"  id: {item['cueId']}\n"
        f"  bus: {AUDIO_BUS_ENUM_VALUES[cue['bus']]}\n"
        "  clips:\n"
        f"  - {{fileID: 8300000, guid: {item['clipGuid']}, type: 3}}\n"
        "  selection: 0\n"
        "  loop: 1\n"
        "  volume:\n"
        f"    Min: {cue['volume']:.3f}\n"
        f"    Max: {cue['volume']:.3f}\n"
        "  pitch:\n"
        "    Min: 1\n"
        "    Max: 1\n"
        "  spatialBlend: 1\n"
        f"  minDistance: {cue['minDistance']:.1f}\n"
        f"  maxDistance: {cue['maxDistance']:.1f}\n"
        "  priority: 128\n"
        "  cooldown: 0\n"
        f"  maxInstances: {cue['maxInstances']}\n"
        "  concurrencyPolicy: 1\n"
        "  rolloffMode: 1\n"
        "  outputOverride: {fileID: 0}\n"
    )
    write_text(cue_path, content)
    write_text(cue_path.with_suffix(cue_path.suffix + ".meta"), (
        "fileFormatVersion: 2\n"
        f"guid: {item['cueGuid']}\n"
        "NativeFormatImporter:\n"
        "  externalObjects: {}\n"
        "  mainObjectFileID: 11400000\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def create_nature_audio_folder_meta(settings: dict) -> None:
    path = PROJECT / "Assets/Audio/Nature.meta"
    if path.exists():
        current = path.read_text(encoding="utf-8")
        if f"guid: {settings['folderGuid']}" not in current:
            raise RuntimeError(f"自然音频目录 GUID 与来源记录不一致：{path}")
        return
    write_text(path, (
        "fileFormatVersion: 2\n"
        f"guid: {settings['folderGuid']}\n"
        "folderAsset: yes\n"
        "DefaultImporter:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    ))


def create_unity_audio_cue_assets(items: list[dict], settings: dict) -> None:
    create_nature_audio_folder_meta(settings)
    for item in items:
        create_unity_audio_cue_asset(item)
# endregion


# region 来源与成品校验
def verify_nature_audio_assets(items: list[dict], settings: dict, manifest: dict, ffprobe: str) -> None:
    if not re.fullmatch(r"[0-9a-f]{32}", settings["folderGuid"]):
        raise RuntimeError("Assets/Audio/Nature.meta 的 GUID 格式错误")
    for item, saved in zip(items, manifest["sounds"], strict=True):
        source = ROOT / item["sourceFile"]
        output = PROJECT / item["outputFile"]
        preview = PROJECT / item["previewFile"]
        cue_path = PROJECT / item["cueFile"]
        for name, guid in (("clip", item["clipGuid"]), ("cue", item["cueGuid"])):
            if not re.fullmatch(r"[0-9a-f]{32}", guid):
                raise RuntimeError(f"{item['cueId']} 的 {name} GUID 格式错误")
        if sha256(source) != item["sourceSha256"] or sha256(output) != saved["outputSha256"]:
            raise RuntimeError(f"{item['cueId']} 的原声或成品哈希不匹配")
        if sha256(preview) != saved["previewSha256"]:
            raise RuntimeError(f"{item['cueId']} 的试听文件哈希不匹配")
        expected_curve = f"{settings['crossfadeCurve']}/{settings['crossfadeCurve']}"
        if saved["crossfadeCurve"] != expected_curve or manifest["crossfadeCurve"] != expected_curve:
            raise RuntimeError(f"{item['cueId']} 的循环曲线必须与 sources.json/manifest 一致")
        info = probe_audio(output, ffprobe)["stream"]
        if int(info["sample_rate"]) != int(settings["sampleRate"]) or int(info["channels"]) != 1:
            raise RuntimeError(f"{item['cueId']} 的成品采样规格不匹配")
        if abs(float(info["duration"]) - float(item["loopSeconds"])) > 0.001:
            raise RuntimeError(f"{item['cueId']} 的循环时长不匹配")
        seam_delta = measure_pcm_loop_boundary_delta(output)
        if seam_delta > 0.01:
            raise RuntimeError(f"{item['cueId']} 的循环边界有明显采样跳变：{seam_delta:.6f}")
        rms_metrics = measure_pcm_windowed_rms(output, 0.1)
        if rms_metrics["minWindowRmsToGlobalRmsRatio"] < 0.03:
            raise RuntimeError(f"{item['cueId']} 存在低于总 RMS 3% 的 100 ms 窗口")
        if rms_metrics["loopSeamMinWindowRmsToGlobalRmsRatio"] < 0.03:
            raise RuntimeError(f"{item['cueId']} 的循环接缝 RMS 低于总 RMS 3%")
        for metric in ("globalRmsDbfs", "minWindowRmsDbfs", "minWindowRmsToGlobalRmsRatio", "loopSeamMinWindowRmsToGlobalRmsRatio"):
            if abs(saved[metric] - rms_metrics[metric]) > 0.002:
                raise RuntimeError(f"{item['cueId']} 的 manifest {metric} 与音频不匹配")
        if saved["windowRmsSeconds"] != 0.1 or len(saved["windowRmsDbfs"]) != 200:
            raise RuntimeError(f"{item['cueId']} 的 manifest 缺少完整 100 ms RMS 采样")
        if any(abs(saved_value - actual_value) > 0.002 for saved_value, actual_value in zip(saved["windowRmsDbfs"], rms_metrics["windowRmsDbfs"], strict=True)):
            raise RuntimeError(f"{item['cueId']} 的 manifest RMS 采样与音频不匹配")
        meta = Path(str(output) + ".meta").read_text(encoding="utf-8")
        cue = cue_path.read_text(encoding="utf-8")
        cue_meta = cue_path.with_suffix(cue_path.suffix + ".meta").read_text(encoding="utf-8")
        if f"guid: {item['clipGuid']}" not in meta or f"guid: {item['clipGuid']}" not in cue:
            raise RuntimeError(f"{item['cueId']} 的 AudioClip GUID 引用不匹配")
        if f"guid: {item['cueGuid']}" not in cue_meta or f"id: {item['cueId']}" not in cue:
            raise RuntimeError(f"{item['cueId']} 的 AudioCue GUID 或 ID 不匹配")
        cue_fields = (
            f"  bus: {AUDIO_BUS_ENUM_VALUES[item['cue']['bus']]}",
            "  loop: 1", "  spatialBlend: 1", "  priority: 128", "  cooldown: 0",
            f"    Min: {item['cue']['volume']:.3f}", f"    Max: {item['cue']['volume']:.3f}",
            f"  minDistance: {item['cue']['minDistance']:.1f}",
            f"  maxDistance: {item['cue']['maxDistance']:.1f}",
            f"  maxInstances: {item['cue']['maxInstances']}", "  rolloffMode: 1",
        )
        if any(field not in cue for field in cue_fields):
            raise RuntimeError(f"{item['cueId']} 的 AudioCue 配置不符合 sources.json")
        if saved["cueBus"] != item["cue"]["bus"] or saved["cueBusEnum"] != AUDIO_BUS_ENUM_VALUES[item["cue"]["bus"]]:
            raise RuntimeError(f"{item['cueId']} 的 manifest 总线与 sources.json 不匹配")
        if "    loadType: 0" not in meta or "    compressionFormat: 0" not in meta or "    preloadAudioData: 1" not in meta or "  normalize: 0" not in meta:
            raise RuntimeError(f"{item['cueId']} 的 AudioImporter 未设为预加载 PCM")
        if not all(path.read_bytes().endswith(b"\n") for path in (
            Path(str(output) + ".meta"), cue_path, cue_path.with_suffix(cue_path.suffix + ".meta"),
        )):
            raise RuntimeError(f"{item['cueId']} 的 Unity 资源缺少结尾换行")
        levels = measure_volume(output, resolve_audio_tool("ffmpeg"))
        print(f"{item['cueId']}: {item['loopSeconds']:.1f}s, 48000 Hz mono PCM16, peak {levels['peakDbfs']:.2f} dBFS, min 100 ms RMS/global {rms_metrics['minWindowRmsToGlobalRmsRatio']:.3%}, loop seam {rms_metrics['loopSeamMinWindowRmsToGlobalRmsRatio']:.3%}, sample delta {seam_delta:.6f}")
# endregion


def main() -> None:
    parser = argparse.ArgumentParser(description="整理自然环境开放授权实录并创建 FlatWorld 音频资源")
    parser.add_argument("command", choices=("build", "verify"), nargs="?", default="build")
    args = parser.parse_args()
    document = json.loads(SOURCES.read_text(encoding="utf-8"))
    settings = document["processing"]
    items = document["sounds"]
    ffmpeg = resolve_audio_tool("ffmpeg")
    ffprobe = resolve_audio_tool("ffprobe")
    if args.command == "build":
        create_unity_audio_cue_assets(items, settings)
        sounds = [render_continuous_nature_loop_from_recording(item, settings, ffmpeg, ffprobe) for item in items]
        manifest = {
            "engine": "Original nature field recordings; offline trim, mono fold-down, equal-power loop crossfade, and static gain only",
            "sampleRate": settings["sampleRate"], "channels": 1, "encoding": "PCM16 WAV",
            "peakTargetDbfs": settings["peakTargetDbfs"], "crossfadeCurve": f"{settings['crossfadeCurve']}/{settings['crossfadeCurve']}", "sounds": sounds,
        }
        write_text(ROOT / "manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
    manifest = json.loads((ROOT / "manifest.json").read_text(encoding="utf-8"))
    if settings["crossfadeCurve"] != "qsin":
        raise RuntimeError("自然环境循环接缝必须保留 qsin 等功率曲线")
    verify_nature_audio_assets(items, settings, manifest, ffprobe)


if __name__ == "__main__":
    main()
