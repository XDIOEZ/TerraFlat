---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的世界地图、Chunk 流送、Tilemap、程序化生成、Biome、River、Structure、TileData、地图保存或区块 Prefab。关键词：ChunkMgr、Chunk、Map、ChunkGenerator、MapSave。"
argument-hint: "地图、区块或生成问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 地图与 Chunk 系统定位

> 最后核对：2026-07-31。导航节点更新请同时加载 `flatworld-navigation`。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.cs`：区块字典、加载队列、激活窗口、对象池。
2. `Assets/5_Scripts/5-3_GamePlay/Chunk/Chunk.cs`：区块实例、地图与运行时 Item 生命周期。
3. `Assets/5_Scripts/5-3_GamePlay/Map/Base/Map.cs`：Tilemap 加载、程序化生成流水线、就绪状态与导航脏区衔接。
4. `Assets/5_Scripts/5-3_GamePlay/Map/Base/ChunkGeneratorBase.cs`：生成器抽象。
5. 涉及地下矿洞或跨星球地图时读取 `flatworld-dimension`；维度配置入口为 `DimensionManager.ConfigureMap()`。

## 地图主链

```text
Mod_ChunkLoader / NetworkChunkStreamingCoordinator
→ ChunkMgr 请求区块
→ Chunk 获取 MapSave 或创建新区块
→ Map.GenerateByPipeline
→ Land / River / SpawnItems / Structures
→ Data_TileMap + TileData
→ Chunk Ready
```

## 关键文件

- 联机扩展：`Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.Networking.cs`。
- 玩家区块加载器：`Assets/5_Scripts/5-3_GamePlay/Chunk/Mod_ChunkLoader.cs`。
- Item 区块归属：`Assets/5_Scripts/5-3_GamePlay/Chunk/Mod_ItemChunkAssigner.cs`。
- 生成上下文：`Assets/5_Scripts/5-3_GamePlay/Map/Base/MapGenerationContext.cs`。
- 地形生成：`Assets/5_Scripts/5-3_GamePlay/Map/Controller/ChunkGenerator_Land.cs`。
- 噪声基类与实现：`Assets/5_Scripts/5-3_GamePlay/Map/Base/BaseNoise.cs`、`Assets/5_Scripts/5-3_GamePlay/Map/MapMaker/LandNoise.cs`、`PerlinNoise.cs`。
- 河流生成：`Assets/5_Scripts/5-3_GamePlay/Map/Controller/ChunkGenerator_River.cs`。
- 物品生成：`Assets/5_Scripts/5-3_GamePlay/Map/Controller/ChunkGenerator_SpawnItems.cs`。
- 结构生成：`Assets/5_Scripts/5-3_GamePlay/Map/Structures/ChunkGenerator_Structures.cs`。
- 矿洞生成：`Assets/5_Scripts/5-3_GamePlay/Dimension/ChunkGenerator_Cave.cs`。
- 地图存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/MapSave.cs`。
- Tilemap 数据：`Assets/5_Scripts/5-1_Data/ItemData/Data_TileMap.cs`。
- 静态阻挡层：`Assets/5_Scripts/5-3_GamePlay/Map/BlockingTilemapLayer.cs`。
- 地块权威数据：`Assets/5_Scripts/5-1_Data/TileData/`。

## 资源目录

- Map Prefab：`Assets/2_Prefabs/Map/`。
- TileBlock Prefab：`Assets/2_Prefabs/TileBlock/`。
- Tile 资源：`Assets/7_Tiles/`。
- TileBlock SO：`Assets/4_ScriptObjects/4-1_TileBlock/`。
- Biome SO：`Assets/4_ScriptObjects/4-8_Biome/`。
- Structure SO：`Assets/4_ScriptObjects/4-9_Structures/`。
- 结构目录：`Assets/5_Scripts/5-3_GamePlay/Map/Structures/`。
- 结构目录资产：`Assets/Resources/Config/StructureCatalog_Default.asset`。

## 系统边界

