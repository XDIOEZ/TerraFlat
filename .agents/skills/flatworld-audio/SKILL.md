---
name: flatworld-audio
description: "FlatWorld 音频服务、Cue、声源池、空间衰减、音量设置与音效资源。"
---

# FlatWorld 音频

## 入口

- 核心：`Assets/5_Scripts/5-6_Audio/Runtime/` 的 `AudioService`、`AudioCatalog`、`AudioCue`、`AudioRuntimeConfig`、`AudioTypes`。
- 玩法桥接：`Assets/5_Scripts/5-3_GamePlay/Presentation/Audio/Mod_AudioEmitter.cs`、`Entities/Combat/CombatAudioRouter.cs`；UI 桥接：`Assets/5_Scripts/5-5_UI/Audio/`。
- 配置：`Assets/Resources/Audio/`；工具：`Assets/5_Scripts/5-6_Audio/Editor/`。
- 已接入天气音：`Assets/Audio/Generated/weather.*.wav`；LMMS MCP 工程与母带整理脚本在 `SoundDesign/Weather_LMMS/`。
- 玩法音效：`SoundDesign/Gameplay_SFX_LMMS/` 保存攻击、材质命中和交互三组工程；30 个现有 Cue 绑定 88 个单声道变体，`build_sfx.py build/finalize` 负责编排与母带整理。旧 C# 默认音效生成器已删除，避免覆盖 LMMS 成品。
- 候选素材/工程：`Assets/Audio/UI/{LMMS_Clicks,InventoryItems_LMMS}/`、`SoundDesign/{UI_Click_LMMS,Inventory_Items_LMMS}/`。

## 职责与契约

- 业务通过稳定 Cue ID 发声；`AudioService` 持有跨场景生命周期、声源池、并发优先级、淡入淡出及音量。循环 Cue 的句柄由调用方停止/回收；生成音效 ID 的 `.loop` 后缀表示循环，`player.*` 属于世界 SFX。
- 世界音效使用 `At/Attached`、完整空间混音与线性衰减：`MinDistance` 内正常，`MaxDistance` 处静音。`Mod_Cam` 绑定本机玩家和已有监听器，服务将声源投影到监听器 XY 平面，排除相机高度、前探与跟随偏移；解绑按目标身份。范围外短音不占池/冷却，循环音保留句柄待靠近恢复；角色循环音绑定角色 Transform。
- Catalog/Config 的 Resources 路径与加载常量对应。候选 UI 音效按风格分目录，位于自动扫描目录 `Assets/Audio/Generated/` 之外，选定后再绑定 Cue；`build_preview.py --unity .` 重导保留 WAV GUID。
- 玩法短音采用 48 kHz 单声道 PCM、预加载、关闭导入归一化；旧 `__01` WAV 和 Cue 保留 GUID，新变体必须写 `.meta` 并加入现有 Cue。按工程分组经 LMMS MCP 渲染后再执行 `build_sfx.py finalize`，脚本仅裁切、接缝、增益和绑定，不合成替代 LMMS 声源；临时 WAV 完整写好后原子替换，避免 Unity 导入半个文件。
- 取放音索引按操作、材质、重量 kg、体积 L 选择；重量/体积按实际转移数量独立划档，背包/箱子区分接触音色。拿起在点击取物或开始拖拽时播放一次，放入跟随成功事务；候选索引与运行时 Cue 接入独立。
- 服务直接实现 `ISettingsProvider`，经 `SettingsProviderRegistry` 暴露音量/静音；`FlatWorld.Audio` 只依赖 Data 设置契约。六路滑块使用 `UI_SliderControl`，绑定名为 `MasterVolume/MusicVolume/SfxVolume/UIVolume/AmbientVolume/VoiceVolume` 及对应 `_数值`。
- `AudioService.SetVolumeScale` 调整调用方音量，保留 Cue 随机增益、淡入淡出与用户设置；`SetWorldPosition` 更新非附着空间声源。`WeatherMgr.Audio` 管雨风循环及雷声落点/近远选音/延迟，通用服务不识别天气和拓扑；近远雷 Cue 均完整空间混音、线性衰减 8–192 格，使用单声道真实录音，来源见 `SoundDesign/Weather_LMMS/sources.json`。LMMS 雨声不再由默认生成器覆盖。
