---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的地图内容、Tilemap、地形规则、Biome、River、Structure、TileData、地图差量与保存、旧 Map 兼容生成器或地图资源。关键词：Map、TileData、ChunkGenerator_Land、MapSave、TerrainNoise。"
---

# FlatWorld 地图内容与生成

## 入口

- 地图内容：`Assets/5_Scripts/5-3_GamePlay/World/Map/`
- Chunk 加载与物品归属：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/`
- 地图数据与存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/`
- 地块配置唯一真源：`Assets/StreamingAssets/GameConfig/Tiles/tile-manifest.json` 及其显式分包；构建入口为 `World/Map/Definitions/`。`Assets/7_Tiles/` 保存 Unity 外观资源；`Assets/4_ScriptObjects/World/Tiles/` 的 `Tile_Block` SO 仅保留稳定 ID 和原 GUID，供旧群系/结构/Prefab 引用，不再保存数值、Behaviour 或 TileBase 配置。
- 自然物及洞穴矿脉规则的唯一真源是 `Assets/StreamingAssets/GameConfig/WorldGeneration/NaturalItems/natural-item-manifest.json` 及其分包；地表/洞穴 `ChunkGenerationProfile_*.asset` 只保存 `ecologyRuleIds`、`caveResourceRuleIds` 与全局倍率，矿脉 ID 顺序仍代表筛选优先级。新增规则先加入 JSON 分包和清单，再由 SO 引用 ID；缺失、重复或无效规则阻止资源发布。`BiomeData.TerrainConfig.ItemSpawn_NoSO` 属于旧生成链，不用于调整 WorldModel 生态数量。存档默认冻结首次使用时的生成 Profile；玩家可在存档管理页关闭“冻结世界生成规则”以显式跟随当前版本。关闭时只丢弃冻结 Profile，保留生态区块的删除 GUID、状态覆盖和恢复年份；重新开启后在下一次进入世界时冻结当时的当前配置。
- 自然物单次数量默认由 `itemCount` 固定；需要偏置随机时使用 `itemCountDistribution=1`。此时 `itemCount` 是上限，`itemCountMin` 是下限，`itemCountPeak` 是概率峰值，`itemCountQuadraticRadius` 控制二次曲线宽度；数量采样必须保持确定性，并进入生成指纹和冻结规则存档。
- 地表 `river.*` 和洞穴 `cave.river.*` 的生成参数唯一真源是 `Assets/StreamingAssets/GameConfig/WorldGeneration/Hydrology/river-generation.json`；Profile SO 不再保存同名参数，资源加载时合入快照。地形预览器里的河流参数修改只影响本次预览，要持久调整请编辑 JSON。
- 河流河床横截面材质由 `river.bedCenterTileId`、`river.bedEdgeTileId` 和 `river.bedCenterStrengthThreshold` 配置；边缘底材不是全河固定铺设，只在偏下游的局部沉积段启用，其他河段两侧回退到中央底材。沉积段范围参数继续放在 Hydrology JSON，湖泊使用 `terrain.riverbedTileId`。
- 草和可采集地表植被分别由 `ChunkGrassRenderer` 与 `ChunkGroundCoverRenderer` 批量绘制。JSON 生态规则继续生成确定性数据点；物品定义声明 `groundCover: true` 时跳过自然 Item 实例化，采集才生成普通 Item。选格和图层共用 `GroundCoverSystem`，采集持久化复用生态删除 GUID；不得用草层消费状态记录花朵，也不得在图层解绑时把生成点标记为已采集。
- 草层生成先受 `grass.minimumTemperature`、`grass.maximumTemperature`、`grass.minimumPrecipitation`、`grass.maximumHeight` 硬门槛限制，再由现有 moisture 公式调节密度；花朵等可采集植被继续直接用 NaturalItems JSON 的温度、降水、高度区间。
- 耐旱植物按沙漠群系与降水区间筛选，不能仅凭沙地 Tile 生成；可选 `maxRiverFloodplainStrength` 默认 1，设为 0 可排除河岸湿地。环境上下限必须同时进入快照、冻结存档及生态指纹，不能在表现层临时删植物。
- 生态伴生物的 `CompanionHostTag` 要求同格实际生成的宿主；`RequiredChunkTag` 与 `RequiredTagChunkRadius` 联合查询天然宿主标签，半径 0 只看本区块，半径 1 看含本区块的九宫格。邻区标签须用相同种子、Profile 和地形规则独立计算，不依赖区块加载顺序；同步生态快照、冻结存档与配置指纹。
- 洞穴可配置植物由 Cave SO 的 `ecologyRuleIds` 选择 JSON 规则；洞穴生成按“入口 → 配置植物 → 藤蔓/矿物”占格，出生安全区不生成配置植物。
- Chunk 运行时、生成调度和表现绑定改用 `flatworld-world-model`。
- 动物刷新的群系筛选和真实脚下地块筛选分别由 `SpawnerConfig.AllowedBiomeNames` 与 `AllowedGroundTileIds` 控制；石块 Blocking Tile 的遮挡检查不能代替 Ground Tile 过滤。

