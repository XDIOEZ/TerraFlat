---
name: flatworld-dimension
description: "Use when: 定位或修改 FlatWorld 的维度、星球表面/地下矿洞切换、世界地址、动态世界 Scene、维度独立地图与种子、维度入口、维度环境覆盖或未来星球旅行。关键词：DimensionManager、WorldAddress、DimensionPortal、DimensionCatalogSO、ChunkGenerator_Cave。"
argument-hint: "维度、地下矿洞或星球旅行问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 维度与星球世界定位

> 最后核对：2026-07-31。首版只开放离线地表与地下矿洞往返。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Dimension/WorldAddress.cs`：星球与维度的稳定世界地址。
2. `Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionManager.cs`：维度激活、动态 Scene、切换、失败恢复与生成配置。
3. `Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionCatalogSO.cs`：维度定义、入口目标、环境覆盖和矿洞资源规则。
4. `Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionTravelProgressStore.cs`：玩家每维度最后位置。
5. `Assets/5_Scripts/5-3_GamePlay/Dimension/ChunkGenerator_Cave.cs`：地下矿洞地面与矿脉生成。
6. `Assets/5_Scripts/5-3_GamePlay/Dimension/CaveLayoutSampler.cs`：跨 Chunk 连续的房间、弯曲隧道、入口室和矿床强度采样。
7. `Assets/5_Scripts/5-3_GamePlay/Manager/GameManager.Dimension.cs`：复用世界加载 UI 与生命周期事件的桥接入口。

## 世界地址契约

- 地表地址继续使用旧键：`PlanetId`，例如 `Earth`；不得改成带后缀的形式，否则会破坏旧存档兼容。
- 非地表地址：`PlanetId__dimension__DimensionId`，例如 `Earth__dimension__cave`。
- 统一使用 `WorldAddress.FromWorldKey()`、`WithDimension()` 和 `WorldKey`，禁止在业务脚本里自行拼接后缀。
- `GameSaveData.PlanetData_Dict` 直接以 `WorldKey` 为键；每个维度因此拥有独立 `PlanetData.MapData_Dict`、Chunk 差量和环境状态。

## 运行链

```text
DimensionPortal.Interact
→ DimensionManager.TryBeginTransition
→ 保存当前世界和玩家位置
→ ItemMgr.ReleasePlayerForWorldTransition
→ GameManager 世界退出事件
→ 卸载当前动态世界 Scene
→ 创建/激活目标 WorldKey 的空 Scene
→ GameManager.RunWorld
→ ChunkMgr 按维度实例化并配置 MapCore
→ ItemMgr 创建玩家
→ 以源世界坐标 1:1 设置目标维度位置
→ 在已加载 Chunk 的对应坐标生成返回入口
→ 等待首批 Chunk 完成并关闭加载界面
```

- 动态世界 Scene 名与 `WorldKey` 一致，固定玩法世界不需要加入 Build Settings。
- `GameManager.RunWorld()` 仍是进入世界的权威入口；维度系统通过 `GameManager.Dimension.cs` 复用加载面板与事件，不得复制一套生命周期。
- `ItemMgr.ReleasePlayerForWorldTransition()` 只服务完整世界切换，必须在卸载旧动态 Scene 前安全注销玩家运行时 Item。
- 切换失败必须走 `DimensionManager.RecoverAfterTransitionFailure()`，恢复原世界并关闭加载遮罩。

## 地图与确定性生成

- `ChunkMgr.TryCreateMapCore()` 从当前 `DimensionDefinition.MapCorePrefabId` 解析地图 Prefab。
- `DimensionManager.ConfigureMap()` 在矿洞维度把原 `Map.mapGenerators` 替换为 `ChunkGenerator_Cave`；地表继续使用 MapCore 原管线。
- `MapGenerationContext` 携带当前 `WorldAddress` 与 `DimensionDefinition`。
- 程序生成种子由基础种子、`WorldKey` 和 `SeedSalt` 混合；不同维度不得共享同一确定性 GUID 空间。
- `CaveLayoutSampler` 按绝对世界坐标划分稳定区域，在区域内生成不规则椭圆房间，并用带双弯点的宽隧道连接相邻房间；采样不依赖 Chunk 局部坐标，因此跨 Chunk 无接缝。
- 入口位置固定雕刻安全室并连接最近房间；禁止读取玩家实时位置参与基线生成，否则区块加载顺序会改变地图。
- 开放格只铺 `TileBase_Stone`；封闭格在数据层追加不可走的 `TileBase_StoneWall`。`Map` 将 `TileTag=Blocking` 的顶层数据路由到独立“建筑阻挡层” Tilemap，由该层持有碰撞，地面 Tilemap 始终保留底层石地。
- 矿床只生成在开放格与岩壁相邻的边缘，并由宽域/细节噪声聚集成矿床；具体矿种继续由 `DimensionResourceRule` 阈值和 Item ID 决定。
- 生成矿物必须走 Chunk 确定性 Item 创建和差量基线，不得仅 `Instantiate()` 临时物体。

## 环境覆盖

- 地表使用原昼夜、天气和怪物生成逻辑。
- 矿洞默认固定光照 `0.08`，`DayTimeSystem.GetLighting()` 优先读取维度覆盖。
- `WeatherMgr` 在 `SuppressWeather=true` 时关闭天气与雨效。
- `MonsterSpawnerManager` 在 `EnableMonsterSpawning=false` 时停止维度内生成器。
- 新增维度环境差异时优先扩展 `DimensionDefinition`，不要在环境管理器中硬编码维度 ID。

## 玩家进度与入口

- 玩家每个 `WorldKey` 的最后位置和已发现入口坐标存于 `Data_Player.ItemSpecialData` 的 `flatworld.dimensions` 命名空间，不修改 MemoryPack 布局。
- 地表入口按世界种子、Chunk 坐标和 `CaveEntranceChunkChance` 确定性概率生成；矿洞会在同一世界坐标雕刻安全区并生成返回入口。
- 每次维度往返都保留玩家源位置，地表与矿洞的世界坐标始终 1:1 对应；`PortalOffset` 仅保留旧目录兼容，不参与切换。
- `DimensionPortal` 通过现有 `IInteractable`/E 键链触发，目标维度来自当前定义的 `PortalTargetDimensionId`。
- 默认目录资源：`Assets/Resources/Config/DimensionCatalog_Default.asset`。
- 默认目录安装器：`Assets/5_Scripts/5-2_Editor/Dimension/DimensionProjectInstaller.cs`，菜单 `FlatWorld/Dimension/Install Default Catalog`；安装器只重建目录资产，不重存矿物 Prefab。
- 矿洞地块 SO：`Assets/4_ScriptObjects/4-1_TileBlock/TileBase_Stone.asset` 与 `TileBase_StoneWall.asset`；资源名、`TileData.ID/Name` 和对应 TileBase 名称保持一致。

## 矿物与掉落

- 矿洞资源 Prefab：`Assets/2_Prefabs/Mine/Mine_Coal.prefab`、`Mine_Copper.prefab`、`Mine_Tin.prefab`、`Mine_Iron.prefab`、`Mine_Stone.prefab`。
- 金属/煤矿掉落对应 `Ore_*` 并伴生 `Ore_Stone`；石矿只掉落 `Ore_Stone`。
- 这些掉落由 Prefab 的 `DamageReceiver.Data.LootTable` 持久化；不得恢复 Chicken 等模板继承掉落。
- 项目的嵌套 `DamageType` 数据不支持用 `PrefabUtility.SaveAsPrefabAsset()` 全量重存该 DamageReceiver Prefab；修改掉落时应使用 Inspector 的 Prefab override 或精确 YAML/SerializedProperty，并在刷新后检查 Console。

## 联机边界

- 首版维度切换只支持离线模式；`GameNetwork.IsOnline` 为真时 `TryBeginTransition()` 必须拒绝。
- 未完成服务器权威目标地址、全体观察者迁移、Chunk 流送重建和玩家同步前，不得解除该限制。
- 后续星球旅行应继续复用 `WorldAddress`，星球 ID 放在 `PlanetId`，星球表面维度仍为 `surface`。

## 修改后验证

- 基础测试：`Assets/GameTest/Dimension/DimensionSmokeTests.cs`；分类：`Dimension.Smoke`。
- 当前覆盖世界键地表兼容/往返、默认目录与矿洞环境配置、洞穴布局确定性、开放/封闭格混合、阻挡层路由、岩壁可走性、必要脚本/资产，以及五种矿物只能掉落 `Ore_*`。
- 按项目约束默认只检查 Unity 编译与 Console；仅当用户明确要求时运行 Test Runner。
- 手动 Play Mode 建议验证：地表入口交互、矿洞大量生成矿物、开采掉落、返回地表、两边位置恢复、退出重进后的 Chunk 差量。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-02：地表矿洞入口改为按 Chunk 确定性概率生成；地表/矿洞入口和玩家切换位置统一为世界坐标 1:1 对应。
- 2026-07-31：矿洞岩壁从地面 Tilemap 分离到通用“建筑阻挡层”；底层地面、阻挡视觉/碰撞和顶层导航 TileData 各自保持单一职责。
- 2026-07-31：矿洞升级为跨 Chunk 连续的不规则房间、弯曲隧道和入口安全室；新增实体岩壁与沿墙噪声矿床，取代整块平坦石地随机散点矿物。
- 2026-07-31：新增统一星球/维度世界地址、动态世界 Scene、独立地图与种子、玩家位置进度、地表与地下矿洞往返。
- 2026-07-31：新增确定性矿洞生成器、固定低光照、禁天气/怪物和煤铜锡铁石矿脉；修正矿物掉落表。
- 2026-07-31：首版明确禁止联机维度切换，为后续服务器权威星球旅行保留边界。

## 修改后维护本 Skill

改变世界键格式、动态 Scene、切换链、目录字段、生成种子、玩家位置命名空间、矿洞资源、环境覆盖、联机限制或测试目录后必须同步更新本 Skill；同时更新 Core、Map、DataSave、Space、Environment、PlayerInteraction、Combat 或 Networking 中实际受影响的条目。
