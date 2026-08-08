---
name: flatworld-building
description: "Use when: 定位或修改 FlatWorld 的建筑放置预览、安装拆除、召唤器/世界建筑角色、建筑快照、占地、门、堆肥、结构生成、建筑 Prefab 或结构编辑器。关键词：Mod_Building、BuildingShadow、BuildingOccupancyRegistry。"
argument-hint: "建筑放置、拆除或占地问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 建造系统定位

> 最后核对：2026-08-08。建筑占地会直接影响导航。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Building.cs`：放置、拆除、状态、角色、快照和联机事务。
2. `Assets/5_Scripts/5-3_GamePlay/World/Building/BuildingShadow.cs`：放置预览与候选位置。
3. `Assets/5_Scripts/5-3_GamePlay/World/Building/BuildingOccupancyRegistry.cs`：运行时动态占地。
4. `Assets/5_Scripts/5-3_GamePlay/World/PathFinding/AstarGameManager.cs`：占地变化后的导航更新。

## 核心模型

```text
Summoner（库存中的持久化载体）
→ BuildingShadow 预览与校验
→ 创建 PlacedBuilding（世界实例）
→ 注册 BuildingOccupancyRegistry
→ 提交导航脏格

拆除：PlacedBuilding
→ 生成带 Snapshot 的 Summoner
→ 成功后删除原建筑
```

## 关键文件与资源

- 门：`Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_Door.cs`。
- 堆肥箱：`Assets/5_Scripts/5-3_GamePlay/World/Building/Mod_CompostBin.cs`。
- 结构生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/ChunkGenerator_Structures.cs`。
- 结构数据：`Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureData.cs`。
- 结构作者组件：`Assets/5_Scripts/5-3_GamePlay/World/Map/Structures/StructureItemAuthoring.cs`。
- 结构 SO：`Assets/4_ScriptObjects/4-9_Structures/`。
- 结构目录：`Assets/Resources/Config/StructureCatalog_Default.asset`。
- 静态 Tile 阻挡层：`Assets/5_Scripts/5-3_GamePlay/World/Map/BlockingTilemapLayer.cs`。
- 建筑 Prefab：`Assets/2_Prefabs/Building/`。
- 正式矿坑建筑：`Assets/2_Prefabs/Building/MineEntrance.prefab`；对应召唤器为 `Assets/2_Prefabs/Building/Summoners/MineEntrance_Summoner.prefab`。
- 编辑器工具：`Assets/5_Scripts/5-2_Editor/Structures/`。

## 约束

