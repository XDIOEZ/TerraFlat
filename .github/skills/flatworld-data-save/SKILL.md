---
name: flatworld-data-save
description: "Use when: 定位或修改 FlatWorld 的数据模型、MemoryPack 存档、自动保存、区块差量、玩家数据、星球数据、Addressables 配置或 Excel 配置。关键词：SaveDataMgr、GameSaveData、ItemData、ModuleData、MapSave、PlanetData。"
argument-hint: "数据、存档或资源注册问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 数据与存档定位

> 最后核对：2026-07-27。序列化字段改动属于高影响变更。

## 修改前先读

1. `Assets/5_Scripts/5-3_GamePlay/Manager/SaveDataMgr.cs`：磁盘读写、压缩、备份、差量、联机快照。
2. `Assets/5_Scripts/5-3_GamePlay/Map/Data/GameSaveData.cs`：总存档根对象。
3. `Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs`：物品数据与 `ModuleDataDic`。
4. `Assets/5_Scripts/5-1_Data/ModData/ModuleData.cs`：模块持久化基类。

## 权威数据链

```text
GameSaveData
├─ PlayerData_Dict → Data_Player
├─ PlanetData_Dict → PlanetData
│  └─ MapData_Dict → MapSave → ItemData → ModuleData
├─ DayTimeData
├─ Difficulty（partial）
├─ Mods（partial）
└─ MonsterSpawnerData（partial）
```

## 关键目录

- 基础数据：`Assets/5_Scripts/5-1_Data/CoreData/`。
- 物品数据：`Assets/5_Scripts/5-1_Data/ItemData/`。
- 模块数据：`Assets/5_Scripts/5-1_Data/ModData/`。
- 地块数据：`Assets/5_Scripts/5-1_Data/TileData/`。
- 存档 partial：`Assets/5_Scripts/5-3_GamePlay/Map/Data/GameSaveData.*.cs`。
- 地图存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/MapSave.cs`。
- 星球存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/PlanetData.cs`。
- 自动保存：`Assets/5_Scripts/5-3_GamePlay/Manager/AutoSaveController.cs`。
- Addressables：`Assets/AddressableAssetsData/`。
- Excel 配置：`Assets/GameConfig/Excel/`；读取工具为 `Assets/5_Scripts/Utilitiles/ExcelManager.cs`。

## 资源注册边界

- `Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs` 按 Addressables 标签注册 Prefab、Recipe、TileBase、TileBlock、Buff、InventoryInit、Skill。
- 仅移动资源文件并不能保证运行时可用；需同时验证 Addressables 地址/标签与 `GameRes` 字典键。
- 玩家正式存档位于 `Application.persistentDataPath`；`Assets/Saves/` 主要用于项目内编辑或测试资源。

## 易误判点

- `PlanetData` 是 partial class，另一部分位于 `Assets/5_Scripts/5-3_GamePlay/Space/PlanetData.cs`。
- 新增 MemoryPack 派生类型时必须检查 `MemoryPackUnion`、版本迁移和旧存档兼容。
- 联机世界快照由 `SaveDataMgr` 生成/应用，但网络传输流程位于 Networking Skill。

## 近期变更

- 2026-07-27：完成数据与存档系统首版索引；当前权威链仍为 `GameSaveData → PlanetData → MapSave → ItemData → ModuleData`。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/DataSave/DataSaveSmokeTests.cs`；当前基础覆盖SaveDataMgr 与 GameSaveData、ItemData、ModuleData 权威数据入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；存档测试约定目录：`Assets/GameTest/DataSave/`；场景目录：`Assets/GameTest/Scenes/DataSave/`；冒烟分类：`DataSave.Smoke`。
- 新增数据字段、MemoryPack Union、自动保存、区块差量或配置加载行为时必须增加往返测试；修复 Bug 时先增加回归测试。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；不得写入玩家真实存档，必须使用临时路径并验证序列化前后关键字段一致。
- 完成修改后检查 Unity 编译和 Console，再运行 `DataSave.Smoke`；涉及地图、Item/Module、建筑、对话一次性标记或联机快照时同步运行对应系统测试。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

新增/移动数据类、存档 partial、配置表、Addressables 标签、资源目录或序列化迁移入口后，必须更新本 Skill 的数据链、路径和近期变更；不要只记录类名而遗漏持久化位置。
