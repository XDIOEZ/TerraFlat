# 地图内容系统

## 系统定位

负责 Tile、Biome、Structure、地形规则、生态内容、地块环境数据和地图玩法语义；不负责 Chunk 并发调度与表现窗口生命周期。

## 当前机制

- 权威地块状态进入 `ChunkTerrainData`。
- 地形生成后再叠加结构、自然物和运行时差量。
- Tile 栈只能通过正式 API 修改，静态 Blocking Tile 与动态建筑占地保持分离。
- 水上平台不是“填水”：平台写入 `TerrainSupportLayer`，底层水格和水深继续保留。
- 地图生成必须在固定种子下保持稳定 BiomeId、噪声、气候、水文与规则顺序。

## 地表生态

当前地表自然物密度的权威配置：

`Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset`

生态由 `ecologyRules` 控制。规则会在新世界首次创建时冻结到 PlanetData，因此修改 Profile 默认只影响**新世界**，不会静默重写已有存档。

## 洞穴生态

洞穴生态使用：

`Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Cave.asset`

洞穴生成顺序按入口、配置植物、藤蔓/矿物等规则占格；出生安全区不生成配置植物。

## 自然补位

- 自然植物恢复资格由生态存档记录。
- 只有补位实际成功才清除移除标记。
- 玩家种植、建筑、耕地和平台占格不会被自然补位覆盖。

## 关键入口

- `Assets/5_Scripts/5-3_GamePlay/World/Map/`
- `Assets/5_Scripts/5-3_GamePlay/World/Chunk/`
- `Assets/7_Tiles/`
- `Assets/4_ScriptObjects/World/Tiles/`
- `Assets/4_ScriptObjects/World/Biomes/`
- `Assets/4_ScriptObjects/World/Structures/`

## 边界

- 内容规则在本系统；Chunk 生命周期、后台生成和 View 绑定在 WorldModel。
- 洞穴地址与切换属于维度系统。
- 建筑占地和 Tile 建筑属于建筑系统，但最终写入地图权威层。

## 对应 Skill

`.agents/skills/flatworld-map/SKILL.md`
