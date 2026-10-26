---
name: flatworld-map
description: "FlatWorld 地块定义、确定性地形、气候水文、生态、结构及地表采挖。"
---

# 地图内容与生成

## 入口

代码根目录 `Assets/5_Scripts/`：

| 目录/文件 | 职责 |
| --- | --- |
| `5-3_GamePlay/World/Map/` | 地块、定义、地图数据；`Definitions/` 构建运行时地块目录 |
| `5-0_WorldModel/Generation/` | `DeterministicChunkGenerator.*`、`SurfaceBiomeClassifier`、`LargeFreshwaterLakeKernel` |
| `5-3_GamePlay/World/Planting/GroundTileLayerSystem.cs` | 天然深度、玩家覆盖层与恢复 |
| `5-3_GamePlay/World/Chunk/` | 生成结果接入、自然物归属与差量 |

内容根目录 `Assets/StreamingAssets/GameConfig/`：

- `Tiles/tile-manifest.json` 及分包是地块真源；`Assets/7_Tiles/` 是外观，`Assets/4_ScriptObjects/World/Tiles/` 的 SO 仅为稳定 ID/GUID 引用壳。
- `WorldGeneration/NaturalItems/natural-item-manifest.json` 定义自然物/矿脉；Profile SO 只选 `ecologyRuleIds/caveResourceRuleIds` 和倍率，矿脉 ID 顺序是优先级。
- `WorldGeneration/Hydrology/river-generation.json` 定义地表 `river.*` 与洞穴 `cave.river.*`；新增参数同步 `RiverGenerationConfigCatalog` 严格清单（包括 `river.estuary.*`），回归 `terrain/river_generation_config` 直接读取正式 JSON。Profile 加载时合入快照，预览器编辑只作用于本次预览。
- `Structures/fixed-structures.json` 经 `FixedStructureCatalog` 展开布局；后端管理占地、能力与回滚，自动遗迹走独立 SO/确定性生成链。
- 星体地表覆盖来自 `Space/solar-system.json`；月球只用 `Tile_MoonRegolith`（独立灰白 TileBase），自然物/矿物、洞口和遗迹关闭，环形山半径 3～50 格，坑沿仍用月壤。尺寸分布复用 `SpaceSurfaceTerrain`，不靠石地外圈表现坑沿。
- 大型矿石小行星用 `SurfaceProfile.SurfaceRadiusTiles` 固定有限环绕地表，矿物规则在 `NaturalItems/surface-asteroids.json`，复用正式 `Mine_*` 采矿与差量；`weather.enabled=0` 关闭自然天气，真空地表清空气湿度和风矢量。

主链：配置 → 地形/气候/水文 → `ChunkTerrainData` → 结构/生态 → 差量 → 表现。Chunk 生命周期由 WorldModel 管理。旧地图核心 Prefab、维度物品配置和物品管理器地图缓存已移除；遗迹编辑器安装器只维护目录/示例内容，不再挂旧生成器或提供强制生成开关。

生成热路径：河流每块冻结 `HydrologyOutputWindow`，宏观区域共享 `PrimaryUpstream`；湖泊按稀疏河格和湖盆范围合成。优化须保留环绕、外圈、盆地优先级和稳定取整，不能靠缩短降水影响距离提速。

河流慢生成先读 `[ChunkGenSlow]` 的 `river.local_refine.*`：centerline、render（含 channel_fill/floodplain）、terminal_lakes、height_compute（交叉子集），结合调用次数、候选/局部唯一格累计、最近输出 padding 和缓存命中数区分规模与真实采样；同一请求的邻区细化累计，父子耗时不能相加。Profiler 固定入口为 `FlatWorld.Generation.River.{LocalRefine,Centerline,Render,TerminalLakes}`。数字跟随现有请求诊断，后台只记数，由主线程统一输出；详见 `开发文档文件夹/07_网络存档与底层架构/关键性能DEBUG点位说明.md`。

## 地块与液体

