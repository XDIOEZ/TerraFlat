---
name: flatworld-dimension
description: "Use when: 定位或修改 FlatWorld 的维度、星球表面/地下矿洞切换、世界地址、动态世界 Scene、维度独立地图与种子、维度入口、维度环境覆盖或未来星球旅行。关键词：DimensionManager、WorldAddress、DimensionPortal、DimensionCatalogSO、ChunkGenerator_Cave。"
---

# FlatWorld 维度与星球世界定位

> 最后核对：2026-08-10。首版只开放离线地表与地下矿洞往返。

## 修改前先读
1. `Assets/5_Scripts/5-3_GamePlay/World/Dimension/WorldAddress.cs`：星球与维度的稳定世界地址。
2. `Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionManager.cs`：维度激活、动态 Scene、切换、失败恢复与生成配置。
3. `Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionCatalogSO.cs`：维度定义、入口目标、环境覆盖和矿洞资源规则。
4. `Assets/5_Scripts/5-3_GamePlay/World/Dimension/DimensionTravelProgressStore.cs`：玩家每维度最后位置与矿坑入口/出口锚点。

## 世界地址契约
- 地表地址继续使用旧键：`PlanetId`，例如 `Earth`；不得改成带后缀的形式，否则会破坏旧存档兼容。
- 非地表地址：`PlanetId__dimension__DimensionId`，例如 `Earth__dimension__cave`。
- 统一使用 `WorldAddress.FromWorldKey()`、`WithDimension()` 和 `WorldKey`，禁止在业务脚本里自行拼接后缀。
- `GameSaveData.PlanetData_Dict` 直接以 `WorldKey` 为键；每个维度因此拥有独立 `PlanetData.MapData_Dict`、Chunk 差量和环境状态。

## 运行链
```text
DimensionPortal.Interact
→ DimensionManager.TryBeginTransition
→ 保存当前世界和玩家位置
→ ItemMgr.ReleasePlayerForWorldTransition
→ GameManager 世界退出事件
→ 卸载当前动态世界 Scene
→ 创建/激活目标 WorldKey 的空 Scene
→ GameManager.RunWorld
→ ChunkMgr 按维度实例化并配置 MapCore
→ ItemMgr 创建玩家
→ 按入口锚点定位目标出口旁的安全位置
→ 等待目标 Chunk 基线完成并查找或创建正式 CaveExit Item
→ 等待完整 Runtime Window 与 GameManager 世界进入收尾完成
→ 解锁玩家输入并关闭加载界面
```

## 地图与确定性生成
- `ChunkMgr.TryCreateMapCore()` 从当前 `DimensionDefinition.MapCorePrefabId` 解析地图 Prefab。
- `DimensionManager.ConfigureMap()` 在矿洞维度把原 `Map.mapGenerators` 替换为 `ChunkGenerator_Cave`；地表继续使用 MapCore 原管线。
- 该 `ChunkGenerator_Cave` 只服务旧 `Map` 兼容流程；正式新世界经 `ChunkMgr.RefreshRuntimeWindow()` 进入 `DeterministicChunkGenerator + CaveLayoutKernel + CaveGenerationFeatureGenerator`，不得在新版 View 再调用旧 Chunk 生成器。
- `MapGenerationContext` 携带当前 `WorldAddress` 与 `DimensionDefinition`。

## 环境覆盖
- 地表使用原昼夜、天气和怪物生成逻辑。
- 矿洞默认光照上限为 `0.08`；`DayTimeSystem.GetLighting()` 取地表昼夜结果与该上限的较小值，因此白天不超过洞穴上限、夜晚仍继续变暗。
- `WeatherMgr` 在 `SuppressWeather=true` 时关闭天气与雨效。
- `MonsterSpawnerManager` 在 `EnableMonsterSpawning=false` 时停止维度内生成器。

## 玩家进度与入口
- 玩家每个 `WorldKey` 的最后位置存于 `Data_Player.ItemSpecialData` 的 `flatworld.dimensions` 命名空间，不修改 MemoryPack 布局。
- 正式矿坑锚点同样存于 `flatworld.dimensions.portalAnchors`：以地表 `WorldKey + MineEntrance Guid` 为稳定键，保存地表入口 GUID/位置、矿洞世界键和 CaveExit GUID/位置。
- 使用矿坑入口时抵达对应 CaveExit 的 `PortalOffset` 安全偏移；使用 CaveExit 时返回绑定的地表矿坑旁，不再生成免费运行时 Portal。
- `DimensionPortal` 通过现有 `IInteractable`/E 键链触发；地表入口必须是已安装 `MineEntrance`，生成的 `MineEntrance_Summoner` 因 `BuildingRole.Summoner` 会被拒绝。
- `ChunkNaturalItemRenderer` 为确定性 `CaveExit` 调用 `DimensionPortal.ConfigureGenerated()`；此分支不写玩家锚点，按同一世界格切到另一维度，并在 `WaitForRuntimeChunkPresentation()` 确认目标 `ChunkView` 与自然出口已绑定。
- 维度切换不得只等待 Chunk 队列；`DimensionManager` 必须继续等待 `GameManager.IsWorldEntryInProgress == false` 后才能解锁新玩家输入，避免落在出口上的首次 E 被仍在收尾的世界进入流程拒绝。