- `BuildingRole` 明确区分 `Summoner` 与 `PlacedBuilding`，禁止再用血量或位置推断角色。
- 动态占地不修改地形 `TileData`，由 `BuildingOccupancyRegistry` 叠加阻挡。
- “建筑阻挡层”只处理矿洞岩壁、地牢墙体、结构模板墙体等静态 Tile 障碍；数据顶层需使用 `TileTag=Blocking` 且 `IsWalkable=false`。玩家放置/拆除的动态建筑仍是 GameObject，并继续注册 `BuildingOccupancyRegistry`，不得迁移到 Tilemap。
- 静态地形只能通过 `Data_TileMap` 的地形栈 API 读取顶层或修改层级；建筑占地保持独立，不得直接取得或改写格子的可变 Tile 列表。
- 放置和拆除必须保持事务顺序，失败时不能同时丢失召唤器和世界建筑。
- 联机权威校验位于 `Mod_Building` 与网络序列化桥接，客户端预览不能作为服务端最终依据。
- 教程进度事件必须使用请求开始时捕获的 `_placementActor`：单机仅在创建建筑且成功消耗召唤器后发布；联机仅在 accepted 回执并应用权威剩余数量后发布。Reject、创建失败或 actor 丢失路径不得发布。
- 矿坑入口的 `DimensionPortal` 与交互 Trigger 必须位于 Item 根 GameObject；Summoner 会复制该组件，但必须通过 `BuildingRole.Summoner`/未安装状态拒绝维度切换。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；Prefab 外观或放置预览配色变化不要扩散检查。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `BuildingOccupancyRegistry`、占地格计算、安装/拆除时的注册顺序 | `flatworld-navigation`、`flatworld-map` | 动态占地不写 TileData，提交对应导航脏格且无残留 | `Navigation.Smoke`、`Map.Smoke` |
| Summoner/PlacedBuilding 角色、放置消耗或拆除返还事务 | `flatworld-item-module`、`flatworld-inventory-crafting` | Item 创建/销毁与库存扣除/返还原子完成 | `ItemModule.Smoke`、`InventoryCrafting.Smoke` |
| Building Snapshot、Chunk 归属或持久化字段 | `flatworld-data-save` | 安装/拆除和卸载重载后角色、GUID 与模块数据一致 | `DataSave.Smoke` |
| 服务端放置校验、accepted/reject 回执或网络剩余数量 | `flatworld-networking` | 客户端预览不成为权威，成功事件只在最终提交点发布 | `Networking.Smoke` |
| `MineEntrance`、`DimensionPortal` 或入口锚点 | `flatworld-dimension` | Summoner 不可传送，已安装入口与 CaveExit 稳定绑定 | `Dimension.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-08：`Mod_Building.TryGetBuildingPreviewVisual()` 统一暴露真实预览图片/根节点/占地；`BuildingShadow` 仅在本体材质有效时继承，否则保留 Prefab 默认 Sprite 材质，避免旧建筑材质 GUID 丢失后虚影再次变空。
- 2026-08-08：建筑召唤器的预览与最终放置改为优先读取 `ChunkMgr.TryGetRuntimeTerrainTile()` 的 `ChunkRuntime/ChunkTerrainData` 权威地块，旧 `Chunk.Map` 仅作兼容回退；`BuildingShadow` 继承建筑本体材质并使用 `Shadow` 排序层，修复新区块下“地块尚未加载”和虚影不显示。
- 2026-08-05：静态结构与阻挡层迁移到连续 `TileStackCell` API；动态建筑继续只叠加 `BuildingOccupancyRegistry`，导航同时读取地形栈顶层与建筑占用。
- 2026-07-31：新增正式 `MineEntrance`/`MineEntrance_Summoner` 建筑对；安装后的建筑可进入矿洞，召唤器不可触发，拆除与 Chunk 存档继续复用建筑快照事务。
- 2026-07-31：新增通用静态“建筑阻挡层” Tilemap；明确只承载地图生成的静态墙体，动态建造系统继续使用建筑实体、快照和占地注册表。
- 2026-07-30：结构作者物件新增稳定 `MemberId` 和容器槽位配置；遗迹中的箱子等带 `Mod_Inventory` 建筑可在结构编辑器中选择目标库存并按真实槽位配置物品。Loot Marker 仍只负责按 `ContentId` 生成物件。
- 2026-07-29：统一内容校验器检查建筑本体/召唤器双向配对、角色、`BuildingPrefabId`/`SummonerPrefabId`、嵌入 BitData、可拾取状态以及 `DamageReceiver`、`BoxCollider2D`、`SpriteRenderer`、`BuildingShadow` 必填引用。
- 2026-07-28：`Mod_Building` 在放置事务中暂存玩家 actor，并于单机提交或联机 accepted 回执后的最终成功点发布 `GameplayProgressEvents.BuildingPlaced`。
- 2026-07-27：建筑占地已从地形数据中分离，统一通过 `BuildingOccupancyRegistry` 提交导航脏格。
- 2026-07-27：建筑使用召唤器/世界实例双角色与嵌入式快照事务。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Building/BuildingSmokeTests.cs`；当前基础覆盖建筑模块、动态占地、放置预览 Prefab、虚影材质/排序层与结构目录入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；建筑测试约定目录：`Assets/GameTest/Building/`；场景目录：`Assets/GameTest/Scenes/Building/`；冒烟分类：`Building.Smoke`。
- 新增放置、占地、安装拆除或建筑快照行为时必须增加系统测试；修复 Bug 时先增加回归测试。建筑主流程变化时同步更新建筑冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试创建的占地、Prefab 和临时快照必须清理。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Building.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有放置预览或最终外观变化才做定向截图。
- 建筑教程事件的 actor、稳定 ID 与成功锚点由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 建筑占地与地形共同控制可走性的关键行为由 `Assets/GameTest/Navigation/NavigationSmokeTests.cs`（`Navigation.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变建筑角色、快照版本、Prefab 命名、占地算法、放置校验、结构资源路径或编辑器烘焙流程后，必须更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。