- JSON 构建 `RuntimeTileDefinition` 和共享 Behaviour；`TileBehaviourRegistry` 按显式 `type` 工厂解析，未知字段/非法数值失败。`TileData.ID/Name` 来自定义，位置/进度属于实例，`CreateTileData/Clone` 产生单格副本。`TileDefinitionEditorCatalog` 保存 JSON，`GameRes.GetTileBlock` 返回运行时定义。
- Ground 是真实底材，Liquid 独立保存深度与身份。`WorldLiquidSystem.TryPump/TrySet` 改液体，地面替换不自动清水。`TerrainSupportLayer` 投影平台表面并遮断底部液体接触，底部液体仍存在。
- 天然水域底床写入 `terrain.surface.naturalDepthLevels`：海洋为初始水面的十档等级加 1（2～11 层），河湖/洞穴水量 1～50 份降低 2 层、51～100 份降低 3 层；冻结前生成，抽水/倒水不改底床。天然水面统一低于同地势岸边 1 层，抽干后露出真实底床；原始 `height`、玩家采挖与填土独立叠加。
- 海洋水深由 `OceanShoreDistanceBathymetryKernel` 按最近陆地的欧氏距离生成，`terrain.ocean.shelfWidthTiles=16` 保留 1～3 级浅海，随后 `slopeWidthTiles=4` 陡降到 10 级；距离跟随空间倍率。分块/单格/供湿与洋流共享线性欧氏距离变换，水深仍只截取陆架加陡坡范围。`OceanShoreCurrentKernel` 用平滑距离梯度生成 `ocean.currentX/Y` 带强度向量：陆架内侧朝岸0.45、外侧离岸最高1，`terrain.ocean.current.transitionWidthTiles=3` 在分界两侧经过零流，`searchDistanceTiles=32` 的24～32格渐变回远洋风场；两岸等距梯度保留幅度，避免中轴被放大为满流。回归 `terrain/ocean_shelf_bathymetry`、环境 `ocean_shore_current` 与 `Tools/Tests/OceanCurrent/verify.py`。
- `WorldLiquidSourceResolver` 从 Liquid 层解析 `LiquidDefinition`；`WorldLiquidSourceTarget` 独立于 TileData。世界液体权威水量是 `int LiquidLevel` 0～100 份，每份 10L、满格 1000L；生成沿原十档量化后乘 10，运行时允许最小一份。`GetLiquidDepth` 为真实份数除以 100，`ToVisualLevel/ToVisualDepth` 向上取十档，1～10 份均显示第一档、0 才干涸。`TryPour` 成功只发布本格 `SurfaceLiquidAdded`，耕地按本格订阅吸水；取水派生规则等待端口事务成功 `PublishState` 后的 `SurfaceLiquidPumped`。
- `WorldLiquidReplenishment` 订阅 `SurfaceLiquidPumped`，固定取水点及四邻涉及的最多 16 条邻边，双向按真实份数差的一半转移，直到邻边差不超过一份；供水格及共享抽水来源保留至少一份（10L），空格与浅水格均可补水。目标须为 `GetLiquidBaseline` 中原有同种水域，普通陆地、异种液体和显式挡水格不参与；不递归扩张、不凭空造水，复用 `TrySet` 的差量/表现通知，规则在 `SurfaceFluids/replenishment.lua`。
- 地表倒水/取水事件只在订阅变化时生成监听器快照，派发时不调用 `GetInvocationList`；补水邻边用 `ListPool` 借还，通知中的新增/退订影响下一次派发，异常仍逐监听器隔离。回归 `surface_fluids/notification_dispatch`。
- 海底材质读取 `height`：`terrain.oceanSandMinimumHeight..SeaLevel` 为沙底，其下石底；沙滩由 `BeachLevel` 控制。河床中心/边缘材质来自 `river.bed*`，边缘底材只用于局部沉积段；湖泊读 `terrain.riverbedTileId`。
- `riverFlowX/Y` 保存真实河道下游切线，`ChunkMgr.TryGetRuntimeWaterCurrent` 是消费入口；湖面静止，普通海洋读取 `ocean.currentX/Y`。入海口独立生成 `estuary.currentX/currentY/influence` 扇面，默认延伸18格、半角35°（`river.estuary.length/halfAngleDegrees`），仅覆盖连通海水，不改水量、盐水身份或淡水降水源。核心延续下游，两侧发散，外缘按转角衔接海流以免正反速度抵消；运行时、冷区块和海面表现共用 `WaterEnvironmentRules` 并保留弱流幅度。回归为环境 Lua 的 `estuary_current` 与 `Tools/Tests/EstuaryCurrent/verify.py`。实验流向经 `TryGetExperimentalLiquidFlow` 查询，不覆写天然流向；显式挡水来源登记 `WorldLiquidFlowObstacles`，平台不自动挡水。
- 承重读取地块 `loadCapacity` 或液体 `worldWater.loadCapacity`，覆盖层优先于底部液面。共享 Behaviour 只保存规则，长按/Tick/接触时钟属于角色。

- `Mod_TileEffectReceiver` 在单轮脚下/四邻液体接触查询时先筛真实水量及平台遮断，干格不构建完整 `RuntimeTerrainTileSample`；入水、岸边行为仍实时结算，旧 Map 回退不变。回归 `ecology/soil_liquid_prefilter`。

## 确定性气候与水文

