---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
---
# FlatWorld AI

## 入口

- 状态机：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/{AI_Base,AI_StateMachineRunner}.cs`
- 感知：同目录 `Mod_ItemDetector.cs`；空间索引与批处理在 `Entities/Item/Management/ItemMgr.cs`
- 攻击/闲逛：`AI_AttackController.cs`、`AI_WanderUtility.cs`
- 生成：`Entities/AI/Spawning/{MonsterManager,MonsterSpawnerManager}.cs`、`Entities/Spawner/SpawnerConfig.cs`、`Resources/Config/SpawnerConfig*.asset`
- 存档：`World/Map/Data/{GameSaveData.MonsterSpawner,MonsterSpawnerSaveData}.cs`
- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/{actor-manifest.json,definitions/core-actors.json}`；加载器在 `Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 不变量

- 感知链为 Detector 请求 → ItemMgr 空间格粗筛 → Collider2D 精确确认 → 应用进入/离开结果。
- 目标感知范围由 Detector 的 `DetectionRadius` 与 Item 的 `PerceptionRadiusMultiplier` 共同决定；修改感知逻辑时必须同步空间粗筛、目标快照精筛和 AI 状态阈值，避免大体型目标被漏筛或状态机仍使用旧距离。
- 现代 AI 位于 `Entities/AI/`；修改 Prefab 前确认其使用状态机还是旧 Kiwi 行为树。
- `Chicken_Tree`、`WildBoar_Tree` 是历史 Kiwi 兼容 Prefab，不实现 `IAIActor`，不加入正式 Actor JSON/MOD 继承目录。
- 狼只使用 `Assets/2_Prefabs/Gameplay/AI/Wolf.prefab`；不要恢复已删除的 `Wolf_Tree.prefab`。
- 正式 Chicken/WildBoar/Wolf/Ghost 由 Actor JSON 提供名称、视觉和模块参数；Prefab 只保留组件结构、事件引用与回退值。
- 动物被动回血统一由 `Mod_Food.HealthState` 依据蛋白质驱动；`AI_Base` 不管理回血，长间隔回血使用 `HealthState.HealInterval/HealAmount` 配置。
- Actor 外壳、AnimatorController 使用 `flatworld.actor.*` Addressables 地址；Actor 的 SpriteRenderer 由动画状态机驱动，运行时不得读取 Actor 的 Sprite 子资源或 `sourcePrefab`。
- `ActorShell` 标签的 Prefab 只由 `ActorDefinitionCatalogLoader` 加载并注册；即使资源同时带有通用 `Prefab` 标签，`GameRes` 的通用加载计划也必须排除它们，避免 Addressables 重复实例造成 Actor ID 别名冲突。
- `Mod_TurnBack` 按动画素材默认朝向控制 Y 轴翻转；狼的素材默认朝右，因此 Wolf Actor JSON 的 `visual.flipX` 必须保持 `false`，否则初始镜像会与运行时转向叠加，表现为背对目标移动。
- Actor 模块参数中的 `LayerMask` 使用 JSON 位掩码整数；`ModuleJsonConfigurator` 负责将数值转换到 `LayerMask.value`，不要直接依赖 Json.NET 的默认转换。
- `UnboundedDailyGrowth` 会跳过生态预算与存活上限；修改生成条件时保留其独立语义。
- `IgnorePopulationLimits` 只取消物种、生成组、玩家周边与全局数量上限；生成计划、概率、生态预算和远距离回收仍然生效，不能与 `UnboundedDailyGrowth` 混为一谈。
- 怪物实例、物种/生成组计数、死亡订阅和回收保护统一由 `MonsterManager` 通过 `ItemMgr` 生命周期事件维护；`MonsterSpawnerManager` 只注入物种目录并执行生成/生态策略，其他系统必须查询注册表或复制无分配快照，禁止再用 `FindObjectsOfType` 或维护第二套怪物实例表。
- 动物头顶调试 HUD 由全局 `AI_DebugOverlay.Visible` 控制，GM 面板通过 `GMConsolePreferences` 持久化开关；动物自身的 `debugLog` 只负责日志，不要重新用它控制 HUD 显示。
- 动物头顶调试 HUD 在 `AI_Base` 统一显示当前 `BuffManager.ActiveBuffs` 的名称与剩余时间；只读读取 Buff，不在 HUD 层修改 Buff 生命周期。
- 生物生成规则统一来自 `Assets/StreamingAssets/GameConfig/Spawners/spawner-manifest.json`；`MonsterSpawnerManager` 在生态生成的 `Load` 后应用条目出生初始化，AI 组件只负责运行时行为，普通 `ItemMgr.InstantiateItem`、事件生成和存档恢复不得自动套用生态出生随机。
- 需要短时保留正式生态生物用于跨区块、存档或可见性验证时，使用 `MonsterManager.AcquireEcologyRecycleProtection` 的作用域租约；它只绕过数量与距离回收，不能阻止区块休眠显隐或调用方的正式 `DespawnItem`，并且必须在清理路径释放。
- 移动/可走性改动联动 `flatworld-navigation`；伤害联动 `flatworld-combat`；注册/存档联动 Item/Data Skill。
- 使用 `AI_AttackController` 的动物，前摇、伤害窗口和后摇由控制器统一驱动；修改攻击时序时必须同步 Actor JSON、Prefab 回退值与 `Attack.anim` 的 `IsAttacking` 曲线，避免配置与可视/伤害帧错位。
- 可组合动物技能统一实现 `IAnimalCombatSkill` 并作为 Item Module 挂载；`AI_Base` 会自动收集到 `_animalSkills`，技能自行控制移动时状态节点必须使用 `CreateStateNode`，不能套用每帧停车的 `CreateStoppedActionStateNode`。
- 动物技能数值来自 `Assets/StreamingAssets/GameConfig/Skills/animal-skills.json`，Actor JSON 只声明模块和技能模板 ID；独立技能碰撞模块不要继承 `Mod_Damage`，否则会被 `AI_AttackController` 当作普通攻击窗口一起启停。

## 工作流与验证

1. 从目标 Prefab 的实际模块进入，不按类名猜运行链。
2. 随机行为使用固定种子或可注入输入；Bug 修复保留确定性回归。
3. 默认做静态诊断、编译和 Console 检查；达到测试门槛或用户要求时按 `flatworld-test-automation` 运行 `AI.Smoke`。
4. 测试入口：`Assets/GameTest/AI/AISmokeTests.cs`；真实世界行为可放入 Golden Path。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
