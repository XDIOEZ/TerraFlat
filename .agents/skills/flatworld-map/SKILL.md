---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的地图内容、Tilemap、地形规则、Biome、River、Structure、TileData、地图差量与保存、旧 Map 兼容生成器或地图资源。关键词：Map、TileData、ChunkGenerator_Land、MapSave、TerrainNoise。"
---

# FlatWorld 地图内容与生成

## 入口

- 地图内容：`Assets/5_Scripts/5-3_GamePlay/World/Map/`
- Chunk 加载与物品归属：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/`
- 地图数据与存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/`
- 资源：`Assets/2_Prefabs/World/Map/`、`Assets/7_Tiles/`、`Assets/4_ScriptObjects/World/{Tiles,Biomes,Structures}/`
- 当前地表自然物密度以 `Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset` 的 `ecologyRules` 为权威；`BiomeData.TerrainConfig.ItemSpawn_NoSO` 属于旧生成链，不用于调整 WorldModel 生态数量。存档默认冻结首次使用时的生成 Profile；玩家可在存档管理页关闭“冻结世界生成规则”以显式跟随当前版本。关闭时只丢弃冻结 Profile，保留生态区块的删除 GUID、状态覆盖和恢复年份；重新开启后在下一次进入世界时冻结当时的当前配置。
- 草和可采集地表植被分别由 `ChunkGrassRenderer` 与 `ChunkGroundCoverRenderer` 批量绘制。`ecologyRules` 继续生成确定性数据点；物品定义声明 `groundCover: true` 时跳过自然 Item 实例化，采集才生成普通 Item。选格和图层共用 `GroundCoverSystem`，采集持久化复用生态删除 GUID；不得用草层消费状态记录花朵，也不得在图层解绑时把生成点标记为已采集。
- 洞穴可配置植物同样使用 `Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Cave.asset` 的 `ecologyRules`；洞穴生成按“入口 → 配置植物 → 藤蔓/矿物”占格，出生安全区不生成配置植物。
- Chunk 运行时、生成调度和表现绑定改用 `flatworld-world-model`。

## 主链

`Tile/Biome/Structure 配置 → 地形生成规则 → ChunkTerrainData → 结构与自然物 → 差量存档 → WorldModel 表现`

## 边界

- 本 Skill 负责地图内容规则；WorldModel 负责 Chunk 生命周期、并发、租约和表现绑定。
- Tile 栈只通过 API 修改；静态 Blocking Tile 与动态建筑占地不要混用。
- 真正填水改地形时，地表身份、Water 标记与通行成本应同步变化；水上平台不属于填水，使用独立 `TerrainSupportLayer` 和有效地表查询，原始水格、水深等保持不变。不能只盖图片或把平台存成永久陆地。
- 可被容器提取的世界液体由 TileData 实现 `IWorldLiquidSourceData` 并保存稳定 `LiquidId`；`TileData_Water.Clone()` 必须保留该 ID，玩法通过 `ChunkMgr.TryGetRuntimeTileEffect` 的权威 WorldCell 解析来源，不依赖水体 Collider。当前 Tile 水源没有有限份数层时保持无限源，不因取液删除或改写水格。
- 河流生成把真实下游单位方向保存到 `riverFlowX/riverFlowY` 环境层；运行时水流玩法统一通过 `ChunkMgr.TryGetRuntimeWaterCurrent` 读取，禁止在物品、角色等消费方重复按邻格高度猜河道方向。海洋暂无独立洋流层时由该接口使用现有风场近似表层漂移，湖泊保持静止。
- 生成保持固定种子、稳定 BiomeId/顺序和统一噪声、气候、水文规则。
- 修改算法时考虑生成签名、旧存档、联机指纹和 Wrapped 坐标。
- 雪山地表固定使用纯白 `Tile_Snow`，禁止按随机噪声混入雪地变体；若未来恢复变体，只能按温度区间确定。
- 萝卜聚落由 `surface.forest.radish` 与 `surface.grassland.radish` 两条独立规则控制；全局调整时必须同步审计两条，`PatchChance` 控制聚落数量，`SpawnChance` 与 `PatchRadius` 控制聚落内部密度。
- 洞穴入口联动 `flatworld-dimension`，可走性联动 `flatworld-navigation`，差量联动 `flatworld-data-save`。
- 地块可提供环境动作与被动效果定义，但共享 `TileBlockBehaviour` 只保存规则；玩家长按、Tick、环境倍率等实例状态必须留在角色侧运行器。

- 自然植物恢复资格由 `INaturalRenewalPolicy` 记录到生态存档的 `RenewalYears`；只有生成成功才清除移除标记和补位计划。玩家种植不参加自然补位，建筑、耕地及平台所在格不补野生植物。

## 验证

- `WorldTopologyDomain` 是 `Shared/WorldTopology/FlatWorld.WorldTopology.asmdef` 中的纯数学坐标真源；Bounds 只负责配置/类型封装，Runtime 仍读取 SaveDataMgr，只允许主线程使用。禁止在 Map 或数学核心直接创建 Wrapped Physics Proxy。
- Tilemap 镜像由独立 `WrappedTilemapPhysicsAdapter` 消费表现生命周期：旧 Map 发布 `TilemapPresentationChanged`，新版 ChunkView 经 `ChunkCollisionRenderer` 的绑定/失效通知接入。不得假定地图一定由 ItemMgr.InstantiateItem 创建；重新加载开始、失败、停用、重新启用和增量编辑都须同步失效。
- Item 的严格接缝带与 Tilemap 的包含边界触边是两种调用语义，共用 Domain 的无分配镜像偏移查询，但不能合并阈值。旧 Map 的镜像 `TilemapDamageReceiver` 必须绑定真实 Map 与镜像 Tilemap，不能用源 Tilemap 坐标代替；新版 WorldModel 继续通过权威格子查询结算伤害。

- 默认检查静态诊断、Unity 编译和 Console。
- 仅用户明确要求时运行 `Map.*` 分类；涉及纯生成或持久化时追加对应 `WorldModel.*` 分类。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
