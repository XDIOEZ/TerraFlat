---
name: flatworld-audio
description: "Use when: 定位或修改 FlatWorld 的音频服务、AudioCue、声源池、战斗/实体/UI 音效、音量、淡入淡出或音频资源。关键词：AudioService、AudioCatalog、AudioCue、Mod_AudioEmitter、CombatAudioRouter。"
argument-hint: "音频服务、Cue 或音效问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 音频系统定位

> 最后核对：2026-07-30。

## 修改前先读

1. `Assets/5_Scripts/5-6_Audio/Runtime/AudioService.cs`：跨场景服务、声源池、并发、优先级、淡入淡出和音量。
2. `Assets/5_Scripts/5-6_Audio/Runtime/AudioCatalog.cs`：Cue 索引。
3. `Assets/5_Scripts/5-6_Audio/Runtime/AudioCue.cs`：单个音频事件定义。
4. `Assets/5_Scripts/5-6_Audio/Runtime/AudioRuntimeConfig.cs`：运行时配置。

## 关键路径

- 音频类型：`Assets/5_Scripts/5-6_Audio/Runtime/AudioTypes.cs`。
- 实体发声：`Assets/5_Scripts/5-3_GamePlay/Audio/Mod_AudioEmitter.cs`。
- 战斗路由：`Assets/5_Scripts/5-3_GamePlay/Combat/CombatAudioRouter.cs`。
- UI 音频：`Assets/5_Scripts/5-5_UI/Audio/`。
- 编辑器生成：`Assets/5_Scripts/5-6_Audio/Editor/`。
- Catalog/Config：`Assets/Resources/Audio/`。
- Cue 资产：`Assets/Resources/Audio/Cues/`。
- 生成素材：`Assets/Audio/Generated/`。
- 雨声 Cue：`Assets/Resources/Audio/Cues/weather.rain.loop.asset`。
- 原创循环雨声：`Assets/Audio/Generated/weather.rain.loop__01.wav`。

## 系统边界

- 音效调用优先使用 Cue ID，不要让业务系统直接管理临时 `AudioSource`。
- `AudioService` 从 `Resources/Audio/AudioRuntimeConfig` 与 `AudioCatalog` 回退加载；移动资源必须同步常量和本 Skill。
- 战斗音效路由修改时同步检查 `flatworld-combat`；UI 音效修改时同步检查 `flatworld-ui`。
- 角色自言自语、气泡、Speech Provider 和 JSON 台词配置属于 `flatworld-dialogue`，不要放回音频 Skill。
- 天气雨声稳定 ID 为 `AudioEventIds.WeatherRainLoop`（`weather.rain.loop`）；由天气状态变化开始/淡出，不得在 `Update()` 每帧重播。
- `AIAudioWaveGenerator` 会生成原创雨噪循环，`AIAudioCatalogBuilder` 将 `weather.*` 归入 Ambient、启用循环并限制单实例。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-30：新增原创 `weather.rain.loop` Ambient Cue；降雨开始时淡入、结束时淡出，状态未变化不重复播放。
- 2026-07-28：从原音频与对话混合 Skill 中拆分，音频 Skill 仅维护音频服务、Cue、路由与资源边界。
- 2026-07-27：音频统一通过跨场景 `AudioService` 与 Cue Catalog。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Audio/AudioSmokeTests.cs`；当前覆盖 AudioService、AudioCue、RuntimeConfig、Catalog 以及循环雨声 Cue 的资源、总线和循环配置。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；音频测试约定目录：`Assets/GameTest/Audio/`；场景目录：`Assets/GameTest/Scenes/Audio/`；冒烟分类：`Audio.Smoke`。
- 新增 AudioCue、声源池、路由或淡入淡出行为时必须增加系统测试；修复 Bug 时先增加回归测试。核心播放与回收流程变化时同步更新音频冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试必须使用短测试音频或可观察状态，避免依赖人工听感作为唯一判定。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Audio.Smoke`；无需视觉模型或测试工具卡片。涉及战斗、实体或 UI 音效时追加对应分类；听感只在确有音频内容变化时人工确认。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动 Audio Catalog/Cue/音频素材、修改 Cue ID、声源池、并发、优先级、淡入淡出或音量策略后，必须更新本 Skill；若音效由战斗或 UI 触发，也同步更新对应 Skill。
