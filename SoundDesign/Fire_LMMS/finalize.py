"""整理 LMMS 母带的频段、增益和循环接缝，不合成替代火焰录音。"""
from pathlib import Path
import json
import sys

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parents[1]
sys.path.insert(0, str(PROJECT / ".codex-tmp/audio-python"))
import numpy as np
import soundfile as sf
from scipy.signal import butter, sosfilt
from scipy.ndimage import maximum_filter1d, gaussian_filter1d

# region 火焰循环母带整理
def main():
    data, rate = sf.read(PROJECT / ".codex-tmp/fire/Fire_Burning_raw.wav", always_2d=True)
    assert rate == 48000
    data = data.mean(axis=1)[rate:22 * rate].copy()
    assert len(data) == 21 * rate
    data = sosfilt(butter(2, [120, 4800], btype="bandpass", fs=rate, output="sos"), data)
    data -= data.mean()
    # 压住少数木柴爆点的增益，避免底声被一个尖峰压得太轻。
    threshold = float(np.percentile(np.abs(data), 98.5))
    envelope = maximum_filter1d(np.abs(data), size=1921)
    gain = np.minimum(1, (np.maximum(envelope, threshold) / max(threshold, 1e-9)) ** -0.95)
    data *= gaussian_filter1d(gain, sigma=96)
    # 一秒首尾交叠保留稳定底声，循环边缘不淡到静音。
    mix = np.linspace(0, 1, rate, endpoint=False)
    data[:rate] = data[-rate:] * (1 - mix) + data[:rate] * mix
    data = data[:-rate]
    # 轻柔底声按均方根定音量，并限制爆点峰值，避免压峰后又把全段放大。
    data *= min(0.30 / max(float(np.max(np.abs(data))), 1e-9),
                0.045 / max(float(np.sqrt(np.mean(data * data))), 1e-9))
    assert np.isfinite(data).all() and np.sqrt(np.mean(data * data)) > 0.001
    output = PROJECT / "Assets/Audio/Generated/fire.burning.loop__01.wav"
    temporary = output.with_suffix(".tmp.wav")
    with sf.SoundFile(temporary, "w", samplerate=rate, channels=1, subtype="PCM_16") as stream:
        for offset in range(0, len(data), 4096):
            stream.write(data[offset:offset + 4096])
    temporary.replace(output)
    print(json.dumps({"file": str(output), "seconds": len(data) / rate,
                      "sampleRate": rate, "channels": 1, "peak": float(np.max(np.abs(data))),
                      "rms": float(np.sqrt(np.mean(data * data))),
                      "seamStep": float(abs(data[0] - data[-1]))}, indent=2))
# endregion

if __name__ == "__main__":
    main()
