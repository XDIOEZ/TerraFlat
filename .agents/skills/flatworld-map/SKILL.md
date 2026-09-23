---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的地图内容、Tilemap、地形规则、Biome、River、Structure、TileData、地图差量与保存、旧 Map 兼容生成器或地图资源。关键词：Map、TileData、ChunkGenerator_Land、MapSave、TerrainNoise。"
---

# FlatWorld 地图内容与生成

## 入口

- 地图内容：`Assets/5_Scripts/5-3_GamePlay/World/Map/`
- Chunk 加载与物品归属：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/`
- 地图数据与存档：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/`
- 地块配置唯一真源：`Assets/StreamingAssets/GameConfig/Tiles/tile-manifest.json` 及其显式分包；构建入口为 `World/Map/Definitions/`。`Assets/7_Tiles/` 保存 Unity 外观资源；`Assets/4_ScriptObjects/World/Tiles/` 的 `Tile_Block` SO 仅保留稳定 ID 和原 GUID，供旧群系/结构/Prefab 引用，不再保存数值、Behaviour 或 TileBase 配置。
- 当前地表自然物密度以 `Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Surface.asset` 的 `ecologyRules` 为权威；`BiomeData.TerrainConfig.ItemSpawn_NoSO` 属于旧生成链，不用于调整 WorldModel 生态数量。存档默认冻结首次使用时的生成 Profile；玩家可在存档管理页关闭“冻结世界生成规则”以显式跟随当前版本。关闭时只丢弃冻结 Profile，保留生态区块的删除 GUID、状态覆盖和恢复年份；重新开启后在下一次进入世界时冻结当时的当前配置。
- 草和可采集地表植被分别由 `ChunkGrassRenderer` 与 `ChunkGroundCoverRenderer` 批量绘制。`ecologyRules` 继续生成确定性数据点；物品定义声明 `groundCover: true` 时跳过自然 Item 实例化，采集才生成普通 Item。选格和图层共用 `GroundCoverSystem`，采集持久化复用生态删除 GUID；不得用草层消费状态记录花朵，也不得在图层解绑时把生成点标记为已采集。
- 洞穴可配置植物同样使用 `Assets/Resources/Config/WorldModel/ChunkGenerationProfile_Cave.asset` 的 `ecologyRules`；洞穴生成按“入口 → 配置植物 → 藤蔓/矿物”占格，出生安全区不生成配置植物。
- Chunk 运行时、生成调度和表现绑定改用 `flatworld-world-model`。

## 主链

`Tile/Biome/Structure 配置 → 地形生成规则 → ChunkTerrainData → 结构与自然物 → 差量存档 → WorldModel 表现`

## 边界

