# 掉落、水态与自然实体后端

## 掉落与物质推进

- 掉落显示的 `Resolve/ResolveSprite` 支持显式 `GameRes`，未提供才借现有资源；空载荷/贴图与资源未就绪明确拒绝，不创建单例或留下半成品。世界渲染登记者直接订阅物品销毁，注销/回池撤订阅，不依赖清理时的全局单例身份；规则 `rendering/item_sorting_configuration`、`unified_manager`。
- `WorldPresentationProjection.Synchronize` 与 Awake 共用幂等材质参数块初始化，停用父节点下可先同步、激活不覆盖数据；规则 `rendering/cold_projection_initialization`。编辑态物品销毁夹具必须实际接收 Unity 生命周期回调，不能把普通 MonoBehaviour 不执行回调误判为运行时回收失败。

- `Mod_BaseDroper.StaticDropItem_Pos` 是旧入口的唯一装配流程，先交 `DroppedItemService.ScheduleLegacyDrop`，拒绝后才用完整 Item 轨迹；`Mod_Droping` 共用入口及曲线/本机投影计算，归属、旋转和落水生命周期仍由各调用方维护。
- 单机 DroppedItemService.Spawn/SpawnLoot：passive 为纯C#载荷+近镜头池Sprite，interactive 为完整 Item，需飞行模块效果用后者+IDroppedItemSpawnContextReceiver。两者复制独立载荷/新GUID，完整Item注册前清载荷GUID，不改来源身份。普通掉落缩放0.5、destination定精确点；联机仍Item权威链。
- 太空 SpaceSession 接管，SpaceDropView 仅冷载荷/Sprite，不 Load/ModuleInit 或实例化原建筑设备。ShipWreck 一个无设备模块Item存组成，回收后才释载荷；拾取走正式库存事务。
- 两后端共 WorldItemMatterRules/ThermalRuntime.AdvanceAmbient，完整Item注册/加载建立基线、逐件累计间隔分帧。高温液体接触读物质 temperature/contactHeatingPerSecond，到 matter.ignitionTemperature 才点燃，设备可显式速率；airExposure 仅普通干燥。已有 Mod_Combustion 复用且不再扣材料堆，两后端共火焰粒子。
- 完整散物结构/燃烧引用依结构事件、模块版本、载荷引用、运行代际失效；同件末尾可用已验结构更新火焰，帧表现再验。启停/燃烧/登记/飞行/附着实时读。轻量单Tick共地形寻址，世界/代际变重建，每样本权威，入水/浮力/接触共液体定义，不跨燃烧回调存温度源。
- 完整散物的每帧火焰表现只遍历实际拥有火焰的条目（`ItemMgr.LateUpdate.WorldMatterFlames`），火焰创建入索引、停燃/销毁/卸载移除、退出世界清空；回调中集合可变，表现用池化独立快照避免嵌套遍历损坏。抛物线模块身份随结构版本缓存，但 `DropInProgress` 仍即时读取 drop/waterFloating/waterSinking；规则 `item_runtime/world_matter_structure` 保留掉落轨迹及模块移除边界。
- Matter.Environment/Combustion/Transition 分段记时，约1ms批预算只在件间检查，单件环境用温度场单格入口。

## 水与风

- 完整Item由 WorldItemWaterSystem 按最终位置推水态，轻量共 WorldItemWaterRules；水态不进库存，轨迹完先移 Mod_Droping 再准拾。河流加速按总重，浮沉按单件kg/L：普通水阈1，乘 buoyancyThresholdMultiplier（海水1.5/岩浆6），达阈沉，倍率不改液体重量。IWaterEntryTransformEffect 回收前脱源。
- 轻量水态/流查询同Tick共 RuntimeTerrainTileSample，仅省寻址，水深/流向/实验流实时。完整散物登记缓存建筑/水态引用，ActivateExistingItem 更新水态、注销回收；两后端 WindSamplingContext 每批共质量范围/星球重力。
- 完整散物的水流/风/传送带单次推进复用 `RuntimeTerrainPresenceQuery`，逐步仍查当前液深、遮挡及区块代际；运动拓扑从 `PressureExplosionQueue.TryGetTopology` 按原星球规则单次冻结，不混用当前维度地形的拓扑。缺刚体/水态组件用 `TryGetComponent`，避免 Editor 生成缺组件诊断分配；规则 `mechanical/loose_item_terrain_batch`。
- 轻量/稳定浮Item保留0.5s逻辑漂流；DriftInterpolation 仅显示匀速追已确认位置。轻量只可见视图存插值；完整仅偏 Sprite并保局部基准，水线逐帧跟。环绕最短位移，离水/拾取/回池清偏移，不改索引/存档。
- 完全下沉后转河底静止态，只隐藏表现、停水流/风推动，保留实物和拾取索引；必须走到同格才可摸回。轻量 WaterKind=3 并释放视图、退出漂流批次，地形事件负责干格恢复；完整 Item 用 forceRenderingOff 保留碰撞，以 `flatworld.worldItemWater` 保存沉底状态，入包候选清除此标记。活动/后台/存档共用轻量水态恢复，禁止沉底完成时删除载荷。回归 `surface_fluids/bottom_recovery`、`processing/cold_motion_workspace`。

