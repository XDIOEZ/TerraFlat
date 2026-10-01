---
name: flatworld-item-module
description: "Use when: 定位或修改 FlatWorld 的 Item/Module 组合架构、实体创建销毁、对象池、模块加载保存、Tick 调度、运行时注册、空间索引或网络模块序列化。关键词：ItemMgr、Item、Module、ItemMods、ItemMaker。"
---

# FlatWorld Item / Module

## 入口

- 生命周期：`Assets/5_Scripts/5-3_GamePlay/Entities/Item/{Item,Module,ItemMods,ItemMaker}.cs`
- 管理器：`Entities/Item/Management/ItemMgr*.cs`（Spawning/Perception/Players/RandomDrop partial）
- 数据：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`
- 本体定义：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`
- 模板化物品编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`
- Actor 定义复用 Item/Module 实例化与池化：`Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs`
- 资源产出倍率契约与聚合：`Entities/Item/Modules/World/ResourceYieldUtility.cs`；温度修饰：`Mod_TemperatureYield.cs`。

## 主链与不变量

- 定义实体装配存档模块之前先建立通用装备组件；不能依赖稍后的 `Item.Load`，也不能把模块能力 ID 当作具体 Prefab 地址。

- `Item.RuntimeGeneration` 是每次 Load 更新的运行态代际，不进存档；纯数据战斗 Bridge 将其与持久 UID、world/dimension 一起构成外部身份。对象池复用、读档重载后即使 UID 相同，也不能接收上一代事件；它与 ItemMgr 感知索引自身的注册代际不是同一个生命周期。

- 新建物品尚无正式美术、明确需要占位贴图时，优先复用 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png`，禁止借用其他具体物品的贴图充当通用占位。JSON `visual.spriteAddress` 使用 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，资源标签为 `ItemSprite`。内容工坊图标留空时由 `ContentWorkshopRepository.ResolveItemIcon` 统一提供预览与保存图标；手选正式素材优先，不能将现有物品的加载错误静默改成占位图。

`ItemMaker/ItemMgr → ItemData → ItemMods → ModuleInit/Load → ItemMgr 分级 Tick → Save/Despawn/Pool`

