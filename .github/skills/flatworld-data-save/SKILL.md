---
name: flatworld-data-save
description: "Use when: 定位或修改 FlatWorld 的数据模型、MemoryPack 存档、自动保存、区块差量、玩家数据、星球数据、Addressables 配置或 Excel 配置。关键词：SaveDataMgr、GameSaveData、ItemData、ModuleData、MapSave、PlanetData。"
argument-hint: "数据、存档或资源注册问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 数据与存档定位

> 最后核对：2026-07-29。序列化字段改动属于高影响变更。

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
├─ Difficulty（partial：官方/自定义类型、规则版本与 17 项自定义规则）
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
- `ItemSpecialData` 命名空间合并：`Assets/5_Scripts/5-3_GamePlay/Progress/ItemSpecialDataJsonStore.cs`。
- Addressables：`Assets/AddressableAssetsData/`。
- Excel 配置：`Assets/GameConfig/Excel/`；读取工具为 `Assets/5_Scripts/Utilitiles/ExcelManager.cs`。

## 资源注册边界

- `Assets/5_Scripts/5-3_GamePlay/Manager/GameRes.cs` 按 Addressables 标签注册 Prefab、TileBase、TileBlock、Buff、InventoryInit、Skill；配方由 `Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json` 聚合业务分包后注册。
- 仅移动资源文件并不能保证运行时可用；需同时验证 Addressables 地址/标签与 `GameRes` 字典键。
- 配方编辑源为 `Assets/GameConfig/Excel/RecipeConfig.xlsx`，由 Editor 工具整表校验并根据 `Recipes.Package` 导出 8 个业务 JSON；运行时不读取 Excel。
- 玩家正式存档位于 `Application.persistentDataPath`；`Assets/Saves/` 主要用于项目内编辑或测试资源。
- 新建存档首写与已有存档进入的等待反馈由 `GameManager` 的 `UI_WorldLoading.prefab` 生命周期负责；`SaveDataMgr` 仍只承担磁盘读写，不得直接创建 UI。

## 易误判点

- `PlanetData` 是 partial class，另一部分位于 `Assets/5_Scripts/5-3_GamePlay/Space/PlanetData.cs`。
- 新增 MemoryPack 派生类型时必须检查 `MemoryPackUnion`、版本迁移和旧存档兼容。
- 世界难度位于 `GameSaveData.Difficulty.cs`：`Difficulty` 保存官方预设或自定义类型，`CustomDifficultyDataVersion` 当前为 1，另有死亡掉落开关与战斗、生存、世界、生产共 16 个倍率字段。读写必须通过 `GameDifficultyCatalog.ReadCustomRules()` / `WriteCustomRules()`；旧存档版本为 0 时保留旧死亡掉落值，其余新增倍率统一迁移为 100%。
- 联机世界快照由 `SaveDataMgr` 生成/应用，但网络传输流程位于 Networking Skill。
- 对话与教程不得各自覆盖 `Data_Player.ItemSpecialData`：统一通过 `ItemSpecialDataJsonStore` 替换目标命名空间并保留未知根属性；旧非 JSON 字符串保存到 `flatworld.legacyItemSpecialData`。
- 新手引导状态位于 `flatworld.tutorial`，保存 `eligible`、幂等 `milestones`、派生 `stage` 与 `completed`；新玩家资格需跨保存保留，旧存档无资格标记时默认禁用。禁止为教程修改 `Data_Player` 的 MemoryPack 布局。
- `SpawnerProgressSaveData.DataVersion` 当前为 1；新增字段保存生成总时间游标、可用生态预算、最后预算恢复日和待补位数量，旧版本加载时必须重置游标避免重放历史周期。
- `SerializableTimeData.TotalDays` 是世界时间存档的一部分，不能仅保存日内 `CurrentTime`。

## 近期变更

- 2026-07-29：新建存档首写和进入已选存档接入 Prefab 加载反馈；存档层职责不变，界面由 GameManager 驱动并持续到玩家周围区块就绪。
- 2026-07-29：新增统一只读内容校验入口，检查本体 Prefab/物品注册键、Addressables `Assets/2_Prefabs` 的 `Prefab` 标签，以及天气、结构、Spawner、联机玩家、音频和对话的固定 Resources 路径；正式构建前有错误会被阻断。
- 2026-07-29：自定义难度存档升级为版本 1，新增 16 个倍率字段并加入旧存档默认 100% 的迁移保护。
- 2026-07-29：世界难度存档支持 `Custom` 类型与自定义死亡掉落规则；新世界创建前选择会随首个存档一起写入。
- 2026-07-29：生成器存档增加版本、跨窗口总时间游标、生态预算与补位债务；时间存档补齐 `TotalDays`。
- 2026-07-28：配方存储由单 JSON 改为清单驱动的业务分包；编辑层统一、存储层分包、运行时聚合注册。
- 2026-07-28：新增 `ItemSpecialDataJsonStore` 命名空间合并；对话使用 `flatworld.dialogue`，教程使用 `flatworld.tutorial`，并兼容保留旧非 JSON 数据。
- 2026-07-28：配方配置脱离 ScriptableObject/Addressables，改为 Excel 编辑、JSON 运行时加载；`GameRes` 建立配方 ID 与输入签名双索引。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/DataSave/DataSaveSmokeTests.cs`；当前基础覆盖 SaveDataMgr、GameSaveData、ItemData、ModuleData 权威入口、生成周期/预算字段、自定义难度规则往返与版本 0 迁移默认值。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；存档测试约定目录：`Assets/GameTest/DataSave/`；场景目录：`Assets/GameTest/Scenes/DataSave/`；冒烟分类：`DataSave.Smoke`。
- 新增数据字段、MemoryPack Union、自动保存、区块差量或配置加载行为时必须增加往返测试；修复 Bug 时先增加回归测试。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；不得写入玩家真实存档，必须使用临时路径并验证序列化前后关键字段一致。
- 完成修改后检查 Unity 编译和 Console，再运行 `DataSave.Smoke`；涉及地图、Item/Module、建筑、对话一次性标记或联机快照时同步运行对应系统测试。
- 教程存档、旧数据兼容及命名空间共存由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

新增/移动数据类、存档 partial、配置表、Addressables 标签、资源目录或序列化迁移入口后，必须更新本 Skill 的数据链、路径和近期变更；不要只记录类名而遗漏持久化位置。
