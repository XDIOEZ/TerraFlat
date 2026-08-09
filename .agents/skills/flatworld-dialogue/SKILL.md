---
name: flatworld-dialogue
description: "Use when: 定位或修改 FlatWorld 的角色自言自语、屏幕气泡、Speech Provider、Facts、JSON 台词配置、触发条件、冷却或一次性台词存档。关键词：CharacterSoliloquyController、ConfiguredSpeechProvider、CharacterSpeechContext、ScreenSpaceSpeechBubblePresenter。"
argument-hint: "自言自语、对话配置或气泡问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 角色对话系统定位

> 最后核对：2026-08-04。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/CharacterSoliloquyController.cs`：唯一调度入口。
2. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/CharacterSpeechContracts.cs`：上下文、Provider、Presenter、Trigger 接口与请求模型。
3. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/ScreenSpaceSpeechBubblePresenter.cs`：屏幕空间气泡。
4. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/HungerSpeechProvider.cs`：只读取 `Mod_Food` 并贡献饥饿 Facts。
5. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/ConfiguredSpeechProvider.cs`：JSON 条目匹配、上升沿、运行时冷却和一次性完成标记。
6. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/CharacterSpeechConfigLoader.cs`：Resources 多文件加载、确定排序、合并与容错校验。
7. `Assets/5_Scripts/5-3_GamePlay/Presentation/Guide/NewPlayerGuideController.cs`：只贡献教程 Facts，不创建第二套调度器或直接调用气泡。
8. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/PlayerChatInputController.cs`：本地玩家 T 键聊天、输入锁、气泡提交与斜杠命令分发。
9. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/PlayerChatContracts.cs`：显式命令处理器接口与提交上下文。
10. `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/WeatherExposureSpeechProvider.cs`：天气、雨中暴露、火源 Facts 与玩家运行时体温修正。

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

- `CharacterSoliloquyController` 仍是唯一调度入口，不内置饥饿或引导规则。
- `CharacterSpeechFacts` 集中维护已注册 Fact；除 `hunger.*`、`tutorial.*` 外，天气使用 `weather.type`、`weather.phase`、`weather.intensity`、`weather.isRaining`、`weather.isExposed`、`weather.hasHeatSource`、`weather.remainingSeconds`。
- JSON 条件只允许 `Equal`、`NotEqual`、`Greater`、`GreaterOrEqual`、`Less`、`LessOrEqual`、`Exists`、`NotExists`，多条件固定为 AND。
- 触发只允许 `StateChanged` 与 `Idle`；状态触发使用 false→true 上升沿，普通冷却使用 `Time.unscaledTime`。
- `once=true` 必须有 `completionFlag`；完成标记通过 `ItemSpecialDataJsonStore` 写入 `Data_Player.ItemSpecialData` 的 `flatworld.dialogue.completed`，与教程及未知命名空间合并，不扩展旧 MemoryPack 核心字段。
- `Player.prefab` 根节点挂载 `ConfiguredSpeechProvider`、`NewPlayerGuideController` 与 `WeatherExposureSpeechProvider`，均由 `RebuildExtensions()` 自动发现；不要给 Controller 添加手工 Provider 引用。
- `CharacterSoliloquyController` 对 Player 显式要求 `IsLocalProfile=true`；远程视觉副本不启动调度，本地提升通过 `ProfileContextChanged` 自动恢复。不要依赖 `IsInitialized` 无限等待隔离远程副本。
- 玩家手动聊天调用 `CharacterSoliloquyController.Present()`，topic/sourceId 为 `player.chat`，优先级使用 `Player`（25）：高于普通 Need/Critical 提示、低于 Emergency，不建立第二套气泡或调度器。
- `/` 开头文本先交给玩家节点上的 `IPlayerChatCommandHandler`；处理器按 `CommandOrder` 排序。命令必须显式注册、校验参数和权限，联机权威操作交给服务端，禁止反射执行任意方法。未识别命令仍作为普通聊天显示。

## 配置与校验

- 运行时配置：`Assets/Resources/Dialogue/Soliloquy/*.json`；JSON 是角色自言自语内容、条件、优先级、时长和冷却的唯一来源。
- 饥饿迁移配置：`Assets/Resources/Dialogue/Soliloquy/need_hunger.json`。
- 生存引导配置：`Assets/Resources/Dialogue/Soliloquy/guide_survival.json`；九个阶段均使用 Ambient、StateChanged + Idle、一次性完成标记，教程不得使用 Emergency 抢占生存警告。
- 降雨反馈配置：`Assets/Resources/Dialogue/Soliloquy/weather_rain.json`；覆盖预兆、雨中暴露、强降雨、火源恢复与雨后恢复。
- 编辑器校验：`FlatWorld/自言自语/校验配置 JSON`；实现位于 `Assets/5_Scripts/5-3_GamePlay/Presentation/Dialogue/Editor/ConfiguredSpeechJsonValidator.cs`。
- 配置按资源名确定排序；跨文件重复 ID 报错。单个坏文件或坏条目只被跳过，其他有效条目继续加载。
- 新增 Fact 时必须同时更新 Contributor、`CharacterSpeechFacts` 和 `CharacterSpeechConfigLoader` 的已知 Fact 注册表。

## 对话 UI Prefab

