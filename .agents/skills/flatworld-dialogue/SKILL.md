---
name: flatworld-dialogue
description: "Use when: 定位或修改 FlatWorld 的角色自言自语、屏幕气泡、Speech Provider、Facts、JSON 台词配置、触发条件、冷却或一次性台词存档。关键词：CharacterSoliloquyController、ConfiguredSpeechProvider、CharacterSpeechContext、ScreenSpaceSpeechBubblePresenter。"
---

# FlatWorld 角色对话

## 入口

- 调度/上下文：定位 `CharacterSoliloquyController`、`CharacterSpeechContext`
- 配置 Provider：定位 `ConfiguredSpeechProvider` 与 Facts/条件实现
- 表现：定位 `ScreenSpaceSpeechBubblePresenter`
- 台词配置：在 `Assets/StreamingAssets/GameConfig/` 下搜索对应 dialogue/speech manifest 或 JSON
- Prefab：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/`

## 不变量

- Provider 只产出请求，Presenter 只负责显示；不要把触发、冷却或存档逻辑塞入气泡 UI。
- 只有本地 `Player.IsLocalProfile` 运行玩家语音；远程副本不调度、不写一次性进度。
- 一次性完成标记通过共享 `ItemSpecialDataJsonStore` 命名空间保存，保留其他系统未知字段。
- Facts 从权威状态读取；扫描普通物品时缺少可选模块应静默跳过，避免重复警告。
- 随机候选测试使用唯一候选或固定随机状态。
- 文本本地化联动 `flatworld-localization`；气泡层级/节点联动 UI；存档或网络身份变化联动对应 Skill。

## 验证

- 覆盖 Fact→Provider→调度→Presenter、优先级/冷却、一次性恢复、远程副本隔离及解除订阅。
- 默认不主动跑测试；需要时运行 `Dialogue.Smoke`。测试入口：`Assets/GameTest/Dialogue/DialogueSmokeTests.cs`；真实链可用 Golden Path `dialogue.player-speech`。

接口、Fact/Provider、Prefab、JSON 或完成标记变化时更新本 Skill；近期变更最多 5 条。
