---
name: flatworld-map
description: "Use when: 定位或修改 FlatWorld 的世界地图、Chunk 流送、Tilemap、程序化生成、Biome、River、Structure、TileData、地图保存或区块 Prefab。关键词：ChunkMgr、Chunk、Map、ChunkGenerator、MapSave。"
---

# FlatWorld 地图与 Chunk 系统定位

> 最后核对：2026-08-09。

## 修改前先读
1. `Assets/5_Scripts/5-0_WorldModel/ChunkRuntime.cs`：区块纯数据、三类租约与数据/模拟/表现状态。
2. `Assets/5_Scripts/5-0_WorldModel/ChunkTerrainData.cs`：地形、环境、草地、阻挡与导航权威查询。
3. `Assets/5_Scripts/5-0_WorldModel/ChunkMgr.cs`：纯 C# 缓存、生成调度、三级窗口与逐出。
4. `Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.WorldRuntime.cs`、`ChunkMgr.RuntimeWindow.cs`：Unity 侧主线程提交与视图租约适配。

## 地图主链
```text
Mod_ChunkLoader / NetworkChunkStreamingCoordinator
→ ChunkMgr.RefreshRuntimeWindow
→ 纯 ChunkMgr 请求/生成 ChunkRuntime
→ 主线程校验世界纪元和请求版本后原子提交 ChunkTerrainData
→ 活跃区块持有 Simulation 租约；远区块休眠；超距逐出
→ RuntimeWindow 协程按玩家距离分帧绑定 ChunkView
→ ChunkView 完成后持有 Presentation / Navigation 租约
→ Tilemap / 草地 / 环境 / Collider / A* 只作表现与适配
```

## 关键文件
- 联机扩展：`Assets/5_Scripts/5-3_GamePlay/Core/Manager/ChunkMgr.Networking.cs`。
- 玩家区块加载器：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ChunkLoader.cs`。
- Item 区块归属：`Assets/5_Scripts/5-3_GamePlay/World/Chunk/Mod_ItemChunkAssigner.cs`。
- 生成上下文：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/MapGenerationContext.cs`。
- 地形生成：`Assets/5_Scripts/5-3_GamePlay/World/Map/Controller/ChunkGenerator_Land.cs`。
- 统一噪声核与生成签名：`Assets/5_Scripts/5-3_GamePlay/World/Map/Base/TerrainNoise.cs`。

## 资源目录
- Map Prefab：`Assets/2_Prefabs/Map/`。
- TileBlock Prefab：`Assets/2_Prefabs/TileBlock/`。
- Tile 资源：`Assets/7_Tiles/`。
- TileBlock SO：`Assets/4_ScriptObjects/4-1_TileBlock/`。
- Biome SO：`Assets/4_ScriptObjects/4-8_Biome/`。
- Structure SO：`Assets/4_ScriptObjects/4-9_Structures/`。

## 系统边界
- 区块权威是无 Unity 引用的 `ChunkRuntime + ChunkTerrainData`；`ChunkView` 可重复绑定、解绑和池化，不能反向成为数据来源。
- 运行时窗口分为三圈：`LoadChunkDistance` 内领取 Simulation 并显示，`UnActiveDistance` 内只提前生成数据，`DestroyChunkDistance` 内只保留缓存；预取任务必须排在可见任务之后，同圈优先玩家最近跨区移动方向。预取区块不得领取模拟、表现或导航租约。
- 已就绪区块不能在后台完成回调里直接集中 `ChunkView.Bind()`；统一进入 `ChunkMgr.RuntimeWindow` 的全局协程队列，按距玩家由近到远、每帧最多 `maxChunkPresentationsPerFrame` 个完成 Tilemap、草地、碰撞和导航绑定。离开窗口、重置世界或对象池回收必须同步取消待表现项。
- `ChunkView` 池条目必须记录入池时间；建议容量固定按 `PendingRuntimeChunkPresentationCount + 4` 计算，超量 View 只在闲置满 10 秒后从最早项开始销毁。取出时清除池化/裁剪状态，世界退出与管理器销毁必须释放全部池项；入池和销毁前都要先 `Unbind()` 释放表现与导航租约。