- 聊天输入：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab`，固定节点 `Text Area`、`Placeholder`、`Text`。
- 角色气泡：`Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_CharacterSpeechBubble.prefab`，固定节点 `Tail`、`Message`，根节点包含 `CanvasGroup`。
- 角色气泡是非交互提示，`ScreenSpaceSpeechBubblePresenter` 必须将其固定为 `UIManager.PanelRoot` 的第一个子节点；禁止置顶，背包和所有模态交互面板必须覆盖气泡。
- `PlayerChatInputController` 与 `ScreenSpaceSpeechBubblePresenter` 只通过 `GameRes` 实例化 Prefab、查找节点和更新数据，禁止运行时创建背景、文本、输入框或气泡尾部。
- Prefab 查询键统一位于 `Assets/5_Scripts/5-5_UI/RuntimeUIPrefabKeys.cs`；重建入口为 `Assets/Editor/FlatWorld/PrefabBuilders/UI/RuntimeUIPrefabBuilder.cs` 的菜单 `FlatWorld/UI/Rebuild Runtime Prefab UI`。
- `FlatWorld.Dialogue.asmdef` 直接引用 `GamePlay`、`UI` 与 `m_Utilitiles`；访问 `GameRes.Instance` 时不要移除基类程序集引用。

## 系统边界

- `CharacterSoliloquyController` 只负责组合上下文、Provider、Trigger 与 Presenter，不应内置具体饥饿或任务规则。
- `HungerSpeechProvider` 不得重新加入硬编码台词、台词选择或重复冷却；这些职责属于 JSON 与 `ConfiguredSpeechProvider`。
- 当前未发现项目自有独立 Quest 系统；剧情需求先检查 Dialogue 目录、场景对象与 Fungus 资产。
- `AudioService`、AudioCue、声源池、战斗/UI 音效属于 `flatworld-audio`；对话 Skill 不维护音频播放架构。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-09：玩家聊天输入框为空或仅包含空白字符时按 Enter 直接关闭输入框，不重新聚焦，也不发布空聊天消息；有内容时继续沿原提交链处理。
- 2026-08-09：`ScreenSpaceSpeechBubblePresenter` 不再将角色气泡置顶，改为固定在 `PanelRoot` 最底层；背包及其他交互面板始终显示在气泡上方。
- 2026-08-04：`WeatherExposureSpeechProvider` 扫描附近火源时，先确认物品存在燃料模块再读取点燃状态；普通物品不是错误条件，必须静默跳过，避免按扫描频率重复输出“找不到燃料模块”警告。
- 2026-07-30：新增天气 Facts Contributor 与 `weather_rain.json`；现有 Controller 通过扩展发现自动接入，不建立第二套天气台词调度器。
- 2026-07-29：新增 T 键玩家聊天，Enter 提交到既有角色气泡、Esc 取消；新增 `Player` 台词优先级和显式斜杠命令处理接口，远程 Player 禁止本地输入。
- 2026-07-29：玩家聊天输入与屏幕空间角色气泡固化为 `Runtime/Dialogue` 下的可视化 Prefab；Presenter 只绑定现有节点，不再程序化创建视觉树。
- 2026-07-28：新增依赖自言自语链的新手生存引导；Guide 仅贡献 `tutorial.*` Facts，台词全部位于 `guide_survival.json`，远程 Player 改为显式本地档案门控。
- 2026-07-28：从原音频与对话混合 Skill 中拆分；角色自言自语维持 Resources JSON 配置驱动、结构化条件、StateChanged/Idle、运行时冷却和一次性完成标记。
- 2026-07-28：饥饿三档台词迁移到 `need_hunger.json`。

## 修改后自动测试

- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；测试脚本：`Assets/GameTest/Dialogue/DialogueSmokeTests.cs`；场景探针：`Assets/GameTest/Dialogue/DialogueSmokeTestProbe.cs`；测试场景：`Assets/GameTest/Scenes/Dialogue/DialogueSmokeTest.unity`；冒烟分类：`Dialogue.Smoke`。
- 当前冒烟覆盖 `CriticalHungerFact_ShowsConfiguredSpeech` 完整调度链、角色气泡低于交互面板的层级约束、聊天/气泡 Prefab 约束，以及 Player 天气 Contributor 与降雨 JSON 的已知 Fact 校验。
- 新增 Fact、Provider、Trigger、Presenter 或 JSON 行为时必须增加系统测试；修复 Bug 时先增加可复现问题的回归测试。核心调度链变化时同步更新此场景和冒烟用例。
- 测试失败时优先修复生产代码，禁止删除测试、弱化断言或修改 JSON 输入来制造通过；随机台词测试必须限制为唯一候选或固定随机状态。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Dialogue.Smoke`；无需视觉模型或测试工具卡片。涉及一次性完成标记、玩家状态、UI 气泡或联机边界时追加对应分类；只有气泡布局或最终观感变化才做定向截图。
- 教程链测试位于 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`，分类 `Guide.Smoke`；覆盖 Facts、JSON、一次性标记、Player Prefab、远程隔离与成功事件边界。
- 精简 Smoke 位于 `Assets/GameTest/Dialogue/DialogueSmokeTests.cs`（`Dialogue.Smoke`），只保留关键饥饿事实触发配置台词的行为；玩家聊天细节不再属于 Smoke 集合。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

修改角色语音接口、Facts、Provider、Presenter、对话 Prefab、JSON 配置目录、一次性完成标记或新增剧情入口后，必须更新本 Skill；涉及玩家存档或联机边界时同步更新对应 Skill。
