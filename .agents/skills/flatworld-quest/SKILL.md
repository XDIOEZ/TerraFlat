---
name: flatworld-quest
description: "FlatWorld 任务目录、玩家进度、玩法信号、阶段目标、原子奖励与 MOD 扩展。"
---

# FlatWorld 任务

## 入口与数据流

`Assets/5_Scripts/5-3_GamePlay/Core/`：

- `Quests/`：`QuestDefinitions`、`QuestCatalog/QuestCatalogLoader` 定义与目录，`PlayerQuestRuntime/QuestManager` 运行时，`QuestExtensions` 扩展，`QuestProgressStore` 保存。
- `Progress/`：`GameplayProgressEvents` 成功事务信号，`ItemSpecialDataJsonStore` 命名空间数据。
- 本体内容：`Assets/StreamingAssets/GameConfig/Quests/quest-manifest.json`。

`本体 JSON + MOD quests → Catalog Finalize → 本地玩家 Runtime → GameplayProgressEvents → 目标/阶段 → QuestRewardPlan → flatworld.quests`

## 契约

- 本体 ID 使用 `flatworld:`，MOD 使用 `modId:`；阶段/目标/奖励 ID 在各自作用域唯一。`debugOnly` 不自动接取，`definitionVersion` 标识语义版本，已完成记录不重复奖励。
- 类型通过 Handler 注册并在 Finalize 校验；信号 Actor 为实际本地玩家、Amount>0，仅在玩法事务成功后发布，监听异常不回滚玩法。
- 奖励在同一库存快照中全部可放才原子提交；满包保留 ReadyToClaim。进度存于 `ItemSpecialData.flatworld.quests`，保持其它命名空间；未知任务/字段保留，高于支持版本时停止运行与写回。
- 远程副本不建本地 Runtime；目录、GameRes、MOD 共同 Ready 前入世请求排队，最终内容就绪后补启动。
- 阅读入口为书籍物品；显示层只读 Snapshot、订阅 QuestChanged，独立于可写运行态。