## 高耦合联动
只在本次改动命中下表契约时加载对应 Skill 并追加最小测试；单纯地形美术或不改变数据的生成参数调整不要扩散检查。
| 本系统变更 | 联动检查 | 必查契约 | 追加测试 |
|---|---|---|---|
| `TileData` 可走性、Penalty、顶层 Tile 或导航脏格/脏区通知 | `flatworld-navigation` | A* 仍从权威 TileData 取值，节点和连接在数据变化后刷新 | `Navigation.Smoke` |

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-11：高度驱动河网新增内陆汇流终点淡水湖；终点汇流量达标后按 `river.lakeChance=0.75` 确定性生成湖盆，复用现有湖面抬升和 `18–220` 格面积边界，生成签名升级到 25。
- 2026-08-11：地表 `legacyLand` 群系的草原最大降水阈值从 `0.75` 降为 `0.50`，使高降水森林区间从 `0.25` 扩大到 `0.50`，生成签名升级到 24。
- 2026-08-11：矿洞 `Twine` 分布向地下湖集中；水池两格内保持原概率，其他干燥洞壁概率乘 `0.2`，生成签名升级到 23。
- 2026-08-11：纯 WorldModel 矿洞新增确定性可采集藤蔓；`CaveLayoutKernel` 限定干燥洞壁边缘并让地下水附近更茂盛，`CaveGenerationFeatureGenerator` 输出 `Twine` 自然物，复用现有拾取与区块差量链，生成签名升级到 22。
- 2026-08-11：纯 WorldModel 矿洞新增洞室地下湖；`CaveLayoutKernel` 按世界区域与种子输出跨 Chunk 连续水深，`DeterministicChunkGenerator` 写入淡水 Tile、Water 标记和 `riverDepth/riverKind/groundwater` 环境层，直接复用现有水体表现与 Buff。出生点、天然入口和连接通道禁止积水，生成签名升级到 21。

## 修改后自动测试
- 基础测试脚本：`Assets/GameTest/Map/MapSmokeTests.cs`、`Assets/GameTest/WorldModel/WorldModelSmokeTests.cs`、`Assets/GameTest/WorldModel/WorldModelPersistenceTests.cs`、`Assets/GameTest/WorldModel/LegacyHydrologyKernelTests.cs` 与 `LegacyTerrainClimateKernelTests.cs`；当前基础覆盖 Chunk 生命周期、固定失败种子的完整半径出生搜索、南北/四角周期地形哈希、正式 D∞ 河流 Profile、二维山地石地、旧版气候等价/风场/地形降雨、旧版水文盆地/出口/区域接缝/取消、跨接缝休眠保留窗口、区块加载倍率 API、Tilemap、地形噪声与结构入口。
- 统一测试程序集：`Assets/GameTest/FlatWorld.GameTest.asmdef`；地图测试约定目录：`Assets/GameTest/Map/`；场景目录：`Assets/GameTest/Scenes/Map/`；冒烟分类：`Map.Smoke`。
- 新增 Chunk 流送、Tilemap、程序生成、Biome、River、Structure 或地图差量行为时必须增加系统测试；修复 Bug 时先增加回归测试。中心 Chunk 加载与卸载主流程变化时同步更新地图冒烟场景。
- 测试失败时优先修复生产代码，禁止删除测试或弱化断言；程序生成必须固定种子，测试结束必须清理 Chunk、Tilemap 与临时地图数据。
- 完成修改后执行 `python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --category Map.Smoke`；无需视觉模型或测试工具卡片。仅按“高耦合联动”表命中项追加分类；只有地形、河流或 Tilemap 最终观感变化才做定向截图。
- 维度管理器的基础 PlayMode 生命周期由 `Assets/GameTest/Dimension/DimensionLifecycleTests.cs`（`Dimension.Smoke`）覆盖；地图 Smoke 不再承载完整洞穴生成契约。

## 修改后维护本 Skill
移动生成器、地图 Prefab、Biome/Structure/Tile 资源，改变 Chunk 生命周期、生成顺序、MapSave 结构或就绪条件后，必须同步更新本 Skill；仅在“高耦合联动”表契约变化时更新对应 Skill。

## 有限环绕世界契约（2026-08-06）
- `WorldTopologyBounds` 是世界坐标、格子和 Chunk 原点归一化及最短环面位移的唯一公共入口。
