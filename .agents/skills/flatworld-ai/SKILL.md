---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
---
# FlatWorld AI

## 入口

- 状态机：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/{AI_Base,AI_StateMachineRunner}.cs`
- 感知：同目录 `Mod_ItemDetector.cs`；空间索引与批处理在 `Entities/Item/Management/ItemMgr.Perception.cs`，作者形状编译及旧对象桥接在 `Entities/AI/Perception/`，无业务依赖的几何在 `Shared/Utilities/Geometry/PerceptionShape2D.cs`。
- 攻击/闲逛：`AI_AttackController.cs`、`AI_WanderUtility.cs`
- 生成：`Entities/AI/Spawning/{MonsterManager,MonsterSpawnerManager}.cs`、`Entities/Spawner/SpawnerConfig.cs`、`Resources/Config/SpawnerConfig*.asset`
- 存档：`World/Map/Data/{GameSaveData.MonsterSpawner,MonsterSpawnerSaveData}.cs`
- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/{actor-manifest.json,definitions/core-actors.json}`；加载器在 `Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 不变量

- 感知链为 Detector 请求 → ItemMgr 空间格粗筛 → Burst 圆形/AABB 最终相交判断 → 整数格 LOS → 进入/离开结果。Actor/旧 AI 不再通过 `Collider2D.bounds/ClosestPoint` 复核；只有玩家及必须使用 GameObject 几何的旧目标进入 `PerceptionColliderBridge`，不能把 Bridge 当作不支持的 Actor 能力的静默回退。
- Actor 感知形状由当前外壳根级作者数据与合并后的 `visual.collider` 编译，共享于 `RuntimeItemDefinition`；子级攻击盒、生命受击盒不是感知体型。运行时位置、缩放和旋转只变换纯数据形状，不能再因 Collider 开关或 Physics2D 同步时机改变 AI 感知结果；圆采用包围圆，复杂形状采用明确的 AABB 语义，不能把这一感知近似当成精确伤害形状。
- 异步、同步圆形查询与 `IsWithinEffectivePerceptionRange` 必须使用同一 Actor 几何和循环镜像规则；空间格按中心登记时，粗筛范围须覆盖最大变换后体型。Job 结果还须核对注册代际、Guid、实例与当前层；仅检查 GetInstanceID 无法防止对象池原对象复用。
- 生物感知默认经过整数格 LOS：动态建筑以 `BuildingOccupancyRegistry` 的离散占地为权威遮挡，格子墙/岩壁以运行时 `TerrainCell.BlockingTileId + TerrainCellFlags.Blocking` 为权威遮挡；`Mod_ItemDetector.wallsBlockPerception` 允许特殊生物显式关闭。AI 的持续锁定/状态距离判断必须复用 `IsWithinEffectivePerceptionRange`，避免目标进入遮挡后仍只按距离保持感知。
- LOS 属于感知热路径；沿格检测禁止 Physics2D 射线、Collider 扫描或逐格调用 `ChunkMgr.TryGetRuntimeTerrainTile`。应从观察者格到目标格逐格查询 `BuildingOccupancyRegistry`，同时缓存当前 `ChunkRuntime/ChunkTerrainData`，仅跨 Chunk 时重新解析地址并直接读取 `BlockingTileId + Blocking`；任一格命中遮挡立即终止。
- 目标感知范围由 Detector 的 `DetectionRadius` 与 Item 的 `PerceptionRadiusMultiplier` 共同决定；修改感知逻辑时必须同步空间粗筛、目标快照精筛和 AI 状态阈值，避免大体型目标被漏筛或状态机仍使用旧距离。
- 现代 AI 位于 `Entities/AI/`；修改 Prefab 前确认其使用状态机还是旧 Kiwi 行为树。
- `Chicken_Tree`、`WildBoar_Tree` 是历史 Kiwi 兼容 Prefab，不实现 `IAIActor`，不加入正式 Actor JSON/MOD 继承目录。
- 狼只使用 `Assets/2_Prefabs/Gameplay/AI/Wolf.prefab`；不要恢复已删除的 `Wolf_Tree.prefab`。
- 正式 Chicken/WildBoar/Wolf/Ghost 由 Actor JSON 提供名称、视觉和模块参数；Prefab 只保留组件结构、事件引用与回退值。
- Actor 与 Item 分批注册，死亡掉落可引用同批 Actor（如极低概率掉落 Chicken）；校验必须合并当前批次的具体定义 ID 与已注册 Item ID，不能只查询尚未完成的运行时注册表。死亡掉落继承使用顶层 `lootTableId`，子 Actor 替换表时不得残留内联 `Data.LootTable`。
- 动物被动回血统一由 `Mod_Food.HealthState` 依据蛋白质驱动；`AI_Base` 不管理回血，长间隔回血使用 `HealthState.HealInterval/HealAmount` 配置。
- `AI_Chicken` 的产蛋周期使用当前世界 `TimeData.DayLength * layEggIntervalDays`，进度由 `DayTimeSystem.TimeAdvanced` 的权威游戏时间推进量累计，只在 `isAdult && enableEggLaying && eggItemId` 有效时生效；Rabbit 复用鸡 AI 时必须显式 `enableEggLaying=false`，禁止再用超大秒数伪禁用产蛋。
- Actor 外壳、AnimatorController 使用 `flatworld.actor.*` Addressables 地址；Actor 的 SpriteRenderer 由动画状态机驱动，运行时不得读取 Actor 的 Sprite 子资源或 `sourcePrefab`。
- 复用动物状态机但更换整套动作素材时，使用 `AnimatorOverrideController` 覆盖所有被引用的动作，并同步外壳与 Actor JSON 的控制器 Addressables 地址；禁止通过 `LateUpdate` 写入静态 Sprite 覆盖 Animator，否则会冻结动画，误绑未切片图集时还会把全部帧同时显示。
- Actor 外壳中的 `Mod_AnimatorController_Receiver`、`Mod_TurnBack` 属于结构组件，不一定进入 JSON `itemMods` 字典；绑定时必须从 Item 层级查找，禁止按模块 ID 查询并误报缺失。
- `ActorShell` 标签的 Prefab 只由 `ActorDefinitionCatalogLoader` 加载并注册；即使资源同时带有通用 `Prefab` 标签，`GameRes` 的通用加载计划也必须排除它们，避免 Addressables 重复实例造成 Actor ID 别名冲突。
- `Mod_TurnBack` 按动画素材默认朝向控制 Y 轴翻转；狼的素材默认朝右，因此 Wolf Actor JSON 的 `visual.flipX` 必须保持 `false`，否则初始镜像会与运行时转向叠加，表现为背对目标移动。
- Actor 模块参数中的 `LayerMask` 使用 JSON 位掩码整数；`ModuleJsonConfigurator` 负责将数值转换到 `LayerMask.value`，不要直接依赖 Json.NET 的默认转换。
- `UnboundedDailyGrowth` 会跳过生态预算与存活上限；修改生成条件时保留其独立语义。
- `IgnorePopulationLimits` 只取消物种、生成组、玩家周边与全局数量上限；生成计划、概率、生态预算和远距离回收仍然生效，不能与 `UnboundedDailyGrowth` 混为一谈。
- 怪物实例、物种/生成组计数、死亡订阅和回收保护统一由 `MonsterManager` 通过 `ItemMgr` 生命周期事件维护；`MonsterSpawnerManager` 只注入物种目录并执行生成/生态策略，其他系统必须查询注册表或复制无分配快照，禁止再用 `FindObjectsOfType` 或维护第二套怪物实例表。
- `MonsterManager` 注册表会保留区块休眠实体供后续唤醒与远距离回收，但生成上限、物种/分组存活数和溢出裁剪只统计 `activeInHierarchy` 且未进入销毁流程的实例；新增数量限制必须复用 `IsActiveForPopulationLimits`，不能直接按注册总数计算。
- 动物头顶调试 HUD 由全局 `AI_DebugOverlay.Visible` 控制，GM 面板通过 `GMConsolePreferences` 持久化开关；动物自身的 `debugLog` 只负责日志，不要重新用它控制 HUD 显示。
- 动物头顶调试 HUD 在 `AI_Base` 统一显示当前 `BuffManager.ActiveBuffs` 的名称与剩余时间；只读读取 Buff，不在 HUD 层修改 Buff 生命周期。
- 现代动物的睡眠可被有效伤害打断：`AI_Base` 在睡眠中收到正伤害时锁存一次 `SleepInterruptedByDamage`，具体动物的睡眠条件必须优先退出当前睡眠；真正离开睡眠后再清除锁存，并继续使用动物自己的睡醒冷却控制重新入睡。
- 生物生成规则统一来自 `Assets/StreamingAssets/GameConfig/Spawners/spawner-manifest.json`；`MonsterSpawnerManager` 在生态生成的 `Load` 后应用条目出生初始化，AI 组件只负责运行时行为，普通 `ItemMgr.InstantiateItem`、事件生成和存档恢复不得自动套用生态出生随机。
- 需要短时保留正式生态生物用于跨区块、存档或可见性验证时，使用 `MonsterManager.AcquireEcologyRecycleProtection` 的作用域租约；它只绕过数量与距离回收，不能阻止区块休眠显隐或调用方的正式 `DespawnItem`，并且必须在清理路径释放。
- 移动/可走性改动联动 `flatworld-navigation`；伤害联动 `flatworld-combat`；注册/存档联动 Item/Data Skill。
- 追击路径代价限制统一通过 `AI_Base.MoveToChaseTarget` 提交；具体动物只配置自身上限，闲逛、逃跑和外部推进仍使用不受限的普通移动入口。
- 使用路径代价限制的追击必须消费 `RejectedByPathCost`，并通过 `AI_Base.TryHandleRejectedChasePath` 暂停同一目标后再重试；禁止让状态机继续停留在无可执行路线的追击状态。
- 使用 `AI_AttackController` 的动物，前摇、伤害窗口和后摇由控制器统一驱动；修改攻击时序时必须同步 Actor JSON、Prefab 回退值与 `Attack.anim` 的 `IsAttacking` 曲线，避免配置与可视/伤害帧错位。
- 攻击状态条件应先保留已经开始的 `IsAttackLocked` 攻击，再判断新攻击的冷却与起手距离；冷却中不能仅凭距离进入停车攻击节点，否则目标后退会造成反复切换，且 `OnExitAttackState` 重置冷却会进一步推迟下一击。近身起手距离与远处感知追击范围、实际伤害盒是三个独立概念。
- 可组合动物技能统一实现 `IAnimalCombatSkill` 并作为 Item Module 挂载；`AI_Base` 会自动收集到 `_animalSkills`，技能自行控制移动时状态节点必须使用 `CreateStateNode`，不能套用每帧停车的 `CreateStoppedActionStateNode`。
- 动物技能数值来自 `Assets/StreamingAssets/GameConfig/Skills/animal-skills.json`，Actor JSON 只声明模块和技能模板 ID；独立技能碰撞模块不要继承 `Mod_Damage`，否则会被 `AI_AttackController` 当作普通攻击窗口一起启停。

## AIECS 正式框架与阶段边界

- `Entities/AIECS/FlatWorld.AIECS.asmdef` 只承载 Core、Perception、Decision、Navigation、Combat；不引用 GamePlay、Item、Collider 或 MonoBehaviour。表现和早期轨迹原型在独立 `Presentation` 程序集，旧内容编译、玩家和死亡产物适配在独立 `Gameplay` 程序集。不能把旧原型的 Slot、运动轨迹、视觉水参数当正式身份、行为或环境状态。
- 正式世界的 AI 后端由 `AiRuntimeBackendService` 单向路由：`MonsterSpawnerManager` 继续持有生成时间、群系、光照、预算和种群规则，`AiecsEcologyRuntimeHost` 只接管 Entity 创建、模拟、批量表现、计数和远距离回收。GamePlay 不得反向引用 `FlatWorld.AIECS.Gameplay`；新增 ECS 接入能力必须继续通过 GamePlay 侧契约实现，禁止制造程序集循环依赖。
- Entities 后端启用时 `AI_Base.TickMode` 必须保持 `Disabled`，独立旧实现（当前包括 `AI_Ghost`）也必须关闭 Tick/Load 副作用；进入世界时清理已有旧 Actor GameObject，生态和普通事件生成不得再 `InstantiateItem` 创建 Legacy AI。当前 AIECS 基础切片无法编译的 Actor 必须明确跳过并停止该配置的生成积压，禁止静默回退 Legacy AI；开发 `AiecsPlayground` 与正式生态宿主也不得同时驱动两个正式模拟。带专属命令语义的事件（如旧 `creature.advance`）需要单独迁移对应 ECS 行为阶段，不能靠恢复旧 Actor 实现。
- `AiecsSimulation` 持有独立 World 与批次资源，`AiecsDefinitionCompiler` 在冷路径读取当前合并 Actor/MOD 定义。生命、记忆、攻击阶段属于每实体运行态，定义、阵营矩阵与战略 Goal 共享；不得通过实例化旧 AI 获得模板，也不得用 P0 能力报告充当运行时配置。
- 原生感知直接从 ECS 位置、身份、体型、生命构建稀疏桶，桶键包含阵营以避免同阵营占满候选预算。锁定目标只做有效性与低频追击规则复核，失效才错峰搜桶；不可达目标按配置延迟重试。形状偏移和外部玩家缩放必须纳入粗筛扩张上限，循环桶去重与最近镜像必须一起使用。
- LOS 独立复制 TerrainCell 的 Blocking 和建筑占地，不能把导航不可走当作遮挡。移动前快照用于感知，移动后重建快照用于命中；所有 Native 借用必须进入依赖链，重建和释放前完成旧读取者。武器 Pulse 在模拟批次之外发生时须重新借用当前导航索引，不能跨 Update 缓存可能已被导航发布替换的 LOS 视图。
- Brain 只选择 Intent，Behavior 只准备局部移动或共享 Goal，Attack 在真实 Active Tick 再确认目标、几何、朝向和 LOS。扩展行为通过共享优先级规则或 `AiecsBehaviorProposal` 接入；特殊能力注册少量 `IAiecsSimulationStage`，在实际 Pulse 向 `AiecsFrame.HitEvents` 写入，返回完整 JobHandle，禁止逐 AI 托管状态机/事件/Job。当前 Tick 的技能命中最迟在 BeforeSettlement 生产，AfterDamage 用于消费已提交状态。
- 每个开发模拟只采集少量外部玩家代理，身份同时验证 UID、generation、world、dimension 和 Entity 版本；AI↔AI 不走 ItemMgr 快照。旧 Item/AI 的纯几何感知仍是独立兼容后端，不要将它与原生 ECS 感知混为一条运行链。
- 正式小规模手测入口是 `FlatWorld/AIECS/打开实战开发入口`；`AiecsPlayground`、`AiecsNavigationCrowd`、P1 轨迹原型各自持有不同 World，不能同时创建后统计为同一批正式单位。当前正式生态、完整生存/技能、保存和联网尚未接入；阶段门槛以开发文档为准，编译与开发入口不等于 Play 或两万性能通过。

## 工作流与验证

1. 从目标 Prefab 的实际模块进入，不按类名猜运行链。
2. 随机行为使用固定种子或可注入输入；Bug 修复保留确定性回归。
3. 默认做静态诊断、编译和 Console 检查。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
