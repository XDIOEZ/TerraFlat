---
name: flatworld-dialogue
description: "FlatWorld 角色台词、Facts、请求调度、冷却、一次性进度与屏幕气泡。"
---

# 角色对话

代码：`Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/`。

| 入口 | 职责 |
| --- | --- |
| `CharacterSoliloquyController`、`CharacterSpeechContext` | 上下文、优先级、冷却与调度 |
| `ConfiguredSpeechProvider`、Facts/条件实现 | 从配置与权威状态产出台词请求 |
| `ScreenSpaceSpeechBubblePresenter` | 气泡显示 |
| `SeasonSpeechContextContributor` | 比例化季节预告 Fact |

本体自言自语 JSON 位于 `Assets/Resources/Dialogue/Soliloquy/`；每个 `lines` 数组配同句数的 `localizedLines.en`，由 `dialogue/builtin_locales` 快速规则查漏。气泡 Prefab 位于 `Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/`。

- 玩家台词仅由 `Player.IsLocalProfile` 调度；一次性标记存入 `ItemSpecialDataJsonStore` 命名空间并保留其他系统字段。
- Provider 产请求，调度器管理时序，Presenter 显示。玩法语义事件由 `FlatWorld.Dialogue` 独立桥接筛选本地玩家、完成本地化并 `Say`；GamePlay 无 Dialogue 反向依赖。
- 聊天斜杠命令实现 `IPlayerChatCommandHandler`；`PlayerCarryWeightChatCommand` 在入世/角色重建事件定向装配并重建命令列表，`/carryweight 5000` 经 `PlayerCarryCapacityUtility.TrySetMaxWeight` 调整本地权威玩家的当前负重，创造无限状态明确拒绝，保留体积规则。
- Facts 读权威状态，可选模块缺失表示无该 Fact。`weather.isRaining` 表达降水过程，雨雪文案另读 `weather.isSnowing`；物品失败反馈也经语义事件进入气泡。
- `WeatherExposureSpeechProvider` 只贡献所在地天气与附近燃烧源 Facts；实际热影响由权威环境温度场和温度模块结算。
