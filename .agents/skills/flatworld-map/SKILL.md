---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的世界地图、Chunk 流送、Tilemap、程序化生成、Biome、River、Structure、TileData、地图保存或区块 Prefab。关键词：ChunkMgr、Chunk、Map、ChunkGenerator、MapSave。"
argument-hint: "地图、区块或生成问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 地图与 Chunk 系统定位

> 最后核对：2026-08-07。

## 修改前先读

1. `Assets/5_Scripts/5-0_WorldModel/ChunkRuntime.cs`：区块纯数据、三类租约与数据/模拟/表现状态。
2. `Assets/5_Scripts/5-0_WorldModel/ChunkTerrainData.cs`：地形、环境、草地、阻挡与导航权威查询。
3. `Assets/5_Scripts/5-0_WorldModel/ChunkMgr.cs`：纯 C# 缓存、生成调度、三级窗口与逐出。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.WorldRuntime.cs`、`ChunkMgr.RuntimeWindow.cs`：Unity 侧主线程提交与视图租约适配。
5. `Assets/5_Scripts/5-3_GamePlay/World/WorldModel/Presentation/ChunkView.cs`：Tilemap、草地、环境、碰撞和导航表现绑定。
6. 旧 `Chunk`、`Map`、`Data_TileMap` 仍是待移除的迁移源，不得再扩展为新区块权威；普通物品、玩家、AI、建筑等实体继续使用现有 `Item/Module`。
7. 涉及地下矿洞或跨星球地图时读取 `flatworld-dimension`；维度配置入口为 `DimensionManager.ConfigureMap()`。

## 地图主链

```text
Mod_ChunkLoader / NetworkChunkStreamingCoordinator
→ ChunkMgr.RefreshRuntimeWindow
→ 纯 ChunkMgr 请求/生成 ChunkRuntime
→ 主线程校验世界纪元和请求版本后原子提交 ChunkTerrainData
→ 活跃区块持有 Simulation 租约；远区块休眠；超距逐出
→ ChunkView 按 Presentation / Navigation 租约绑定纯数据
→ Tilemap / 草地 / 环境 / Collider / A* 只作表现与适配
```

## 关键文件

