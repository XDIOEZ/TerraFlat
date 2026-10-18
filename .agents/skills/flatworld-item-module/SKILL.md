---
name: flatworld-item-module
description: "FlatWorld 物品与模块的组合、生命周期、调度、对象池、定义装配及世界实体后端。"
---

# Item / Module

## 代码与内容

以下代码目录以 `Assets/5_Scripts/5-3_GamePlay/` 为根：

| 目录 | 入口与职责 |
| --- | --- |
| `Entities/Item/Core/` | `Item`、`Module`、`ItemMods`、`ItemMaker`：实例与模块生命周期 |
| `Entities/Item/Management/` | `ItemMgr*.cs`：注册、生成、玩家、感知、距离分档 |
| `Entities/Item/Definitions/` | `ItemDefinitionRuntime`、`ModuleJsonConfigurator`：定义装配、参数与资源刷新 |
| `Entities/DroppedItems/` | `DroppedItemService`、`DroppedItemRuntime*`：世界掉落 |
| `Entities/NaturalECS/` | `NaturalEntityEcsService*`、`NaturalEntityEcsProfileCompiler`：自然资源后端 |
| `World/Chunk/Management/` | `WorldItemWaterSystem`：完整 Item 的水体运行态 |

- 数据：`Assets/5_Scripts/5-1_Data/{ItemData/ItemData,ModData/ModuleData}.cs`。
- 内容：`Assets/StreamingAssets/GameConfig/Items/item-manifest.json`；Actor 使用独立目录但复用装配与池化。
- 编辑：`Assets/Editor/FlatWorld/ContentTools/ContentWorkshop/`。

## 身份与生命周期

`当前定义 → ItemData → 全部模块登记 → ModuleInit → 依赖绑定 → OnLoad → Tick/Save → OnUnload → Despawn/Pool`

- 能力来自 Prefab/JSON 的显式组合。`StableName` 是 Item 内唯一模块实例键；`ModuleId` 是可共享能力 ID；`PrefabId`/JSON `prefab` 是具体实现地址，不入实例存档。JSON 键决定 StableName，Prefab-only 回退确定性对象名；`CanonicalModuleId` 提供固定能力默认值，JSON 显式 ID 仍须符合依赖契约。
- 默认能力通过 `CanonicalModuleId` 声明，由统一绑定入口写入；不要在 `Awake` 改写空模板 ID，否则可能同时破坏来源身份和当前定义身份，造成 AI/转身模块生成失败。
- 内嵌模块节点改名且同能力有多个实现时，在 Prefab 显式填写与 JSON 槽位一致的 `StableName`，不要依赖不唯一的能力别名定位。
- 未绑定模块只在精确编译位置接受当前定义或冷编译记录的来源模板身份，允许共享 Prefab 默认槽位/能力转为具体定义身份；既不属于来源也不属于定义的 ID/槽位、已绑定的其他 Prefab 不可覆盖。独立 Prefab 必须同时命中根地址和编译组件位置。
- 编译、实例绑定及额外组件清退都遵循最近 Item 所有权，不能穿过子物品；独立模块 Prefab 不得包含 Item。资源目录预检直接复用编译计划定位目标组件，纯数据能力不查找 GameObject 模块。
- 跨模块依赖由 `IItemModuleDependencyBinder` 在配置完成后解析唯一 ID/接口。`ItemMods` 的只读视图通过 `AddMod/RemoveMod/BeginMutation` 更新结构版本和能力缓存；可变 ModuleData 不跨宿主共享。
- `Module.Load/Save/Unload` 为受控入口，子类实现 `OnLoad/OnSave/OnUnload`。实际加载态决定保存和 Tick；Enabled 是当前配置，运行切换用 `SetEnabled`。资源建立与清理分别属于 OnLoad/OnUnload，保存只捕获状态。
- `RuntimeGeneration` 每次 Load 更新，不持久化；战斗身份包含 UID、world/dimension 与该代际。感知注册有独立代际。注册同一实例幂等，不同实例 GUID 冲突报错；注销使用登记时身份。
- `BindData` 只绑定未加载、未注册外壳；世界物换定义由 `ReplaceRuntimeItem` 创建停用候选并提供失败恢复。独立模块拥有自身 Prefab 根，内嵌模块只拥有组件。
- `ItemMgr.PrepareItem` 复用现有外壳池，在停用候选中提前装配模块、配置和依赖，不分配世界 GUID 或调用 OnLoad；`CommitPreparedItem` 绑定最新数据后正式登记/加载，可保持停用供整批显示。句柄 Dispose 取消，资源实例或定义/重载版本变化使其失效，机器仍只走 MachineWorld。
- 池身份在 Item 序列化字段，`PooledItemMarker` 是运行时层级基线；JSON 装配完成后记录基线，卸载完成且层级匹配才可回池，资格与 saveData 独立。临时缓冲按调用借还，嵌套生成不共用同一缓冲。