## 矿物与掉落
- 矿洞资源 Prefab：`Assets/2_Prefabs/Mine/Mine_Coal.prefab`、`Mine_Copper.prefab`、`Mine_Tin.prefab`、`Mine_Iron.prefab`、`Mine_Stone.prefab`。
- 金属/煤矿掉落对应 `Ore_*` 并伴生 `Ore_Stone`；石矿只掉落 `Ore_Stone`。
- 这些掉落由 Prefab 的 `DamageReceiver.Data.LootTable` 持久化；不得恢复 Chicken 等模板继承掉落。
- 项目的嵌套 `DamageType` 数据不支持用 `PrefabUtility.SaveAsPrefabAsset()` 全量重存该 DamageReceiver Prefab；修改掉落时应使用 Inspector 的 Prefab override 或精确 YAML/SerializedProperty，并在刷新后检查 Console。

## 联机边界
- 首版维度切换只支持离线模式；`GameNetwork.IsOnline` 为真时 `TryBeginTransition()` 必须拒绝。
- 未完成服务器权威目标地址、全体观察者迁移、Chunk 流送重建和玩家同步前，不得解除该限制。
- 后续星球旅行应继续复用 `WorldAddress`，星球 ID 放在 `PlanetId`，星球表面维度仍为 `surface`。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；矿洞装饰、光照数值等局部表现变化不要扩散检查。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `WorldAddress`、`WorldKey`、动态 Scene 或世界切换事件顺序 | `flatworld-core`、`flatworld-data-save` | 旧地表键兼容、世界事件/加载遮罩顺序和每维度存档隔离 | `Core.Smoke`、`DataSave.Smoke` |

## 修改后验证
- 基础测试：`Assets/GameTest/Dimension/DimensionLifecycleTests.cs`；分类：`Dimension.Smoke`。
- 当前覆盖世界键地表兼容/往返、默认目录与矿洞环境配置、洞穴布局确定性、开放/封闭格混合、阻挡层路由、岩壁可走性、正式入口/召唤器/出口角色、锚点 JSON 往返、矿坑配方，以及五种矿物只能掉落 `Ore_*`。
- 地块效果专项：`Assets/GameTest/Dimension/DimensionTileEffectTests.cs`；覆盖运行时淡水 TileId 到 `Tile_Water_Fresh`/水深/Buff 行为的还原，以及切维度前退出与普通保存不退出水体。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Dimension.Smoke`；无需视觉模型或测试工具卡片。涉及 Tile Effect 时追加 `--category Dimension.TileEffects`；只有维度场景最终观感变化才做定向截图。
- 手动 Play Mode 建议验证：地表入口交互、矿洞大量生成矿物、开采掉落、返回地表、两边位置恢复、退出重进后的 Chunk 差量。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：`Tile_Water` 为无盐淡水授予饮水能力 Buff；新版 `riverKind=River` 为干净淡水，湖泊/地下水及旧版未知淡水为脏淡水，海水不授予，离水与维度切换会成对移除。
- 2026-08-11：矿洞 `Twine` 藤蔓改为向地下湖周围集中；水池两格内保持原湿润生成量，其他干燥区域概率降低 80%，生成签名升级到 23。
- 2026-08-11：新版矿洞加入确定性可采集藤蔓；只占用干燥可走洞壁边缘，邻近地下水时提高概率，避开出生点和入口网络，并直接复用现有 `Twine` 自然物、拾取、制作与区块差量链，生成签名升级到 22。
- 2026-08-11：新版矿洞加入确定性地下湖；约 28% 洞室按世界区域蓄水，湖泊跨 Chunk 连续并使用现有淡水 Tile、水深与水体 Buff，出生安全区、天然出口和连接通道保持干燥。矿洞 Profile 暴露湖泊概率、半径比例和深度范围，生成签名升级到 21。
- 2026-08-11：矿洞 `FixedLighting=0.08` 从恒定亮度改为光照上限；实际强度与颜色解析地表引用时间，使用 `min(地表昼夜亮度, 0.08)`，白天保持洞穴上限，夜晚随太阳继续变暗。

## 修改后维护本 Skill
改变世界键格式、动态 Scene、切换链、目录字段、生成种子、玩家位置命名空间、矿洞资源、环境覆盖、联机限制或测试目录后必须同步更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。

## 有限世界继承契约（2026-08-06）
- `EnsureWorldData` 从地表深克隆新维度世界时必须继承 `PlanetData.TopologyMode`、`Radius` 和 `ChunkSize`，只清空维度独立的地图字典与环境运行态。
