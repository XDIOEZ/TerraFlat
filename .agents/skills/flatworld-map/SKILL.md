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
- `WorldGeneration/Hydrology/river-generation.json` 定义地表 `river.*` 与洞穴 `cave.river.*`；Profile 加载时合入快照，预览器编辑只作用于本次预览。
- `Structures/fixed-structures.json` 经 `FixedStructureCatalog` 展开布局；后端管理占地、能力与回滚，自动遗迹走独立 SO/确定性生成链。

主链：配置 → 地形/气候/水文 → `ChunkTerrainData` → 结构/生态 → 差量 → 表现。Chunk 生命周期由 WorldModel 管理。

生成热路径：河流每块冻结 `HydrologyOutputWindow`，宏观区域共享 `PrimaryUpstream`；湖泊按稀疏河格和湖盆范围合成。优化须保留环绕、外圈、盆地优先级和稳定取整，不能靠缩短降水影响距离提速。

## 地块与液体

- JSON 构建 `RuntimeTileDefinition` 和共享 Behaviour；`TileBehaviourRegistry` 按显式 `type` 工厂解析，未知字段/非法数值失败。`TileData.ID/Name` 来自定义，位置/进度属于实例，`CreateTileData/Clone` 产生单格副本。`TileDefinitionEditorCatalog` 保存 JSON，`GameRes.GetTileBlock` 返回运行时定义。
- Ground 是真实底材，Liquid 独立保存深度与身份。`WorldLiquidSystem.TryPump/TrySet` 改液体，地面替换不自动清水。`TerrainSupportLayer` 投影平台表面并遮断底部液体接触，底部液体仍存在。
- `WorldLiquidSourceResolver` 从 Liquid 层解析 `LiquidDefinition`；`WorldLiquidSourceTarget` 独立于 TileData。世界液体权威水量是 `int LiquidLevel` 0～10，一份液体等于一级；生成向上取整，正值至少一级。`GetLiquidDepth` 只由等级除以 10 派生表现比例。`TryPour` 成功只发布本格 `SurfaceLiquidAdded`，耕地按本格订阅吸水；取水派生规则等待端口事务成功 `PublishState` 后的 `SurfaceLiquidPumped`。
- 海底材质读取 `height`：`terrain.oceanSandMinimumHeight..SeaLevel` 为沙底，其下石底；沙滩由 `BeachLevel` 控制。河床中心/边缘材质来自 `river.bed*`，边缘底材只用于局部沉积段；湖泊读 `terrain.riverbedTileId`。
- `riverFlowX/Y` 保存真实河道下游切线，`ChunkMgr.TryGetRuntimeWaterCurrent` 是消费入口；湖面静止，海洋表层暂用 `windX/Y`。实验流向经 `TryGetExperimentalLiquidFlow` 查询，不覆写天然流向；显式挡水来源登记 `WorldLiquidFlowObstacles`，平台不自动挡水。
- 承重读取地块 `loadCapacity` 或液体 `worldWater.loadCapacity`，覆盖层优先于底部液面。共享 Behaviour 只保存规则，长按/Tick/接触时钟属于角色。

## 确定性气候与水文

- 固定种子、规范逻辑坐标、稳定排序和生成签名共同决定结果；正式批次、单格、邻区、预览和定位共用规则。`WorldTopologyDomain` 提供纯坐标数学，物理镜像属于表现层。
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
- `itemCountDistribution=1` 使用 `itemCountMin..itemCount` 的二次峰值数量；`itemCountPeak/itemCountQuadraticRadius` 控制分布。`distributionMode=2` 聚落按 `(1-d/r)^2` 衰减，中心来自全局种子网格。
- `CompanionHostTag` 要求同格实际宿主；`RequiredChunkTag/RequiredTagChunkRadius` 查确定性邻区标签，不依赖加载顺序。洞穴占格顺序为入口 → 配置植物 → 藤蔓/矿物，安全区不生植物。
- 草与可采植被分别由 `ChunkGrassRenderer/ChunkGroundCoverRenderer` 绘制；`groundCover:true` 保留确定性点，采集才创建 Item，持久化用生态删除 GUID。`GroundCoverSystem` 管理选格/图层，解绑不是采集。
- `Mod_GroundCoverHarvest` 组合使用能力：`requiredUses` 只累计同一地形实例/格子/植被身份上真正开始的挥动，换目标或卸载清零，完成后才出货/移除来源。剪刀 1 次得 2 份纤维，打制石器 4 次得 1 份；花朵均保留原物品。草资格须等于 `ChunkTerrainData.GrassPresent`，清草复用 `TryConsumeGrass`，不能将非零空格状态当作草。
- 采集的中间次数不发送角色台词，不借自言自语气泡显示操作进度。
- 草读取可自然种植底材、摄氏温度和高度资格，水下不生成；降水按 `grass.rainfallForFullDensity/dryDensityMultiplier` 连续调密度，不设骤然清空的降水门槛。草原花由群系、自然底材和稀疏分布筛选；耐旱植物按群系/降水筛选，`maxRiverFloodplainStrength` 可排湿地。自然作物用 `naturalPlantable/requireNaturalPlantableGround` 表达基质；伴生矿产用 `requiredEnvironmentLayer/minimumEnvironmentValue`，缺层拒绝。
- 草花规则回归：`Tools/Tests/GroundCover/verify.ps1` 编译生产代码，检查干湿密度、确定性、底材和摄氏资格；不证明游戏画面或原生 BRG 恢复效果。
- `INaturalRenewalPolicy` 保存 `RenewalYears`；成功补位后才清移除标记，玩家种植、建筑/耕地/平台不参加自然补位。萝卜分森林/草原两条规则；泥炭仅在草原侧石地交界且排除 floodplain。
- 山顶岩浆湖由 `lake.lava.*` 和确定性山峰候选生成；`rareMaxRadius` 是长尾上限，`lava.shore` 仅标天然岸带。接触属性来自 `WorldLiquidSettings`，热源读未被平台遮挡的实际液体。
- `groundHarvest` 声明工具/等级/产物/距离/使用次数/挖后地表。`Mod_GroundHarvestToolBase → GroundTileHarvestSystem` 累积并保存进度，完成一层才生成掉落/提交地面；`GroundTileLayerSystem` 保存天然层数和覆盖栈，覆盖栈含原地块、进度、草层，开采优先弹栈。
- 采挖 Ground 后 `RuntimeGrassClearing.Clear` 同步独立草层和差量；工具白框共用实际操作的只读校验，铲子优先积雪。平台/地板仍属于独立支撑层。
