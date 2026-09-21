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

- `AI_Bird` 的起飞入口必须统一执行飞行耐力门禁；耗尽后的强制降落先于逃跑/觅食，落地回满才能解锁，不能让受击逃跑在地面仍调用空中位移。耐力与恢复锁写入独立模块快照，`LiftRoot` 已包含飞行高度，头顶条不能重复叠加。
- `IItemModuleDependencyBinder` 运行时尚未经过 `Module.LoadMod` 的宿主赋值，只能解析传入的模块注册表；依赖 `Module.item` 的运行时表现组件必须留到 `Load` 再创建。
- 鸟类警惕距离需要同时覆盖 Detector 粗筛和带目标感知倍率的逃离阈值；扩大随机巡航半径不等于扩大警惕范围。种子与水果按最近掉落选择，JSON 中实际物品必须带语义 Tag，不能把物品 ID 当作已经存在的 Tag。

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
- 活动种群计数由 `RuntimeItemRegistered/Unregistered` 与无 Update 的 `MonsterPopulationObserver` 增量维护；必须覆盖祖先显隐、直接销毁和回池解绑，不得恢复每帧全表重数。`RegistrationVersion` 只表示注册表结构变化，不能用它缓存会随移动改变的周边数量。
- 刷怪休眠复用 `ItemMgr` 的 WorldAddress 索引：注册时先建立地址，移动跨块以及 `ChunkView` 完整绑定/解绑才检查相关实体。退出世界先解除表现通知，再清理登记，禁止唤醒卸载中的对象；命名空间中存在两种 `WorldAddress`，运行时通知须明确使用 `FlatWorld.WorldModel.WorldAddress`。
- 生成与裁剪必须使用一致的难度倍率；候选重试只共用本次搜索的周边数量，树容量和选树共用合格快照。分帧生成预算只延后完整操作，不丢弃 Pending、不提前消耗重试时间，并轮转配置避免饥饿；查询 ECS 前先检查该配置是否含 ECS 路由。
- `gameplay_spawner_debug` 提供真实注册表审计、有限时间采样及 `profile_frames` 原始 Profiler 读取；Unity Mono 的 `GC.GetAllocatedBytesForCurrentThread` 可能恒为零，必须以自检和标记子树中的 `GC.Alloc` 元数据确认，不能把不支持的计数器当成零分配。
- 动物头顶调试 HUD 由全局 `AI_DebugOverlay.Visible` 控制，GM 面板通过 `GMConsolePreferences` 持久化开关；动物自身的 `debugLog` 只负责日志，不要重新用它控制 HUD 显示。
- 动物头顶调试 HUD 在 `AI_Base` 统一显示当前 `BuffManager.ActiveBuffs` 的名称与剩余时间；只读读取 Buff，不在 HUD 层修改 Buff 生命周期。
- 现代动物的睡眠可被有效伤害打断：`AI_Base` 在睡眠中收到正伤害时锁存一次 `SleepInterruptedByDamage`，具体动物的睡眠条件必须优先退出当前睡眠；真正离开睡眠后再清除锁存，并继续使用动物自己的睡醒冷却控制重新入睡。
- 生物生成规则统一来自 `Assets/StreamingAssets/GameConfig/Spawners/spawner-manifest.json`；`MonsterSpawnerManager` 在生态生成的 `Load` 后应用条目出生初始化，AI 组件只负责运行时行为，普通 `ItemMgr.InstantiateItem`、事件生成和存档恢复不得自动套用生态出生随机。
- 需要短时保留正式生态生物用于跨区块、存档或可见性验证时，使用 `MonsterManager.AcquireEcologyRecycleProtection` 的作用域租约；它只绕过数量与距离回收，不能阻止区块休眠显隐或调用方的正式 `DespawnItem`，并且必须在清理路径释放。
- 移动/可走性改动联动 `flatworld-navigation`；伤害联动 `flatworld-combat`；注册/存档联动 Item/Data Skill。
- 追击路径代价限制统一通过 `AI_Base.MoveToChaseTarget` 提交；具体动物只配置自身上限，闲逛、逃跑和外部推进仍使用不受限的普通移动入口。
- GameObject 动物逃跑统一通过 `AI_Base.MoveAwayFrom` 提交目标，不得在具体动物内重复计算逃跑点绕过公共水体策略；动物已经位于水格时优先选择最近可走陆地脱水，位于陆地时继续避免主动进入水面。
- 使用路径代价限制的追击必须消费 `RejectedByPathCost`，并通过 `AI_Base.TryHandleRejectedChasePath` 暂停同一目标后再重试；禁止让状态机继续停留在无可执行路线的追击状态。
- 使用 `AI_AttackController` 的动物，前摇、伤害窗口和后摇由控制器统一驱动；修改攻击时序时必须同步 Actor JSON、Prefab 回退值与 `Attack.anim` 的 `IsAttacking` 曲线，避免配置与可视/伤害帧错位。
- 攻击状态条件应先保留已经开始的 `IsAttackLocked` 攻击，再判断新攻击的冷却与起手距离；冷却中不能仅凭距离进入停车攻击节点，否则目标后退会造成反复切换，且 `OnExitAttackState` 重置冷却会进一步推迟下一击。近身起手距离与远处感知追击范围、实际伤害盒是三个独立概念。
- 可组合动物技能统一实现 `IAnimalCombatSkill` 并作为 Item Module 挂载；`AI_Base` 会自动收集到 `_animalSkills`，技能自行控制移动时状态节点必须使用 `CreateStateNode`，不能套用每帧停车的 `CreateStoppedActionStateNode`。
- 动物技能数值来自 `Assets/StreamingAssets/GameConfig/Skills/animal-skills.json`，Actor JSON 只声明模块和技能模板 ID；独立技能碰撞模块不要继承 `Mod_Damage`，否则会被 `AI_AttackController` 当作普通攻击窗口一起启停。

