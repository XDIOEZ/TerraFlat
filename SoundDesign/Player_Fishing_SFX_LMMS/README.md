# 角色脚步与钓鱼音效

- 脚步：泥土、草地、沙地、石地、积雪、浅水各 3 个变体。
- 钓鱼：成功抛竿、真正咬钩、首次收线各 2 个变体。
- 共 9 个独立 Cue、24 个 48 kHz 单声道 PCM WAV；草地使用真实录音经 LMMS Sample Track 编排渲染，雪地使用独立整理的 CC0 干雪实录，其余音源由 LMMS 1.2.2 TripleOscillator 生成。

草地的 3 个变体来自 Fission9 的 [Hiking Boot Footsteps on Grass](https://freesound.org/people/Fission9/sounds/521587/)，许可为 CC0。`grass_sources.json` 保存下载地址、来源哈希与裁切参数，`Sources/grass_*.wav` 是可直接随工程使用的采样。整理时削弱低频闷响、轻压鞋底尖峰，保留草叶摩擦；每步 0.42 秒，沿用原 Cue 与 WAV GUID。

雪地的 3 个变体来自 qubodup 的 [4 dry snow steps](https://opengameart.org/content/4-dry-snow-steps)，许可为 CC0。原始 FLAC、授权、处理脚本和鸣谢在 `SoundDesign/Snow_Footsteps/`；先运行其中的 `prepare_snow.py prepare` 可独立重建。下面的 LMMS 流程保留雪原有槽位，但不再生成或覆盖雪音，只校验并复用这三个实录成品。

## 再制作

```powershell
python SoundDesign/Player_Fishing_SFX_LMMS/prepare_grass.py
python SoundDesign/Player_Fishing_SFX_LMMS/build_sfx.py build
& 'C:\Users\PC\Documents\Codex\2026-10-01\yz\outputs\lmms-mcp\.venv\Scripts\python.exe' SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render --project SoundDesign/Player_Fishing_SFX_LMMS/Player_Fishing.mmp --output SoundDesign/Player_Fishing_SFX_LMMS/renders/Player_Fishing_raw.wav
python SoundDesign/Player_Fishing_SFX_LMMS/build_sfx.py finalize
python SoundDesign/Player_Fishing_SFX_LMMS/build_sfx.py verify
```

只替换草地时，渲染后执行 `build_sfx.py finalize --only-grass`；该选项只更新草地的三个 WAV，并同步清单和总预览。`prepare_grass.py` 需要 NumPy、SciPy、SoundFile，可复用项目 `.codex-tmp/audio-python/` 的依赖。

`Player_Fishing.mmp` 是可编辑 LMMS 工程；`renders/` 为忽略的 MCP 渲染母带，`Preview_Player_Fishing.wav` 串联每种首个变体。分切脚本只做淡入淡出、响度整理、GUID 保留与 Cue 装配。

脚步只在本地玩家主动移动、实际跨过步距时触发；不根据输入次数、风、水流或载具位移伪造脚步。按脚下已加载地块实际液深、雪层、群系和草被选择音效，深水或无有效地块不播放。钓鱼在完成鱼饵投出事务、鱼真正上钩及首次收线时各发声一次。
