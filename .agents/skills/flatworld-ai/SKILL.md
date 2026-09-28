---
name: flatworld-ai
description: "Use when: 定位或修改 FlatWorld 的动物/怪物 AI、状态机、感知、目标选择、攻击、闲逛、AI 移动、行为树兼容、怪物生成器或 AI Prefab。关键词：AI_Base、Mod_ItemDetector、AI_StateMachineRunner、MonsterSpawnerManager。"
---
# FlatWorld AI

## 入口

- 状态机：`Assets/5_Scripts/5-3_GamePlay/Entities/AI/{AI_Base,AI_StateMachineRunner}.cs`
- 感知：同目录 `Mod_ItemDetector.cs`；空间索引与批处理在 `Entities/Item/Management/ItemMgr.Perception.cs`，作者形状编译及旧对象桥接在 `Entities/AI/Perception/`，无业务依赖的几何在 `Shared/Utilities/Geometry/PerceptionShape2D.cs`。
- 攻击/闲逛：`AI_AttackController.cs`、`AI_WanderUtility.cs`
- 生成：`Entities/AI/Spawning/{MonsterManager,MonsterSpawnerManager}.cs`、`Entities/Spawner/{SpawnerConfig,SpawnerConfigCatalog,SpawnerConfigCatalogLoader}.cs`、`Resources/GameConfig/Spawners/{spawner-settings.json,Rules/*.json}`
- 存档：`World/Map/Data/{GameSaveData.MonsterSpawner,MonsterSpawnerSaveData}.cs`
- Actor 定义：`Assets/StreamingAssets/GameConfig/Actors/{actor-manifest.json,definitions/core-actors.json}`；加载器在 `Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`

## 不变量

- `AI_Bird` 的起飞入口必须统一执行飞行耐力门禁；耗尽后的强制降落先于逃跑/觅食，落地回满才能解锁，不能让受击逃跑在地面仍调用空中位移。耐力与恢复锁写入独立模块快照，`LiftRoot` 已包含飞行高度，头顶条不能重复叠加。
- `AI_Bird` 的 Flying 阶段不能把“已到固定目标”或“直线路径进入未加载地形”留给 `MoveFlightStep` 原地返回：逃跑到点须结束该目标，巡航目标保持最短前进距离，空中追逐与漫游共用转向通行逻辑；周围无可通行空路而脚下可落地时转入降落。
- 鸟类飞行只检查经过格所在的权威 Chunk 是否 Ready；单次移动共用拓扑与区块查询、同格去重，未加载格仍阻挡，不要为通行检查逐采样读取地块表面。
- 共用 `AIFleeStateNode` 在当前逃离段接近终点时接续下一段，导航确认失败时重选段；普通威胁位置抖动不应触发重新寻路，新伤害仍可显式 `Retarget`。
- 常驻飞行 Actor 使用 `AI_Bird.permanentFlight`，默认保持 Flying 且不进入普通鸟耐力/觅食降落循环；物种行为经 `IBirdFlightPilot` 注册，明确的地面行为可通过 Pilot 临时切到 Ground，后续 `FlyTo/WanderAroundHome` 会恢复 Flying。`Mod_HiveColony` 只在新巢首次创建初始成员，之后死亡成员须按繁殖规则补充；蜂巢持有成员 GUID 与独立行为快照，成员的 `AI_Bird.HomeHiveGuid` 标记归属，并通过 `IRuntimeAiPersistencePolicy` 排除独立 AI 快照。蜂巢领地共享警戒只作用于同时位于领地内的成员，Scene Gizmo 与蜜蜂随机巡逻必须复用同一套整格领地判定；普通巡逻使用 `PatrolFlightSpeedMultiplier` 缩放基础飞行速度，追击、返巢和采蜜赶路不得继承该减速。
- 蜂群夜间睡眠由蜂巢统一调度：日落后清醒成员真实飞回巢位，到达后保存 `BeeState` 并卸载 GameObject；睡眠饱食按 `ModUpdate` 的缩放时间以清醒速率的一半直接推进快照，日出恢复同一 GUID。普通领地警戒不能唤醒睡蜂，蜂巢 `DamageReceiver` 的有效受击必须唤醒全巢并把武器/投射物 `Owner` 解析为最高优先级攻击目标；蜂巢死亡时解除 `HomeHiveGuid` 并保留蜜蜂实体继续复仇，不能随蜂巢 `Unload` 一起回收。
- 鸟类动画以 `AI_Bird` 飞行阶段为权威：切换时核对 Animator 当前状态，不能只缓存上次请求的动画名；觅食降落还须确认鸟当前位置可落脚，食物目标格可走不代表脚下可落地。
- 鸟从地面起飞须先经 `RunUp` 走地面导航、累计真实向前位移，再沿助跑方向水平移动并抬升 `BirdLift`；助跑仍受地块和近战影响，离地后才屏蔽。起飞速度与助跑距离的默认值、鸟/海鸥 Prefab、Actor JSON 运行时参数应同步。
- 鸟与海鸥的飞行高度以 Actor JSON 的 `modules.ai.parameters.flightHeight` 为运行时配置，`AI_Bird` 默认值和两个外壳 Prefab 须与之同步；海鸥继承鸟的 Actor 参数。`BirdLift` 同时包含贴图和受击盒，飞行表现只移动该节点，不改写 Item/刚体的地面坐标；独立阴影通过 `IVisualGroundOffset` 扣除视觉抬升。
- `IItemModuleDependencyBinder` 运行时尚未经过 `Module.LoadMod` 的宿主赋值，只能解析传入的模块注册表；依赖 `Module.item` 的运行时表现组件必须留到 `Load` 再创建。
- 鸟类警惕距离需要同时覆盖 Detector 粗筛和带目标感知倍率的逃离阈值；扩大随机巡航半径不等于扩大警惕范围。种子与水果按最近掉落选择，JSON 中实际物品必须带语义 Tag，不能把物品 ID 当作已经存在的 Tag。
- 玩家 Prefab 的 Unity 标签是 `Untagged`，鸟类与其他动物的玩家威胁筛选须识别 `Player` 实体或物品数据中的 `Player` 标签；只检查 `CompareTag("Player")` 会让玩家贴近时仍无法触发逃跑。

