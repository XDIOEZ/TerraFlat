# 游泳：LMMS MCP 原创音效

三类世界音效各保留 3 个变体，共 9 个 48 kHz、单声道、16-bit PCM WAV。

| Cue | 内容 | 长度 | 峰值上限 | 平均电平上限 |
| --- | --- | --- | --- | --- |
| `player.swim.enter` | 一次入水水花，接少量落水滴 | 0.65 秒 | -13 dBFS | -29 dBFS |
| `player.swim.stroke` | 短促水膜划动，接两三颗细水滴 | 0.60 秒 | -17 dBFS | -31 dBFS |
| `player.swim.exit` | 轻出水与稀疏滴水，逐渐变弱 | 0.55 秒 | -18 dBFS | -32 dBFS |

音源由 LMMS 1.2.2 原生 TripleOscillator 白噪声、音量包络和低通/带通滤波器制作。水膜采用短包络；少量带共鸣的水滴按非等距间隔编排。避免低频鼓点、连续沙沙底噪和密集重复。三个变体调整滤波频率、衰减与水滴落点；运行时由 Cue 随机选取。

## 再制作

```powershell
python SoundDesign/Swimming_SFX_LMMS/build_swimming.py build
& 'C:\Users\PC\Documents\Codex\2026-10-01\yz\outputs\lmms-mcp\.venv\Scripts\python.exe' SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render --project SoundDesign/Swimming_SFX_LMMS/Swimming.mmp --output SoundDesign/Swimming_SFX_LMMS/renders/Swimming_raw.wav
python SoundDesign/Swimming_SFX_LMMS/build_swimming.py finalize
python SoundDesign/Swimming_SFX_LMMS/build_swimming.py verify
```

`Swimming.mmp` 是可编辑 LMMS 工程，必须通过真实 LMMS MCP 加载/保存/渲染后再整理；Python 仅编写工程、裁切、去直流、淡入淡出、调整增益、写 Unity meta 和 Cue，不合成替代声源。`renders/` 忽略渲染母带，`manifest.json` 保存母带摘要与成品参数；`Preview_Swimming.wav` 按入水、划水、出水顺序串联首个变体。

三个 Cue 复用浅水脚步的世界 SFX 总线与线性空间衰减：1.5 格内正常，14 格静音；冷却 0.12 秒，划水最多重叠 2 个声音。玩法按真实水体状态切换和主动移动距离触发；浅水继续使用已有涉水脚步声，静止游泳不连续播放划水。

官方参数说明：[Triple Oscillator](https://docs.lmms.io/user-manual/instruments/triple-oscillator)、[乐器窗口与滤波/包络](https://docs.lmms.io/user-manual/jian-ti-zhong-wen/shu-xi-lmms/yue-qi-chuang-kou)。技术检查只确认素材和引用完整，游戏内听感由用户验收。