- 固定种子、规范逻辑坐标、稳定排序和生成签名共同决定结果；正式批次、单格、邻区、预览和定位共用规则。`WorldTopologyDomain` 提供纯坐标数学，物理镜像属于表现层。海洋水源/第一层降水缓存使用纯 Profile 的 `ClimateFingerprint`：临时输出尺寸、生态/矿脉与门户配对不拆分缓存，全部数字/文本参数和真实气候格网尺寸仍使缓存失效；世界纪元、维度、种子、拓扑和节点坐标继续独立入键。
- `Precipitation` 按冻结的地形区块和水源距离、地理温度算两层长期降水：第一层只看海洋，`Runoff/MacroHydrology` 恢复稀疏河源、下坡汇流，`LargeFreshwaterLakeKernel` 恢复独立内陆湖岸/岛屿。第二层加入实际天然河湖有水区块，只提升最终降水，不重建河网。默认海洋 32 区块、河湖 8 区块，冷暖 -10～30℃，降水 0.04～0.9；`climate.distance.*` 可调，相邻区块 SmoothStep 过渡。
- 河源每 3×3 宏观格组稳定选一处；海洋第一层降水扣渗透后决定水量，湖候选保留至少一半基础机会。`lake.large.*` 管独立湖泊，不做蒸发干湖；天然终点小湖由 `river.lake*` 控制，默认机会 0。主河/支流共享汇合切线和流量过渡；计算窗口、追踪预算不冒充天然终点。水文外圈覆盖河湖降水影响及区块插值，只提交核心地形。
- `SurfaceBiomeClassifier.Resolve/ResolveRule` 按 priority、稳定编号、规则名匹配高度/摄氏温度/降水/湿度，失败读 `biome.fallback.id`。`groundTileId/wetGroundTileId/wetGroundMinimumMoisture` 控制干湿底材；沙漠还要求最低摄氏温度。
- `biome.<规则名>.spawnChance` 在环境条件之外控制出现概率，默认 1；`biome.regions.size` 控制平滑区域尺度。独立种子的区域场校正为均匀概率，单格/区块/定位共用并支持环绕；减少沙漠概率无需改变降水门槛或仙人掌密度。
- 地理气温由纬度底温、局部噪声、海拔修正合成；`climate.temperature.blendRadius` 以固定求和顺序平滑，归一化温度从同值派生。季节/天气只进入运行时温度。
- `climate.polarBand.halfWidth` 是整条冷带占比，`position=0` 在上下边缘各占一半；冷带到温带按 `transitionTiles` 过渡。极圈边界与赤道底温使用固定种子控制点和平滑插值，共用环绕端点；`climate.equator.widthRatio` 是赤道两侧总占比。
- 天然水体在最终 `temperature.celsius<0` 时写冰 Ground 并清 Liquid，保留水文/群系身份，岩浆除外。雪是独立 `snow.depth`；极圈资格由 `biome.snow.polar.*` 决定，极圈外另读 `biome.snow.regions/large/peak.*` 并要求零下与降水条件。天然雪十档，极圈层数 `ceil(1+clamp((-10-T)/20,0,1)*9)`。
- `PlanetData.SpatialDistanceScale` 默认 1、范围 0.25～4：空间距离同倍率、面积同平方倍率、噪声频率反算；固定步数、气候阈值、概率、液深及实体/Tile/Chunk 物理尺寸不缩放。
- `ChunkMgr.FindSurfaceBiomeAsync` 使用当前 Profile、种子、拓扑、纪元；有限世界逐级加密，无限世界扩圈。关闭面板/换世界取消，传送前核对世界与玩家；河流/雪区读正式身份。

## 生态与采挖