- 感知链为 Detector 请求 → ItemMgr 空间格粗筛 → Burst 圆形/AABB 最终相交判断 → 整数格 LOS → 进入/离开结果。Actor/旧 AI 不再通过 `Collider2D.bounds/ClosestPoint` 复核；只有玩家及必须使用 GameObject 几何的旧目标进入 `PerceptionColliderBridge`，不能把 Bridge 当作不支持的 Actor 能力的静默回退。
- Actor 感知形状由当前外壳根级作者数据与合并后的 `visual.collider` 编译，共享于 `RuntimeItemDefinition`；子级攻击盒、生命受击盒不是感知体型。运行时位置、缩放和旋转只变换纯数据形状，不能再因 Collider 开关或 Physics2D 同步时机改变 AI 感知结果；圆采用包围圆，复杂形状采用明确的 AABB 语义，不能把这一感知近似当成精确伤害形状。
- 异步、同步圆形查询与 `IsWithinEffectivePerceptionRange` 必须使用同一 Actor 几何和循环镜像规则；空间格按中心登记时，粗筛范围须覆盖最大变换后体型。Job 结果还须核对注册代际、Guid、实例与当前层；仅检查 GetInstanceID 无法防止对象池原对象复用。
- 生物感知默认经过整数格 LOS：动态建筑以 `BuildingOccupancyRegistry` 的离散占地为权威遮挡，格子墙/岩壁以运行时 `TerrainCell.BlockingTileId + TerrainCellFlags.Blocking` 为权威遮挡；`Mod_ItemDetector.wallsBlockPerception` 允许特殊生物显式关闭。AI 的持续锁定/状态距离判断必须复用 `IsWithinEffectivePerceptionRange`，避免目标进入遮挡后仍只按距离保持感知。
- LOS 属于感知热路径；沿格检测禁止 Physics2D 射线、Collider 扫描或逐格调用 `ChunkMgr.TryGetRuntimeTerrainTile`。每条视线只取一次 `WorldTopologyDomain`，仅跨 Chunk 时重新解析地址，已加载格直接读取 `ChunkTerrainData.IsSightBlockingCell`；地形统一写入口和建筑占地登记负责维护遮挡位，未加载格始终视为遮挡。AIECS 快照按 `BlockingRevision`、占地格通知和整层重建版本刷新。
- 目标感知范围由 Detector 的 `DetectionRadius` 与 Item 的 `PerceptionRadiusMultiplier` 共同决定；修改感知逻辑时必须同步空间粗筛、目标快照精筛和 AI 状态阈值，避免大体型目标被漏筛或状态机仍使用旧距离。
- 现代 AI 位于 `Entities/AI/`；修改 Prefab 前确认其使用状态机还是旧 Kiwi 行为树。
- `Chicken_Tree`、`WildBoar_Tree` 是历史 Kiwi 兼容 Prefab，不实现 `IAIActor`，不加入正式 Actor JSON/MOD 继承目录。
- 狼只使用 `Assets/2_Prefabs/Gameplay/AI/Wolf.prefab`；不要恢复已删除的 `Wolf_Tree.prefab`。
- 正式 Chicken/WildBoar/Wolf/Ghost 由 Actor JSON 提供名称、视觉和模块参数；Prefab 只保留组件结构、事件引用与回退值。
- Actor 与 Item 分批注册，死亡掉落可引用同批 Actor（如极低概率掉落 Chicken）；校验必须合并当前批次的具体定义 ID 与已注册 Item ID，不能只查询尚未完成的运行时注册表。死亡掉落继承使用顶层 `lootTableId`，子 Actor 替换表时不得残留内联 `Data.LootTable`。
- 世界内 F5 的 Actor 候选须逐定义校验并按最终发布的 ID 集复核引用；单项失败保留旧运行时定义、外壳别名与已解析来源，新增无效定义不发布，避免污染其他 Actor 与 MOD 继承链。
- 动物被动回血统一由 `Mod_Food.HealthState` 依据蛋白质驱动；`AI_Base` 不管理回血，长间隔回血使用 `HealthState.HealInterval/HealAmount` 配置。
- `AI_Chicken` 的产蛋周期使用当前世界 `TimeData.DayLength * layEggIntervalDays`，进度由 `DayTimeSystem.TimeAdvanced` 的权威游戏时间推进量累计，只在 `isAdult && enableEggLaying && eggItemId` 有效时生效；产蛋与交配保持鸡的独立机制，不作为普通动物节点默认继承。
- Actor 外壳、AnimatorController 使用 `flatworld.actor.*` Addressables 地址；Actor 的 SpriteRenderer 由动画状态机驱动，运行时不得读取 Actor 的 Sprite 子资源或 `sourcePrefab`。
- 复用动物状态机但更换整套动作素材时，使用 `AnimatorOverrideController` 覆盖所有被引用的动作，并同步外壳与 Actor JSON 的控制器 Addressables 地址；禁止通过 `LateUpdate` 写入静态 Sprite 覆盖 Animator，否则会冻结动画，误绑未切片图集时还会把全部帧同时显示。
- Actor 外壳中的 `Mod_AnimatorController_Receiver`、`Mod_TurnBack` 属于结构组件，不一定进入 JSON `itemMods` 字典；绑定时必须从 Item 层级查找，禁止按模块 ID 查询并误报缺失。
- `ActorShell` 标签的 Prefab 只由 `ActorDefinitionCatalogLoader` 加载并注册；即使资源同时带有通用 `Prefab` 标签，`GameRes` 的通用加载计划也必须排除它们，避免 Addressables 重复实例造成 Actor ID 别名冲突。
- `Mod_TurnBack` 按动画素材默认朝向控制 Y 轴翻转；狼的素材默认朝右，因此 Wolf Actor JSON 的 `visual.flipX` 必须保持 `false`，否则初始镜像会与运行时转向叠加，表现为背对目标移动。
- Actor 模块参数中的 `LayerMask` 使用 JSON 位掩码整数；`ModuleJsonConfigurator` 负责将数值转换到 `LayerMask.value`，不要直接依赖 Json.NET 的默认转换。
- `UnboundedDailyGrowth` 会跳过生态预算与存活上限；修改生成条件时保留其独立语义。
- `IgnorePopulationLimits` 只取消玩法数量上限；生成计划、概率、生态预算仍生效，也不能绕过 GameObject/ECS 的技术装载容量。`UnboundedDailyGrowth` 另行跳过生态预算与玩法数量上限，需求在达到装载容量时保留待执行。
- 怪物实例、物种/生成组计数、死亡订阅和回收保护统一由 `MonsterManager` 通过 `ItemMgr` 生命周期事件维护；`MonsterSpawnerManager` 只注入物种目录并执行生成/生态策略，其他系统必须查询注册表或复制无分配快照，禁止再用 `FindObjectsOfType` 或维护第二套怪物实例表。
- 生态活体不得因超额或远离玩家直接销毁。GameObject 生物远距后先 `Item.Save` 克隆完整 `ItemData` 到 `SaveDataMgr` 的区块差量，再 `DespawnItem(saveData:false)` 卸载实体；再次靠近时从休眠记录恢复同一 GUID、位置与模块状态。装载和区块隐藏的居民占当前活动种群名额；远处休眠者不占装载名额，但须计入其所在玩家区域的密度与物种限制，避免返程重复出生、远行又无新生态。状态机卸载时只清本轮运行态与订阅。
- `MonsterManager` 只登记已装载生物；物种/分组玩法数量只统计 `activeInHierarchy` 且未销毁的实体，新增数量限制复用 `IsActiveForPopulationLimits`。技术装载容量按注册数限制新出生，不能为了腾名额裁掉可见活体。
- 活动种群计数由 `RuntimeItemRegistered/Unregistered` 与无 Update 的 `MonsterPopulationObserver` 增量维护；必须覆盖祖先显隐、直接销毁和回池解绑，不得恢复每帧全表重数。`RegistrationVersion` 只表示注册表结构变化，不能用它缓存会随移动改变的周边数量。
- 区块显隐复用 `ItemMgr` 的 WorldAddress 索引，变化地址排队后分批处理；任何相机可见位置不施加显隐切换。退出世界先解除表现通知，再清理登记；命名空间中存在两种 `WorldAddress`，运行时通知明确使用 `FlatWorld.WorldModel.WorldAddress`。
- 自然与事件出生必须在所有活动游戏相机的视口外，并确认地形、导航和 ChunkView 表现已就绪；循环世界按最近镜像判可见。生态 Tick 不追帧，每轮轮转配置并只搜索一个候选或创建一个实体；事件出生每次最多创建一只并限制候选检查，成功后共用出生间隔。重试与技术容量阻塞均保留 Pending。树容量和选树共用合格快照，查询 ECS 前先确认路由。
- 休眠快照与活动 AI 共用 `ChunkSaveRecord.ChangedItems`：自动保存采集期间暂停生态出生/休眠/唤醒，合并活动快照时保留同区块休眠记录；唤醒成功才移除旧记录。运行中区块恢复先入休眠索引，镜头外分帧实体化；进入世界加载阶段仍可随加载恢复。
- `gameplay_spawner_debug` 提供真实注册表审计、有限时间采样及 `profile_frames` 原始 Profiler 读取；Unity Mono 的 `GC.GetAllocatedBytesForCurrentThread` 可能恒为零，必须以自检和标记子树中的 `GC.Alloc` 元数据确认，不能把不支持的计数器当成零分配。
- 动物头顶调试 HUD 由全局 `AI_DebugOverlay.Visible` 控制，GM 面板通过 `GMConsolePreferences` 持久化开关；动物自身的 `debugLog` 只负责日志，不要重新用它控制 HUD 显示。
- 不继承 `AI_Base` 的独立动物行为模块（如蜜蜂）若需要头顶调试参数，也必须直接复用 `AI_DebugOverlay.Visible`，保持与 GM 的“动物参数”总开关一致。
- 蜂巢自身参数使用独立 `HiveColonyDebugOverlay.Visible`，由 GM 的“蜂巢参数”开关与 `GMConsolePreferences` 持久化控制，不与蜜蜂/动物参数总开关绑定。
- 动物头顶调试 HUD 在 `AI_Base` 统一显示当前 `BuffManager.ActiveBuffs` 的名称与剩余时间；只读读取 Buff，不在 HUD 层修改 Buff 生命周期。
- 现代动物的睡眠可被有效伤害打断：`AI_Base` 在睡眠中收到正伤害时锁存一次 `SleepInterruptedByDamage`，具体动物的睡眠条件必须优先退出当前睡眠；真正离开睡眠后再清除锁存，并继续使用动物自己的睡醒冷却控制重新入睡。
- 本体生态生成规则位于 `Assets/Resources/GameConfig/Spawners/Rules/*.json`，每文件一组 `schemaVersion + config`，新增规则直接添加独立 JSON；全局调度间隔、扫描预算和两种后端装载上限位于同目录的 `spawner-settings.json`。`SpawnerConfigCatalogLoader` 只在资源加载阶段自动发现、严格校验和排序，游戏帧内只读取内存目录；`config.id` 是存档身份，不得随意改名，`spawnEntries[].prefabName` 必须引用已注册 Actor，JSON 只能配置现有调度算法。`MonsterSpawnerManager` 在生态生成的 `Load` 后应用条目出生初始化，AI 组件只负责运行时行为，普通 `ItemMgr.InstantiateItem`、事件生成和存档恢复不得自动套用生态出生随机。
- 需要短时保持 GameObject 生物装载时使用 `MonsterManager.AcquireEcologyRecycleProtection` 作用域租约；它阻止远距序列化休眠，但不阻止区块表现显隐或调用方的正式 `DespawnItem`，须在清理路径释放。
- 移动/可走性改动联动 `flatworld-navigation`；伤害联动 `flatworld-combat`；注册/存档联动 Item/Data Skill。
- 生物水流推动速度统一读取 `StreamingAssets/GameConfig/Movement/water-current.json`：表中 Actor 用指定速度，其余按 Actor 定义的 `weight`/运行时 `ItemData.Stack.Weight` 与 `weightRule` 换算；新增物种不能长期沿用壳 Prefab 的默认 1 kg。`Mover_AI` 经导航代理显式绑定 Mover，纯 ECS 经共享导航快照，独立幽灵的寻路与直追入口都须叠加水流；`AI_Bird` 只在地面/助跑接收水流，离地阶段按 `IWaterCurrentExposure` 屏蔽。
- 追击路径代价限制统一通过 `AI_Base.MoveToChaseTarget` 提交；具体动物只配置自身上限，闲逛、逃跑和外部推进仍使用不受限的普通移动入口。
- 地面动物逃离统一注册 `AI_Base.CreateFleeStateNode`，共享 `AIFleeStateNode` 在进入时锁定目标，之后按威胁方向变化或目标到达节流重规划；威胁引用是运行时状态，存档恢复到 Flee 时若进入节点尚无威胁必须继续尝试获取，不能永久停住。物种只提供威胁位置与 Actor JSON 距离，目标规划仍须经过 `AIFleeUtility` 和 `AI_WanderUtility` 的水体策略；远距逃离拆成可直达的局部路段，不能只凭终点可走就提交不连通的长距离导航目标。导航连续四次无路径后逃离节点进入 `Error` 并只报一次带实体与目标坐标的 Console 错误；改换目标、新路径被接受或退出节点时清除错误。
- 通用 AI 可将 `Module_AI_BehaviorGraph` 加入 Actor JSON 模块，并在 `parameters.behaviorGraph` 配置初始状态、节点参数及有序转换；转换条件使用已注册类型，支持 `all`/`any` 组合，错误节点、条件、状态键和参数在 Actor 定义预检时报错。节点和条件逻辑由 C# 注册，物种行为数值与状态流转留在 JSON，所需移动/感知/生命能力由 Actor 壳与模块提供。
- `AI_BehaviorGraph` 是可独立使用的通用 AI 执行模块；现有 Chicken、WildBoar、Wolf、Bird 等专用 AI 迁移前不可与它同时驱动同一个 Actor 的移动或状态。
- `AI_BehaviorGraph` 的标准节点已覆盖 `wander/flee/sleep/forage/attack`：共享 Context 负责可存档命名计时器、受击记忆、Food/Health/Detector/Mover 能力与跨状态攻击冷却；`forage` 同时支持 Tag 食物和运行时草层，并用世界天数维护草食营养维持期。Sheep 是第一份正式 JSON 模板，新增普通动物优先复制该图结构后按需求删减，而不是再复制 `AI_Chicken` 状态机。
- BehaviorGraph Actor 的外壳仍需内嵌且仅内嵌一个 `IAIActor` 以通过 Actor 目录预检；JSON 模块表不会阻止 `Item.ModuleLoad` 注册外壳内已有的专用 AI/生产组件。迁移动物时同时清理外壳里的旧驱动器和物种生产模块，并用 `removeModules` 删除父 JSON 继承的专用模块；运行态由 Blackboard/感知服务持有，计时器与受击坐标按标量字段存档。
- 状态 `transitions` 数组按顺序决定优先级；每条转换的 `conditionMode` 是 `all`/`any`，条件通过 `{ "type": "threatWithinDistance", "parameters": { "distance": 12, "includePlayers": true, "threatTags": [] } }` 引用注册条件。逃离节点配置 `distance`（一次移动目标距离）与 `threatDistance`（持续检测威胁距离），退出条件的感知范围必须覆盖持续逃离范围。
- 鸟类保留助跑、起飞、耐力和降落阶段执行，但逃离距离判断与单次目标规划复用 `AIFleeUtility`；不要把鸟的空中位移替换成地面逃离节点。
- 玩家避让的触发距离与持续逃离安全距离必须分开配置；`Mod_ItemDetector.DetectionRadius` 和当前目标保留范围覆盖安全距离，否则动物会在还没退出逃离状态前丢失威胁。
- `AI_Wolf` 独狼避让以 `chaseTriggerDistance` 进入、以 `avoidSafeDistance` 退出；Detector 半径和当前威胁保留距离须覆盖后者，不能扩大双狼追击距离来延长独狼避让。
- 使用路径代价限制的追击必须消费 `RejectedByPathCost`，并通过 `AI_Base.TryHandleRejectedChasePath` 暂停同一目标后再重试；禁止让状态机继续停留在无可执行路线的追击状态。
- 使用 `AI_AttackController` 的动物，前摇、伤害窗口和后摇由控制器统一驱动；修改攻击时序时必须同步 Actor JSON、Prefab 回退值与 `Attack.anim` 的 `IsAttacking` 曲线，避免配置与可视/伤害帧错位。
- 攻击状态条件应先保留已经开始的 `IsAttackLocked` 攻击，再判断新攻击的冷却与起手距离；冷却中不能仅凭距离进入停车攻击节点，否则目标后退会造成反复切换，且 `OnExitAttackState` 重置冷却会进一步推迟下一击。近身起手距离与远处感知追击范围、实际伤害盒是三个独立概念。
- 可组合动物技能统一实现 `IAnimalCombatSkill` 并作为 Item Module 挂载；`AI_Base` 会自动收集到 `_animalSkills`，技能自行控制移动时状态节点必须使用 `CreateStateNode`，不能套用每帧停车的 `CreateStoppedActionStateNode`。
- 动物技能数值来自 `Assets/StreamingAssets/GameConfig/Skills/animal-skills.json`，Actor JSON 只声明模块和技能模板 ID；独立技能碰撞模块不要继承 `Mod_Damage`，否则会被 `AI_AttackController` 当作普通攻击窗口一起启停。