## 调度与空间

- 覆写 `ModUpdate` 的模块显式选择 EveryFrame/FixedInterval/Disabled；启停、结构、配置、池复用使调度失效，事件能力状态切换用 `MarkModuleScheduleDirty`。投掷伤害可用 `SetIdleTickSuppressed`，手持武器保持完整更新。
- Tick 捕获注册令牌及 Item/Module 代际，回调后复核。重建保留未变模块时钟；休眠不补算。Profiler 标记为 `FlatWorld.Module.<类型名>`。
- Tick 峰值先读原始帧子标记；`FlatWorld.Hive.SpawnResident` 区分真实成员生成，不能把合法实例化峰当成调度频率问题。水生线路保留 0.2 米采样，只在单次调用内合并连续相同规范液体格，不跨调用缓存水态。
- 距离档按同场景最近玩家及循环最短距离限制 ItemMgr Tick，不提高模块自身频率。玩家、地图、手持物保持完整更新；Owner 不代表豁免。`ItemSimulationRangeIndex` 使用近档集合、到期堆及区域判档。
- 休眠重置时钟、停根刚体并通知 `ISimulationRangeAware`；重入先恢复原 simulated 值再重提目标。`NotifyRuntimeItemMoved` 更新空间索引与分档后发布移动事件。
- Actor 感知几何来自当前定义；Collider Bridge 仅服务旧对象。注册/结构变化选择后端并增代际，移动只更新位置；格访问以 `LastVisitedBatch` 去重。`ItemPhysicsProjection2D` 将总重量投影成质量，将速度/接触写入非持久化 PhysicsState。
- 掉落拾取/吸燃料用 `QueryItemCentersInCircleNonAlloc` 查附近中心空间桶；不要复用会扫描全世界感知倍率与几何的 Actor 感知查询，循环接缝仍按真实中心半径去重。
- `Mod_DiscardItem` 只在实际丢弃动作需要 UI 槽位时查询悬停，保留手部→快捷栏→悬停优先级和原长按时钟；`Mod_TileEffectReceiver` 的本格/四邻复用单轮寻址上下文，每次读取最新权威层，不缓存跨帧水态。
- `ItemData.transform`、索引与存档使用规范逻辑坐标；Transform/刚体可位于客户端镜像。跨周事件通过 `ReprojectRuntimeItemsToLocalAnchor` 重投影，根刚体由 `WrappedWorldPhysicsAdapter` 管理；Physics2D 命中经 `GameplayPhysics2D.ResolveComponent<T>` 解析源对象。远程副本不进本地 Tick、感知或存档索引。

## 定义、资源与状态

