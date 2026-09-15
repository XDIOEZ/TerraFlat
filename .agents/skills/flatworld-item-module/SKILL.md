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

- 新建物品尚无正式美术、明确需要占位贴图时，优先复用 `Assets/6_Art/Generated/ItemPlaceholder/素材占位符.png`，禁止借用其他具体物品的贴图充当通用占位。JSON `visual.spriteAddress` 使用 `Assets/6_Art/Generated/ItemPlaceholder/素材占位符.png[素材占位符]`，资源标签为 `ItemSprite`。内容工坊图标留空时由 `ContentWorkshopRepository.ResolveItemIcon` 统一提供预览与保存图标；手选正式素材优先，不能将现有物品的加载错误静默改成占位图。

`ItemMaker/ItemMgr → ItemData → ItemMods → ModuleInit/Load → ItemMgr 分级 Tick → Save/Despawn/Pool`

- Module 明确选择 EveryFrame、FixedInterval 或 Disabled；增删模块、配置变化和池复用必须使调度缓存失效。
- 注册/注销、保存/销毁各执行一次；`PrepareForDespawn` 与 `OnDestroy` 不得被外部重复调用。
- 远程网络副本不进入本地 Tick、感知和存档索引。
- 感知后端在注册和 `Item.RuntimeStructureChanged` 边界选择：Actor 使用当前 `RuntimeItemDefinition` 的共享根级纯几何，旧对象才缓存 Collider Bridge；移动通知仅更新位置索引，不能重新扫描组件。注销必须移除后端映射，重建索引前完成并丢弃旧 Job；每次重新注册/结构变化递增代际以拒绝对象池复用前的结果。正式 Actor 的物理 Collider 尺寸不是运行时感知配置权威，新增动态体型应提供纯数据输入。
- 新模块同时检查脚本、ModuleData、模块/Item Prefab、Addressables 与 JSON 定义。
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
- 堆叠身份统一由 `ItemData` 判定，空与 null 特殊数据按现有规范处理。
- 模块 Prefab 的 `ModuleData.Name/ID` 可能未序列化；进入 `ItemMods`、`ModuleInit` 或网络更新前必须统一建立非空身份，禁止直接把空值写入字典。
- JSON 动态组合存在跨模块引用时实现 `IItemModuleDependencyBinder`；`Item` 会在全部模块进入 `ItemMods` 后、`ModuleInit/Load` 前统一绑定，依赖必须按唯一稳定 ID 解析并对缺失或重复直接报错。
- JSON 的 `modules.*.prefab` 是模块变体的唯一实例化地址；多个专用 Prefab 可以共用同一玩法 `ModuleData.ID`，`GameRes` 只能为唯一候选登记该 ID 的兼容别名，禁止按加载顺序静默覆盖。
- `ItemPicker` 不能只依赖 `OnTriggerEnter2D`：掉落/飞行或联机预约可能让物品先以不可拾取状态进入范围，状态恢复后应补偿检查，并限制为一次性请求以避免部分入包或网络请求重复执行。
- 掉落拾取时序由 `Mod_Droping` 的轨迹状态决定：必须先移除掉落模块，再把 `CanBePickedUp` 设为 true；拾取器不能只信任这个数据标志。
- 世界掉落物的水体浮沉只能把 `ItemStack.Weight / Volume` 当作玩法比值，不能直接按真实水密度把阈值写成 1.0；当前内容数据里木墙约 0.32、石墙约 0.67、铜/青铜/铁墙约 0.69/0.78/0.88，因此阈值应按这些已定义物品重新标定，而不是套物理单位常数。
- 世界散落物的水体浮沉/漂流统一由 `WorldItemWaterSystem` + `WorldItemWaterRuntime` 根据最终世界位置派生；`ItemMgr.InstantiateItem` 只登记延迟检查，抛掷/投射等入口在轨迹结束后交回该系统，禁止把水体逻辑重新塞回 `Mod_Droping` 或只覆盖丢弃路径。
- 水体运行态不写入库存 `ItemData/ModuleData`，也不能用 `CanBePickedUp=false` 表示“在水中”；拾取时无需再剔除临时水体模块，对象池复用由 `IItemPoolLifecycle` 清理运行态。世界散落物随水移动统一读取 `ChunkMgr.TryGetRuntimeWaterCurrent`，跨新版 ChunkView 时通过 `ItemWorldPlacement.TryAttachWorldModelTransientItem` 重绑临时归属。
- 入水真实转换的源物品若需要一次性表现，实现 `IWaterEntryTransformEffect`；`WorldItemWaterSystem` 会在 `DespawnItem` 前调用，表现对象必须自行脱离源物品，避免源物品同帧回收时把粒子一起清掉。
- Wrapped World 物理属于 `Physics2D/WrappedWorld/`，由 `WrappedWorldPhysicsAdapter` 订阅完整的 `RuntimeItemRegistered/RuntimeItemUnregistered` 注册链；不要改订阅仅覆盖 Instantiate 的网络生成事件。注入、加载、回池重绑和远程纯表现注销都必须经过该链，`ItemMgr` 不直接创建具体物理代理。
- `WrappedItemPhysicsAdapter` 的碰撞体角色筛选在注册、重绑或结构失效时完成并缓存，不在每次 `FixedUpdate` 扫描层级。模块装卸与 Item.Load 发布 `NotifyRuntimeStructureChanged`；业务直接增删 Collider 等组件后也须发布此通知，已有形状的尺寸变化由适配器的 shape hash 同步。嵌套 Item 的碰撞体只由最近 Item 根负责，发送器、手持物和纯表现对象不得借父 Item 被镜像。
- Physics2D 命中统一通过 `GameplayPhysics2D.ResolveComponent<T>` 解析源对象；通用 `ColliderSource2D` 标记不依赖 Item，Item 根下兄弟模块的查找仅留在 Gameplay 解析入口，不能把镜像识别成独立物品或库存对象。

## 验证

- 检查加载→Tick→保存→Despawn→复用后无旧状态、订阅、空间索引或调度残留。
- 生命周期/ModuleData 联动 Data Skill；网络状态联动 Networking；具体玩法只加载其领域 Skill。
- 默认不主动跑测试；需要时运行 `ItemModule.Smoke`。入口：`Assets/GameTest/ItemModule/ItemModuleSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