- 联机扩展：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.Networking.cs`。
- 玩家区块加载器：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ChunkLoader.cs`。
- Item 区块归属：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ItemChunkAssigner.cs`。
- 生成上下文：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/MapGenerationContext.cs`。
- 地形生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Controller/ChunkGenerator_Land.cs`。
- 统一噪声核与生成签名：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/TerrainNoise.cs`。
- Biome 集中解析：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/BiomeResolver.cs`。
- 河流生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Controller/ChunkGenerator_River.cs`。
- 无头河流调参记录：`.agents/river-heightmap-tuning.json`。
- 物品生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Controller/ChunkGenerator_SpawnItems.cs`。
- 结构生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/ChunkGenerator_Structures.cs`。
- 矿洞生成：`Assets/5_Scripts/5-3_GamePlay/World/Dimension/ChunkGenerator_Cave.cs`。
- 地图存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/MapSave.cs`。
- Tilemap 数据：`Assets/5_Scripts/5-1_Data/ItemData/Data_TileMap.cs`、`Assets/5_Scripts/5-1_Data/TileData/TileStackCell.cs`。
- 静态阻挡层：`Assets/5_Scripts/5-3_GamePlay/World/Map/BlockingTilemapLayer.cs`。
- 地块权威数据：`Assets/5_Scripts/5-1_Data/TileData/`。

## 资源目录

- Map Prefab：`Assets/2_Prefabs/Map/`。
- TileBlock Prefab：`Assets/2_Prefabs/TileBlock/`。
- Tile 资源：`Assets/7_Tiles/`。
- TileBlock SO：`Assets/4_ScriptObjects/4-1_TileBlock/`。
- Biome SO：`Assets/4_ScriptObjects/4-8_Biome/`。
- Structure SO：`Assets/4_ScriptObjects/4-9_Structures/`。
- 结构目录：`Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/`。
- 结构目录资产：`Assets/Resources/Config/StructureCatalog_Default.asset`。

## 系统边界

- 区块权威是无 Unity 引用的 `ChunkRuntime + ChunkTerrainData`；`ChunkView` 可重复绑定、解绑和池化，不能反向成为数据来源。
- `WorldRuntimeHost : MonoBehaviour` 只转发 Unity 生命周期、主线程提交和时间参数，不包含实体决策。
- 实体迁移已撤销：玩家、AI、建筑和普通物品继续以 `Item/Module` 为权威；纯世界模型不得新增 `EntityRuntime`、`EntityComponent` 或实体 Prefab 映射。
- 区块数据状态、模拟状态和表现状态分别判断，不得重新引入一个同时代表三者的 `Ready`。
- 地形可走性来源于 `TileData`，动态建筑占地来源于 `BuildingOccupancyRegistry`。
- `Map` 完成 Tilemap 加载后通过脏格/脏区通知导航，不应全场扫描碰撞体。
- Chunk 流送不得在 `Map.IsReadyForChunkLifecycle` 为 false 时直接失活对象，否则 Unity 会中断生成或 Tilemap 写入协程；延迟失活请求必须在地图视觉完成后执行。
- Chunk 对象池复用前后必须重置地图就绪状态、运行时 Item 和事件订阅。
- 运行时群系查询统一调用 `ChunkGenerator_Land.TryGetBiomeAtWorld()`，使用正式地形生成时的有序 `biomes` 和 `EnvironmentLayers`，不要在生成器外复制匹配逻辑。
- 地形只有 `Height=0`、`Precipitation=2`、`Temperature=3` 三个 `TerrainNoiseConfig` 通道；`PlanetData.NoiseScale`、`coordScale` 与 `frequency` 共同决定最终采样频率。区块 Job、同步单点、出生点与结构定位必须复用 `TerrainNoiseKernel` / `TerrainPreviewSampler`。
- `BiomeResolver` 按稳定 `BiomeId` 和 MapCore 列表顺序解析，`255` 为未匹配标记；重复 ID、非法范围、超过 255 个 Biome、未匹配格或结构引用未启用 Biome 都是生成失败。
- `Data_TileMap` 以 `TileStackCell[,]` 持久化基础层、覆盖层和第三层起才分配的溢出层；只能通过 `GetTileAt/GetTopTile/PushTile/RemoveTile/ReplaceStack` 等 API 读写，不得恢复可变 `List<TileData>` 暴露。
- `Map` 按阶段和序列化原序稳定排序，且必须恰好有一个 `BaseTerrain`。生成器仅以 `GenerateAsync` 为逻辑入口；失败或取消时必须完成并释放 Job/NativeArray。
- 必需阶段、差量应用或 Tilemap 最终化失败时，Chunk 进入 `Failed`，不设置 `TileLoaded`、不捕获成功基线、不渲染也不发送 Ready；`ChunkMgr` 必须解除 loading 并结束等待者。
- 初次 Tilemap 渲染按行复用数组并调用 `SetTilesBlock`，同一次遍历产生地面与顶层阻挡层，最后统一刷新碰撞体；单格实时编辑仍走单格刷新。
- Unity 旧 Map 管线的 `ChunkGenerator_River` 与无头 `DeterministicChunkGenerator` 都必须从各自权威高度图和降水图计算径流：无头生成器以高度梯度 D∞ 坡向栅格化、严格下坡接收格和汇流量扩宽主河，并由汇流与局部坡度输出 `riverFloodplain`；正式地表先以 `river.minimumVisibleCourseLength` 保留成熟主河，再只显示流量达到 `river.tributaryStartFlow` 且连入成熟主河的细支流；新世界 `PlanetData.NoiseScale` 必须写入纯 Profile 的 `world.coordinateScale`，地形/气候频率按 `scale/0.01` 变化，径流单元、追踪河程、最短河程和前视距离按反比 `clamp(0.01/scale, 0.25, 4)` 变化；不能靠继续抬高 `river.startFlow` 把河道切得更碎，禁止恢复独立河流噪声、正弦水带或其他脱离高度图的函数绘制。
- `MapGenerationContext` 现携带 `WorldAddress` 与 `DimensionDefinition`；基础种子按 `WorldKey + SeedSalt` 派生，保证同星球不同维度的地图、确定性 Item GUID 和 Chunk 差量隔离。
- `ChunkMgr.TryCreateMapCore()` 按当前维度的 `MapCorePrefabId` 创建地图；矿洞运行时替换为 `ChunkGenerator_Cave`，地表继续使用 MapCore 原生成管线。
- 矿洞的房间/隧道由 `CaveLayoutSampler` 以绝对世界坐标采样；每格先铺可走地面，封闭格再叠加不可走岩壁，保证 Tilemap、导航和 Chunk 存档读取同一顶层 `TileData`。
- `BlockingTilemapLayer` 负责 `TileTag=Blocking` 的静态 Tile 障碍：地面 Tilemap 渲染阻挡层下方的数据，独立“建筑阻挡层”渲染顶层障碍并持有 `TilemapCollider2D`；A* 与存档仍读取原顶层 TileData。
- 动态可放置建筑不得写入阻挡 Tilemap，继续使用 GameObject Collider + `BuildingOccupancyRegistry`；阻挡层只服务矿洞岩壁、地牢墙体和结构模板中的静态 Tile 障碍。
- `ChunkMgr.TrySetChunkLoadSpeedMultiplier()` 是运行时区块加载调速的统一公开入口；倍率只缩放加载队列、生成与 Tilemap 铺图的分帧预算，不得改变加载距离、世界数据或确定性生成结果。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；单纯地形美术或不改变数据的生成参数调整不要扩散检查。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `TileData` 可走性、Penalty、顶层 Tile 或导航脏格/脏区通知 | `flatworld-navigation` | A* 仍从权威 TileData 取值，节点和连接在数据变化后刷新 | `Navigation.Smoke` |
| Chunk 激活窗口、加载/卸载、对象池、Ready 条件或 MapCore 创建 | `flatworld-navigation`；涉及观察者/维度时再加载 `flatworld-networking`、`flatworld-dimension` | GridGraph 窗口、观察者并集、维度 MapCore 与延迟失活顺序 | `Navigation.Smoke`；按命中项追加 `Networking.Smoke` 或 `Dimension.Smoke` |
| `MapSave`、Chunk 差量、确定性 Item GUID 或 Item 区块归属 | `flatworld-data-save`、`flatworld-item-module` | 基线与 ChangedItems、不重复注册、卸载重载结果一致 | `DataSave.Smoke`、`ItemModule.Smoke` |
| 结构生成、静态阻挡层或动态建筑边界 | `flatworld-building`、`flatworld-navigation` | 静态 Tile 阻挡与 `BuildingOccupancyRegistry` 动态占地不混用 | `Building.Smoke`、`Navigation.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-08：为无头 `DeterministicChunkGenerator`、`ChunkMgr.WorldRuntime`、运行时区块窗口和生成 Profile 快照补齐通俗中文方法注释；仅整理说明，不改变生成规则或区块生命周期。
- 2026-08-08：按长度二分试验只将无头河流 `river.maxTraceSteps` 从 256 提至 384，不改河宽、弯曲、密度或支流阈值；玩家已认可前两项观感，生成签名升至 12，试验区间与后续分支记录在 `.agents/river-heightmap-tuning.json`。
- 2026-08-08：玩家的新世界“世界坐标缩放”现写入 WorldModel Profile；纯生成器与旧 `ChunkGenerator_River` 的径流单元、采样步长、最长/最短河程和前视距离按坐标倍率反比缩放，河宽与冲积半径按平方根缩放，安全范围为 `0.25x-4x`；生成签名升至 11，并补固定参数覆盖与 Golden Path 接线断言。
- 2026-08-08：无头河网新增 `river.tributaryStartFlow=0.195` 两级显示：主河仍需满足 `river.startFlow=0.405` 与最短 96 格，低流量支流只有连入成熟主河才显示；固定种子 384x384 数据覆盖、Map Smoke、扩大视野 Golden Path 与 Console 均通过，截图可见连续长主河和细支流，记录已同步到 `.agents/river-heightmap-tuning.json`。
- 2026-08-08：无头河流新增 `river.minimumVisibleCourseLength=96`，按四邻域整条剔除未达最短河程的短小河网，不再通过抬高汇流阈值制造更短片段；固定种子、世界半径 512、截图视野 24 的 Map Smoke 与 Golden Path 通过，三张截图未见孤立短碎河段，本轮记录已写入 `.agents/river-heightmap-tuning.json`。
- 2026-08-08：无头河流改为高度梯度 D∞ 有序栅格化，主河按汇流扩宽并输出低坡 `riverFloodplain`/冲积沉积带；正常半径 512、缩放 24 的第 3 次 Golden Path 自动化与 Console 通过，但目视仍有长水平段且冲积带不够连续，三轮结果已写入 `.agents/river-heightmap-tuning.json`，本批停止重跑。
- 2026-08-08：高度图水文调参记录落到 `.agents/river-heightmap-tuning.json`；Golden Path 固定种子确认 `river.startFlow=0.405` 的密度可接受但略偏稀，`river.meanderTieTolerance=0.011` 仍无法消除 D8 长直段，后续应改方向惯性/曲率代价而非继续盲目二分该参数。
- 2026-08-08：修正无头 `DeterministicChunkGenerator` 的河流权威：移除独立 `riverField` 与环绕世界 `Math.Sin` 保底水带，改为复用地表高度/降水图进行确定性下坡汇流，并输出 `riverFlow` 供运行时与 Golden Path 验证。
- 2026-08-08：`GeneralWorldEdge.prefab` 移除已删除 `SceneChange` 留下的 Missing Script；世界边界继续只保留表现与物理组件，不承担切场景职责。
- 2026-08-07：迁移范围收缩为 Chunk 数据/表现分离；`ChunkRuntime` 和 `ChunkTerrainData` 负责区块权威，`ChunkView` 只负责渲染/碰撞/导航适配，所有玩法实体继续保留 `Item/Module`。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Map/MapSmokeTests.cs` 与 `Assets/GameTest/WorldModel/WorldModelSmokeTests.cs`；当前基础覆盖 Chunk 生命周期、南北/四角周期地形哈希、跨接缝休眠保留窗口、区块加载倍率 API、Tilemap、地形噪声与结构入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；地图测试约定目录：`Assets/GameTest/Map/`；场景目录：`Assets/GameTest/Scenes/Map/`；冒烟分类：`Map.Smoke`。
- 新增 Chunk 流送、Tilemap、程序生成、Biome、River、Structure 或地图差量行为时必须增加系统测试；修复 Bug 时先增加回归测试。中心 Chunk 加载与卸载主流程变化时同步更新地图冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；程序生成必须固定种子，测试结束必须清理 Chunk、Tilemap 与临时地图数据。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Map.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有地形、河流或 Tilemap 最终观感变化才做定向截图。
- 维度管理器的基础 PlayMode 生命周期由 `Assets/GameTest/Dimension/DimensionLifecycleTests.cs`（`Dimension.Smoke`）覆盖；地图 Smoke 不再承载完整洞穴生成契约。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

移动生成器、地图 Prefab、Biome/Structure/Tile 资源，改变 Chunk 生命周期、生成顺序、MapSave 结构或就绪条件后，必须同步更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。

## 有限环绕世界契约（2026-08-06）

- `WorldTopologyBounds` 是世界坐标、格子和 Chunk 原点归一化及最短环面位移的唯一公共入口。
- `ChunkMgr` 的查询、队列、窗口、字典和 MapSave 键必须使用规范 Chunk 原点；窗口必须去重，有限世界不得产生边界外键。
- 纯生成请求必须携带 `ChunkGenerationTopologySnapshot`；地形、气候、河流、洞穴和离散草地采样在两个轴及四角重复，边界相邻格不得出现非周期断层。
- 活跃与 `destroyDistance` 保留窗口都以归一化后的 Chunk 地址集合判断，不得用规范坐标的直接绝对差计算跨接缝距离。
- 相关精简回归位于 `MapSmokeTests`（`Map.Smoke`）及 `WorldModelSmokeTests`（`WorldModel.Smoke`）。