- Manifest 唯一发现内容；装配计划在 `RuntimeItemDefinition` 冷路径编译具体 Prefab、组件槽与参数。`shellPrefab` 引用通用目录，独立外壳用 `shellAddress`；`sourcePrefab` 只供编辑器与冗余排除，源资产删除后同步移除该字段。创建物品不回退同名 Prefab。
- ItemMgr 冷工厂只取 `GameRes.InstantiateItemShell` 原始外壳，用最终 ItemData 装配一次；公共 `InstantiatePrefab` 仍返回已配置实例。Load 按本 Item 的 StableName 单次建索引，排除子 Item 并拒绝重复槽位；回归见 `item_runtime/shell_final_configuration`。
- 非 Actor 定义须显式提供或继承正数 `weight/volume` 与明确的 `stackable`；冷载荷表现外壳也遵守此契约。
- `ModuleJsonConfigurator` 对 Item/Actor 参数采用严格契约；向量 JSON 显式写 x/y/z，Unity Vector 的计算属性不能交给 `JToken.FromObject`。显示名 gameName 属于具体定义；业务 id、翻译键和 `formerIds` 别名各自独立。
- `GameRes.ResourceAssets` 持有 Sprite/材质/动画/外壳句柄。Shell 使用 Sprite Pivot 排序，子装饰以根 SortingGroup 保持整体深度；材质池复用恢复配置值或外壳默认值。通用占位图为 `Assets/6_Art/Generated/Shared/ItemPlaceholder/素材占位符.png[素材占位符]`，标签 ItemSprite。
- 生物动画共用 `Entities/Item/Modules/Animation/Mod_AnimatorController`：`visualMode` 选择 Animator 或 SingleSprite，单图只驱动显式视觉子节点。移动优先级为远端显式状态→已加载且启用的 `IActorAnimationMotionSource`→Mover；来源报告主动移动及外观 `WorldOffset/RollDegrees`，被动位移不算迈步。攻击、跳跃调用 `PlayAttack(worldDirection)`、`PlayJump()`，动画不负责伤害或物理。
- 单图 `singleSpriteStates` 在具体外壳配置状态名与 Sprite，复用 `PlayAnimation/TryPlayAnimation` 切图；`TrySetSprite(Sprite)` 支持直接换图及跨图集 Sprite，不启动身体 Animator。空图/未知状态拒绝，复位、停用、卸载及切模式还原换图前的图片，回池不残留状态图。
- 单图动作通过独立父层组合，不改原视觉 localPose、Item 根或刚体；只暂停身体 Animator，附加视觉拒绝刚体/碰撞体，手持武器沿正式命中姿态链。切模式/卸载恢复父级与同级顺序并保留最新翻面；停用组件拒绝新动作，停用 GO 先归零表现层，正式卸载再解除。宿主 `OnDestroy` 通过非持久化 `IsBeingDestroyed` 标记只释放表现层引用，不向正在销毁的宿主重新挂节点；仅移除动画组件或正常回池仍恢复存活层级。回归归属 `item_runtime/single_sprite_animation`，独立规则不代表动作观感已验收。
- F5 `RefreshLiveConfiguration` 原位更新改变的显式参数和仍受定义控制的视觉，不替换 ItemData/模块集合或重新 Load；结构与删除参数后的默认值应用于新实例。`ApplyResourceConfiguration/OnResourcesReloaded` 保留进度并更新缓存/订阅。发布时清闲置池并禁用活跃旧实例回池。
- 恢复先重建当前定义，再按稳定模块名合并实例状态；嵌套二进制库存由持有模块逐槽 rebase。生产配置与进度分离，产物身份匹配保留时间、次数和初始化标记。制作耐久倍率进入实例堆叠身份。
- 材料热转化由 `ItemMatterRuntime` 与 `MaterialThermalProcessor` 按自身温度结算，热源只供热；`matter.transitions` 的 `inputAmount/remainderOutputItemId` 支持同堆整组与尾数多产物，全部产物和扣料共用原子事务。Wood 标签 500℃起每3份产1炭、余数每份1灰，木炭1000℃起1:1灰；转化继承真实温度，禁止跨槽拼木材组。
- 物质状态在 `ItemData.MatterState`，静态相变/反应在定义 `matter/reactions`。玩法分类使用 `ItemData.Tags`；战利品通过顶层 lootTableId 引用稳定物品 ID。`IResourceYieldModifier` 在资源源头聚合，每条产出链应用一次并统一随机取整。

