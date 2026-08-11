---
name: flatworld-dialogue
description: "Use when: 定位或修改 FlatWorld 的角色自言自语、屏幕气泡、Speech Provider、Facts、JSON 台词配置、触发条件、冷却或一次性台词存档。关键词：CharacterSoliloquyController、ConfiguredSpeechProvider、CharacterSpeechContext、ScreenSpaceSpeechBubblePresenter。"
---

# FlatWorld 角色对话系统定位

> 最后核对：2026-08-04。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/CharacterSoliloquyController.cs`：唯一调度入口。
2. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/CharacterSpeechContracts.cs`：上下文、Provider、Presenter、Trigger 接口与请求模型。
3. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/ScreenSpaceSpeechBubblePresenter.cs`：屏幕空间气泡。
4. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/HungerSpeechProvider.cs`：只读取 `Mod_Food` 并贡献饥饿与水分 Facts。

## 自言自语数据链
```text
ICharacterSpeechContextContributor
→ CharacterSpeechContext.Facts
→ ConfiguredSpeechProvider
→ Resources/Dialogue/Soliloquy/*.json
→ CharacterSpeechRequest
→ CharacterSoliloquyController
→ ScreenSpaceSpeechBubblePresenter
```

## 配置与校验
- 运行时配置：`Assets/Resources/Dialogue/Soliloquy/*.json`；JSON 是角色自言自语内容、条件、优先级、时长和冷却的唯一来源。
- 饥饿迁移配置：`Assets/Resources/Dialogue/Soliloquy/need_hunger.json`。
- 水分提示配置：`Assets/Resources/Dialogue/Soliloquy/need_hydration.json`；按 Thirsty、VeryThirsty、Dehydrated 三档提示，水分耗尽与真实扣血条件保持一致。
- 生存引导配置：`Assets/Resources/Dialogue/Soliloquy/guide_survival.json`；九个阶段均使用 Ambient、StateChanged + Idle、一次性完成标记，教程不得使用 Emergency 抢占生存警告。

## 对话 UI Prefab
- 聊天输入：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab`，固定节点 `Text Area`、`Placeholder`、`Text`。
- 角色气泡：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_CharacterSpeechBubble.prefab`，固定节点 `Tail`、`Message`，根节点包含 `CanvasGroup`。
- 角色气泡是非交互提示，`ScreenSpaceSpeechBubblePresenter` 必须将其固定为 `UIManager.PanelRoot` 的第一个子节点；禁止置顶，背包和所有模态交互面板必须覆盖气泡。
- `PlayerChatInputController` 与 `ScreenSpaceSpeechBubblePresenter` 只通过 `GameRes` 实例化 Prefab、查找节点和更新数据，禁止运行时创建背景、文本、输入框或气泡尾部。

## 系统边界
- `CharacterSoliloquyController` 只负责组合上下文、Provider、Trigger 与 Presenter，不应内置具体饥饿或任务规则。
- `HungerSpeechProvider` 不得重新加入硬编码台词、台词选择或重复冷却；这些职责属于 JSON 与 `ConfiguredSpeechProvider`。
- 当前未发现项目自有独立 Quest 系统；剧情需求先检查 Dialogue 目录、场景对象与 Fungus 资产。
- `AudioService`、AudioCue、声源池、战斗/UI 音效属于 `flatworld-audio`；对话 Skill 不维护音频播放架构。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：水分自言自语统一使用玩家角色第一人称，不得以“小人”等第三人称称呼自身；三档配置继续沿用 `hydration.rate/tier/isTakingDamage` Facts。
- 2026-08-11：Golden Path 自动化程序集显式引用 `FlatWorld.Dialogue`，新增 `dialogue.player-speech`，在真实玩家上调用 `CharacterSoliloquyController.Say` 并断言 Presenter 的 `SpeechShown` 事件。
- 2026-08-09：玩家聊天输入框为空或仅包含空白字符时按 Enter 直接关闭输入框，不重新聚焦，也不发布空聊天消息；有内容时继续沿原提交链处理。
- 2026-08-09：`ScreenSpaceSpeechBubblePresenter` 不再将角色气泡置顶，改为固定在 `PanelRoot` 最底层；背包及其他交互面板始终显示在气泡上方。
- 2026-08-04：`WeatherExposureSpeechProvider` 扫描附近火源时，先确认物品存在燃料模块再读取点燃状态；普通物品不是错误条件，必须静默跳过，避免按扫描频率重复输出“找不到燃料模块”警告。

## 修改后自动测试
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；测试脚本：`Assets/GameTest/Dialogue/DialogueSmokeTests.cs`；场景探针：`Assets/GameTest/Dialogue/DialogueSmokeTestProbe.cs`；测试场景：`Assets/GameTest/Scenes/Dialogue/DialogueSmokeTest.unity`；冒烟分类：`Dialogue.Smoke`。
- 当前冒烟覆盖 `CriticalHungerFact_ShowsConfiguredSpeech` 完整调度链、角色气泡低于交互面板的层级约束、聊天/气泡 Prefab 约束，以及 Player 天气 Contributor 与降雨 JSON 的已知 Fact 校验。
- 真实单机 Presenter 链由 Golden Path 操作 `dialogue.player-speech` 覆盖；使用短时确定文本、临时订阅显示事件并立即解除，不写一次性台词存档。
- 新增 Fact、Provider、Trigger、Presenter 或 JSON 行为时必须增加系统测试；修复 Bug 时先增加可复现问题的回归测试。核心调度链变化时同步更新此场景和冒烟用例。
- 测试失败时优先修复生产代码，禁止删除测试、弱化断言或修改 JSON 输入来制造通过；随机台词测试必须限制为唯一候选或固定随机状态。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Dialogue.Smoke`；无需视觉模型或测试工具卡片。涉及一次性完成标记、玩家状态、UI 气泡或联机边界时追加对应分类；只有气泡布局或最终观感变化才做定向截图。

## 修改后维护本 Skill
修改角色语音接口、Facts、Provider、Presenter、对话 Prefab、JSON 配置目录、一次性完成标记或新增剧情入口后，必须更新本 Skill；涉及玩家存档或联机边界时同步更新对应 Skill。
