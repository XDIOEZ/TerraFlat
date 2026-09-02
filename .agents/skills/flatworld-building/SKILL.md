---
name: flatworld-building
description: "Use when: 定位或修改 FlatWorld 的建筑放置预览、安装拆除、召唤器/世界建筑角色、建筑快照、占地、门、堆肥、结构生成、建筑 Prefab 或结构编辑器。关键词：Mod_Building、BuildingShadow、BuildingOccupancyRegistry。"
---

# FlatWorld 建造

## 入口

- 主链：`Assets/5_Scripts/5-3_GamePlay/World/Building/{Mod_Building,BuildingShadow,BuildingOccupancyRegistry}.cs`
- 建筑 JSON：`Assets/StreamingAssets/GameConfig/Items/shells/{building_summoners,building_bodies}.json`；召唤器统一使用 `BuildingSummonerShell`，动态本体统一使用 `BuildingBodyShell`。
- 建筑 Shell/模块迁移：`Assets/Editor/FlatWorld/ContentTools/Migrations/BuildingShellMigrationTool.cs`；差异玩法位于 `Assets/2_Prefabs/Gameplay/Modules/Building/`。
- 门/堆肥：同目录 `Mod_Door.cs`、`Mod_CompostBin.cs`
- 结构：`World/Map/Structures/{ChunkGenerator_Structures,StructureData,StructureItemAuthoring}.cs`
- 结构资源：`Assets/4_ScriptObjects/World/Structures/`

## 核心链与不变量

`Summoner → BuildingShadow 校验 → PlacedBuilding → 注册占地 → 导航脏格`；拆除反向生成带 Snapshot 的 Summoner，成功后才删除建筑。

- 以 `BuildingRole` 区分 Summoner/PlacedBuilding，禁止用血量或位置推断。
- 无快照的新放置必须通过 `GameRes.CreateItemData(BuildingPrefabId)` 创建本体 JSON 数据；禁止再克隆 Summoner 数据后改 ID。拆除快照仍由 Summoner 携带并优先恢复。
- 通用建筑本体只提供 `Item + SpriteRenderer + BoxCollider2D`；伤害由 JSON `health` 注入，门、容器、工作台等反馈由独立 `IInteractable` Module 提供，不再依赖通用 `Mod_InteractReciver` 转发。
- 手持火把与建筑火把职责不同：手持物保持 `Torch`，建筑本体使用 `Torch_Building`，`Torch_Summoner` 只能指向后者。
- 动态建筑保持 GameObject + Collider + `BuildingOccupancyRegistry`，不得写入地形 `TileData`。
- 静态岩壁/结构墙才使用 Blocking Tile；例如 `Wall_Stone` 只有 Summoner JSON，不创建动态本体定义。Tile 栈只通过 `Data_TileMap` API 读写。
- 新版 WorldModel 的玩家格子建筑虽使用 `ChunkTerrainData.BlockingTileId`，仍必须接入存档的运行时区块差量；不能只依赖 `MapSave.items`。
- 新版 WorldModel 的动态建筑 Item 不挂旧 `Chunk.RunTimeItems`；必须按 `Mod_Building` 的角色筛选，在 `ChangedItems` 中保存完整 `ItemData`，并在区块就绪后恢复模块状态/耐久。
- `BuildingShadow` 的 `sourceRenderer` 与 `sourceRoot` 必须来自同一对象层级；共享本体 Shell 资源本身没有 Sprite，预览应从 `RuntimeItemDefinition` 创建无模块的轻量预览源，同时使用本体 JSON Collider 计算占地，禁止回退到召唤器图标或共享 Shell 默认尺寸。
- 带 `Building_Data.TileBlockId` 的建筑最终由 `TileBuildingSystem` 写入 Tilemap，预览图片与占地范围必须以格心为锚点，不能继承本体 Sprite 子节点的局部偏移。
- 手机准星可以停在最大建造半径；格心吸附会产生每轴最多半格的偏差，距离校验应按目标格最近边缘判断，禁止直接用吸附后格心距离或 `Ceil` 取整决定预览与放置资格。
- Summoner 只能由快捷栏的真实手持实例放置：`IsItemInInventory`、`BuildingShadow` 与源槽扣减都依赖 `InHand + Owner + CurrentSelectItemSlot`；库存菜单不得临时实例化 Summoner 后直接 `Act`。
- `GamePlay` 程序集不能反向引用已依赖它的 `FlatWorld.Dialogue`；放置失败等玩家反馈由玩法层发布语义事件，再由 Dialogue 表现桥接，并且只能在实际 `Install` 提交失败时发布，禁止从逐帧虚影校验中触发。
- 建筑模块对 `DamageReceiver` 等模块的依赖必须在加载阶段从 `ItemMods` 注册表解析；禁止序列化嵌套模块 Prefab 的组件引用，模块缺失修复后原引用可能成为无效组件。
- 占地算法或安装/拆除顺序变化时联动 `flatworld-navigation` 与 `flatworld-map`。

## 验证

- 检查预览与最终占地一致、注册/注销成对、失败路径无残留、快照可还原。
- 默认做静态诊断、编译和 Console；需要时运行 `Building.Smoke`，真实放置链可用 Golden Path `building.placement`。
- 测试入口：`Assets/GameTest/Building/BuildingSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
