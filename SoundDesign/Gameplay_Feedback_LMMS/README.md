# 玩法成功/失败反馈音效（LMMS MCP）

本组填补已有玩法事件缺少反馈音的问题：建筑落位/位置非法、手工制作完成、熔炼出炉、引火成功与炉火点燃，共 6 个 Cue、12 个原创 LMMS 变体。

调用端监听正式的 `GameplayProgressEvents.SignalPublished` 和 `BuildingPlacementFeedbackEvents.PlacementRejected`，只在成功提交后或玩家主动尝试失败时播放。空间声源跟随执行者，受当前 SFX 音量和传播距离限制。

## 复现步骤

```powershell
python SoundDesign/Gameplay_Feedback_LMMS/build_feedback.py build
& 'C:\Users\PC\Documents\Codex\2026-10-01\yz\outputs\lmms-mcp\.venv\Scripts\python.exe' SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render
python SoundDesign/Gameplay_Feedback_LMMS/build_feedback.py finalize
python SoundDesign/Gameplay_Feedback_LMMS/build_feedback.py verify
```

- `Gameplay_Feedback.mmp` 是可在 LMMS 1.2.2 中编辑的原生工程，沿用 `Gameplay_SFX_LMMS` 的 TripleOscillator 合成模块。
- `renders/` 仅保存 MCP 输出母带，加入 gitignore；`finalize` 裁切并调节增益，不自行合成或代替 LMMS 音源。
- 成品为 `Assets/Audio/Generated/` 中的 48 kHz/16-bit/单声道 WAV；`Assets/Resources/Audio/Cues/` 保存稳定事件及 GUID；`Preview_Feedback.wav` 串联每个事件的第一变体用于试听。
- 反复制作时保留原 WAV/Cue GUID，保留已有 Cue 配置并只更新 `clips` 数组。