- `ModuleData` 的权威身份字段是 `StableName / ModuleId / Enabled`：`StableName` 是单个 Item 内唯一实例键，`ModuleId` 是可一对多复用的能力 ID，`PrefabId` 只描述当前定义选用的具体实现且不进入实例存档。禁止再生成随机模块名；重复 StableName 必须直接报错。
- 会覆写 `ModUpdate` 的模块必须显式选择 EveryFrame、FixedInterval 或 Disabled；`Module.TickMode` 默认 Unspecified，新模块不得依赖隐式 EveryFrame。增删模块、启停、配置变化和池复用必须使调度缓存失效。
- JSON `enabled` 统一写入 `ModuleData.Enabled`；运行中切换必须走 `Module.SetEnabled`，由框架负责 Load/Unload 与 Tick 参与资格，禁止各模块各自维护第二套启用状态。
- 距离模拟档只限制 `ItemMgr` 驱动的玩法 Tick 频率，不改变模块自身更慢的 FixedInterval；以同场景最近玩家和循环世界最短距离判定，玩家、地图、手持物保持完整更新。`Owner` 不能作为免降频条件，因为在飞投射物也会保留发射者引用。范围外暂停时重置调度时钟，停用根刚体并回调 `ISimulationRangeAware` 模块释放导航运行态；重入时先恢复原 `Rigidbody2D.simulated` 值再重提目标，不能补算休眠期间的 Tick。对象池或模块卸载也须恢复刚体开关；不要把摄像机缩放当模拟距离。
- 世界内 F5 经 `ItemDefinitionRuntime.RefreshLiveConfiguration` 只更新现有模块的已改变显式参数，以及仍由原定义控制的 Sprite/材质；不替换 ItemData、模块集合或调用 Load。外壳/模块结构变化与删除参数后的 Prefab 默认值由后续新实例应用，不能把旧实例伪装为已完整迁移。
- 模块通过具名 `ApplyResourceConfiguration` 保留配置对象内部的运行态，通过 `OnResourcesReloaded` 更新派生缓存；依赖 JSON 能力字段的事件订阅也须在该回调中按当前配置解绑、重绑，不能只在 `Load` 订阅。禁止用重新 Load 代替配置刷新。生产模块更换规则列表时按产物身份保留累计时间、次数与初始化标记。
- 原位更新发布时清空闲置物品池，并把现有活跃实例的 `PooledItemMarker.PoolingDisabled` 置为 true；只清闲置池会让旧外壳稍后回池，再污染新定义实例。
- 对象池身份由 `Item` 的序列化字段持有，`PooledItemMarker` 是纯运行时层级快照，不再作为 MonoBehaviour 动态添加到每个新物品；装配 JSON 模块后才抓取层级基线。改回池逻辑时须同时检查脚本重载后的身份保留与回池前的层级校验。
- 注册/注销、保存/销毁各执行一次；`PrepareForDespawn` 与 `OnDestroy` 不得被外部重复调用。
- Item 回池资格独立于 `saveData`；JSON 模块装配完成后才记录层级基线，回池时按子节点身份核验。模块若用 `OnDestroy` 清理订阅或资源，必须在 `Unload` 提供同等清理，池复用才安全。
- 模块 JSON 配置计划属于当前 `RuntimeItemDefinition`：解析和严格校验只做一次，实体每次 Load 仍重新应用字段；资源重载通过替换定义实例自然丢弃旧计划。
- 远程网络副本不进入本地 Tick、感知和存档索引。
- 感知后端在注册和 `Item.RuntimeStructureChanged` 边界选择：Actor 使用当前 `RuntimeItemDefinition` 的共享根级纯几何，旧对象才缓存 Collider Bridge；移动通知仅更新位置索引，不能重新扫描组件。注销必须移除后端映射，重建索引前完成并丢弃旧 Job；每次重新注册/结构变化递增代际以拒绝对象池复用前的结果。正式 Actor 的物理 Collider 尺寸不是运行时感知配置权威，新增动态体型应提供纯数据输入。`ItemPhysicsProjection2D` 只把 `ItemData.Stack.CurrentWeight` 映射成动态刚体质量，并将实际速度与接触事实写回不入存档的 `ItemData.PhysicsState`。
- 感知批次对已存在空间格的重复访问用格子内 `LastVisitedBatch` 访问戳去重，禁止恢复每批 `HashSet<long>` 已访问集合；格子回池时必须清空成员并重置访问戳。
- `ItemMgr.NotifyRuntimeItemMoved` 完成位置索引刷新后发布通用 `RuntimeItemMoved` 适配事件；移动订阅方只维护显式声明需要跟随的状态，并按网格根格变化去重，避免轮询或给普通 Item 增加每帧扫描。
- 新模块同时检查脚本、ModuleData、模块/Item Prefab、Addressables 与 JSON 定义。
- 游戏内容分类（武器类别、生物种类、阵营语义、资源类型等）统一使用 `ItemData.Tags`，以便 JSON/MOD 扩展；Unity Tag 只用于 `MainCamera`、`MapCore`、UI/编辑器辅助等场景与开发基础设施，玩法判定不得依赖 Unity Tag。
- `Module.Load()` 与 `Module.Save()` 均为抽象方法；无持久化运行态的模块也需显式实现空 `Save()`，说明状态由宿主或配置恢复。
- 遇到“物品找不到模块 Prefab”时先核对 `[GameRes] Prefab 加载计划` 和失败阶段；通用 Prefab 数量为 0 时先查标签、目录与初始化，不能直接断言某个物品定义错误。
- JSON 本体按职责组合通用模块；单个资源节点的名称和玩法配置不能成为专用模块 Prefab。周期资源应由生产模块写入库存接收契约，再由采集模块处理交互和掉落。
- `MineResource_Base` 只提供矿点外壳与基础数据，不会自动附加采矿门槛；每个具体矿物资源节点都必须显式组合 `Mod_ResourceHarvest`，并配置有效 `requiredTool/minimumTier`，否则会退化为普通可受伤世界物。
- 通用世界实体外壳只能提供 `Item`、表现节点与查询 Collider；作物等玩法必须由 JSON 组合模块。成熟交互的可扩展副作用通过 `ICropHarvestAction` 注册，权威状态模块只负责按顺序调度动作与结束实体生命周期。
- Prefab 必须由 Unity 序列化生成，禁止手写根对象 `fileID: 100100000`；该值是 Prefab 资产保留 ID，把它分配给 GameObject 会触发 `GameObject to Prefab` 的 PPtr 转换错误。
- 批量调整 Prefab override 后不得在 `m_Modification.m_Modifications` 序列中留下空项；Unity 会把对应 `PrefabInstance` 判为损坏并在导入时删除整个嵌套模块，修改后必须重新导入并核对实际层级。
- Item 与 Actor 的 `modules.*.parameters` 共用 `ModuleJsonConfigurator` 严格契约；删除或改名可配置字段后必须同步现行 JSON，并运行“FlatWorld/内容配置/校验全部本体内容”，禁止等到具体实例生成时才发现漂移。
- 运行时生成模块参数时，`Vector2/Vector3` 必须显式写成 `x/y/z` 的 `JObject`；禁止 `JToken.FromObject(UnityEngine.Vector*)`，否则 Json.NET 会遍历 `normalized` 等计算属性并形成自引用。
- JSON 定义实体的死亡战利品由顶层 `lootTableId` 引用全局 `GameConfig/LootTables/loot-tables.json`；表内 `itemId` 是稳定 ItemDefinition ID，运行时才展开为 `LootPrefabName`，禁止再内联 `Data.LootTable` 或保存 `LootPrefab` 对象引用。
- 产出修饰统一实现 `IResourceYieldModifier`，由 `ResourceYieldUtility.GetMultiplier(资源实体, 产物ID)` 聚合有效模块；外部采集设备应传资源源头而非自身。每条产出链只在最终数量或累计生产进度中应用一次，整数掉落与难度倍率合并后统一随机取整，禁止重复放大或改写基础战利品表。
- Manifest 是唯一发现入口；包的最终 `shellPrefab` 必须与声明一致。启动只异步解析一次 Item Manifest，Prefab 排除计划和物品构建共用该结果，Android 不得退回另一套发现规则。
- 本体 Item/Actor 的 Sprite、材质、动画和独立外壳请求由 `GameRes.ResourceAssets` 持有；新增加载分支不能丢失句柄所有权。`shellPrefab` 引用通用目录，独立加载只接受 `shellAddress`，禁止从编辑器 `sourcePrefab` 推导运行时外壳。
- 每个具体定义必须独立填写默认显示名 `gameName`；`id` 与名称翻译键分别承担业务引用和界面查询职责。继承不能把父物品的名称或翻译键带入子物品，编辑器也不能把 GameObject 名和 `ItemData.ToString()` 写成显示名与说明。
- 物品身份合并用当前定义的 `formerIds` 声明旧查找名，别名不继承、不进入物品枚举；查询统一走 `TryGetItemDefinition`，存档重建生成当前 ID。别名字典与正式目录一起隔离、重载和清理，不能把已合并的旧定义作为退役内容重新放回目录。
- JSON 通用 Item Shell 的 SpriteRenderer 必须使用 `SpriteSortPoint.Pivot`；运行时换图也要重新写入该值，透明排序锚点以 Sprite 导入 Pivot 为唯一权威。
- 通用 `Module_Production` Prefab 不能内置 Apple 等具体产物；具体产物由物品 JSON 或专用 Prefab override 明确配置。`ProductionList` 的产物、数量、周期等属于当前定义；读取模块存档时只恢复 `ProductionTime`、`CurrentProductionCount`、`IsInitialized` 等运行时进度，禁止让 BitData 整体覆盖当前配置，否则内容改动后会继续生产历史产物。
- 世界物品若由主体 SpriteRenderer + 子提示/装饰 SpriteRenderer 组成，子层级需要 `sortingOrder` 偏移时必须用根 `SortingGroup` 把整件物品作为一个 Y 深度单元；禁止让子 Renderer 的正偏移直接跨过角色等外部实体的世界排序。
- 世界物品可用 `visual.materialAddress` 声明共享 Addressable 材质；运行时定义必须在对象池复用时显式恢复“配置材质或外壳默认材质”，避免共用 Shell 把上一个物品的材质带给下一个实例。
- `GameRes.CreateItemData`、群系生成与生产模块只接受 Manifest 中存在的 JSON 物品 ID；缺失定义必须直接报错，禁止回退到同名 Prefab。`sourcePrefab` 用于编辑器迁移定位与通用 Prefab 目录的冗余排除，不得作为运行时加载依赖。
- 既有具体 Prefab 改为 JSON 共用 Shell 后，只要源 Prefab 仍保留在 Addressables 中，就必须在具体定义填写匹配其 Address 的 `sourcePrefab`；否则旧 Prefab 名称会先占用物品 ID，使 `RegisterItemDefinition` 报别名冲突。修正发现配置，禁止放宽冲突校验或静默覆盖注册。
- 具体 Prefab 删除后，JSON 必须同步移除 `sourcePrefab`，迁移器则把这类无源定义登记为手工保留项；否则再次执行全量迁移会误删权威 JSON。
- 内容工坊创建物品时只写继承差异：父定义和参考模块必须来自启用分包，Sprite 先生成稳定 Addressables 地址，JSON 写入前校验继承、重复 ID、文件指纹与分包外壳边界。
- `RuntimeItemDefinition.IsActor` 只表示复用通用管线；Actor 还必须登记到 `GameRes.ActorDefinitions` 且外壳包含 `IAIActor`。
- 存档恢复不能把历史 `ItemData/ModuleDataDic` 当配置真源；必须先按当前 `RuntimeItemDefinition` 重建静态数据和模块集合，再恢复匹配稳定模块名的运行态。这样 F5 资源重载或版本更新后的 JSON 配置会覆盖旧档配置，已删除模块也不会被旧档复活。
- `Ex_ModData_MemoryPackable` 若序列化了带 `ItemData` 的内嵌 `Inventory_Data`，`ItemDefinitionRuntime` 不会自动遍历这段二进制负载；模块读取状态后应逐槽调用 `RebasePersistedData(GameRes.Instance, itemData)`，再绑定库存 UI，确保内嵌物品按当前定义恢复。
- 制作材料赋予的实例耐久使用 `ItemData.CraftedDurabilityMultiplier` 持久化；定义重建后以当前定义的基础耐久重新应用倍率，不能直接沿用旧 `MaxDurability`。堆叠身份必须包含该倍率，避免不同品质实例合并后丢失品质。
- 物品连续物质状态统一保存在 `ItemData.MatterState`，静态参数与相变写在 `ItemDefinition.matter`，多物料条件反应写在 `ItemDefinition.reactions`。反应不要求固定归属物，可声明在任一参与物品；运行时按规范化反应签名去重，设备不得复制具体材料反应表。
- 液体粘度属于 `LiquidDefinition.viscosity` 的数据属性，1 表示普通水；容器倾倒速度和液流/液面表现都从同一值派生，UI Prefab 不再作为粘度权威。未配置专用 `visualState` 样式的液体按 `primaryColor` 自动生成容器液面表现。
- 堆叠身份统一由 `ItemData` 判定，空与 null 特殊数据按现有规范处理。
- 模块 Prefab 的 `StableName/ModuleId` 可能未序列化；进入 `ItemMods`、`ModuleInit` 或网络更新前必须统一建立非空确定性身份。JSON 模块以 `modules` 的键作为 StableName；Prefab-only 模块才允许回退到确定性的 GameObject 名，禁止随机后缀。
- JSON 动态组合存在跨模块引用时实现 `IItemModuleDependencyBinder`；`Item` 会在全部模块进入 `ItemMods` 后、`ModuleInit/Load` 前统一绑定，依赖必须按唯一稳定 ID 解析并对缺失或重复直接报错。
- 可燃物品采用纯组合：`Mod_Fuel` 提供燃料数据，`Mod_Combustion` 提供燃烧状态与世界时间消耗，`Mod_FuelInteraction` 提供通用投料/点火交互；光源、燃烧粒子、局部温度、命中 Buff 等通过 `ICombustionStateReceiver` 独立响应。具体物品名称、外观与组件选择只存在于 JSON，禁止新增 `Mod_具体物品名` 来重新聚合这些职责。
- JSON 的 `modules.*.prefab` 是模块变体的唯一实例化地址；多个专用 Prefab 可以共用同一玩法 `ModuleData.ID`，`GameRes` 只能为唯一候选登记该 ID 的兼容别名，禁止按加载顺序静默覆盖。
- `Mod_ItemPicker` 不能只依赖 `OnTriggerEnter2D`：掉落/飞行或联机预约可能让物品先以不可拾取状态进入范围，状态恢复后应补偿检查，并限制为一次性请求以避免部分入包或网络请求重复执行。
- 掉落拾取时序由 `Mod_Droping` 的轨迹状态决定：必须先移除掉落模块，再把 `CanBePickedUp` 设为 true；拾取器不能只信任这个数据标志。
- 世界掉落物的水体浮沉只能把 `ItemStack.Weight / Volume` 当作玩法比值，不能直接按真实水密度把阈值写成 1.0；当前内容数据里木墙约 0.32、石墙约 0.67、铜/青铜/铁墙约 0.69/0.78/0.88，因此阈值应按这些已定义物品重新标定，而不是套物理单位常数。
- 世界散落物的水体浮沉/漂流统一由 `WorldItemWaterSystem` + `WorldItemWaterRuntime` 根据最终世界位置派生；`ItemMgr.InstantiateItem` 只登记延迟检查，抛掷/投射等入口在轨迹结束后交回该系统，禁止把水体逻辑重新塞回 `Mod_Droping` 或只覆盖丢弃路径。
- 水体运行态不写入库存 `ItemData/ModuleData`，也不能用 `CanBePickedUp=false` 表示“在水中”；拾取时无需再剔除临时水体模块，对象池复用由 `IItemPoolLifecycle` 清理运行态。世界散落物随水移动统一读取 `ChunkMgr.TryGetRuntimeWaterCurrent`，跨新版 ChunkView 时通过 `ItemWorldPlacement.TryAttachWorldModelTransientItem` 重绑临时归属。
- 入水真实转换的源物品若需要一次性表现，实现 `IWaterEntryTransformEffect`；`WorldItemWaterSystem` 会在 `DespawnItem` 前调用，表现对象必须自行脱离源物品，避免源物品同帧回收时把粒子一起清掉。
- Wrapped World 的 Item 只保留真实根刚体适配：`WrappedWorldPhysicsAdapter` 经完整的 `RuntimeItemRegistered/RuntimeItemUnregistered` 链管理 `WrappedRigidbody2DAdapter`；禁止恢复逐 Item 的接缝 Collider Proxy 或为镜像对象新增 `Update/FixedUpdate`。
- 世界 Item 的 `ItemData.transform.position`、存档与空间索引使用规范逻辑坐标；`Transform/Rigidbody2D` 可以位于当前客户端的局部世界镜像。玩家跨周时只在事件边界由 `ItemMgr.ReprojectRuntimeItemsToLocalAnchor` 批量重选镜像，连续运动期间不得逐帧把表现坐标强制归一化。
- Physics2D 命中统一通过 `GameplayPhysics2D.ResolveComponent<T>` 解析源对象；通用 `ColliderSource2D` 标记不依赖 Item，Item 根下兄弟模块的查找仅留在 Gameplay 解析入口，不能把镜像识别成独立物品或库存对象。

