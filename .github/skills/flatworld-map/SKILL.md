---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的世界地图、Chunk 流送、Tilemap、程序化生成、Biome、River、Structure、TileData、地图保存或区块 Prefab。关键词：ChunkMgr、Chunk、Map、ChunkGenerator、MapSave。"
argument-hint: "地图、区块或生成问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 地图与 Chunk 系统定位

> 最后核对：2026-07-27。导航节点更新请同时加载 `flatworld-navigation`。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Manager/ChunkMgr.cs`：区块字典、加载队列、激活窗口、对象池。
2. `Assets/5_Scripts/5-3_GamePlay/Chunk/Chunk.cs`：区块实例、地图与运行时 Item 生命周期。
3. `Assets/5_Scripts/5-3_GamePlay/Map/Base/Map.cs`：Tilemap 加载、程序化生成流水线、就绪状态与导航脏区衔接。
4. `Assets/5_Scripts/5-3_GamePlay/Map/Base/ChunkGeneratorBase.cs`：生成器抽象。

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
- 河流生成：`Assets/5_Scripts/5-3_GamePlay/Map/Controller/ChunkGenerator_River.cs`。
- 物品生成：`Assets/5_Scripts/5-3_GamePlay/Map/Controller/ChunkGenerator_SpawnItems.cs`。
- 结构生成：`Assets/5_Scripts/5-3_GamePlay/Map/Structures/ChunkGenerator_Structures.cs`。
- 地图存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/MapSave.cs`。
- Tilemap 数据：`Assets/5_Scripts/5-1_Data/ItemData/Data_TileMap.cs`。
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
- Chunk 对象池复用前后必须重置地图就绪状态、运行时 Item 和事件订阅。

## 近期变更

- 2026-07-27：导航与地图生成已解耦为 TileData 权威数据 + 导航脏区批处理；Chunk 流送后禁止无条件全窗口重烘焙。

## 修改后维护本 Skill

移动生成器、地图 Prefab、Biome/Structure/Tile 资源，改变 Chunk 生命周期、生成顺序、MapSave 结构或就绪条件后，必须同步更新本 Skill；涉及可走性时同步更新 Navigation Skill。
