---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的世界地图、Chunk 流送、Tilemap、程序化生成、Biome、River、Structure、TileData、地图保存或区块 Prefab。关键词：ChunkMgr、Chunk、Map、ChunkGenerator、MapSave。"
---

# FlatWorld 地图与 Chunk

## 入口

- 纯模型：`Assets/5_Scripts/5-0_WorldModel/{ChunkRuntime,ChunkTerrainData,ChunkMgr}.cs`
- Unity 适配：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.{WorldRuntime,RuntimeWindow,Networking}.cs`
- 加载/归属：`World/Chunk/{Mod_ChunkLoader,Mod_ItemChunkAssigner}.cs`
- 生成：`World/Map/Base/{MapGenerationContext,TerrainNoise}.cs`、`World/Map/Controller/ChunkGenerator_Land.cs`
- 资源：`Assets/2_Prefabs/Map/`、`Assets/7_Tiles/`、`Assets/4_ScriptObjects/4-{1_TileBlock,8_Biome,9_Structures}/`

## 主链

`观察者 → RefreshRuntimeWindow → 后台生成 ChunkRuntime/TerrainData → 主线程按纪元/版本提交 → Simulation 租约 → 分帧绑定 ChunkView → Presentation/Navigation 租约 → 远距解绑/逐出`

## 不变量

- `ChunkRuntime + ChunkTerrainData` 是权威；`ChunkView/Tilemap/Collider` 只是可池化表现。
- 数据、模拟、表现状态分开；预取不领取任何租约，可见任务优先。解绑/销毁前必须释放表现与导航租约。
- 后台不访问 Unity；完成结果在主线程校验世界纪元和请求版本后提交。失败 Chunk 不得 Ready、渲染或注册导航。
- Tile 栈只走 API；静态 Blocking Tile 与动态 `BuildingOccupancyRegistry` 不混用。
- 生成保持固定种子、稳定 BiomeId/顺序与统一噪声/气候/水文核；修改算法时升级生成签名并考虑存档/联机指纹。
- Surface 正式水文使用高度驱动 D∞；洞穴正式链为 `DeterministicChunkGenerator + CaveLayoutKernel`，旧 Map 生成器只作兼容。
- Wrapped 坐标统一使用 `WorldTopologyBounds`；当前 AI 导航不跨环面接缝建边。
- 运行时加载倍率只改变预算/并发，不改变窗口、数据或确定性结果。

## 联动与验证

- Tile/可走性→Navigation；差量/GUID→Data+Item；结构/占地→Building；观察者/协议→Networking；洞穴→Dimension。
- 默认静态诊断、编译和 Console；系统级变化运行 `Map.Smoke`，按契约追加相关分类。测试目录：`Assets/GameTest/Map/`、`Assets/GameTest/WorldModel/`。
- 生成测试固定种子并清理 Chunk/Tilemap；视觉只在地形最终观感变化时检查。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