- 地形可走性来源于 `TileData`，动态建筑占地来源于 `BuildingOccupancyRegistry`。
- `Map` 完成 Tilemap 加载后通过脏格/脏区通知导航，不应全场扫描碰撞体。
- Chunk 流送不得在 `Map.IsReadyForChunkLifecycle` 为 false 时直接失活对象，否则 Unity 会中断生成或 Tilemap 写入协程；延迟失活请求必须在地图视觉完成后执行。
- Chunk 对象池复用前后必须重置地图就绪状态、运行时 Item 和事件订阅。
- 运行时群系查询统一调用 `ChunkGenerator_Land.TryGetBiomeAtWorld()`，使用正式地形生成时的有序 `biomes` 和 `EnvironmentLayers`，不要在生成器外复制匹配逻辑。
- 地形尺度分为三层：`PlanetData.NoiseScale` 是世界级坐标缩放，`BaseNoise.coordScale` 是单通道坐标倍率，`BaseNoise.frequency` 是单通道基础频率；三者共同决定最终采样频率。
- `ChunkGenerator_River` 正式入口使用真实水文算法；旧 Voronoi 参数和 `TriFractalRiverNoise`/`RiverNoiseSettings` 只保留序列化兼容，不应作为新配置入口。
- `MapGenerationContext` 现携带 `WorldAddress` 与 `DimensionDefinition`；基础种子按 `WorldKey + SeedSalt` 派生，保证同星球不同维度的地图、确定性 Item GUID 和 Chunk 差量隔离。
- `ChunkMgr.TryCreateMapCore()` 按当前维度的 `MapCorePrefabId` 创建地图；矿洞运行时替换为 `ChunkGenerator_Cave`，地表继续使用 MapCore 原生成管线。
- 矿洞的房间/隧道由 `CaveLayoutSampler` 以绝对世界坐标采样；每格先铺可走地面，封闭格再叠加不可走岩壁，保证 Tilemap、导航和 Chunk 存档读取同一顶层 `TileData`。
- `BlockingTilemapLayer` 负责 `TileTag=Blocking` 的静态 Tile 障碍：地面 Tilemap 渲染阻挡层下方的数据，独立“建筑阻挡层”渲染顶层障碍并持有 `TilemapCollider2D`；A* 与存档仍读取原顶层 TileData。
- 动态可放置建筑不得写入阻挡 Tilemap，继续使用 GameObject Collider + `BuildingOccupancyRegistry`；阻挡层只服务矿洞岩壁、地牢墙体和结构模板中的静态 Tile 障碍。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-07-31：新增通用静态“建筑阻挡层” Tilemap；阻挡 Tile 与地面分层渲染，支持独立碰撞和单格刷新，同时不改变顶层 TileData 导航/存档契约。
- 2026-07-31：矿洞地图由整块石地改为跨 Chunk 连续的房间与弯曲隧道；新增地面/岩壁双层 TileData、Tilemap 墙体碰撞和沿墙聚集矿床。
- 2026-07-31：地图生成接入维度上下文和派生种子；新增全石地面、入口安全区和确定性矿脉生成器，各维度使用独立 `PlanetData.MapData_Dict`。
- 2026-07-31：修复区块分帧生成或 Tilemap 写入期间被流送失活后形成整块黑洞；地图就绪状态新增 Tilemap 视觉完成条件，区块失活会等待地图完整就绪，重新激活时可恢复中断的视觉加载。
- 2026-07-30：整理地形噪声 Inspector 与参数职责，统一世界坐标缩放默认值和合法性校验；新建世界会在点击创建时重新读取半径/尺度；旧河流噪声配置已明确标记为兼容项，正式河流仅配置 `ChunkGenerator_River` 水文参数。
- 2026-07-30：遗迹普通物件支持模板内唯一 `MemberId` 与固定槽位容器内容；结构生成在世界 Item `Load()` 后按 `Mod_Inventory` 目标库存写入物品，并为容器内物品派生确定性 GUID。配置会进入结构目录内容哈希和区块程序生成基线。
- 2026-07-29：统一内容校验器检查 `BiomeData` ID、地块/物品生成条目、生成物 Prefab 与 `itemName`、概率倍率、环境条件和伴生宿主配置，并为 Spawner 群系引用提供权威 ID 集合。
- 2026-07-29：`ChunkGenerator_Land` 公开世界格群系查询，供生态生成位置校验复用。
- 2026-07-27：导航与地图生成已解耦为 TileData 权威数据 + 导航脏区批处理；Chunk 流送后禁止无条件全窗口重烘焙。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Map/MapSmokeTests.cs`；当前基础覆盖 ChunkMgr、Chunk、Map、静态阻挡层入口与底层地面解析、地图 Prefab、MapCore 环境噪声通道、非法 Perlin 参数有限值保护、世界坐标缩放规则、Tilemap 视觉完成前禁止地图进入就绪态、结构目录入口，以及遗迹容器配置深复制与内容哈希。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；地图测试约定目录：`Assets/GameTest/Map/`；场景目录：`Assets/GameTest/Scenes/Map/`；冒烟分类：`Map.Smoke`。
- 新增 Chunk 流送、Tilemap、程序生成、Biome、River、Structure 或地图差量行为时必须增加系统测试；修复 Bug 时先增加回归测试。中心 Chunk 加载与卸载主流程变化时同步更新地图冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；程序生成必须固定种子，测试结束必须清理 Chunk、Tilemap 与临时地图数据。
- 完成修改后检查 Unity 编译和 Console，再运行 `Map.Smoke`；涉及导航、存档、建筑、Item/Module 或环境时同步运行对应系统测试。
- 地表兼容、矿洞目录、洞穴布局确定性、阻挡层路由、墙地可走性和资源生成入口由 `Assets/GameTest/Dimension/DimensionSmokeTests.cs`（`Dimension.Smoke`）补充覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动生成器、地图 Prefab、Biome/Structure/Tile 资源，改变 Chunk 生命周期、生成顺序、MapSave 结构或就绪条件后，必须同步更新本 Skill；涉及可走性时同步更新 Navigation Skill。
