---
name: flatworld-quest
description: "Use when: 定位或修改 FlatWorld 的任务定义、任务目录、接取/阶段/目标/交付、统一进度信号、任务奖励、任务存档、MOD 任务或任务 UI 接口。关键词：QuestManager、PlayerQuestRuntime、QuestCatalog、QuestProgressStore、GameplayProgressEvents、flatworld.quests。"
---

# FlatWorld 任务

## 入口

- 定义/目录：`Assets/5_Scripts/5-3_GamePlay/Core/Quests/{QuestDefinitions,QuestCatalog,QuestCatalogLoader}.cs`
- 运行时/扩展：同目录 `{PlayerQuestRuntime,QuestManager,QuestExtensions}.cs`
- 信号/存档：`Core/Progress/{GameplayProgressEvents,ItemSpecialDataJsonStore}.cs`、`Core/Quests/QuestProgressStore.cs`
- 内容：`Assets/StreamingAssets/GameConfig/Quests/quest-manifest.json`
- 测试：`Assets/GameTest/Quest/QuestSmokeTests.cs`

## 权威链

`本体 JSON → Catalog → MOD quests → Finalize → 本地玩家入世创建 Runtime → GameplayProgressEvents → 目标/阶段 → 原子奖励 → flatworld.quests`

## 不变量

- 本体 ID 用 `flatworld:`，MOD 用 `modId:`；阶段、目标、奖励 ID 在各自作用域唯一。
- `debugOnly` 永不自动接取。改变现有语义时递增 `definitionVersion`；已完成记录不重复奖励。
- 新类型实现并注册 Handler，在 Catalog Finalize 时严格校验；不要在 `PlayerQuestRuntime` 写玩法名称分支。
- 信号只在玩法事务最终成功后发布，Actor 为实际本地玩家，Amount>0；监听异常不回滚已完成玩法。
- 奖励先准备 `QuestRewardPlan`，物品在同一库存快照中全部可放才提交；满包保持 ReadyToClaim，不部分发放。
- 进度使用 `ItemSpecialData` 的 `flatworld.quests`，保留未知任务/字段；未来版本禁用运行时并停止写回。
- 远程玩家不创建本地任务 Runtime；UI 只读 Snapshot 并订阅 QuestChanged，不持有可写记录。

## 联动与验证

- 存档→Data；物品奖励→Inventory；入世/加载顺序→Core；MOD 内容→Modding；UI 文本→UI+Localization；真实玩法→对应 Skill+Golden Path。
- 默认静态诊断、编译和 Console；需要时运行 `Quest.Smoke`/`Quest.Save`。真实流程聚焦时同时启用 `quest.progression` 与 `inventory.crafting`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
