---
name: flatworld-quest
description: "Use when: 定位或修改 FlatWorld 的任务定义、任务目录、接取/阶段/目标/交付、统一进度信号、任务奖励、任务存档、MOD 任务或任务 UI 接口。关键词：QuestManager、PlayerQuestRuntime、QuestCatalog、QuestProgressStore、GameplayProgressEvents、flatworld.quests。"
---

# FlatWorld 任务系统导航

> 最后核对：2026-08-11。当前基础任务为玩家独立、一次性、多阶段、事件驱动任务。

## 修改前按意图读取

只读命中问题的入口；定位明确后不要继续泛化搜索。

1. 改任务数据结构或校验：`Assets/5_Scripts/5-3_GamePlay/Core/Quests/QuestDefinitions.cs`、`QuestCatalog.cs`。
2. 改接取、推进、交付或状态快照：`PlayerQuestRuntime.cs`。
3. 改目标、条件、奖励类型：`QuestExtensions.cs`。
4. 改玩家绑定或世界生命周期：`QuestManager.cs`、`Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameManager.cs`。
5. 改本体 JSON 加载：`QuestCatalogLoader.cs`、`Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameRes.cs`。
6. 改进度存档：`QuestProgressStore.cs`、`Assets/5_Scripts/5-3_GamePlay/Core/Progress/ItemSpecialDataJsonStore.cs`。
7. 改玩法推进信号：`Assets/5_Scripts/5-3_GamePlay/Core/Progress/GameplayProgressEvents.cs`，再定位发布信号的成功事务。
8. 改 MOD 任务：`Assets/5_Scripts/5-3_GamePlay/Extensibility/Mods/ModRuntimeManager.cs`。

## 权威链路

```text
StreamingAssets/GameConfig/Quests/quest-manifest.json
→ QuestCatalogLoader 合并启用分包
→ QuestCatalog 注册并统一校验
→ ModRuntimeManager 追加 definitionFiles[].quests
→ QuestCatalog.FinalizeRegistration
→ GameManager 创建并绑定 QuestManager
→ Event_PlayerEnterWorld 创建 PlayerQuestRuntime
→ QuestManager.RuntimeReady 绑定本地玩家任务追踪 HUD
→ GameplayProgressEvents.SignalPublished 路由到对应本地玩家
→ 目标推进 / 阶段切换 / 原子奖励
→ QuestProgressStore 写入 Data_Player.ItemSpecialData["flatworld.quests"]
```

## 关键目录与职责

