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
- Chunk 运行时、生成调度和表现绑定改用 `flatworld-world-model`。

## 主链

`Tile/Biome/Structure 配置 → 地形生成规则 → ChunkTerrainData → 结构与自然物 → 差量存档 → WorldModel 表现`

## 边界

- 本 Skill 负责地图内容规则；WorldModel 负责 Chunk 生命周期、并发、租约和表现绑定。
- Tile 栈只通过 API 修改；静态 Blocking Tile 与动态建筑占地不要混用。
- 生成保持固定种子、稳定 BiomeId/顺序和统一噪声、气候、水文规则。
- 修改算法时考虑生成签名、旧存档、联机指纹和 Wrapped 坐标。
- 洞穴入口联动 `flatworld-dimension`，可走性联动 `flatworld-navigation`，差量联动 `flatworld-data-save`。
- 地块可提供环境动作与被动效果定义，但共享 `TileBlockBehaviour` 只保存规则；玩家长按、Tick、环境倍率等实例状态必须留在角色侧运行器。

## 验证

- 默认检查静态诊断、Unity 编译和 Console。
- 仅用户明确要求时运行 `Map.*` 分类；涉及纯生成或持久化时追加对应 `WorldModel.*` 分类。

## Skill 维护原则

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