- Profile 首次使用时默认冻结；关闭冻结只丢 Profile，保留删除 GUID、覆盖与 `RenewalYears`，重开后下次进入世界冻结当前配置。快照/生态指纹包含数量、环境边界与依赖标签。
- 洞穴散矿读取基础生态阶段实际矿山：距离任意矿山至少 5 格、来源最多 12 格（固定地图格数）；`cave.resource.looseDensity/looseStoneDensity` 默认互斥 2% 对应矿石、5% 石头，石头矿山不贡献额外矿石概率。邻块基础生态通过有界缓存共享，来源坐标保存在散矿 RuleId，不能用只支持同块宿主显示的 `HostGuid`。
- 洞穴河湖岸泥土由 `cave.dirtGround.enabled/tileId/chance` 控制，只替换开放干格的 Ground（默认 Profile 为 `Tile_Dirt` 29），不写 Blocking、不提高导航代价；普通岩壁仍独立生成。
- 自然物数量和聚落分布以规则配置与正式生成器为准；聚落中心来自全局种子网格。
- 自然物数量、岩浆湖半径、河床边缘沉积及聚落距离档位共用 `DiscreteProbability` 与 `generation.*` 概率表；Profile 创建时冻结最终概率到数字参数，保存/派生窗口继续沿用，生成与生态指纹均覆盖概率。聚落用11个归一化距离档的二项概率表读取生成倍率；原有地形、范围和总生成率规则仍由各系统负责。
- `CompanionHostTag` 要求同格实际宿主；`RequiredChunkTag/RequiredTagChunkRadius` 查确定性邻区标签，不依赖加载顺序。洞穴占格顺序为入口 → 配置植物 → 藤蔓/矿物，安全区不生植物。
- 草与可采植被分别由 `ChunkGrassRenderer/ChunkGroundCoverRenderer` 绘制；`groundCover:true` 保留确定性点，采集才创建 Item，持久化用生态删除 GUID。`GroundCoverSystem` 管理选格/图层，解绑不是采集。
- 草外观在 `ChunkView.spriteVariants` 选切片：前 24 项为基础草，其后按 `accentVariantChance` 点缀；跨源图统一目标 PPU 与中心锚点并共享 Sprite。目前使用 `Low_Grass/Grass.png` 和 `GrassAndFlower.png` 的简约小草、小花，不用 `GroundGrass_Atlas`。
- `Mod_GroundCoverHarvest` 组合使用能力：`requiredUses` 只累计同一地形实例/格子/植被身份上真正开始的挥动，换目标或卸载清零，完成后才出货/移除来源。剪刀 1 次得 2 份纤维，打制石器 4 次得 1 份；花朵均保留原物品。草资格须等于 `ChunkTerrainData.GrassPresent`，清草复用 `TryConsumeGrass`，不能将非零空格状态当作草。
- 采集的中间次数不发送角色台词，不借自言自语气泡显示操作进度。
- 草读取可自然种植底材、摄氏温度和高度资格，水下不生成；低于 `grass.coldThresholdCelsius`（默认 15℃）时概率再乘 `grass.coldDensityMultiplier`（默认 0.5），恰好阈值不减量；降水按 `grass.rainfallForFullDensity/dryDensityMultiplier` 连续调密度，不设骤然清空的降水门槛。低温与积雪、降水倍率相乘，不改稳定随机选格。草原花由群系、自然底材和稀疏分布筛选；耐旱植物按群系/降水筛选，`maxRiverFloodplainStrength` 可排湿地。自然作物用 `naturalPlantable/requireNaturalPlantableGround` 表达基质；伴生矿产用 `requiredEnvironmentLayer/minimumEnvironmentValue`，缺层拒绝。
- 草花规则回归：`Tools/Tests/GroundCover/verify.ps1` 编译生产代码，检查干湿密度、确定性、底材和摄氏资格；不证明游戏画面或原生 BRG 恢复效果。
- `INaturalRenewalPolicy` 保存 `RenewalYears`；成功补位后才清移除标记，玩家种植、建筑/耕地/平台不参加自然补位。萝卜分森林/草原两条规则；泥炭仅在草原侧石地交界且排除 floodplain。
- 草原/森林普通矿山由 `surface-common.json` 的煤、铜、铁、金、锡、硫规则共享（`biomeMask=48`），有效概率为对应石地矿山的 10%；石头矿山用各群系原规则保持同一比例，零散石头单独配置。新增规则须登记 Surface Profile 的 `ecologyRuleIds`。
- 巨型矿山由 `NaturalItems/giant-mines.json` 配置十一矿种、地表八条/洞穴九条规则，总候选概率预算 0.13；同一 Profile 共享单一间距网格并互斥抽矿种，生态条件只查锚点，完整生成净空仍排除水、障碍、结构和洞口安全区；生成净空可大于真实碰撞与导航占格。
- 山顶岩浆湖由 `lake.lava.*` 和确定性山峰候选生成；`rareMaxRadius` 是长尾上限，`lava.shore` 仅标天然岸带。接触属性来自 `WorldLiquidSettings`，热源读未被平台遮挡的实际液体。
- `groundHarvest` 声明工具/等级/产物/距离/使用次数/挖后地表。`Mod_GroundHarvestToolBase → GroundTileHarvestSystem` 累积并保存进度，完成一层才生成掉落/提交地面；`GroundTileLayerSystem` 保存天然层数和覆盖栈，覆盖栈含原地块、进度、草层，开采优先弹栈。
- 采挖 Ground 后 `RuntimeGrassClearing.Clear` 同步独立草层和差量；工具白框共用实际操作的只读校验，铲子优先积雪。平台/地板仍属于独立支撑层。
- 每采掉天然一层都更新 `TerrainSurfaceHeightLayer` 的表面深度，独立于五层石头产量；快照/回滚/存档同时保存 `ExcavatedSurfaceLevels`。地块 JSON 的 `minimumSurfaceDepth` 声明露出底材的最低深度，基岩为 5；地板与普通地面齐平，高低阴影统一交 `TerrainHeightShadowManager`。
