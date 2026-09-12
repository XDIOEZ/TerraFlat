# 建筑系统

## 系统定位

负责建筑召唤物、放置虚影、占地校验、安装、拆除、动态建筑、Tile 建筑、建筑状态转移和导航更新。

## 当前机制

- 建筑明确区分 `Summoner` 与 `PlacedBuilding` 两种角色，不用血量或位置猜测角色。
- 标准流程：手持 Summoner → `BuildingShadow` 预览/校验 → 提交放置 → 创建建筑本体或写入 Tile → 注册占地 → 标记导航变化。
- 拆除按反向流程产生携带 Snapshot 的 Summoner，返还完成后才删除原建筑。
- 新放置建筑本体必须根据 BuildingPrefabId 从 Item JSON 创建，不能复制召唤物数据再改 ID。
- 可手持又可放地的设施通过共享模块状态转移机制在 Summoner 与 BuildingBody 之间复制必要状态。

## 两类建筑

### 动态建筑

- 运行时是 GameObject + Collider + `BuildingOccupancyRegistry`。
- 建筑状态按区块 ChangedItems 差量保存。
- 门、容器、工作台等交互由独立 Module 提供。

### Tile 建筑

- 墙、地板/平台等进入 `ChunkTerrainData` 的 Tile/Support 权威层。
- Tile 建筑耐久也必须进入 RuntimeTileDeltas，不能只存在表现对象。
- 水上平台使用独立 `TerrainSupportLayer`，不修改底层水格。

## 数据来源

- Summoner：`GameConfig/Items/shells/building_summoners.json`
- BuildingBody：`GameConfig/Items/shells/building_bodies.json`
- 实现：`Assets/5_Scripts/5-3_GamePlay/World/Building/`

## 放置优先级

有效建筑放置模式开启时，普通世界交互必须让位给放置；附近可交互物不能抢占同一输入。

## 修改时联动

- 占地/Tile：地图与 WorldModel。
- 导航：Navigation。
- 建筑库存/工作台：背包与制作。
- 伤害：战斗。
- 拆除存档：存档与数据。

## 对应 Skill

`.agents/skills/flatworld-building/SKILL.md`
