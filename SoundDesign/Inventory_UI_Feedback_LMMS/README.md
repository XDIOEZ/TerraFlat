# 库存交换与装备 UI 音效

LMMS MCP 新增 3 类音效：交换物品、穿戴装备、卸下装备。每类两个变体，48kHz 单声道 PCM，全部通过 TripleOscillator 原生轨道合成。

```powershell
python SoundDesign/Inventory_UI_Feedback_LMMS/build_sfx.py build
& 'C:\Users\PC\Documents\Codex\2026-10-01\yz\outputs\lmms-mcp\.venv\Scripts\python.exe' SoundDesign/Gameplay_Feedback_LMMS/lmms_mcp_render.py --render --project SoundDesign/Inventory_UI_Feedback_LMMS/Inventory_UI_Feedback.mmp --output SoundDesign/Inventory_UI_Feedback_LMMS/renders/Inventory_UI_Feedback_raw.wav
python SoundDesign/Inventory_UI_Feedback_LMMS/build_sfx.py finalize
python SoundDesign/Inventory_UI_Feedback_LMMS/build_sfx.py verify
```

旧的 72 类物品取放音效位于 `SoundDesign/Inventory_Items_LMMS/`，通过 `install_runtime_cues.py install/verify` 为所有现有素材注册正式 UI Cue，不重做已有母带，也不修改原有 WAV GUID。新旧音效统一由 `InventoryInteractionAudio` 从成功的用户操作和装备槽变更事件调用。
