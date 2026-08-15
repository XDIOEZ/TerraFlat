---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
---
# FlatWorld AI

## 入口

- 状态机：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/{AI_Base,AI_StateMachineRunner}.cs`
- 感知：同目录 `Mod_ItemDetector.cs`；空间索引与批处理在 `Entities/Item/Management/ItemMgr.cs`
- 攻击/闲逛：`AI_AttackController.cs`、`AI_WanderUtility.cs`
- 生成：`Entities/AI/Spawning/MonsterSpawnerManager.cs`、`Entities/Spawner/SpawnerConfig.cs`、`Resources/Config/SpawnerConfig*.asset`
- 存档：`World/Map/Data/{GameSaveData.MonsterSpawner,MonsterSpawnerSaveData}.cs`
- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/{actor-manifest.json,definitions/core-actors.json}`；加载器在 `Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 不变量

- 感知链为 Detector 请求 → ItemMgr 空间格粗筛 → Collider2D 精确确认 → 应用进入/离开结果。
- 目标感知范围由 Detector 的 `DetectionRadius` 与 Item 的 `PerceptionRadiusMultiplier` 共同决定；修改感知逻辑时必须同步空间粗筛、目标快照精筛和 AI 状态阈值，避免大体型目标被漏筛或状态机仍使用旧距离。
- 现代 AI 位于 `Entities/AI/`；修改 Prefab 前确认其使用状态机还是旧 Kiwi 行为树。
- `Chicken_Tree`、`WildBoar_Tree` 是历史 Kiwi 兼容 Prefab，不实现 `IAIActor`，不加入正式 Actor JSON/MOD 继承目录。
- 狼只使用 `Assets/2_Prefabs/Gameplay/AI/Wolf.prefab`；不要恢复已删除的 `Wolf_Tree.prefab`。
- 正式 Chicken/WildBoar/Wolf/Ghost 由 Actor JSON 提供名称、视觉和模块参数；Prefab 只保留组件结构、事件引用与回退值。
- Actor 外壳、AnimatorController 使用 `flatworld.actor.*` Addressables 地址；Actor 的 SpriteRenderer 由动画状态机驱动，运行时不得读取 Actor 的 Sprite 子资源或 `sourcePrefab`。
- `Mod_TurnBack` 按动画素材默认朝向控制 Y 轴翻转；狼的素材默认朝右，因此 Wolf Actor JSON 的 `visual.flipX` 必须保持 `false`，否则初始镜像会与运行时转向叠加，表现为背对目标移动。
- Actor 模块参数中的 `LayerMask` 使用 JSON 位掩码整数；`ModuleJsonConfigurator` 负责将数值转换到 `LayerMask.value`，不要直接依赖 Json.NET 的默认转换。
- `UnboundedDailyGrowth` 会跳过生态预算与存活上限；修改生成条件时保留其独立语义。
- `IgnorePopulationLimits` 只取消物种、生成组、玩家周边与全局数量上限；生成计划、概率、生态预算和远距离回收仍然生效，不能与 `UnboundedDailyGrowth` 混为一谈。
- 动物头顶调试 HUD 由全局 `AI_DebugOverlay.Visible` 控制，GM 面板通过 `GMConsolePreferences` 持久化开关；动物自身的 `debugLog` 只负责日志，不要重新用它控制 HUD 显示。
- 需要短时保留正式生态生物用于跨区块、存档或可见性验证时，使用 `MonsterSpawnerManager.AcquireEcologyRecycleProtection` 的作用域租约；它只绕过数量与距离回收，不能阻止区块休眠显隐或调用方的正式 `DespawnItem`，并且必须在清理路径释放。
- 移动/可走性改动联动 `flatworld-navigation`；伤害联动 `flatworld-combat`；注册/存档联动 Item/Data Skill。

## 工作流与验证

1. 从目标 Prefab 的实际模块进入，不按类名猜运行链。
2. 随机行为使用固定种子或可注入输入；Bug 修复保留确定性回归。
3. 默认做静态诊断、编译和 Console 检查；达到测试门槛或用户要求时按 `flatworld-test-automation` 运行 `AI.Smoke`。
4. 测试入口：`Assets/GameTest/AI/AISmokeTests.cs`；真实世界行为可放入 Golden Path。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