## 验证

- 单机世界掉落统一经 `DroppedItemService.Spawn/SpawnLoot`。定义的 `worldDropBehavior` 默认为 `passive`：进入普通 C# 轻量模拟器，只保留位置、数量、短期运动、水体与拾取数据；表现仅在镜头附近从对象池取一个无 Update/Collider/Rigidbody/Item/Module 的 SpriteRenderer GameObject。`interactive` 则始终保留完整 Item，继续运行受伤、死亡掉落、燃烧、水容器等世界交互模块。
- 当前所有 AI（含 Zombie）恢复完整 GameObject/Item 模块主控。保留的可选 ECS AI 与静态资源共用 `WorldEntityRuntime`，但关闭 ECS AI 不影响资源 ECS；不可因共享底座再次强制迁移原生 AI。新增 ECS 领域能力不能另建 World，诊断隔离 World 例外。
- 声明 `entityRuntime: "resource"` 的树、矿点和作物经 `NaturalEntityEcsProfileCompiler` 按模块组合编译，缺失能力必须报错，禁止按距离、联机状态或物种名回退完整 Item。`NaturalEntityEcsService` 持有同一 World 内的实体句柄；`ItemData/ModuleData` 仅用于定义与冷存档，不调用资源 Item.Load 或 Module Tick。
- ECS 模块冷编译统一通过 `DeserializeConfiguration<T>` 以 `ObjectCreationHandling.Replace` 读取配置；显式集合必须替换构造默认集合，缺省字段仍保留默认值，禁止把成长阶段、采集提示点等追加到默认列表后再放宽校验。
- 植物共用 `EntityGrowth/EntityClimate/AiecsVital`，带 `EntityPlantLifecycle` 的实体只由 `EntityPlantModuleSystem` 推进，普通通用能力查询必须排除它，避免成长和耐候重复结算；资源库存与周期生产分别使用 `EntityResourceStock/EntityStockProduction`。树冠的 `EntityCanopyFruitModule` 为托管组件，复用纯 C# 事件时间线，不代表所有树果结算已经 Burst 化。
- 静态资源的配置刷新以当前定义为准，已保存树龄、冷热负担和实例生命保留；天然初始化只执行一次。历史耐候读取冻结季节历史，当前局部热源不能延伸到过去。自然物句柄包含 World 代际，过期 Chunk 回调不能命中新世界复用的整数 ID。
- 资源实体持续维护纯数据导航、矩形接触阻挡和空间交互；玩家武器通过 `GameplayCombatBridge` 直接提交命中。区块解绑只保存释放，不触发采集或死亡；真实死亡/一次性收获才提交来源删除，天然续生和耕地作物快照分开管理。不得恢复近端 Item 接管；接触物理允许独立 Box 代理，由 Chunk 的静态碰撞容器统一管理，禁止给资源创建 Item/Module/逐实体更新回调。
- 自然物 `VisualVersion` 不等于阻挡形状版本：果实、受击和外观变化仍刷新空间索引，只有 `BlocksMovement` 或 `BodyBounds` 改变才发布物理变更；移动/缩放需通知旧、新范围，World 重置先清理旧代理再消费新注册。
- 资源 BRG 使用运行时实体 ID 与部件号分配负槽位，不能用格子或自然 GUID 覆盖树身、果实、阴影。静态自然物表现由 Dirty/Event 队列驱动，成长版本、采集、受击、树冠变化和环世界重投影才触发重提；禁止在 `Present` 中每帧全量遍历所有资源。接触阴影几何只在实体几何/绑定变化时重建，昼夜透明度走批次级材质参数。源材质缓存键必须区分机械与资源 Shader 变体；逻辑坐标进入存档，局部镜像变换只用于表现。渲染卸载必须清除全部部件。
- 带 `Tag.Tree` 的资源主体与附属果实使用 `NaturalEntityEcsService.TreeSortingVisual` 纯视觉桥，以树根本地镜像为 `SortingGroup` 锚点，读取 `world-item` 排序键与玩家混排。不得同时保留主体 BRG 实例；太阳/接触阴影继续合批。视觉外壳只含 Transform、SortingGroup、SpriteRenderer，不恢复 Item、Module、Collider 或逐树 Update，解绑与换世界必须一并释放。
- 资源客户端不推进权威植物 Job；客户端采集命令及资源状态增量同步仍需独立接入，不能把服务端实体迁移当成联机链已经完成，也不能回退旧 GameObject 来掩盖缺口。蜂巢、传送门等未声明资源后端的独立玩法不在植物后端中静默模拟。
- 旧 Item 返回型扩展只有在定义为 `passive` 时才通过 `ScheduleLegacyDrop` 在本轮模块更新完成后移交；`interactive` 不得被转换，避免在 Item.Load 的模块栈内丢失玩法能力。联机仍使用已有 Item 权威链。
- 轻量掉落与完整 Item 的浮沉数值统一读取 `WorldItemWaterRules`；达到有效阈值即下沉，液体倍率只改变浮力阈值，水线、时长和水花曲线保持同一来源。
- `Entities/DroppedItems/Core` 的旧 ECS 掉落实现保留为 Editor-only 学习参考，不参与正式游戏运行；菜单 `FlatWorld/诊断/学习参考/验证旧掉落物 ECS` 只验证这份参考实现。

- 检查加载→Tick→保存→Despawn→复用后无旧状态、订阅、空间索引或调度残留。
- 生命周期/ModuleData 联动 Data Skill；网络状态联动 Networking；具体玩法只加载其领域 Skill。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
