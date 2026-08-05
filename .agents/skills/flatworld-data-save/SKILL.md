---
name: flatworld-data-save
description: "Use when: 定位或修改 FlatWorld 的数据模型、MemoryPack 存档、自动保存、区块差量、玩家数据、星球数据、Addressables 配置或 Excel 配置。关键词：SaveDataMgr、GameSaveData、ItemData、ModuleData、MapSave、PlanetData。"
argument-hint: "数据、存档或资源注册问题"
user-invocable: true
disable-model-invocation: false
---

# FlatWorld 数据与存档定位

> 最后核对：2026-08-05。序列化字段改动属于高影响变更。

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
- 地图栈存储：`Assets/5_Scripts/5-1_Data/ItemData/Data_TileMap.cs`、`Assets/5_Scripts/5-1_Data/TileData/TileStackCell.cs`。
- 存档 partial：`Assets/5_Scripts/5-3_GamePlay/Map/Data/GameSaveData.*.cs`。
- 地图存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/MapSave.cs`。
- 星球存档：`Assets/5_Scripts/5-3_GamePlay/Map/Data/PlanetData.cs`。
- 自动保存：`Assets/5_Scripts/5-3_GamePlay/Manager/AutoSaveController.cs`。
- `ItemSpecialData` 命名空间合并：`Assets/5_Scripts/5-3_GamePlay/Progress/ItemSpecialDataJsonStore.cs`。
- 维度位置与入口锚点进度：`Assets/5_Scripts/5-3_GamePlay/Dimension/DimensionTravelProgressStore.cs`。
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
- 新增 MemoryPack 派生类型时必须检查 `MemoryPackUnion` 与格式版本。当任务明确不兼容旧布局时，必须在解析入口明确拒绝，不得静默迁移、覆盖或删除用户文件。
- `Data_TileMap` 当前 MemoryPack 布局持久化 `TileStackCell[,]`；单层/双层格不分配 `OverflowLayers`，第三层起才分配。非空格计数缓存不进存档。
- 环境只持久化五张网格：归一化温度、摄氏温度、降水、高度、光照；不得恢复湿度、固体比例或污染网格。
- 区块差量 DTO 仍用有序 Tile 列表表达单格层级，但基线哈希、捕获、应用和复用缓冲区都必须通过地形栈 API，不得反射或暴露底层数组。
- 当前 `CompactSaveVersion=2`、`ModdedSaveVersion=2`。版本 1 和无文件头旧二进制存档返回 `SaveVersionIncompatibleException`；正式文件版本不兼容时不得回退到备份伪装成恢复成功。
- 世界难度位于 `GameSaveData.Difficulty.cs`：`Difficulty` 保存官方预设或自定义类型，`CustomDifficultyDataVersion` 当前为 1，另有死亡掉落开关与战斗、生存、世界、生产共 16 个倍率字段。读写必须通过 `GameDifficultyCatalog.ReadCustomRules()` / `WriteCustomRules()`；旧存档版本为 0 时保留旧死亡掉落值，其余新增倍率统一迁移为 100%。
- 联机世界快照由 `SaveDataMgr` 生成/应用，但网络传输流程位于 Networking Skill。
- 对话与教程不得各自覆盖 `Data_Player.ItemSpecialData`：统一通过 `ItemSpecialDataJsonStore` 替换目标命名空间并保留未知根属性；旧非 JSON 字符串保存到 `flatworld.legacyItemSpecialData`。
- 新手引导状态位于 `flatworld.tutorial`，保存 `eligible`、幂等 `milestones`、派生 `stage` 与 `completed`；新玩家资格需跨保存保留，旧存档无资格标记时默认禁用。禁止为教程修改 `Data_Player` 的 MemoryPack 布局。
- `SpawnerProgressSaveData.DataVersion` 当前为 1；新增字段保存生成总时间游标、可用生态预算、最后预算恢复日和待补位数量，旧版本加载时必须重置游标避免重放历史周期。
- `SerializableTimeData.TotalDays` 是世界时间存档的一部分，不能仅保存日内 `CurrentTime`。
- `PlanetData` 保存权威天气事件：`WeatherDataVersion`、`WeatherPhase`、阶段开始/结束绝对总时间、下一事件总时间、`WeatherRandomCursor` 与 `WeatherEventSequence`；剩余时间必须由绝对边界减当前总时间得到，禁止另存递减值。
- 玩家播种作物的权威状态位于 `GrowData`：保存 `GrowProgress`、`growState`、`plantedTilePos`、`isCultivatedCrop`、`isMature`、`isHarvested`、`growthStatus` 与环境初始化状态；`Mod_Grow.ReadGrowthDataWithMigration()` 兼容读取旧版六字段成长数据。
- `Item.ModuleLoad()` 在模块缺失自动修复之前删除 Apple 的旧种子模块数据和 AppleTree 的旧生产模块数据，确保旧区块差量不会把废弃农业链重新实例化。
- 耕地水分和肥力继续随 `TileData_Farmland` 进入 Tile 差量；最大肥力使用非序列化常量边界，禁止为固定上限无意义改变旧 TileData 二进制布局。
- 维度不新增 MemoryPack 字段：地表继续以旧 `PlanetId` 为 `PlanetData_Dict` 键，非地表使用 `PlanetId__dimension__DimensionId`，从而隔离各维度 `MapData_Dict`；玩家每维度位置和矿坑入口/出口锚点通过 `flatworld.dimensions` 写入 `ItemSpecialData`。
- `CaveExit` 必须在程序化 Chunk 基线捕获完成后创建；这样新出口会进入 `ChunkSaveRecord.ChangedItems`。只扫描 `MapSave.items` 会漏掉差量新增 Item。

## 高耦合联动

只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；仅调整编辑器显示名或未参与持久化的字段不要扩散检查。

| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `GameSaveData` 根、磁盘读写、备份、自动保存或首存档顺序 | `flatworld-core` | 新建/继续/退出期间只写目标存档，失败恢复不留下半状态 | `Core.Smoke` |
| `ItemData`、`ModuleData`、MemoryPack Union 或模块序列化布局 | `flatworld-item-module`，并只加载实际数据所属玩法 Skill | 旧数据迁移、模块 ID/类型与 Prefab 挂载仍匹配 | `ItemModule.Smoke` 加对应玩法 Smoke |
| `PlanetData`、`MapSave`、TileData、Chunk 差量或 `WorldKey` | `flatworld-map`、`flatworld-dimension` | 地表旧键兼容、维度隔离、基线与 ChangedItems 往返一致 | `Map.Smoke`、`Dimension.Smoke` |
| 压缩世界快照、MOD 记录或兼容版本 | `flatworld-networking`、`flatworld-modding` | 网络快照可往返，MOD 集合/协议不接受不兼容存档 | `Networking.Smoke`、`Modding.Smoke` |

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-05：`Data_TileMap` 由每格 `List<TileData>` 切换为 MemoryPack `TileStackCell[,]`，环境固定五张网格；紧凑存档与 MOD 封装升到版本 2，明确拒绝版本 1 和无头旧二进制数据。

- 2026-07-31：`flatworld.dimensions` 新增按地表入口 GUID 保存的矿坑双向锚点；正式 `CaveExit` 在 Chunk Ready 后创建并进入差量 `ChangedItems`，未改变 MemoryPack 布局。
- 2026-07-31：新增维度存档隔离；以兼容旧地表键的 `WorldKey` 复用 `PlanetData_Dict`，玩家各维度位置进入 `flatworld.dimensions`，未改变 MemoryPack 布局。
- 2026-07-30：星球存档追加天气阶段、绝对时间边界、确定性随机游标和事件序号；旧存档由 `WeatherEventScheduler.InitializeIfNeeded()` 根据已有天气迁移。
- 2026-07-30：作物存档补齐种植格、成长阶段、成熟、已收获和反馈状态；旧 `GrowData` BitData 可迁移，区块卸载重载后不会重置成长或再次收获。
- 2026-07-29：新建存档首写和进入已选存档接入 Prefab 加载反馈；存档层职责不变，界面由 GameManager 驱动并持续到玩家周围区块就绪。
- 2026-07-29：新增统一只读内容校验入口，检查本体 Prefab/物品注册键、Addressables `Assets/2_Prefabs` 的 `Prefab` 标签，以及天气、结构、Spawner、联机玩家、音频和对话的固定 Resources 路径；正式构建前有错误会被阻断。
- 2026-07-29：自定义难度存档升级为版本 1，新增 16 个倍率字段并加入旧存档默认 100% 的迁移保护。
- 2026-07-29：世界难度存档支持 `Custom` 类型与自定义死亡掉落规则；新世界创建前选择会随首个存档一起写入。
- 2026-07-29：生成器存档增加版本、跨窗口总时间游标、生态预算与补位债务；时间存档补齐 `TotalDays`。
- 2026-07-28：配方存储由单 JSON 改为清单驱动的业务分包；编辑层统一、存储层分包、运行时聚合注册。

## 修改后自动测试

- 基础测试脚本：`Assets/GameTest/DataSave/DataSaveSmokeTests.cs`；当前基础覆盖 SaveDataMgr、GameSaveData、ItemData、ModuleData 权威入口、生成周期/预算字段、自定义难度规则、作物状态以及天气阶段/边界/随机游标的 MemoryPack 往返。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；存档测试约定目录：`Assets/GameTest/DataSave/`；场景目录：`Assets/GameTest/Scenes/DataSave/`；冒烟分类：`DataSave.Smoke`。
- 新增数据字段、MemoryPack Union、自动保存、区块差量或配置加载行为时必须增加往返测试；修复 Bug 时先增加回归测试。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；不得写入玩家真实存档，必须使用临时路径并验证序列化前后关键字段一致。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category DataSave.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 教程存档、旧数据兼容及命名空间共存由 `Assets/GameTest/Guide/NewPlayerGuideSmokeTests.cs`（`Guide.Smoke`）覆盖。
- 维度世界键兼容与默认目录由 `Assets/GameTest/Dimension/DimensionSmokeTests.cs`（`Dimension.Smoke`）覆盖。
- 新增或移动测试脚本、场景、分类及覆盖范围后，必须更新本节；单次测试结果只在任务总结中报告，不写入 Skill。

## 修改后维护本 Skill

新增/移动数据类、存档 partial、配置表、Addressables 标签、资源目录或序列化迁移入口后，必须更新本 Skill 的数据链、路径和近期变更；不要只记录类名而遗漏持久化位置。