## 主链

`Tile/Biome/Structure 配置 → 地形生成规则 → ChunkTerrainData → 结构与自然物 → 差量存档 → WorldModel 表现`

## 边界

- 本 Skill 负责地图内容规则；WorldModel 负责 Chunk 生命周期、并发、租约和表现绑定。
- Tile 栈只通过 API 修改；静态 Blocking Tile 与动态建筑占地不要混用。
- Ground 永远保存真实底部地块，海洋、河流、湖泊、地下水的初始液体在生成阶段另外写入 `ChunkTerrainData`。修改地面不会自动改液体；抽水只走 `WorldLiquidSystem.TryPump/TrySet`，禁止抽水时生成底部或修改 Ground。平台通过 `TerrainSupportLayer` 遮断表面接触，底部液体仍保留。
- 世界液体来源统一由 `WorldLiquidSourceResolver` 读取权威 Liquid 层的深度与稳定 `LiquidId`，再解析 `LiquidDefinition`；不能按 Ground Tile、TerrainCellFlags、盐度或 Collider 猜身份。容器份数与世界液深是不同单位，不能未经规则换算直接互相扣减。液体接触直接使用 `WorldLiquidSourceTarget`，不继承 TileData，不注册地块 water 数据/行为或 MemoryPack Union。
- 河流生成把真实下游单位方向保存到 `riverFlowX/riverFlowY` 环境层；运行时水流玩法统一通过 `ChunkMgr.TryGetRuntimeWaterCurrent` 读取，禁止在物品、角色等消费方重复按邻格高度猜河道方向。海洋暂无独立洋流层时使用生成环境层的 `windX/windY` 作为表层漂移方向，正式水面也消费同一方向，湖泊保持静止。
- `heightDriven` 河网按稳定 Region 缓存低分辨率高度、下游和汇水量；每个 Chunk 只细化相关走廊与终端小湖。边界两侧必须从同一宏观图和世界坐标取样，固定 Seed 不能依赖 Chunk 加载顺序；河槽弯曲后须用局部切线写入 `riverFlowX/Y`；修改拓扑、细化或小湖规则需递增生成签名。
- 生成保持固定种子、稳定 BiomeId/顺序和统一噪声、气候、水文规则。
- 环绕地图极圈由 Surface Profile 的 `climate.polarBand.*` 控制；`halfWidth` 按地图高度的一半归一化，因此数值就是整条降温带占地图的比例（默认 0.25）。`position=0` 时上下边缘各占一半，不能把两边分别设为 25%。`celsius` 与雪地基础温度统一为 -10°C，避免极圈再次压低雪地温度；群系边界仍走共用温度过渡。
- GM 群系定位通过 `ChunkMgr.FindSurfaceBiomeAsync` 使用当前生效的 Profile、维度种子、拓扑和世界纪元，后台搜索找到后自动传送；不设时间/采样数量上限，有限世界逐级加密至逐格覆盖，无限世界持续扩圈。雪原复用正式区域计划，河流沿宏观河网找真实 River 格，不能用旧预览或地块材质猜身份。定位只复用已完成的河网，新计算独立取消且禁止预热，局部缓存有界；进度用不可变快照供 UI 读取，关闭面板/换世界取消，应用落点前核对玩家与世界身份。
- 世界生成里“数量、尺寸、区段长度”等需要常见值与少量惊喜长尾的随机量，优先使用固定种子驱动的二次峰值权重分布，不要默认用均匀分布；纯二元开关仍可保留显式概率。
- 修改算法时考虑生成签名、旧存档、联机指纹和 Wrapped 坐标。
- 雪不占用 Ground 地块身份：雪地群系底层按高度保留 `Tile_Grass` 或 `Tile_Stone`，天然积雪写入独立 `snow.depth` 环境层；深度固定为 0～1 的十档，每 0.1 为一层。冰仍是独立 Ground。
- 雪原分布由 Surface Profile 的 `biome.snow.regions.* / large.* / peak.*` 控制；先按稳定区域抽出现概率，再按 `largeRatio` 分流，半径使用峰值在区间中点的二次权重。小片积雪额外要求山顶海拔，大、小雪原都须满足实际温度与最终降水。区域判定必须覆盖 Burst 批次、单格出生、邻区生态、温度过渡及冰河，并按环绕坐标取最短距离；旧冻结 Profile 缺少 `regions.enabled` 时保留旧分布。
- 萝卜聚落由 `surface.forest.radish` 与 `surface.grassland.radish` 两条独立规则控制；全局调整时必须同步审计两条，`PatchChance` 控制聚落数量，`SpawnChance` 与 `PatchRadius` 控制聚落内部密度。
- 自然作物需要通用可种植基质时，用 Tile JSON 的 `naturalPlantable` 声明能力，并在生态规则启用 `requireNaturalPlantableGround`；不要把草地/泥土 ID 写死进单个作物。摄氏温度硬范围用 `requiredEnvironmentLayer=temperature.celsius` 配合 `minimumEnvironmentValue/maximumEnvironmentValue`。
- 泥炭只生成在草原一侧的石地交界带；`biome.peat.spawnChance` 按斑块区域控制整体出现概率，河流 floodplain 范围一律排除，禁止再用“潮湿低地/河岸”规则生成泥炭。
- 洞穴入口联动 `flatworld-dimension`，可走性联动 `flatworld-navigation`，差量联动 `flatworld-data-save`。
- 地块可提供环境动作与被动效果定义，但共享 `TileBlockBehaviour` 只保存规则；玩家长按、Tick、环境倍率等实例状态必须留在角色侧运行器。
- 可采挖地表通过 Tile JSON 的 `groundHarvest` 声明工具类别、等级、产物、距离、工具使用次数与挖后地表；铲子 `Mod_Shovel` 与镐类 `Mod_ResourceTool` 通过共用 `Mod_GroundHarvestToolBase` 从 `Item.OnAct` 调用 `GroundTileHarvestSystem`，逐次累积进度并保存到区块差量，完成一层时才创建掉落和提交地表。天然石地由 `naturalLayerCount/exhaustedTileId` 声明可采层数与不可破坏基底；`GroundTileLayerSystem` 单独记录深度，采挖与渐显表现共用下一层解析。不能按物品 ID/Tag 猜工具，也不能把铲地代码塞进 `Mod_Damage`。
- 后续玩家地形工具必须经 `GroundTileLayerSystem.TryApplyPlayerCover` 压栈覆盖前的完整地块、工作进度和草层；开采优先弹出覆盖层，不能消耗天然石层计数或用固定石地覆盖原草地。现有平台/地板仍使用独立 `TerrainSupportLayer`，不混入天然深度。
- 地块选中框只依据当前指针所指的已加载地格显示；采挖距离、占用和工具等级属于执行资格，拒绝时给出原因，不应让白框一起消失。
- `Ground` 地块替换不会自动清除独立的草层；采挖完成后要经 `RuntimeGrassClearing.Clear` 同步草层视觉和 `GrassDeltas`，不要只写新的 `TerrainCell`。
- 资源加载时由 JSON 构建 `RuntimeTileDefinition` 和每种定义自己的共享 Behaviour 集合；`type` 经 `TileBehaviourRegistry` 的显式工厂解析，禁止 CLR `$type` 或移动时反序列化。参数使用现有配置字段的 camelCase，私有 `[SerializeField]` 参数也须迁移；封装配置属性必须显式标记 `[JsonProperty]` 且可读写，不能放开所有运行时属性。未知字段和无效数值必须失败，不得静默忽略。
- `TileData.ID/Name` 由定义 ID 注入，位置和工作进度不进入 JSON；单格读取使用 `CreateTileData/Clone`，不得修改共享模板。液体配置只来自 LiquidDefinition.worldWater，水体降温读取当前 C# 规则。
- 地块承重是 Tile JSON 的 `loadCapacity`，由 `RuntimeTileDefinition` 按稳定 ID 查询，不写入每格 `TileData` 存档；世界液面承重来自液体定义的 `worldWater.loadCapacity`，地表覆盖会遮断液面并改用覆盖地块值。
- 编辑器通过 `TileDefinitionEditorCatalog` 解析 ID 壳并显式保存 JSON，不可恢复 SO 与 JSON 双写。`GameRes.GetTileBlock` 返回 `RuntimeTileDefinition`；原 Behaviour 类和生命周期方法继续使用。

