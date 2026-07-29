---
name: flatworld-building
description: "Use when: 定位或修改 FlatWorld 的建筑放置预览、安装拆除、召唤器/世界建筑角色、建筑快照、占地、门、堆肥、结构生成、建筑 Prefab 或结构编辑器。关键词：Mod_Building、BuildingShadow、BuildingOccupancyRegistry。"
argument-hint: "建筑放置、拆除或占地问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 建造系统定位

> 最后核对：2026-07-27。建筑占地会直接影响导航。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Building/Mod_Building.cs`：放置、拆除、状态、角色、快照和联机事务。
2. `Assets/5_Scripts/5-3_GamePlay/Building/BuildingShadow.cs`：放置预览与候选位置。
3. `Assets/5_Scripts/5-3_GamePlay/Building/BuildingOccupancyRegistry.cs`：运行时动态占地。
4. `Assets/5_Scripts/5-3_GamePlay/PathFinding/AstarGameManager.cs`：占地变化后的导航更新。

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

- 门：`Assets/5_Scripts/5-3_GamePlay/Building/Mod_Door.cs`。
- 堆肥箱：`Assets/5_Scripts/5-3_GamePlay/Building/Mod_CompostBin.cs`。
- 结构生成：`Assets/5_Scripts/5-3_GamePlay/Map/Structures/ChunkGenerator_Structures.cs`。
- 结构数据：`Assets/5_Scripts/5-3_GamePlay/Map/Structures/StructureData.cs`。
- 结构作者组件：`Assets/5_Scripts/5-3_GamePlay/Map/Structures/StructureItemAuthoring.cs`。
- 结构 SO：`Assets/4_ScriptObjects/4-9_Structures/`。
- 结构目录：`Assets/Resources/Config/StructureCatalog_Default.asset`。
- 建筑 Prefab：`Assets/2_Prefabs/Building/`。
- 编辑器工具：`Assets/5_Scripts/5-2_Editor/Structures/`。

## 约束

- `BuildingRole` 明确区分 `Summoner` 与 `PlacedBuilding`，禁止再用血量或位置推断角色。
- 动态占地不修改地形 `TileData`，由 `BuildingOccupancyRegistry` 叠加阻挡。
- 放置和拆除必须保持事务顺序，失败时不能同时丢失召唤器和世界建筑。
- 联机权威校验位于 `Mod_Building` 与网络序列化桥接，客户端预览不能作为服务端最终依据。
- 教程进度事件必须使用请求开始时捕获的 `_placementActor`：单机仅在创建建筑且成功消耗召唤器后发布；联机仅在 accepted 回执并应用权威剩余数量后发布。Reject、创建失败或 actor 丢失路径不得发布。

## 近期变更

- 2026-07-29：统一内容校验器检查建筑本体/召唤器双向配对、角色、`BuildingPrefabId`/`SummonerPrefabId`、嵌入 BitData、可拾取状态以及 `DamageReceiver`、`BoxCollider2D`、`SpriteRenderer`、`BuildingShadow` 必填引用。
- 2026-07-28：`Mod_Building` 在放置事务中暂存玩家 actor，并于单机提交或联机 accepted 回执后的最终成功点发布 `GameplayProgressEvents.BuildingPlaced`。
- 2026-07-27：建筑占地已从地形数据中分离，统一通过 `BuildingOccupancyRegistry` 提交导航脏格。
- 2026-07-27：建筑使用召唤器/世界实例双角色与嵌入式快照事务。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/Building/BuildingSmokeTests.cs`；当前基础覆盖建筑模块、动态占地、放置预览 Prefab 与结构目录入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；建筑测试约定目录：`Assets/GameTest/Building/`；场景目录：`Assets/GameTest/Scenes/Building/`；冒烟分类：`Building.Smoke`。
- 新增放置、占地、安装拆除或建筑快照行为时必须增加系统测试；修复 Bug 时先增加回归测试。建筑主流程变化时同步更新建筑冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；测试创建的占地、Prefab 和临时快照必须清理。
- 完成修改后检查 Unity 编译和 Console，再运行 `Building.Smoke`；涉及导航、地图、存档或联机事务时同步运行对应系统测试。
- 建筑教程事件的 actor、稳定 ID 与成功锚点由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

改变建筑角色、快照版本、Prefab 命名、占地算法、放置校验、结构资源路径或编辑器烘焙流程后，必须更新本 Skill；涉及导航或联机时同步更新对应 Skill。