## AIECS 正式框架与阶段边界

- `Entities/AIECS/FlatWorld.AIECS.asmdef` 只承载 Core、Perception、Decision、Navigation、Combat；不引用 GamePlay、Item、Collider 或 MonoBehaviour。表现和早期轨迹原型在独立 `Presentation` 程序集，旧内容编译、玩家和死亡产物适配在独立 `Gameplay` 程序集。不能把旧原型的 Slot、运动轨迹、视觉水参数当正式身份、行为或环境状态。
- 正式世界的 AI 后端由 `AiRuntimeBackendService` 单向路由：`MonsterSpawnerManager` 持有生成时间、群系、光照、预算和种群规则，`AiecsEcologyRuntimeHost` 接管 Entity 创建、模拟、批量表现和增量计数；ECS 远距模拟由距离脉冲冻结，不做生成器定时销毁。GamePlay 不得反向引用 `FlatWorld.AIECS.Gameplay`；新增 ECS 接入能力继续通过 GamePlay 侧契约实现。
- F5 会卸载旧 Player 并隔帧重建本地主角；生态后端须在玩家卸载前解除旧外部代理，暂停无玩家期间的模拟，随后绑定新玩家，同时保留本世界已有 ECS 居民。
- 正式生态允许 GameObject AI 与 AIECS 并行：`SpawnerConfig.SpawnEntry.RuntimeBackend` 是物种后端的权威选择，默认 `GameObject`，只有显式 `Entities` 的大规模物种交给 AIECS。`AI_Base` 与独立旧实现只要被实例化就必须正常 Tick；生成、事件、Actor 掉落和数量统计必须按物种路由，不能用全局开关关闭全部 GameObject AI。同一物种在同一世界只能归属一个后端；AIECS 临时停用或不支持该物种时不得静默回退 GameObject，也不得在世界进入时全表清除旧实体。开发 `AiecsPlayground` 与正式生态宿主仍不得同时驱动两个 AIECS World。
- 旧 GameObject AI 的行为只通过 `ItemMgr -> Item.Tick -> Module.ModUpdate` 推进，包括动物状态机、鸟与幽灵；AI 本体及其附属表现组件不要自设 `Update/LateUpdate` 行为时钟。距离模拟档在 Item 调度器选择，鸟耐力条跟随 `AI_Bird.ApplyFlightPresentation` 刷新；生态生成器的全局维护 Tick 与单体 AI 行为分开。
- 正式生态目录中“单个 Actor 尚未迁移”属于预期能力边界：`PrepareWorld` 必须只记录一次普通诊断日志并从 AIECS 生成候选中排除，不能污染正常 `GameStartScene` 的 Warning 基线；只有后端未注册、目录完全无可运行 Actor、初始化异常等真正阻断正式 AIECS 的情况才使用 Error/Exception。
- 正常 `GameStartScene` 世界的 GM AIECS 分页通过正式 `AiecsEcologyRuntimeHost` 惰性取得一个空闲 `AiecsPlayground` 调试入口；空闲入口不得阻断正式生态，只有真正启动开发场景时才先同步释放正式模拟，清理开发场景后正式宿主再自动恢复，任何时刻禁止两套 AIECS World 同时推进。
- `AiecsSimulation` 持有独立 World 与批次资源，`AiecsDefinitionCompiler` 在冷路径读取当前合并 Actor/MOD 定义。生命、记忆、攻击阶段属于每实体运行态，定义、阵营矩阵与战略 Goal 共享；不得通过实例化旧 AI 获得模板，也不得用 P0 能力报告充当运行时配置。
- 正式 AIECS 的水深和单位流速从共享导航快照单向进入 `AiecsFlowAgent`：水深决定主动减速，JSON 按 Actor ID 配置的推动速度再乘水流，待机也参与漂流；F5 重新发布配置时由 Bridge 一次性同步现有居民。禁止为每只 Entity 创建 `TileEffectReceiver` 或反向查询 `ChunkMgr`；水体是否需要绕行仍由导航高代价决定，而不是由水态表现硬阻挡。
- 原生感知直接从 ECS 位置、身份、体型、生命构建稀疏桶，桶键包含阵营以避免同阵营占满候选预算。锁定目标只做有效性与低频追击规则复核，失效才错峰搜桶；不可达目标按配置延迟重试。形状偏移和外部玩家缩放必须纳入粗筛扩张上限，循环桶去重与最近镜像必须一起使用。
- LOS 独立复制 TerrainCell 的 Blocking 和建筑占地，不能把导航不可走当作遮挡。移动前快照用于感知，移动后重建快照用于命中；所有 Native 借用必须进入依赖链，重建和释放前完成旧读取者。武器 Pulse 在模拟批次之外发生时须重新借用当前导航索引，不能跨 Update 缓存可能已被导航发布替换的 LOS 视图。
- 正式 `AiecsSimulation` 使用两套空间索引：`perceptionSpatial` 冻结移动前感知/决策快照，`combatSpatial` 沿移动 Job 依赖链异步建立移动后攻击/伤害快照；`AiecsSpatialIndex.Build` 只同步该索引上一轮读取者，当前 dependency 必须继续交给 Build Job，禁止重新复用单索引并在 Tick 中途 `Complete` 整条移动链。正式生态宿主 60Hz 基础时钟单帧最多执行 2 个 Tick、最多保留 3 个 Tick 时间债务，避免卡顿后形成追帧尖峰；低于 30 FPS 时实际 Tick 会进一步下降。
- 正式生态距离模拟使用 60Hz 基础时钟和可启停的每实体脉冲：玩法 Job 查询当前激活实体，表现/统计查询全部实体；30/10Hz 档按身份错峰，三级外冻结玩法时间戳，恢复时不集中补算。距离筛选使每帧激活数变化，空间、Crowd、接敌槽位数组须按容量复用并用当前数量切片；独立开发群体没有脉冲组件时仍使用原调用方传入的帧间隔。
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
