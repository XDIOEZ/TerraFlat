# 角色脚步与钓鱼：LMMS MCP 原创音效

- 脚步：泥土、草地、沙地、石地、积雪、浅水各 3 个变体。
- 钓鱼：成功抛竿、真正咬钩、首次收线各 2 个变体。
- 共 9 个独立 Cue、24 个 48 kHz 单声道 PCM WAV；音源由 LMMS 1.2.2 TripleOscillator 生成，不使用代码合成 WAV。

## 再制作

```powershell
python SoundDesign/Player_Fishing_SFX_LMMS/build_sfx.py build
& 'C:\Users\PC\Documents\Codex\2026-10-01\yz\outputs\lmms-mcp\.venv\Scripts\python.exe' SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render --project SoundDesign/Player_Fishing_SFX_LMMS/Player_Fishing.mmp --output SoundDesign/Player_Fishing_SFX_LMMS/renders/Player_Fishing_raw.wav
python SoundDesign/Player_Fishing_SFX_LMMS/build_sfx.py finalize
python SoundDesign/Player_Fishing_SFX_LMMS/build_sfx.py verify
```

`Player_Fishing.mmp` 是可编辑 LMMS 工程；`renders/` 为忽略的 MCP 渲染母带，`Preview_Player_Fishing.wav` 串联每种首个变体。分切脚本只做淡入淡出、响度整理、GUID 保留与 Cue 装配。

脚步只在本地玩家主动移动、实际跨过步距时触发；不根据输入次数、风、水流或载具位移伪造脚步。按脚下已加载地块实际液深、雪层、群系和草被选择音效，深水或无有效地块不播放。钓鱼在完成鱼饵投出事务、鱼真正上钩及首次收线时各发声一次。