## 自然实体

- 原生AI用完整Item；entityRuntime: resource 的树/矿/作物冷编译进共享 WorldEntityRuntime，无资源Item/Module Tick。通用配置集合 Replace，ResourceEntityCapabilityRegistry 扩能力，DLL用 context.SetState/GetState。
- 显式 `physics.mode` 由世界服务分配代理；独立可动资源的原生结果回写原 Entity、区域、导航、空间桶、表现和保存运动状态。`IndependentNaturalEntityRegistry` 按来源身份保存，区块只借当前位置表现；跨块不换主体，出生块解绑不回收已移走资源。重载先识别已存来源再判断当前模式，保存后改为固定模式也只恢复一个实体。
- 自然资源出生身份由 `SourceWorldKey + SourceChunkX/Y + Guid` 确定，当前世界与当前位置区块分别归属；目标世界只认本地出生身份。`WorldFixed` 和 `IndependentDynamic` 经 `TryTransferNaturalEntityToWorld` 迁移：两端需独立 PhysicsScene，目标世界、目标位置的表现与区块物理后端先准备；事务暂停源、准备目标，失败恢复同一 handle/proxy，成功才提交源删除，不触发死亡或掉落。
- 迁移事务期间源与候选目标均 Busy：拒绝采集、伤害、刷新、公开 Remove/TryTakeSnapshotAndRemove 和普通源删除重入；允许捕获快照，最终删除走迁移专用提交入口。源删除通知逐订阅者隔离异常。休眠前发送最后完整权威快照；客户端保存权威速度但不预测。
- 网络采集与生成资源状态回包携带出生身份；缺少身份且 UID/位置匹配多个实体时必须拒绝歧义请求。F5 只延迟当前实例的模式切换，其他有效配置仍刷新。
- 产物目录首次注册校验，同配置复用，失败不缓存；资源重载清编译缓存，新配置/能力版本重编后重验。
- 独立导航/空间交互/接触几何，命中 GameplayCombatBridge；植物仅 EntityPlantModuleSystem 推。区块解绑保存释放、死亡/一次收获才删来源；终端解绑移快照所有权，仍运行才克隆，句柄带World代际。客户端不推进权威植物Job，采集/增量独立接入；旧 DroppedItems/Core ECS 仅Editor参考。
- 普通、分帧与 ECS 自然物快照统一用 `ItemInstanceDataFactory.CloneRuntime`，共享只读配置并独立复制模块/物质/姿态/运动；内存快照重入也合并当前定义与嵌套库存，避免 F5 后沿用旧配置。规则 `ecology/natural_snapshot_copy`，离线 `Tools/Tests/NaturalSnapshots/`。
- NaturalEntityEcsService 的天气/耐候、已种土壤、导航占地索引随注册/卸载/恢复/刷新增量维护。FreezeEnvironment 同批只冻服务引用/PointSamplingContext，邻热源每目标块一次，天气/温度逐件实时、缺样本沿正式回退，同步结束释放，保原0.25秒/距离规则。
- FreezeEnvironment 摘要 work 是输入数、gauge最近批规模；Profiler .Weather/.Temperature/.WriteInputs 分段；Environment.WeatherSamples/TemperatureSamples/MissingWeather/MissingTemperature/InputWrites 区分尝试/缺失/写入，计数仅GM“性能DEBUG”开时，慢摘要独立，Profiler始终可见。
- 土壤九宫格顺序/水量事务不变，先筛真实表层水，干格/遮水不建完整Tile；SoilSamplingBatch 只复用本轮寻址/容器，TrySample/TryReadSampled 共采样，光/灌溉/水肥逐件实时算/同步提交。
- 发布 BodyReadBatch 拷只读值，组件写/NotifyChanged/移除使对应值失效、不跨批；目标≤32且少于世界1/4时 BeginBodyReadBatch(requestedIds) 仅冻所需，否则ECS Chunk整批。几何冷编 NaturalEntitySpatialGeometry，真Bounds变才重建空间桶。
- 空间查询覆盖超过16384格时遍历已激活实体并按环绕包围盒粗筛，再由调用方精筛；预算不能让大范围查询静默变空。真实小行星采矿以96米观察和生成Guid核对防回归。
- 表现按Dirty/Event，几何/外观版本独立；TreeSortingVisual 用根镜像SortingGroup与玩家混排，主体不重复BRG。

回归：`item_runtime/world_matter_structure`、`environment/water_current_reuse`、`ecology/{output_validation_cache,soil_liquid_prefilter,selective_body_read_batch}`。Profiler入口：NaturalEntityRuntime.FreezeNavigation/FreezeEnvironment/FreezeSoil。