- 运行时代码：`Assets/5_Scripts/5-3_GamePlay/Core/Quests/`；位于 `GamePlay.asmdef`，不另建程序集。
- 本体任务入口：`Assets/StreamingAssets/GameConfig/Quests/quest-manifest.json`。
- 示例分包：`Assets/StreamingAssets/GameConfig/Quests/starter.json`；`flatworld:first_chipped_tool` 自动接取，监听 `craft.succeeded / ChippedTool`。
- GM 测试分包：`Assets/StreamingAssets/GameConfig/Quests/debug-tests.json`；四个 `debugOnly` 任务只允许通过 F4 的任务分页显式开启，覆盖事件计数、状态目标、自动/手动交付和多阶段推进。
- 任务追踪 UI：`Assets/5_Scripts/5-3_GamePlay/Presentation/UI/{PlayerQuestTrackerHUD,QuestTrackerRowView}.cs`；正式 Prefab 为 `Assets/2_Prefabs/2-1_UI/Runtime/System/UI_QuestTracker{,Item}.prefab`。
- GM 任务分页：`Assets/5_Scripts/5-3_GamePlay/Development/Debug/GMReflectionConsole.Quests.cs`；只枚举 `QuestCatalog.All` 中的 `DebugOnly` 定义，并调用 `AcceptQuest`、`ClaimQuest`、`Refresh` 公共 API。
- 任务测试：`Assets/GameTest/Quest/QuestSmokeTests.cs`；分类 `Quest.Smoke`、`Quest.Save`。
- 真实单机回归：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.Quest.cs`；稳定操作 ID `quest.progression`，与 `inventory.crafting` 联合验证。
- 玩家进度命名空间：`flatworld.quests`，当前 `version=1`。

## 配置模型

每个任务由稳定 `id`、`definitionVersion`、接取/交付模式、条件、阶段和奖励组成：

```json
{
  "id": "flatworld:example",
  "definitionVersion": 1,
  "titleKey": "quest.flatworld.example.title",
  "title": "回退标题",
  "acceptMode": "auto",
  "turnInMode": "manual",
  "conditions": [],
  "stages": [{
    "id": "stage_1",
    "completionMode": "all",
    "objectives": [{
      "id": "objective_1",
      "type": "signal.count",
      "labelKey": "quest.flatworld.example.objective.objective_1",
      "label": "可读目标回退文本",
      "required": 1,
      "parameters": {
        "eventType": "craft.succeeded",
        "targetId": "ChippedTool"
      }
    }]
  }],
  "rewards": []
}
```

- 本体任务 ID 必须使用 `flatworld:` 前缀；MOD 任务必须使用其 `modId:` 前缀。
- `acceptMode`、`turnInMode` 只允许 `auto` 或 `manual`；`completionMode` 只允许 `all` 或 `any`。
- `debugOnly=true` 的任务永远跳过自动接取，即使误把 `acceptMode` 配成 `auto`；生产任务不要使用该标记。
- 同一任务的阶段 ID、同一阶段的目标 ID、奖励 ID 必须唯一。
- 改变现有任务阶段、目标含义或奖励时递增 `definitionVersion`。已完成记录只更新版本且不重发奖励；未完成记录按新版本从首阶段安全重置。
- `titleKey`/`descriptionKey` 为未来 UI 本地化入口，`title`/`description` 必须保留可读回退文本。
- 需要进入任务追踪器的目标应提供 `labelKey`/`label`；缺失时 UI 会安全回退到目标 ID，但正式本体内容不得依赖该回退。

## 内建扩展类型

| 类别 | type | 参数 | 行为 |
|---|---|---|---|
| 目标 | `signal.count` | `eventType`，可选 `targetId` | 匹配统一信号并按 `Amount` 累加 |
| 目标 | `inventory.owns` | `itemId` | 在接取、信号和显式刷新时统计背包与快捷栏 |
| 条件 | `quest.completed` | `questId` | 要求另一任务已完成，并参与前置循环校验 |
| 奖励 | `item.grant` | `itemId`、可选 `amount` | 复用 `CraftingTransaction.TryCreateGrant` 整批预检和提交 |
| 奖励 | `signal.emit` | `eventType`、可选 `targetId/amount/payload` | 任务保存后延迟发布扩展信号 |

新增类型时实现 `IQuestObjectiveHandler`、`IQuestConditionEvaluator` 或 `IQuestRewardHandler`，再通过 `QuestExtensionRegistry` 注册。不要在 `PlayerQuestRuntime` 添加玩法名称分支；配置校验必须在目录 Finalize 阶段失败，而不是等玩家做到一半才报错。

## 推进信号契约

- 强类型旧事件继续服务新手引导；任务只订阅 `SignalPublished`。
- 内建信号：`inventory.opened`、`item.picked_up`、`craft.succeeded`、`building.placed`、`fire_seed.created`、`furnace.ignited`。
- 只能在玩法事务最终成功后发布；预览、预检失败、回滚或仅播放动画时禁止发布。
- `Actor` 必须是实际本地玩家，`Type` 使用稳定小写命名空间，`TargetId` 使用内容稳定 ID，`Amount` 必须大于 0。
- 发布器逐订阅者隔离异常；监听失败不得回滚已经成功的制作、拾取或建筑事务。
- 状态目标不逐帧轮询；只有入世、统一信号或外部 `Refresh()` 才重新计算。

## 奖励与一致性

- 奖励处理器只能向 `QuestRewardPlan` 准备数据，不能在准备阶段直接改库存、生成实体或发信号。
- 所有物品奖励先在同一库存快照中完整放置；任一物品放不下时任务保持 `ReadyToClaim`，不得部分发放。
- 物品提交成功后才把任务标为 `Completed` 和 `rewardsClaimed=true`；奖励信号在任务进度写入后发布，避免重入重复领奖。
- 当前优先发放到玩家 Bag 的首个有效库存，找不到时回退 Hotbar。若后续加入邮箱/掉落补偿，必须作为新的奖励处理器或明确事务策略实现。

## 存档与兼容

- 禁止为任务进度修改 `Data_Player` 的 MemoryPack 布局；统一使用 `ItemSpecialDataJsonStore` 的 `flatworld.quests` 命名空间。
- 保存文档保留未知任务 ID、未知根字段和未知记录字段；这样移除 MOD 后不会破坏其历史进度。
- 读取到高于当前支持版本的任务文档时禁用该玩家任务运行时并停止写回，禁止静默降级覆盖。
- 不存在命名空间的旧存档按空任务进度处理；任务自动接取发生在本地玩家完成档案绑定与 `Load()` 之后。
- 世界退出时销毁玩家任务运行时；下次进入从当前 `Data_Player` 重建，远端网络玩家不创建本地任务运行时。

## 本体与 MOD 内容加载

- 本体任务加载顺序：JSON Item → Recipe → Buff → Quest → 其余 Addressables → MOD。
- `GameRes.ClearAllDictionaries()` 必须同时 `QuestCatalog.Reset()`，避免热重载残留旧定义。
- MOD `definitionFiles` 可声明顶层 `quests` 数组，数据结构复用 `QuestDefinition`。
- MOD 处理顺序是 Item → Recipe → Buff → Quest → `QuestCatalog.FinalizeRegistration()`；这样奖励物品和跨 MOD 前置任务能统一解析。
- MOD 加载失败或卸载时移除全部外部任务，保留本体任务；随后 `GameRes` 失败清理会重置整个目录。

## 高频修改流程

### 新增纯配置任务

1. 选择或新建 `Quests/` 分包，并登记到 `quest-manifest.json`。
2. 使用现有 handler 组合条件、阶段、目标和奖励；ID 与版本按上述约束填写。
3. 确认对应玩法在最终成功点发布统一信号，避免为了任务再造第二套事件。
4. 增加 `Quest.Smoke` 配置断言；可真实进入世界的链路同步增加或扩展 Golden Path 操作。

### 新增目标、条件或奖励

1. 判断它是事件增量、当前状态、接取资格还是交付副作用。
2. 实现最小处理器和完整参数校验，注册稳定 `type`。
3. 奖励通过计划准备和事务提交；复杂副作用必须有回滚或延迟发布边界。
4. 添加处理器单元测试、非法参数测试及一次真实玩法回归。

### 接任务 UI

1. UI 读取 `GetSnapshots()` / `TryGetSnapshot()` 并订阅 `QuestChanged`，禁止持有可写存档记录。
2. 手动接取调用 `AcceptQuest()`；手动交付调用 `ClaimQuest()`，展示返回的失败原因。
3. UI 关闭时退订；世界退出后旧运行时不可继续引用。
4. UI 文案通过 `titleKey/descriptionKey` 解析，缺键时使用定义回退文本，并同步加载 `flatworld-ui` 与 `flatworld-localization` Skill。
5. 常驻追踪器通过 `QuestManager.RuntimeReady/RuntimeRemoving` 绑定和释放本地运行时；只展示 `Active/ReadyToClaim`，最多复用四条 `UI_QuestTrackerItem`，完成任务立即移出且不拦截输入。

## 易误判点

- `ReadyToClaim` 不是失败：自动交付也可能因背包空间不足暂留此状态，之后 `Refresh()` 可重试。
- 制作信号中的 `Amount` 是实际产物堆叠数量，不是配方次数；按制作次数计数时应新增明确 handler 或发布专用信号。
- 玩家拾取的 Actor 来自 `ItemPicker` 所属玩家模块，不是被拾取的世界物品。
- 未知任务记录不会出现在运行时快照中，但会原样保存在 JSON；不要把它当垃圾清除。
- `QuestCatalog.IsReady=false` 时不能创建玩家运行时；资源或 MOD 校验失败应阻止进入世界，而非加载半套任务。

## 高耦合联动

| 本系统变更 | 联动 Skill | 必查契约 |
|---|---|---|
| `flatworld.quests` 结构、版本或写入时机 | `flatworld-data-save` | 未知字段/任务保留，旧存档默认空，高版本拒绝写回 |
| `item.grant`、Bag/Hotbar 枚举或事务 | `flatworld-inventory-crafting` | 多奖励原子性、满包不部分发放、UI 刷新 |
| 玩家入世/退出绑定或 GameRes 顺序 | `flatworld-core` | 本体和 MOD 目录 Ready 后才创建本地玩家运行时 |
| MOD `quests`、内容 ID 或跨 MOD 引用 | `flatworld-modding` | 命名空间、全量校验、失败卸载无半注册 |
| 新玩法成功信号 | 对应玩法专项 Skill | 只在最终成功事务后发布，Actor/Target/Amount 正确 |
| 可真实入世验证的任务行为 | `flatworld-golden-path` | 稳定操作 ID、可观察断言、通过/失败都可清理 |

## 修改后验证

- 默认先执行静态诊断、`GamePlay.csproj` / `FlatWorld.GameTest.csproj` 编译和 Unity Console 错误检查。
- 项目约束禁止在用户未明确要求时主动运行 Unity Test Runner；任务测试命令仅在获得明确测试请求后使用：
  - `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Quest.Smoke`
  - `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Quest.Save`
- 完整真实单机回归使用默认全量 Golden Path；聚焦任务时白名单至少同时启用 `quest.progression` 与 `inventory.crafting`。
- 新增/移动任务脚本、JSON、处理器、命名空间、测试分类或 Golden Path ID 后，必须同步本 Skill。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-11：新增四个 `debugOnly` 测试任务与 F4 GM“任务”分页；支持单个/批量开启、状态刷新和手动交付，调试任务强制跳过普通入世自动接取，Smoke 与 `quest.progression` 同步保护目录、代表性目标类型和存档隔离。
- 2026-08-11：新增简洁任务追踪 HUD：任务协调器公开运行时就绪/移除事件，Player 事件驱动复用四条只读任务卡，展示本地化标题、说明、状态、当前目标与进度条；目标定义增加可选 `labelKey/label`，示例任务和 Golden Path 同步覆盖 UI 绑定与完成后移除。
- 2026-08-11：建立基础任务系统：内建/MOD JSON 目录、玩家独立多阶段进度、统一玩法信号、可注册目标/条件/奖励、原子物品奖励、`flatworld.quests` 存档、示例任务、Smoke 与 Golden Path 导航。