## AIECS 正式框架与阶段边界

- `Entities/AIECS/FlatWorld.AIECS.asmdef` 只承载 Core、Perception、Decision、Navigation、Combat；不引用 GamePlay、Item、Collider 或 MonoBehaviour。表现和早期轨迹原型在独立 `Presentation` 程序集，旧内容编译、玩家和死亡产物适配在独立 `Gameplay` 程序集。不能把旧原型的 Slot、运动轨迹、视觉水参数当正式身份、行为或环境状态。
- 正式世界的 AI 后端由 `AiRuntimeBackendService` 单向路由：`MonsterSpawnerManager` 继续持有生成时间、群系、光照、预算和种群规则，`AiecsEcologyRuntimeHost` 只接管 Entity 创建、模拟、批量表现、计数和远距离回收。GamePlay 不得反向引用 `FlatWorld.AIECS.Gameplay`；新增 ECS 接入能力必须继续通过 GamePlay 侧契约实现，禁止制造程序集循环依赖。
- 正式生态允许 GameObject AI 与 AIECS 并行：`SpawnerConfig.SpawnEntry.RuntimeBackend` 是物种后端的权威选择，默认 `GameObject`，只有显式 `Entities` 的大规模物种交给 AIECS。`AI_Base` 与独立旧实现只要被实例化就必须正常 Tick；生成、事件、Actor 掉落、数量统计和远距离回收都必须按物种路由，禁止再用一个全局开关关闭全部 GameObject AI。同一物种在同一世界只能归属一个后端，切换或加载时只清理由 ECS 路由物种遗留的 GameObject；AIECS 临时停用或不支持该物种时也不得静默回退 GameObject。开发 `AiecsPlayground` 与正式生态宿主仍不得同时驱动两个 AIECS World。
- 正式生态目录中“单个 Actor 尚未迁移”属于预期能力边界：`PrepareWorld` 必须只记录一次普通诊断日志并从 AIECS 生成候选中排除，不能污染正常 `GameStartScene` 的 Warning 基线；只有后端未注册、目录完全无可运行 Actor、初始化异常等真正阻断正式 AIECS 的情况才使用 Error/Exception。
- 正常 `GameStartScene` 世界的 GM AIECS 分页通过正式 `AiecsEcologyRuntimeHost` 惰性取得一个空闲 `AiecsPlayground` 调试入口；空闲入口不得阻断正式生态，只有真正启动开发场景时才先同步释放正式模拟，清理开发场景后正式宿主再自动恢复，任何时刻禁止两套 AIECS World 同时推进。
- `AiecsSimulation` 持有独立 World 与批次资源，`AiecsDefinitionCompiler` 在冷路径读取当前合并 Actor/MOD 定义。生命、记忆、攻击阶段属于每实体运行态，定义、阵营矩阵与战略 Goal 共享；不得通过实例化旧 AI 获得模板，也不得用 P0 能力报告充当运行时配置。
- 正式 AIECS 的水体环境态从共享导航快照单向进入 `AiecsFlowAgent.WaterDepth/WaterBlend`：移动层按同一水深做减速，`AiecsDisplayRecord` 再把它交给批量水体 Shader。禁止为每只 Entity 创建 `TileEffectReceiver` 或反向查询 `ChunkMgr`；水体是否需要绕行仍由导航高代价决定，而不是由水态表现硬阻挡。
- 原生感知直接从 ECS 位置、身份、体型、生命构建稀疏桶，桶键包含阵营以避免同阵营占满候选预算。锁定目标只做有效性与低频追击规则复核，失效才错峰搜桶；不可达目标按配置延迟重试。形状偏移和外部玩家缩放必须纳入粗筛扩张上限，循环桶去重与最近镜像必须一起使用。
- LOS 独立复制 TerrainCell 的 Blocking 和建筑占地，不能把导航不可走当作遮挡。移动前快照用于感知，移动后重建快照用于命中；所有 Native 借用必须进入依赖链，重建和释放前完成旧读取者。武器 Pulse 在模拟批次之外发生时须重新借用当前导航索引，不能跨 Update 缓存可能已被导航发布替换的 LOS 视图。
- 正式 `AiecsSimulation` 使用两套空间索引：`perceptionSpatial` 冻结移动前感知/决策快照，`combatSpatial` 沿移动 Job 依赖链异步建立移动后攻击/伤害快照；`AiecsSpatialIndex.Build` 只同步该索引上一轮读取者，当前 dependency 必须继续交给 Build Job，禁止重新复用单索引并在 Tick 中途 `Complete` 整条移动链。正式生态宿主 30Hz 追帧单帧最多执行 2 个 Tick、最多保留 3 个 Tick 时间债务，避免卡顿后形成追帧尖峰。
- Brain 只选择 Intent，Behavior 只准备局部移动或共享 Goal，Attack 在真实 Active Tick 再确认目标、几何、朝向和 LOS。扩展行为通过共享优先级规则或 `AiecsBehaviorProposal` 接入；特殊能力注册少量 `IAiecsSimulationStage`，在实际 Pulse 向 `AiecsFrame.HitEvents` 写入，返回完整 JobHandle，禁止逐 AI 托管状态机/事件/Job。当前 Tick 的技能命中最迟在 BeforeSettlement 生产，AfterDamage 用于消费已提交状态。
- AIECS 近战接敌名额按目标批量解析为固定数量 `Engagement Slot`，不能通过导航格容量限制实现。感知后、决策前统一分配槽位：正在攻击和可立即起手的旧持有者优先稳定保留，其余按接近方向占空位；只有当前槽位持有者可进入新的 Attack 起手，未获槽位的追击者停留在外圈并由连续 Crowd Steering/密度场向两翼分流。攻击一旦进入 Windup/Active/Recovery 仍按既有锁定语义完成，不因下一 Tick 槽位重排强制中断。
- AIECS 攻击表现必须按 `AiecsAttackPhase` 的独立阶段时钟采样：`Windup` 与 `Recovery` 在专用动画完成前复用 Idle，只有 `Active` 播放 Attack；不能继续从进入 Attack 行为的总时长采样，否则真正进入 Active 时会从攻击动画中间帧开始。
- 每个开发模拟只采集少量外部玩家代理，身份同时验证 UID、generation、world、dimension 和 Entity 版本；AI↔AI 不走 ItemMgr 快照。正式混合后端目前也只有玩家进入 AIECS 外部代理，GameObject 动物与 ECS 生物尚未互相作为感知/攻击目标；若以后需要跨后端战斗，应显式桥接少量 GameObject Actor 快照，不能把 Collider/ItemMgr 查询塞进 AIECS Native 热路径。旧 Item/AI 的纯几何感知仍是独立兼容后端，不要将它与原生 ECS 感知混为一条运行链。
- 正式小规模手测入口是 `FlatWorld/AIECS/打开实战开发入口`；`AiecsPlayground`、`AiecsNavigationCrowd`、P1 轨迹原型各自持有不同 World，不能同时创建后统计为同一批正式单位。当前正式生态、完整生存/技能、保存和联网尚未接入；阶段门槛以开发文档为准，编译与开发入口不等于 Play 或两万性能通过。