- 自然植物恢复资格由 `INaturalRenewalPolicy` 记录到生态存档的 `RenewalYears`；只有生成成功才清除移除标记和补位计划。玩家种植不参加自然补位，建筑、耕地及平台所在格不补野生植物。

- 正式世界修改和差量存档始终通过 ChunkRuntime 的独立 Liquid 层；Ground 数字 ID 不承担任何液体身份。
- 权威液深仍是 0～1，玩法按十分位解释为 1～10 档；海洋 Ground 不再按液深决定底材，而直接读取生成 `height`：`terrain.oceanSandMinimumHeight`（当前 `0.498`）到 `SeaLevel` 为沙地浅海，低于该阈值为石地海底；海面以上沙滩继续由 `BeachLevel` 控制。湖泊继续使用河床规则，禁止再用 `LiquidDepth` 回改 Ground。
- 天然生成液体在写入 Chunk 前统一向上取整到 0.1 档位，原始深度只要大于 0 就至少写入 0.1；海洋、河流、湖泊、地下水与岩浆共用该量化规则，避免生成 0.001 等无法按玩法份数解释的液深。

## 验证

- 山顶热液湖由冻结的 `lake.lava.*` 参数和确定性区域山峰候选生成；`minRadius/maxRadius` 定义占绝大多数的常规尺寸，`rareMaxRadius` 只作为二次概率曲线的极低概率长尾上限，用于偶发超大岩浆湖，不能把中型湖变成主分布。主体外会按同一盆地种子生成少量确定性的浅小熔岩斑；候选缓存必须有界且按世界、拓扑和 Profile 隔离，批量区块与单格出生查询共用同一盆地。`lava.shore` 只表示自然生成岸带，人工倾倒不能凭空刷出矿产。
- 伴生矿产使用自然物 JSON 的 `requiredEnvironmentLayer / minimumEnvironmentValue`，约束同时参与规则快照、指纹和存档；缺失环境层时拒绝生成，不能回退成全图生成。新增液体身份后要重建资源会话，不把索引写进存档。
- 热液接触性质由 `WorldLiquidSettings` 配置，`waterContact=false` 不触发潮湿、饮水和入水降温；温度场读取当前未被平台遮挡的液体，抽干后撤销热量而不修改气候基温。接触伤害时钟由角色接收器独立保存和清除，客户端不得自行结算。
- `python Tools/Tests/LavaWorld/verify.py` 使用现有 Bee 编译参数和本机 .NET SDK，在忽略的 Temp 下检查生成分块、负坐标、环绕边界、禁用开关和内容引用，不启动或暂停 Unity；它不替代真实世界表现与联机验收。

