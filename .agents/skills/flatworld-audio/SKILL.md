---
name: flatworld-audio
description: "Use when: 定位或修改 FlatWorld 的音频服务、AudioCue、声源池、战斗/实体/UI 音效、音量、淡入淡出或音频资源。关键词：AudioService、AudioCatalog、AudioCue、Mod_AudioEmitter、CombatAudioRouter。"
---

# FlatWorld 音频

## 入口

- 核心：`Assets/5_Scripts/5-6_Audio/Runtime/{AudioService,AudioCatalog,AudioCue,AudioRuntimeConfig,AudioTypes}.cs`
- 实体/战斗：`Assets/5_Scripts/5-3_GamePlay/Presentation/Audio/Mod_AudioEmitter.cs`、`Entities/Combat/CombatAudioRouter.cs`
- UI：`Assets/5_Scripts/5-5_UI/Audio/`
- 资源与工具：`Assets/Resources/Audio/`、`Assets/5_Scripts/5-6_Audio/Editor/`

## 不变量

- 业务系统通过稳定 Cue ID 发声，不自行管理临时 `AudioSource`。
- `AudioService` 负责跨场景生命周期、池化、并发、优先级、淡入淡出与音量。
- Catalog/Config 移动时同步 Resources 加载常量；循环 Cue 必须有明确停止和回收路径。
- 战斗音效联动 `flatworld-combat`，UI 音效联动 `flatworld-ui`；角色台词和气泡属于 `flatworld-dialogue`。

## 验证

- 静态检查 Cue 是否可解析、总线/循环配置是否正确、停止后声源是否回池；听感仅作最终人工确认。
- 默认不主动跑测试；需要时按 `flatworld-test-automation` 运行 `Audio.Smoke`。
- 测试入口：`Assets/GameTest/Audio/AudioSmokeTests.cs`；真实播放链由 Golden Path `audio.cue-playback` 覆盖。

Cue ID、资源路径、池化或路由契约变化时更新本 Skill；近期变更最多 5 条。
