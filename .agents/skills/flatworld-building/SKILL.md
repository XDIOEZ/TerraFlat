---
name: flatworld-building
description: "Use when: 定位或修改 FlatWorld 的建筑放置预览、安装拆除、召唤器/世界建筑角色、建筑快照、占地、门、堆肥、结构生成、建筑 Prefab 或结构编辑器。关键词：Mod_Building、BuildingShadow、BuildingOccupancyRegistry。"
---

# FlatWorld 建造

## 入口

- 主链：`Assets/5_Scripts/5-3_GamePlay/World/Building/{Mod_Building,BuildingShadow,BuildingOccupancyRegistry}.cs`
- 建筑召唤器物品定义：`Assets/StreamingAssets/GameConfig/Items/shells/building_summoners.json`；同一类别文件包含 13 个召唤器各自的抽象基类和具体定义。
- 门/堆肥：同目录 `Mod_Door.cs`、`Mod_CompostBin.cs`
- 结构：`World/Map/Structures/{ChunkGenerator_Structures,StructureData,StructureItemAuthoring}.cs`
- 结构资源：`Assets/4_ScriptObjects/World/Structures/`

## 核心链与不变量

`Summoner → BuildingShadow 校验 → PlacedBuilding → 注册占地 → 导航脏格`；拆除反向生成带 Snapshot 的 Summoner，成功后才删除建筑。

- 以 `BuildingRole` 区分 Summoner/PlacedBuilding，禁止用血量或位置推断。
- 动态建筑保持 GameObject + Collider + `BuildingOccupancyRegistry`，不得写入地形 `TileData`。
- 静态岩壁/结构墙才使用 Blocking Tile；Tile 栈只通过 `Data_TileMap` API 读写。
- `BuildingShadow` 的 `sourceRenderer` 与 `sourceRoot` 必须来自同一对象层级；本体 prefab 可能没有 SpriteRenderer，此时只能用手持实例的 Sprite 和手持实例根节点配对，禁止跨对象计算局部偏移。
- 占地算法或安装/拆除顺序变化时联动 `flatworld-navigation` 与 `flatworld-map`。

## 验证

- 检查预览与最终占地一致、注册/注销成对、失败路径无残留、快照可还原。
- 默认做静态诊断、编译和 Console；需要时运行 `Building.Smoke`，真实放置链可用 Golden Path `building.placement`。
- 测试入口：`Assets/GameTest/Building/BuildingSmokeTests.cs`。

## Skill 维护原则

- 只补充后续维护可复用的易错点、隐含约束和必要注意事项。
- 不记录修改日期、近期变更或仅描述本次改动内容的流水账。