- 燃烧由 Fuel 数据、Combustion 状态、FuelInteraction 交互与 ICombustionStateReceiver 表现组合；视觉尺寸/密度由 flameScale/flameHeightRatio/flameEmissionMultiplier 配置，attachFlameToAnchor 让主体火苗跟随燃烧点，followSourceRenderer/sourceRendererAnchorOffset 绑定真实贴图动画。火把底图为熄灭状态，熄灭清空粒子；氧气模块只管储备/缺氧并暴露 SupplyOxygen，环境/装备供给方负责真实条件与扣量。`requiredTool/minimumTier` 是采集专精加成参考，不是普通采矿资格。

## 世界后端

- 太空掉落由 SpaceSession 接管：SpaceDropView 只绑定冷载荷和 Sprite，不调用 Load/ModuleInit，不实例化原建筑或机器模块；拾取复用正式库存事务。成块 ShipWreck 只用一个无设备模块的 Item 保存组成，回收后才释放载荷。

- 单机掉落经 `DroppedItemService.Spawn/SpawnLoot`：passive 使用纯 C# 轻量数据和近镜头池化 Sprite；interactive 保留完整 Item。两种新生掉落都复制独立载荷并分配新 GUID，完整 Item 在注册前清零载荷 GUID，来源库存/手持物身份保持不变。需要模块飞行效果的物品使用 interactive 与 `IDroppedItemSpawnContextReceiver`。普通世界掉落缩放 0.5，精确落点由 destination 指定；联机沿用 Item 权威链。
- 世界散落物温湿推进共用 `WorldItemMatterRules`，普通环境温差每游戏秒缩小 1%，统一调用 `ThermalRuntime.AdvanceAmbient`；完整 Item 在注册/加载时建立时间基线，两种后端按逐件累计间隔分帧推进。高温液体接触仍以液体自身 `temperature/contactHeatingPerSecond` 固定传热，达到当前定义 `matter.ignitionTemperature` 才点燃；设备可显式传入专属速率，airExposure 只调普通环境下的干燥。已有 `Mod_Combustion` 时复用模块且不再扣材料堆数；两种后端共用材料火焰粒子。
- 完整散落物的层级资格按结构事件、模块版本、载荷引用与运行代际失效，不在反复复核时遍历组件树；物种登记、飞行和投射物附着仍读实时状态。轻量掉落每次同步 Tick 共用地形寻址，世界/代际改变立即重建，每次样本仍读权威层，入水转换、浮力和物质接触复用样本液体定义；不能跨燃烧回调缓存温度来源。
- 掉落物单件物质结算按 `DroppedItems.Matter.Environment/Combustion/Transition` 分段记录；约 1ms 的批次预算只能在件与件之间检查，单件环境查询必须走温度场单格入口，避免查询一格却重建整块。
- 完整 Item 的浮沉/漂流由 `WorldItemWaterSystem` 按最终位置派生，轻量掉落复用 `WorldItemWaterRules`。水体状态不入库存；轨迹结束后交回水系统，先移除 Mod_Droping 再允许拾取。河流加速按总重量，浮沉按单件千克/升：普通水门槛 1 kg/L，其他液体乘 `buoyancyThresholdMultiplier`（海水 1.5、岩浆 6），达到门槛下沉；该倍率不改变液体载荷重量。转换表现用 `IWaterEntryTransformEffect` 在回收前脱离源物。
- 完整散落物的风推动登记时缓存建筑与水态组件引用；水态由 `WorldItemWaterSystem.ActivateExistingItem` 更新引用，注销随物品回收。轻量掉落物与完整物品通过 `WorldMotionSystem.WindSamplingContext` 共用每批固定的质量范围和星球重力。
- 轻量掉落与稳定漂浮的完整 Item 保留 0.5s 水流推进；两种后端共用 `WorldItemWaterRules.DriftInterpolation`，只把显示偏移匀速收敛到已确认的逻辑位置。轻量表现仅给可见视图留插值态；完整 Item 偏移 Sprite 子节点并保留原局部位置，水线逐帧跟随。循环接缝用最短位移，离水、拾取和回池清理偏移，不改空间索引或存档坐标。
- 原生 AI 使用完整 Item。`entityRuntime: resource` 的树、矿点、作物按组合冷编译到共享 `WorldEntityRuntime`，不创建资源 Item 或运行 Module Tick；通用配置集合采用 Replace。能力扩展由 `ResourceEntityCapabilityRegistry` 注册，DLL 状态使用 context.SetState/GetState。
- 自然实体独立维护导航、空间交互与接触几何，命中经 `GameplayCombatBridge`。植物生命周期只由 `EntityPlantModuleSystem` 推进；区块解绑保存释放，死亡/一次收获才删来源。终端解绑转交快照所有权，仍运行的快照才克隆；句柄包含 World 代际。
- `NaturalEntityEcsService` 为天气/耐候、已种植土壤和导航占地维护独立输入索引，注册、卸载、恢复和配置刷新时增量更新；Profiler `NaturalEntityRuntime.FreezeNavigation/FreezeEnvironment/FreezeSoil` 分别记录耗时。
- `NaturalEntityRuntime.FreezeEnvironment` 性能摘要的 work 是输入自然物数，gauge 为最近定时批次的输入规模；Profiler 子段 `.Weather/.Temperature/.WriteInputs` 定位天气查询、温度采样和 ECS 写入（仅 Profiler 细分，不进入摘要计时）。`NaturalEntityRuntime.Environment.WeatherSamples/TemperatureSamples/MissingWeather/MissingTemperature/InputWrites` 按批累计查询尝试、缺失样本与实际组件写次数；GM“性能 DEBUG”统计开启才累计数量，慢摘要另行开启，Profiler 点始终可见。查询失败保留正式回退规则，同批只冻结服务引用，每件天气和温度仍实时读取。
- `FreezeEnvironment` 的一批资源共享 `TemperatureMgr.PointSamplingContext`，目标区块的相邻热源输入只准备一次；同步作用域结束即释放引用，原 0.25 秒间隔和距离规则保持不变。
- 自然物土壤灌溉沿用原九宫格顺序和水量事务；批量寻址先筛实际表层水格，干格及支撑面遮水不再构造完整 Tile 样本，变化实时读取。规则 `ecology/soil_liquid_prefilter` 覆盖抽干、支撑恢复和环绕。
- 自然物土壤输入与提交共享 `FarmlandSystem.SoilSamplingBatch` 的本轮寻址及可复用容器，九宫格灌溉和消费仍逐件同步执行。发布用 `NaturalEntitySimulation.BodyReadBatch` 拷贝只读值，组件写入、外部 `NotifyChanged` 和移除使对应值失效；不跨批保留。碰撞参数冷编译为 `NaturalEntitySpatialGeometry`，外观版本只在真实 Bounds 变化时重建空间桶。
- 自然物 `FreezeSoil` 的光照与水肥读取共用一次 `SoilSamplingBatch.TrySample`，后者通过 `TryReadSampled` 复用采样结果；只复用同轮地块寻址，光照、灌溉与水肥仍实时计算、同步提交。
- 自然资源表现按 Dirty/Event 更新，几何版本与外观版本独立；TreeSortingVisual 用根镜像 SortingGroup 与玩家混排，主体不重复 BRG。客户端不推进权威植物 Job，采集命令/增量同步有独立接入点。旧 `DroppedItems/Core` ECS 实现仅为 Editor 学习参考。
