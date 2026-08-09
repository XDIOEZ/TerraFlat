---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的世界地图、Chunk 流送、Tilemap、程序化生成、Biome、River、Structure、TileData、地图保存或区块 Prefab。关键词：ChunkMgr、Chunk、Map、ChunkGenerator、MapSave。"
argument-hint: "地图、区块或生成问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 地图与 Chunk 系统定位

> 最后核对：2026-08-09。

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
→ RuntimeWindow 协程按玩家距离分帧绑定 ChunkView
→ ChunkView 完成后持有 Presentation / Navigation 租约
→ Tilemap / 草地 / 环境 / Collider / A* 只作表现与适配
```

## 关键文件

- 联机扩展：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.Networking.cs`。
- 玩家区块加载器：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ChunkLoader.cs`。
- Item 区块归属：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ItemChunkAssigner.cs`。
- 生成上下文：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/MapGenerationContext.cs`。
- 地形生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Controller/ChunkGenerator_Land.cs`。
- 统一噪声核与生成签名：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/TerrainNoise.cs`。
- 无头旧版地形/气候核：`Assets/5_Scripts/5-0_WorldModel/Generation/LegacyTerrainClimateKernel.cs`。
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
- 运行时窗口分为三圈：`LoadChunkDistance` 内领取 Simulation 并显示，`UnActiveDistance` 内只提前生成数据，`DestroyChunkDistance` 内只保留缓存；预取任务必须排在可见任务之后，同圈优先玩家最近跨区移动方向。预取区块不得领取模拟、表现或导航租约。
- 已就绪区块不能在后台完成回调里直接集中 `ChunkView.Bind()`；统一进入 `ChunkMgr.RuntimeWindow` 的全局协程队列，按距玩家由近到远、每帧最多 `maxChunkPresentationsPerFrame` 个完成 Tilemap、草地、碰撞和导航绑定。离开窗口、重置世界或对象池回收必须同步取消待表现项。
- 根节点自然生物不属于可池化 `ChunkView` 子物体；本地显隐必须通过 `ChunkMgr.IsRuntimeEntityPresentationReady()` 对齐实际完成绑定的 View，不能只看数据预取或窗口目标。
- 运行时脚下地块效果由 `ChunkMgr.TileEffects.cs` 直接采样 `ChunkTerrainData`；生成 Profile 以 `tile.block.<数字 TileId>` 映射现有 `Tile_Block` 行为，`TileEffectReceiver` 只把旧 `Map` 作为兼容回退，不能再依赖 `Chunk.Map` 判断新版河流/海洋。
- `WorldRuntimeHost : MonoBehaviour` 只转发 Unity 生命周期、主线程提交和时间参数，不包含实体决策。
- 实体迁移已撤销：玩家、AI、建筑和普通物品继续以 `Item/Module` 为权威；纯世界模型不得新增 `EntityRuntime`、`EntityComponent` 或实体 Prefab 映射。
- 区块数据状态、模拟状态和表现状态分别判断，不得重新引入一个同时代表三者的 `Ready`。
- 地形可走性来源于 `TileData`，动态建筑占地来源于 `BuildingOccupancyRegistry`。
- `Map` 完成 Tilemap 加载后通过脏格/脏区通知导航，不应全场扫描碰撞体。
- Chunk 流送不得在 `Map.IsReadyForChunkLifecycle` 为 false 时直接失活对象，否则 Unity 会中断生成或 Tilemap 写入协程；延迟失活请求必须在地图视觉完成后执行。
- Chunk 对象池复用前后必须重置地图就绪状态、运行时 Item 和事件订阅。
- 运行时群系查询统一调用 `ChunkGenerator_Land.TryGetBiomeAtWorld()`，使用正式地形生成时的有序 `biomes` 和 `EnvironmentLayers`，不要在生成器外复制匹配逻辑。
- 旧 Map 地形只有 `Height=0`、`Precipitation=2`、`Temperature=3` 三个 `TerrainNoiseConfig` 通道；`PlanetData.NoiseScale`、`coordScale` 与 `frequency` 共同决定最终采样频率。区块 Job、同步单点、出生点与结构定位必须复用 `TerrainNoiseKernel` / `TerrainPreviewSampler`。无头 Surface Profile 以 `climate.algorithm=legacyLand` 选择纯 `LegacyTerrainClimateKernel`，迁移高度/基础降水经典 Perlin、高度二次强化、区域风场及迎风增雨/背风雨影；它只能读取请求快照并输出 `height/basePrecipitation/precipitation/windX/windY`，不得从后台访问 Unity 或旧 Map。
- `BiomeResolver` 按稳定 `BiomeId` 和 MapCore 列表顺序解析，`255` 为未匹配标记；重复 ID、非法范围、超过 255 个 Biome、未匹配格或结构引用未启用 Biome 都是生成失败。
- `Data_TileMap` 以 `TileStackCell[,]` 持久化基础层、覆盖层和第三层起才分配的溢出层；只能通过 `GetTileAt/GetTopTile/PushTile/RemoveTile/ReplaceStack` 等 API 读写，不得恢复可变 `List<TileData>` 暴露。
- `Map` 按阶段和序列化原序稳定排序，且必须恰好有一个 `BaseTerrain`。生成器仅以 `GenerateAsync` 为逻辑入口；失败或取消时必须完成并释放 Job/NativeArray。
- 必需阶段、差量应用或 Tilemap 最终化失败时，Chunk 进入 `Failed`，不设置 `TileLoaded`、不捕获成功基线、不渲染也不发送 Ready；`ChunkMgr` 必须解除 loading 并结束等待者。
- 初次 Tilemap 渲染按行复用数组并调用 `SetTilesBlock`，同一次遍历产生地面与顶层阻挡层，最后统一刷新碰撞体；单格实时编辑仍走单格刷新。
- Unity 旧 Map 管线的 `ChunkGenerator_River` 与无头 `DeterministicChunkGenerator` 都必须从各自权威高度图和降水图计算径流。无头 Profile 通过文本参数 `river.algorithm=heightDriven|legacy` 保留两套纯算法：正式 Surface 默认使用 `heightDriven` 的 D∞、成熟主河和连通支流筛选，避免 D8 在连续坡面产生密集平行直河；`legacy` 仅作对照，保留实例级并发安全区域缓存、D8 严格下坡、`ResolveBasin` 湖泊与最低出口续流。两者都只能写 `ChunkTerrainBuffer`，都输出 `riverDepth/riverFlow/riverFloodplain/riverSurfaceLevel/riverKind`；`riverKind` 为 `0=None,1=River,2=Lake`。新世界 `PlanetData.NoiseScale` 必须写入纯 Profile 的 `world.coordinateScale`，地形/气候频率按 `scale/0.01` 变化，径流单元和追踪距离按反比 `clamp(0.01/scale, 0.25, 4)` 变化；禁止独立河流噪声、正弦水带或其他脱离高度图的函数绘制。
- 无头 Surface 使用旧版高度二次强化后的 `height` 分类二维山地：`height >= terrain.mountainLevel`（默认 `0.72`）且不是水体时写入可行走的 `StoneTileId` 地面和 `mountain=1` 环境值；河流优先覆盖山地，结构可在生成末尾覆盖基础石地。
- `MapGenerationContext` 现携带 `WorldAddress` 与 `DimensionDefinition`；基础种子按 `WorldKey + SeedSalt` 派生，保证同星球不同维度的地图、确定性 Item GUID 和 Chunk 差量隔离。
- `ChunkMgr.TryCreateMapCore()` 和 `ChunkGenerator_Cave` 仅保留给旧 `Map` 兼容路径；新版 WorldModel 的矿洞不再调用旧 Chunk/Map 生成接口。
- 新版矿洞由 `CaveLayoutKernel` 在后台以绝对世界坐标复现房间、弯曲隧道与入口安全网络，`DeterministicChunkGenerator` 直接输出地面/岩壁 `ChunkTerrainData`；`CaveLayoutSampler` 仅保留旧存档兼容。
- `BlockingTilemapLayer` 负责 `TileTag=Blocking` 的静态 Tile 障碍：地面 Tilemap 渲染阻挡层下方的数据，独立“建筑阻挡层”渲染顶层障碍并持有 `TilemapCollider2D`；A* 与存档仍读取原顶层 TileData。
- 动态可放置建筑不得写入阻挡 Tilemap，继续使用 GameObject Collider + `BuildingOccupancyRegistry`；阻挡层只服务矿洞岩壁、地牢墙体和结构模板中的静态 Tile 障碍。
- `ChunkMgr.TrySetChunkLoadSpeedMultiplier()` 是运行时区块加载调速的统一公开入口；有限倍率同时缩放旧队列和新 WorldModel 生成并发。传入正无穷表示“自动最大”，后台生成按约三分之一逻辑处理器且最多 4 项，旧管线数量预算最多四倍、毫秒预算最多两倍；禁止重新返回 `int.MaxValue/Infinity`。运行中调整必须立即调用 `SetMaxGenerationConcurrency()`，降低上限不强杀已开始任务。Tilemap、碰撞和导航继续由主线程协程按表现组件逐帧绑定。调速不得改变加载距离、世界数据或确定性生成结果。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；单纯地形美术或不改变数据的生成参数调整不要扩散检查。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `TileData` 可走性、Penalty、顶层 Tile 或导航脏格/脏区通知 | `flatworld-navigation` | A* 仍从权威 TileData 取值，节点和连接在数据变化后刷新 | `Navigation.Smoke` |
| Chunk 激活窗口、加载/卸载、对象池、Ready 条件或 MapCore 创建 | `flatworld-navigation`；涉及观察者/维度时再加载 `flatworld-networking`、`flatworld-dimension` | GridGraph 窗口、观察者并集、维度 MapCore 与延迟失活顺序 | `Navigation.Smoke`；按命中项追加 `Networking.Smoke` 或 `Dimension.Smoke` |
| `MapSave`、Chunk 差量、确定性 Item GUID 或 Item 区块归属 | `flatworld-data-save`、`flatworld-item-module` | 基线与 ChangedItems、不重复注册、卸载重载结果一致 | `DataSave.Smoke`、`ItemModule.Smoke` |
| `ChunkEcologyData`、`CaveGenerationFeatureGenerator`、自然物规则、生态放置或 `ChunkNaturalItemRenderer` | `flatworld-data-save`、`flatworld-item-module`、`flatworld-dimension` | 地表生态与矿洞矿脉/传送门均可重放；水域、结构、岩壁和差量过滤正确，Item 池化后不复活 | `WorldModel.Ecology`、`WorldModel.Cave`、`DataSave.Ecology`、`ItemModule.Smoke` |
| 结构生成、静态阻挡层或动态建筑边界 | `flatworld-building`、`flatworld-navigation` | 静态 Tile 阻挡与 `BuildingOccupancyRegistry` 动态占地不混用 | `Building.Smoke`、`Navigation.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-09：新版 `ChunkView` 新增 `LightOccluders` 表现子层；它从 `ChunkTerrainData.BlockingTileId` 合并生成可复用 URP 2D `ShadowCaster2D`，区块解绑时回收/隐藏，墙体 `TileStack` 变化时局部重建。
- 2026-08-09：掉落物归属新增 `ChunkMgr.TryGetRuntimeDropParent`，玩家、战斗和自然物掉落挂到已绑定 `ChunkView` 的临时节点；新区块窗口不再因掉落调用旧 `ChunkMgr` 加载链。
- 2026-08-09：维度切换重新使用玩家 `Mod_ChunkLoader` 的活动/预取/销毁窗口；`ChunkMgr` 暴露活动视野绑定完成状态，避免 1x1 中心区块请求覆盖完整视野。
- 2026-08-09：矿洞出口通过 `CavePortalPairingSnapshot` 复算冻结地表的概率格、候选顺序和可放置性；洞穴只输出同格的唯一 `CaveExit`，不再枚举四个回退候选。新世界 Surface/Cave 默认入口概率均为 1%。
- 2026-08-09：WorldModel 自然物的采摘掉落保持在 `NaturalItems` 表现节点，不再用旧 `ChunkMgr` 加载目标区块；解绑时回收临时掉落，避免旧区块与新版表现窗口交叉触发。
- 2026-08-09：旧 `TileItem_StoneWall` 的右键放置不再写旧 `Map.Data`，通过 `TileBuildingSystem` 写入 `ChunkTerrainData.BlockingTileId`；新增 `CanSetBlockingTile` 供预览阶段检查扩展地块堆栈。
- 2026-08-09：`ChunkTerrainData` 新增运行时阻挡地块写入/移除接口；新版区块渲染器通过 `TerrainChangeKind.TileStack` 即时刷新石墙，动态建筑不再依赖旧 `Map.Data`。
- 2026-08-09：新版矿洞迁入纯 WorldModel：`CaveLayoutKernel` 输出连续房间/隧道/安全区地形，`CaveGenerationFeatureGenerator` 输出稳定洞壁矿脉、散矿和跨维度入口；洞穴继续复用 `ChunkEcologyData` 作为无 Unity 引用的世界物品记录。地形预览器构造快照时必须传递 `CaveResourceRules`，并以 `cave.portal.chunkWidth/Height` 保留正式概率格，不能让临时大预览区块稀释传送门密度。
- 2026-08-09：地形预览器读取自然物图标时不再单独探测 Prefab 根节点，改为遍历层级内的 `SpriteRenderer`，兼容根节点无渲染器的 Weed 等物品；画布裁剪改用 `try/finally`，单个图标资源异常不会破坏后续 IMGUI 布局。
- 2026-08-09：旧 `Chunk` 加载、运行时字典、位置数组和保存流程统一排除实体 AI；AI 按纯 `WorldAddress` 随新版数据窗口恢复，即使无本地 `ChunkView` 也会加载，旧 Chunk 只保留一次性迁移拦截。


## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Map/MapSmokeTests.cs`、`Assets/GameTest/WorldModel/WorldModelSmokeTests.cs`、`Assets/GameTest/WorldModel/WorldModelPersistenceTests.cs`、`Assets/GameTest/WorldModel/LegacyHydrologyKernelTests.cs` 与 `LegacyTerrainClimateKernelTests.cs`；当前基础覆盖 Chunk 生命周期、固定失败种子的完整半径出生搜索、南北/四角周期地形哈希、正式 D∞ 河流 Profile、二维山地石地、旧版气候等价/风场/地形降雨、旧版水文盆地/出口/区域接缝/取消、跨接缝休眠保留窗口、区块加载倍率 API、Tilemap、地形噪声与结构入口。
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
