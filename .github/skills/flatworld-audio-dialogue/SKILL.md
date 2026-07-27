---
name: flatworld-audio-dialogue
description: "Use when: 定位或修改 FlatWorld 的音频服务、AudioCue、声源池、战斗/实体/UI 音效、角色自言自语、屏幕气泡、语音 Provider 或相关资源。关键词：AudioService、AudioCatalog、Mod_AudioEmitter、CharacterSoliloquyController。"
argument-hint: "音频、对话或气泡问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 音频与角色对话定位

> 最后核对：2026-07-27。

## 音频修改前先读

1. `Assets/5_Scripts/5-6_Audio/Runtime/AudioService.cs`：跨场景服务、声源池、并发、优先级、淡入淡出和音量。
2. `Assets/5_Scripts/5-6_Audio/Runtime/AudioCatalog.cs`：Cue 索引。
3. `Assets/5_Scripts/5-6_Audio/Runtime/AudioCue.cs`：单个音频事件定义。
4. `Assets/5_Scripts/5-6_Audio/Runtime/AudioRuntimeConfig.cs`：运行时配置。

## 音频路径

- 音频类型：`Assets/5_Scripts/5-6_Audio/Runtime/AudioTypes.cs`。
- 实体发声：`Assets/5_Scripts/5-3_GamePlay/Audio/Mod_AudioEmitter.cs`。
- 战斗路由：`Assets/5_Scripts/5-3_GamePlay/Combat/CombatAudioRouter.cs`。
- UI 音频：`Assets/5_Scripts/5-5_UI/Audio/`。
- 编辑器生成：`Assets/5_Scripts/5-6_Audio/Editor/`。
- Catalog/Config：`Assets/Resources/Audio/`。
- Cue 资产：`Assets/Resources/Audio/Cues/`。
- 生成素材：`Assets/Audio/Generated/`。

## 对话修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Dialogue/CharacterSoliloquyController.cs`：唯一调度入口。
2. `Assets/5_Scripts/5-3_GamePlay/Dialogue/CharacterSpeechContracts.cs`：上下文、Provider、Presenter、Trigger 接口与请求模型。
3. `Assets/5_Scripts/5-3_GamePlay/Dialogue/ScreenSpaceSpeechBubblePresenter.cs`：屏幕空间气泡。
4. `Assets/5_Scripts/5-3_GamePlay/Dialogue/HungerSpeechProvider.cs`：需求触发示例。

## 边界

- 音效调用优先使用 Cue ID，不要让业务系统直接管理临时 AudioSource。
- `AudioService` 从 `Resources/Audio/AudioRuntimeConfig` 与 `AudioCatalog` 回退加载；移动资源必须同步常量和本 Skill。
- `CharacterSoliloquyController` 只负责组合上下文、Provider、Trigger 与 Presenter，不应内置具体饥饿/任务规则。
- 当前未发现项目自有独立 Quest 系统；剧情需求先检查 Dialogue 目录、场景对象与 Fungus 资产。

## 近期变更

- 2026-07-27：音频统一通过跨场景 `AudioService` 与 Cue Catalog；角色气泡使用可扩展 Provider/Presenter 调度结构。

## 修改后维护本 Skill

移动 Audio Catalog/Cue/音频素材、修改 Cue ID、声源策略、角色语音接口、Presenter、对话 Prefab 或新增任务入口后，必须更新本 Skill；若音效由战斗或 UI 触发，也同步更新对应 Skill。