- `WorldTopologyDomain` 是 `Shared/WorldTopology/FlatWorld.WorldTopology.asmdef` 中的纯数学坐标真源；Bounds 只负责配置/类型封装，Runtime 仍读取 SaveDataMgr，只允许主线程使用。禁止在 Map 或数学核心直接创建 Wrapped Physics Proxy。
- `WrappedTilemapPhysicsAdapter` 同时镜像旧 Map 和 `ChunkCollisionRenderer` 的边界 Collider；Chunk 的权威地址与阻挡规则仍取自 `ChunkTerrainData`，局部显示坐标不得写回 Chunk 地址。
- 旧 Map 的镜像 `TilemapDamageReceiver` 必须绑定真实 Map 与镜像 Tilemap，不能用源 Tilemap 坐标代替；新版 WorldModel 继续通过规范逻辑格子查询结算伤害，不能依赖某个客户端的 ChunkView Transform。

- 功能验收以实际游戏操作和可观察结果为准；不以冒烟、自动化测试或静态检查代替实际验收。
- 世界生成与持久化改动实际覆盖新建世界、抽干露底、区块往返和保存重进；保留用户当前试玩时，不自动停止游戏或排队运行测试。

## Skill 维护原则

- 实验动态流向通过 `ChunkMgr.TryGetExperimentalLiquidFlow` 查询，休眠/停用后归零；不能覆写天然 `riverFlow*` 或持久化流向。`TryGetRuntimeWaterCurrent` 优先消费实际动态流量，关闭实验后仍沿用既有天然水文行为。
- 显式挡水建筑/MOD 通过 `WorldLiquidFlowObstacles.Register/Unregister` 登记世界格；不能从 Collider 或所有导航占地推断挡水。水上平台不自动阻断底部液体。液体批次存档使用 `SaveDataMgr.RecordLiquidBatch` 更新内存差量，不在 Liquid Tick 写磁盘。

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