## 工作流与验证

- Job 布局变更后同时出现“not a known Burst entry point”和多个 JobChunk 包装器空引用时，先保存日志、做托管/Burst 对照，并通过 Unity `CompilationPipeline.RequestScriptCompilation(CleanBuildCache)` 重新生成与注册当前编译产物；禁止手动替换 `Library/ScriptAssemblies`，也不能把关闭 Burst 当成修复。复测必须确认 Burst 开启、真实 Tick 前进及行为/输出正常。
- `AiecsPlayground` 的自动启动与 GM 按钮共用正式 `IsGameplayReady` 门禁，导航缓存就绪不代表出生区域已经完成绑定。测试阵型按整格中心和当前体型间距生成，两军初始格不重叠；拒绝出生不能靠放宽容量或同步加载地图来掩盖。压力结果区分目标数量、累计生成、当前存活，并同时检查模拟 Tick、积压时间和实际绘制，不能用静止画面或累计产出宣称同屏容量。

1. 从目标 Prefab 的实际模块进入，不按类名猜运行链。
2. 随机行为需要复现时固定种子或输入，使真实 Play Mode 中能够重复同一路径。
3. 验收统一在真实 Play Mode / GamePlayMCP 中执行 AI 感知、移动、攻击和状态切换；编译与 Console 只做运行门禁和故障定位。

## Skill 维护原则

- 开发入口暂停正式生态宿主时，死亡产出的 Actor 也必须在当前开发模拟交付：开始场景前按战利品表展开 Actor 引用闭包，渲染目录与模拟定义使用相同索引。不可把生物战利品发给被暂停的生态后端，更不能转换成库存掉落图标。
- 掉落队列只在生成成功后扣减剩余数量。可重试的占格/窗口拒绝保留数量并轮转队列，不能把正常拥挤当异常停止整场 AI；缺失内容或不支持的静态定义仍明确报错。
- GM `AiecsPlayground` 压测场景必须拥有并在 `清理`/Dispose 时回收自己生成的静态掉落实体，否则前一轮战斗战利品会污染后续压力数据；只允许按生成时取得的掉落句柄定向回收，禁止按全局数量或遍历世界清空，以免删除进入压测前已经存在的正式掉落。正式生态 `AiecsGameplayBridge` 默认不得启用该开发清理语义。
- 压力验收同时记录请求、累计生成、存活、可见批次、Tick 与积压；地图拒绝的生成不能算作已生成实体。`gameplay_aiecs_debug` 提供有界只读采样和占格检查；截图请求后的首帧可能包含 PNG 开销，性能采样应与截图编码分开。

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
