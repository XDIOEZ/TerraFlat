---
name: flatworld-data-save
description: "Use when: 定位或修改 FlatWorld 的数据模型、MemoryPack 存档、自动保存、区块差量、玩家数据、星球数据、Addressables 或 JSON 配置。关键词：SaveDataMgr、GameSaveData、ItemData、ModuleData、MapSave、PlanetData。"
---

# FlatWorld 数据与存档定位

> 最后核对：2026-08-08。序列化字段改动属于高影响变更。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/SaveDataMgr.cs`：磁盘读写、压缩、备份、差量、联机快照。
2. `Assets/5_Scripts/5-3_GamePlay/World/Map/Data/GameSaveData.cs`：总存档根对象。
3. `Assets/5_Scripts/5-1_Data/ItemData/ItemData.cs`：物品数据与 `ModuleDataDic`。
4. `Assets/5_Scripts/5-1_Data/ModData/ModuleData.cs`：模块持久化基类。

## 权威数据链
```text
GameSaveData
├─ PlayerData_Dict → Data_Player
├─ PlanetData_Dict → PlanetData
│  ├─ MapData_Dict → MapSave → ItemData → ModuleData
│  └─ EcologyWorldSaveData → 规则快照、删除 GUID、Changed ItemData
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
- 存档 partial：`Assets/5_Scripts/5-3_GamePlay/World/Map/Data/GameSaveData.*.cs`。

## 资源注册边界
- `Assets/5_Scripts/5-3_GamePlay/Core/Manager/GameRes.cs` 按 Addressables 标签注册 Prefab、TileBase、TileBlock、Buff、InventoryInit、Skill；物品由 `Items/item-manifest.json` 按解析后的 `shellPrefab` 聚合分包，配方由 `Recipes/recipe-manifest.json` 聚合业务分包后注册。
- 仅移动资源文件并不能保证运行时可用；需同时验证 Addressables 地址/标签与 `GameRes` 字典键。
- 物品与配方均以 `Assets/StreamingAssets/GameConfig/` 下的 JSON 为唯一真源；旧 `Assets/GameConfig/` Excel/Legacy 目录及 Excel 同步工具链已移除，禁止恢复双向同步。
- 玩家正式存档位于 `Application.persistentDataPath/Saves/LocalSaveData/`；旧 `Assets/Saves/` 与无引用的 `BundleSystem` 编辑工具已移除，禁止再将项目 `Assets` 目录作为存档输出位置。
- 新建存档首写与已有存档进入的等待反馈由 `GameManager` 的 `UI_WorldLoading.prefab` 生命周期负责；`SaveDataMgr` 仍只承担磁盘读写，不得直接创建 UI。

## 易误判点
- Addressables 的 `Address` 是独立于 `AssetPath` 的逻辑键，条目通过 `.meta` GUID 跟踪资源真实位置；即使物品 JSON 的 `spriteAddress` 长得像 `Assets/...` 路径，只要在 Unity 内移动资源且 GUID、Address、子 Sprite 名不变，就不要连带改 JSON。只有修改 Address、修改 `[子 Sprite 名]`、丢失 GUID/重新创建条目或工具重写 Address 时才同步 JSON；使用 `Use Existing Build` 时移动或改动资源后还要重建 Addressables 内容。
- Item Manifest 是唯一入口，不会扫描目录自动发现 JSON；所有启用包会先合并再统一解析 `parent`，因此继承可以跨文件。Manifest 声明了 `shellPrefab` 时，包内每条定义的最终解析值必须与它一致。
- `PlanetData` 是 partial class，另一部分位于 `Assets/5_Scripts/5-3_GamePlay/World/Space/PlanetData.cs`。
- 新增 MemoryPack 派生类型时必须检查 `MemoryPackUnion` 与格式版本。当任务明确不兼容旧布局时，必须在解析入口明确拒绝，不得静默迁移、覆盖或删除用户文件。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；仅调整编辑器显示名或未参与持久化的字段不要扩散检查。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `GameSaveData` 根、磁盘读写、备份、自动保存或首存档顺序 | `flatworld-core` | 新建/继续/退出期间只写目标存档，失败恢复不留下半状态 | `Core.Smoke` |

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-11：WorldModel 临时生成 Hook 在 `ApplyPersistedEcologyConfiguration` 前转换 Profile；黄金路径强化水文参数会先冻结到隔离 PlanetData，退出重进继续得到相同生成指纹，同时不会写回项目 SO 或玩家真实存档。
- 2026-08-11：Golden Path 接管请求时显式把 Addressables Play Mode 数据构建器切到 Fast Mode，进入 PlayMode 后直接读取最新 AssetDatabase；刀类共享外壳 `Dagger_Copper.prefab` 已登记路径地址和 `Prefab` 标签，避免旧 Bundle 或正式 Player 中缺少外壳。
- 2026-08-10：自动保存采集阶段统一按约 2.5ms 单帧预算分段执行：自然物克隆、旧 Chunk 物品、程序化区块差异（物品/删除 GUID/Tile/草地）和运行时 AI 均跨帧处理；完成后仍只把不可变字节数组交给后台原子写盘。
- 2026-08-09：建筑召唤器的 `ItemData`/`ModuleData` 已导出到按召唤器外壳分类的 JSON 分包；建筑本体 Prefab 仍由运行时保留，放置快照与占地数据继续走原有持久化链路。
- 2026-08-09：手动 `GameManager.SaveGame()` 复用 `SaveGameInBackgroundCoroutine()`，区块快照分帧执行、字节快照后台原子写盘；自动保存仍按版本号避免旧任务覆盖新手动/退出保存。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/DataSave/DataSaveSmokeTests.cs`；当前基础覆盖 SaveDataMgr、GameSaveData、ItemData、ModuleData 权威入口、生成周期/预算字段、自定义难度规则、作物状态以及天气阶段/边界/随机游标的 MemoryPack 往返。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；存档测试约定目录：`Assets/GameTest/DataSave/`；场景目录：`Assets/GameTest/Scenes/DataSave/`；冒烟分类：`DataSave.Smoke`。
- 新增数据字段、MemoryPack Union、自动保存、区块差量或配置加载行为时必须增加往返测试；修复 Bug 时先增加回归测试。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；不得写入玩家真实存档，必须使用临时路径并验证序列化前后关键字段一致。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category DataSave.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类。
- 自动保存的完整回归位于 `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.AutoSave.cs`：验证后台任务完成、`GameController` 未锁、`Mover`/Rigidbody2D 可用且 `Time.timeScale` 未变。

## 修改后维护本 Skill
新增/移动数据类、存档 partial、配置表、Addressables 标签、资源目录或序列化迁移入口后，必须更新本 Skill 的数据链、路径和近期变更；不要只记录类名而遗漏持久化位置。

## 有限环绕世界存档契约（2026-08-06）
- `PlanetData.TopologyMode` 必须保持在旧字段布局末尾；枚举 `Infinite = 0` 保证旧布局缺失字段时仍为无限世界，新增世界级字段只能追加在其后。
