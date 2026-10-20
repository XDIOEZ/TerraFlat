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
- `ScreenSpaceSpeechBubblePresenter` 可见时仍逐帧按真实镜头和安全区重投影角色位置，但 `SetActive` 与 `RectTransform.anchoredPosition` 只在结果变化时提交；离屏、恢复、模态面板遮挡与透明度淡出沿原生命周期运行，不能减少跟随采样频率。
- 聊天斜杠命令实现 `IPlayerChatCommandHandler`；`PlayerCarryWeightChatCommand` 在入世/角色重建事件定向装配并重建命令列表，`/carryweight 5000`（kg）与 `/carryvolume 5000`（L）经 `PlayerCarryCapacityUtility.TrySetMaxWeight/Volume` 分别调整本地权威玩家当前容量，保留另一容量及槽位，创造无限状态明确拒绝。
- `/help` 由 `PlayerHelpChatCommand` 同样定向装配，通过中立 `PlayerHelpCatalog.Collect("聊天命令")` 只收聊天栏斜杠命令，再交 `ReadableBookGrantService` 发放一份可编辑的 `CommandManual` 实物书，书内书写槽自带 1 个 `Charcoal`；每次一本、每条一页，保留资格与容量限制，新增命令在所属系统登记说明。
- Facts 读权威状态，可选模块缺失表示无该 Fact。`weather.isRaining` 表达降水过程，雨雪文案另读 `weather.isSnowing`；物品失败反馈也经语义事件进入气泡。
- `WeatherExposureSpeechProvider` 只贡献所在地天气与附近燃烧源 Facts；实际热影响由权威环境温度场和温度模块结算。
- 热源 Facts 扫描复用每个 Provider 的 `Physics2D.OverlapCircleNonAlloc` 缓冲；缓冲满且未发现火源就扩容重查，不能漏掉拥挤区域的点燃物。扫描后清理 Collider 引用，原半径/LayerMask/燃料判定及采样间隔不变。