- 本 Skill 负责地图内容规则；WorldModel 负责 Chunk 生命周期、并发、租约和表现绑定。
- Tile 栈只通过 API 修改；静态 Blocking Tile 与动态建筑占地不要混用。
- Ground 永远保存真实底部地块，海洋、河流、湖泊、地下水的初始液体在生成阶段另外写入 `ChunkTerrainData`。修改地面不会自动改液体；抽水只走 `WorldLiquidSystem.TryPump/TrySet`，禁止抽水时生成底部或修改 Ground。平台通过 `TerrainSupportLayer` 遮断表面接触，底部液体仍保留。
- 世界液体来源统一由 `WorldLiquidSourceResolver` 读取权威 Liquid 层的深度与稳定 `LiquidId`，再解析 `LiquidDefinition`；不能按 Ground Tile、TerrainCellFlags、盐度或 Collider 猜身份。容器份数与世界液深是不同单位，不能未经规则换算直接互相扣减。液体接触直接使用 `WorldLiquidSourceTarget`，不继承 TileData，不注册地块 water 数据/行为或 MemoryPack Union。
- 河流生成把真实下游单位方向保存到 `riverFlowX/riverFlowY` 环境层；运行时水流玩法统一通过 `ChunkMgr.TryGetRuntimeWaterCurrent` 读取，禁止在物品、角色等消费方重复按邻格高度猜河道方向。海洋暂无独立洋流层时由该接口使用现有风场近似表层漂移，湖泊保持静止。
- `heightDriven` 地表河网必须保持“区域级累计 + 最多双接收 D∞ + 连续中心线重建”拓扑：三角坡面负责真实下坡主方向，低坡区可用确定性平滑旋度场增加曲率；可见河槽须经地形感知曲率松弛、Chaikin 圆角和亚格采样重新栅格化，避免直接把 D8 格子链当最终水体；真实接收格仍必须严格更低，不要退回单接收 D8/D∞，也不要把流量分给全部下坡邻格形成扇形水片。
- 生成保持固定种子、稳定 BiomeId/顺序和统一噪声、气候、水文规则。
- 修改算法时考虑生成签名、旧存档、联机指纹和 Wrapped 坐标。
- 雪山地表固定使用纯白 `Tile_Snow`，禁止按随机噪声混入雪地变体；若未来恢复变体，只能按温度区间确定。
- 萝卜聚落由 `surface.forest.radish` 与 `surface.grassland.radish` 两条独立规则控制；全局调整时必须同步审计两条，`PatchChance` 控制聚落数量，`SpawnChance` 与 `PatchRadius` 控制聚落内部密度。
- 洞穴入口联动 `flatworld-dimension`，可走性联动 `flatworld-navigation`，差量联动 `flatworld-data-save`。
- 地块可提供环境动作与被动效果定义，但共享 `TileBlockBehaviour` 只保存规则；玩家长按、Tick、环境倍率等实例状态必须留在角色侧运行器。
- 资源加载时由 JSON 构建 `RuntimeTileDefinition` 和每种定义自己的共享 Behaviour 集合；`type` 经 `TileBehaviourRegistry` 的显式工厂解析，禁止 CLR `$type` 或移动时反序列化。参数使用现有配置字段的 camelCase，私有 `[SerializeField]` 参数也须迁移；未知字段和无效数值必须失败，不得静默忽略。
- `TileData.ID/Name` 由定义 ID 注入，位置和工作进度不进入 JSON；单格读取使用 `CreateTileData/Clone`，不得修改共享模板。液体配置只来自 LiquidDefinition.worldWater，水体降温读取当前 C# 规则。
- 编辑器通过 `TileDefinitionEditorCatalog` 解析 ID 壳并显式保存 JSON，不可恢复 SO 与 JSON 双写。`GameRes.GetTileBlock` 返回 `RuntimeTileDefinition`；原 Behaviour 类和生命周期方法继续使用。

- 自然植物恢复资格由 `INaturalRenewalPolicy` 记录到生态存档的 `RenewalYears`；只有生成成功才清除移除标记和补位计划。玩家种植不参加自然补位，建筑、耕地及平台所在格不补野生植物。

- 正式世界修改和差量存档始终通过 ChunkRuntime 的独立 Liquid 层；Ground 数字 ID 不承担任何液体身份。

## 验证

- `WorldTopologyDomain` 是 `Shared/WorldTopology/FlatWorld.WorldTopology.asmdef` 中的纯数学坐标真源；Bounds 只负责配置/类型封装，Runtime 仍读取 SaveDataMgr，只允许主线程使用。禁止在 Map 或数学核心直接创建 Wrapped Physics Proxy。
- Tilemap 镜像由独立 `WrappedTilemapPhysicsAdapter` 消费表现生命周期：旧 Map 发布 `TilemapPresentationChanged`，新版 ChunkView 经 `ChunkCollisionRenderer` 的绑定/失效通知接入。不得假定地图一定由 ItemMgr.InstantiateItem 创建；重新加载开始、失败、停用、重新启用和增量编辑都须同步失效。
- Item 的严格接缝带与 Tilemap 的包含边界触边是两种调用语义，共用 Domain 的无分配镜像偏移查询，但不能合并阈值。旧 Map 的镜像 `TilemapDamageReceiver` 必须绑定真实 Map 与镜像 Tilemap，不能用源 Tilemap 坐标代替；新版 WorldModel 继续通过权威格子查询结算伤害。

- 功能验收以实际游戏操作和可观察结果为准；不以冒烟、自动化测试或静态检查代替实际验收。
- 世界生成与持久化改动实际覆盖新建世界、抽干露底、区块往返和保存重进；保留用户当前试玩时，不自动停止游戏或排队运行测试。

## Skill 维护原则

- 实验动态流向通过 `ChunkMgr.TryGetExperimentalLiquidFlow` 查询，休眠/停用后归零；不能覆写天然 `riverFlow*` 或持久化流向。`TryGetRuntimeWaterCurrent` 优先消费实际动态流量，关闭实验后仍沿用既有天然水文行为。
- 显式挡水建筑/MOD 通过 `WorldLiquidFlowObstacles.Register/Unregister` 登记世界格；不能从 Collider 或所有导航占地推断挡水。水上平台不自动阻断底部液体。液体批次存档使用 `SaveDataMgr.RecordLiquidBatch` 更新内存差量，不在 Liquid Tick 写磁盘。

- 只补充可复用的易错点、隐含约束和必要注意事项，不记录近期改动流水账。
