---
name: flatworld-dialogue
description: "Use when: 定位或修改 FlatWorld 的角色自言自语、屏幕气泡、Speech Provider、Facts、JSON 台词配置、触发条件、冷却或一次性台词存档。关键词：CharacterSoliloquyController、ConfiguredSpeechProvider、CharacterSpeechContext、ScreenSpaceSpeechBubblePresenter。"
argument-hint: "自言自语、对话配置或气泡问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 角色对话系统定位

> 最后核对：2026-07-28。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Dialogue/CharacterSoliloquyController.cs`：唯一调度入口。
2. `Assets/5_Scripts/5-3_GamePlay/Dialogue/CharacterSpeechContracts.cs`：上下文、Provider、Presenter、Trigger 接口与请求模型。
3. `Assets/5_Scripts/5-3_GamePlay/Dialogue/ScreenSpaceSpeechBubblePresenter.cs`：屏幕空间气泡。
4. `Assets/5_Scripts/5-3_GamePlay/Dialogue/HungerSpeechProvider.cs`：只读取 `Mod_Food` 并贡献饥饿 Facts。
5. `Assets/5_Scripts/5-3_GamePlay/Dialogue/ConfiguredSpeechProvider.cs`：JSON 条目匹配、上升沿、运行时冷却和一次性完成标记。
6. `Assets/5_Scripts/5-3_GamePlay/Dialogue/CharacterSpeechConfigLoader.cs`：Resources 多文件加载、确定排序、合并与容错校验。
7. `Assets/5_Scripts/5-3_GamePlay/Guide/NewPlayerGuideController.cs`：只贡献教程 Facts，不创建第二套调度器或直接调用气泡。

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
- `CharacterSpeechFacts` 集中维护已注册 Fact；除 `hunger.*` 外，教程使用 `tutorial.enabled`、`tutorial.stage`、`tutorial.completed`。
- JSON 条件只允许 `Equal`、`NotEqual`、`Greater`、`GreaterOrEqual`、`Less`、`LessOrEqual`、`Exists`、`NotExists`，多条件固定为 AND。
- 触发只允许 `StateChanged` 与 `Idle`；状态触发使用 false→true 上升沿，普通冷却使用 `Time.unscaledTime`。
- `once=true` 必须有 `completionFlag`；完成标记通过 `ItemSpecialDataJsonStore` 写入 `Data_Player.ItemSpecialData` 的 `flatworld.dialogue.completed`，与教程及未知命名空间合并，不扩展旧 MemoryPack 核心字段。
- `Player.prefab` 根节点挂载 `ConfiguredSpeechProvider` 与 `NewPlayerGuideController`，均由 `RebuildExtensions()` 自动发现；不要给 Controller 添加手工 Provider 引用。
- `CharacterSoliloquyController` 对 Player 显式要求 `IsLocalProfile=true`；远程视觉副本不启动调度，本地提升通过 `ProfileContextChanged` 自动恢复。不要依赖 `IsInitialized` 无限等待隔离远程副本。

## 配置与校验

- 运行时配置：`Assets/Resources/Dialogue/Soliloquy/*.json`；JSON 是角色自言自语内容、条件、优先级、时长和冷却的唯一来源。
- 饥饿迁移配置：`Assets/Resources/Dialogue/Soliloquy/need_hunger.json`。
- 生存引导配置：`Assets/Resources/Dialogue/Soliloquy/guide_survival.json`；九个阶段均使用 Ambient、StateChanged + Idle、一次性完成标记，教程不得使用 Emergency 抢占生存警告。
- 编辑器校验：`FlatWorld/自言自语/校验配置 JSON`；实现位于 `Assets/5_Scripts/5-3_GamePlay/Dialogue/Editor/ConfiguredSpeechJsonValidator.cs`。
- 配置按资源名确定排序；跨文件重复 ID 报错。单个坏文件或坏条目只被跳过，其他有效条目继续加载。
- 新增 Fact 时必须同时更新 Contributor、`CharacterSpeechFacts` 和 `CharacterSpeechConfigLoader` 的已知 Fact 注册表。

## 系统边界

- `CharacterSoliloquyController` 只负责组合上下文、Provider、Trigger 与 Presenter，不应内置具体饥饿或任务规则。
- `HungerSpeechProvider` 不得重新加入硬编码台词、台词选择或重复冷却；这些职责属于 JSON 与 `ConfiguredSpeechProvider`。
- 当前未发现项目自有独立 Quest 系统；剧情需求先检查 Dialogue 目录、场景对象与 Fungus 资产。
- `AudioService`、AudioCue、声源池、战斗/UI 音效属于 `flatworld-audio`；对话 Skill 不维护音频播放架构。

## 近期变更

- 2026-07-28：新增依赖自言自语链的新手生存引导；Guide 仅贡献 `tutorial.*` Facts，台词全部位于 `guide_survival.json`，远程 Player 改为显式本地档案门控。
- 2026-07-28：从原音频与对话混合 Skill 中拆分；角色自言自语维持 Resources JSON 配置驱动、结构化条件、StateChanged/Idle、运行时冷却和一次性完成标记。
- 2026-07-28：饥饿三档台词迁移到 `need_hunger.json`。

## 修改后自动测试

- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；测试脚本：`Assets/GameTest/Dialogue/DialogueSmokeTests.cs`；场景探针：`Assets/GameTest/Dialogue/DialogueSmokeTestProbe.cs`；测试场景：`Assets/GameTest/Scenes/Dialogue/DialogueSmokeTest.unity`；冒烟分类：`Dialogue.Smoke`。
- 当前冒烟用例 `CriticalHungerFact_ShowsConfiguredSpeech` 会加载隔离场景，通过固定 `hunger.*` Facts 验证 `CharacterSpeechContext → ConfiguredSpeechProvider → CharacterSoliloquyController → ICharacterSpeechPresenter` 完整链路，并断言命中 `need.hunger.critical`。
- 新增 Fact、Provider、Trigger、Presenter 或 JSON 行为时必须增加系统测试；修复 Bug 时先增加可复现问题的回归测试。核心调度链变化时同步更新此场景和冒烟用例。
- 测试失败时优先修复生产代码，禁止删除测试、弱化断言或修改 JSON 输入来制造通过；随机台词测试必须限制为唯一候选或固定随机状态。
- 完成修改后检查 Unity 编译和 Console，再运行 `Dialogue.Smoke`；涉及一次性完成标记、玩家状态、UI 气泡或联机边界时同步运行对应系统测试。
- 教程链测试位于 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`，分类 `Guide.Smoke`；覆盖 Facts、JSON、一次性标记、Player Prefab、远程隔离与成功事件边界。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

修改角色语音接口、Facts、Provider、Presenter、对话 Prefab、JSON 配置目录、一次性完成标记或新增剧情入口后，必须更新本 Skill；涉及玩家存档或联机边界时同步更新对应 Skill。
