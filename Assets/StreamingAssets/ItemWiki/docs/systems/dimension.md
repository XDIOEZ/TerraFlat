# 维度系统

## 系统定位

负责同一星球下地表、地下矿洞等独立世界空间的地址、切换、入口、独立 Chunk 差量和环境覆盖。

## 当前地址规则

- 地表：`WorldKey = PlanetId`
- 非地表：`PlanetId__dimension__DimensionId`

业务代码必须通过 `WorldAddress` API 构造/解析地址，不直接拼字符串。

## 当前机制

- 每个 WorldKey 有独立 PlanetData / MapData / Chunk 差量。
- 新维度继承地表的拓扑模式、半径和 ChunkSize，但拥有独立运行时状态。
- 切换维度前先保存并释放当前世界与玩家，再创建目标动态 Scene 并运行目标 WorldKey。
- 目标玩家和完整活动 ChunkView 窗口准备完成后才解除输入锁。
- 玩家在不同维度的位置和入口锚点保存在 ItemSpecialData 的 `flatworld.dimensions` 命名空间。

## 洞穴

- 当前洞穴只使用 WorldModel 正式生成链，不维护旧 Map 洞穴生成器。
- 核心为 `DeterministicChunkGenerator + CaveLayoutKernel + CaveGenerationFeatureGenerator`。
- 洞穴参考地表时使用冻结的 `CavePortalPairingSnapshot` 做纯采样，不查询已加载地表 Chunk/Tilemap。
- 地表入口必须是已安装 MineEntrance；洞穴出口在基线生成后进入运行时差量。
- 洞穴默认抑制天气和怪物生成，并可使用固定光照上限。

## 当前限制

维度切换当前只开放离线流程；服务器权威迁移协议完成前不能直接开放联机维度旅行。

## 关键入口

- `Assets/5_Scripts/5-3_GamePlay/World/Dimension/WorldAddress.cs`
- `DimensionManager.cs`
- `DimensionPortal.cs`
- `DimensionCatalogSO.cs`
- `DimensionTravelProgressStore.cs`

## 对应 Skill

`.agents/skills/flatworld-dimension/SKILL.md`
