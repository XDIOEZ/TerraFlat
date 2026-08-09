---
name: flatworld-golden-path
description: Continuously evolve FlatWorld's real single-player Runtime.GoldenPath test after runtime gameplay changes. Use whenever modifying player/world lifecycle, buffs, combat, items, buildings, AI, environment, Chunk streaming, save behavior, or another feature that can be exercised deterministically after entering a real world; select the correct lifecycle phase, add a bounded scenario with observable assertions and cleanup, and run the complete golden path after subsystem smoke tests.
---

# FlatWorld 黄金路径持续演进

让完整流程测试随生产系统一起演进，覆盖真实启动、创建世界、玩家行为、Chunk 流送与退出世界。

## 必读

1. 完整读取 [references/golden-path-map.md](references/golden-path-map.md)。
2. 读取本次生产代码 diff 和命中的 FlatWorld 领域 Skill。
3. 读取 `Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathCommand.cs` 与现有 `FlatWorldGoldenPathScenarios*.cs`。

## 强制工作流

1. 先完成生产代码与领域 Smoke 测试，再评估黄金路径。
2. 只要新行为能在标准单机世界内通过公开生产 API 确定性执行，就必须添加或更新一个黄金路径场景；不要等用户另行要求。
3. 将场景挂到现有阶段：进入世界后、移动 Tick、Chunk Ready、退出世界前或统一清理。只有新功能确实需要新的生命周期边界时，才修改主编排命令。
4. 用命名清晰的状态化子场景实现“安排行为 → 跨 Tick 观测 → 断言 → 恢复状态”。回调不得阻塞 Editor 主线程；复杂场景拆到 `FlatWorldGoldenPathScenarios.<Subsystem>.cs` partial 文件。
5. 使用隔离存档、固定种子和带超时的条件等待。禁止真实输入、用截图代替可观察状态断言、无界等待、随机重试和静默跳过。
6. 保留原有断言；失败时修复生产代码或测试接线，不得放宽断言制造通过。
7. 执行受影响领域的 Smoke 分类，然后执行：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path
   ```

   黄金路径采用“配置 + 执行器”契约。需要构造特定场景时，优先传临时局部 JSON；也可重复使用 `--golden-set` 调整单个字段。配置只改变本次隔离世界中的真实运行时实例，不得改写 Prefab 或项目默认值：

   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-config .agents/skills/flatworld-golden-path/references/wrapped-river-fast.json
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-set world.radius=64 --golden-set player.maximumMoveSpeed=40.0 --golden-set player.screenshotOrthographicSize=24.0
   ```

   AI 可以按本次目标临时配置世界种子/尺寸/Chunk、拓扑、玩家视野与移动、启用的子场景、河流生成参数、超时和覆盖量门槛。新增可调系统参数时，必须同时更新 C# 配置默认值、Python 默认字典、验证、执行器应用逻辑、结果回显和示例；未知字段或类型不匹配必须在启动 Unity 场景前失败。

8. 按下方“运行结果审计”检查结构化结果、运行时报错和全部截图；不得只看到终端 `PASS` 就结束。
9. 最终总结中说明新场景所在阶段、黄金路径结果、运行时报错检查结论和截图检查结论。

## 最多三次规则

- 每个已经完成并进入验收的独立系统或修改批次，单独拥有最多 3 次 Golden Path 执行机会；计数不按对话累计，也不跨系统、跨修改批次继承。
- 同一批次因第 1 或第 2 次失败而进行的修复仍属于该批次，不重置计数。
- 开始另一个系统，或用户在上一批次结束后要求一轮新的独立修改时，重新获得最多 3 次机会。
- 第 3 次仍未满意或失败时立即停止重跑，提交三次结果 JSON、截图、Console 错误与不满意原因，等待用户指导；未经用户明确授权不得执行第 4 次。

## 运行结果审计（强制）

每次执行 `--golden-path` 后都必须完成以下检查：

1. 打开终端输出指向的 `Library/FlatWorldSkillTests/golden-result-<request-id>.json`，要求 `state` 为 `completed`、`outcome` 为 `Passed`、`failed` 为 `0`，并且 `failures` 为空。退出码为 `0` 或终端显示 `PASS` 不能替代这一步。
2. 明确检查运行时报错。运行前通过 Unity MCP `read_console(action="clear")` 清空 Console；运行后只通过 `read_console(action="get", types=["error"], format="detailed")` 读取 Console 窗口中的本次 `Error`、`Exception` 和 `Assert`，不要读取或扫描完整 `Editor.log`。黄金路径命令也会监听本次 Play Mode 错误并将首个错误转成结构化失败；只要结果或 MCP Console 出现错误，就必须阅读消息与堆栈、修复并重跑。MCP 暂时断线时先恢复/重连 Unity 实例，不用文件日志代替。
3. 从结果 JSON 的 `screenshotPaths` 读取截图路径，要求正好包含可访问的 `initial.png`、`middle.png`、`final.png`。逐张使用 `view_image` 或当前环境等效的图像查看能力实际打开，不能只检查文件存在或大小。
4. 目视确认三张图均不是黑屏、空白、纯色或紫色材质错误；真实玩家、世界地形与主要 HUD 可见；没有报错弹窗或异常遮罩；三个阶段的地形/相机画面与移动流程相符。三张图完全相同或任一图明显异常时，先诊断再重跑。
5. 截图是额外的视觉回归检查，不能替代生产状态、Chunk、存档或玩法断言。任一结构化结果、运行时报错或截图检查未通过，黄金路径都不得报告完成。
6. 最终总结至少给出结果 JSON 路径、是否确认无运行时 `Error/Exception/Assert`、三张截图是否全部目视通过，以及截图目录或三条路径。

## 允许不扩展的情况

纯 Editor 工具、纯视觉布局、不参与运行时的数据整理，或必须依赖不可确定外部服务的功能可不加入。必须在最终总结中给出具体原因，并仍保留领域级自动测试。

空玩家名/存档名的随机数字补全发生在进入世界前，并且结果刻意非确定；保留 Core Smoke 的格式与请求验证覆盖，Golden Path 继续显式命名隔离存档和玩家，便于定位运行产物。

## 当前有限世界场景（2026-08-06）

GoldenPath 在 `OnWorldReady` 后、首次截图与原长距离流程前，通过真实 `Mover.Move` 执行一次右边界环绕；等待对侧 Chunk Ready，验证玩家数据、Chunk/MapSave 规范键，然后恢复原位置并继续完整流程。复杂状态机位于 `FlatWorldGoldenPathScenarios.WorldTopology.cs`。

## 当前燃烧 Buff 表现场景（2026-08-09）

`OnTraversalTick` 通过真实玩家 `BuffManager.AddBuff(燃烧)` 施加正式 Buff，除验证真实伤害 Tick 外，还要求 `ActorStatusVisualEffectController` 已配置完整八帧火焰并立即处于启用状态；统一 Cleanup 移除 Buff 后必须确认火焰同步隐藏并恢复生命。实现位于 `FlatWorldGoldenPathScenarios.Buff.cs`。

## 当前运行时水体场景（2026-08-08）

`OnChunkReady` 在活动 `ChunkRuntime` 中寻找一格可走陆地和一格水体，通过真实玩家 `TileEffectReceiver.RefreshCurrentTileEffects()` 验证进入水体获得 `水体减速/潮湿`、移动倍率按正式 Buff 配置下降、离开后移除并恢复；通过和失败路径都把玩家位置、速度及脚下地块效果恢复到检查前状态。实现位于 `FlatWorldGoldenPathScenarios.TileEffects.cs`。

## 当前新玩家出生场景（2026-08-08）

`OnWorldReady` 在初始 Chunk 窗口稳定后直接读取玩家脚下的 `ChunkRuntime/ChunkTerrainData`，断言出生格非水且可走，并核对运行时位置与 `Data_Player` 存档位置一致；场景不移动玩家、不改生成参数。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前主世界出生点复活场景（2026-08-09）

`OnWorldReady` 读取玩家 `flatworld.playerSpawn` 存档，使用真实 `DamageReceiver.ForceHurt()` 触发濒死，再调用公开 `Mod_PlayerDeathState.RespawnFromDying()`；断言玩家回到持久化主世界出生点且生命恢复，并在清理阶段恢复测试前的位置与生命。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前玩家速度过渡场景（2026-08-09）

`OnWorldReady` 通过真实 `Mover.Move` 和 Rigidbody2D 速度验证走路起步、进入/退出奔跑的平滑过渡，以及松开方向后默认 0.07 秒的极短惯性和停止；场景不依赖真实设备输入，Cleanup 恢复测试前速度与移动状态。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前 GM 管理员无敌场景（2026-08-09）

`OnWorldReady` 先断言管理员无敌默认关闭，随后通过真实 `PlayerAdminController` 关闭无敌并用 `DamageReceiver.ForceHurt()` 观察正常扣血，再重新开启无敌并施加致死环境伤害，断言满血恢复且未进入 `Mod_PlayerDeathState` 濒死。通过和失败路径都会恢复玩家名、生命和原无敌开关。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前自动保存场景（2026-08-09）

`OnWorldReady` 通过 `GameManager.SaveGameInBackgroundCoroutine()` 启动真实分帧快照与后台原子写盘，随后调用 `GameManager.SaveGame()` 验证手动保存入口和 `LastSaveSucceeded`；`OnTraversalTick` 等待最多 10 秒，断言写入成功、保存状态最终结束、`GameController` 没有输入锁、Mover/Rigidbody2D 可用且 `Time.timeScale` 未改，退出前必须完成。实现位于 `FlatWorldGoldenPathScenarios.AutoSave.cs`。

## 当前建筑放置场景（2026-08-08）

`OnWorldReady` 使用正式 `Wall_Wood_Summoner` 在玩家附近扫描可放置的新版权威地块，通过 `ValidateAuthoritativePlacement()` 与 `TryCreateInstalledBuilding()` 验证新区块读取、建筑实例和 `BuildingOccupancyRegistry` 动态占地；同时初始化正式 `BuildingShadow`，断言图片、材质和 `Shadow` 排序层接线，随后立即清理虚影、建筑和召唤器。实现位于 `FlatWorldGoldenPathScenarios.Building.cs`。

## 当前 GM 区块加载调速场景（2026-08-08）

`OnWorldReady` 先通过 `ChunkMgr.TrySetChunkLoadSpeedMultiplier()` 验证有限倍率会提升旧队列预算并同步到新 WorldModel 的实际生成并发，再传入正无穷验证“自动最大”会达到 CPU 安全并发上限、旧管线四倍数量预算与两倍毫秒预算，而不会返回 `int.MaxValue/Infinity` 堵塞主线程。`finally` 和统一 `Cleanup` 都按场景开始前的有限/无限状态恢复。实现位于 `FlatWorldGoldenPathScenarios.ChunkLoading.cs`。

## 当前 WorldModel 流送场景（2026-08-08）

`OnWorldReady` 断言主线程表现协程队列已排空且空闲预取实际并发不超过 1；已完成的 `LoadChunkDistance..UnActiveDistance` 外圈数据必须保持 Dormant 且不持有 Simulation/Presentation/Navigation 租约，但不要求整圈阻塞式生成完。原有往返场景离开可见圈后验证 View 与三类租约释放，并验证固定创建的 Chicken 已随起始区块休眠；返回时等待分帧表现完成，再验证该生物恢复、同一 `ChunkRuntime`、地形哈希、唯一租约和 `ChunkCommitted` 订阅均未重复。清理阶段销毁测试生物。实现位于 `FlatWorldGoldenPathScenarios.WorldModel.cs`。

## 近期变更

> 最多保留 10 条，按新到旧排列；新增后超过上限时删除最旧条目。

- 2026-08-09：建筑黄金路径在新区块石墙写入/移除前后新增 `ChunkLightOccluderRenderer.ActiveOccluderCount` 断言，验证阻挡层与 URP 光照遮挡子层同步且可逆。
- 2026-08-09：自动保存场景继续验证手动 `GameManager.SaveGame()` 的分帧快照、后台写盘与 `LastSaveSucceeded`，保持输入、Mover/Rigidbody2D 与 `Time.timeScale` 不受保存影响。
- 2026-08-09：`ItemLifecycle` 场景在真实 WorldModel 世界中生成短距离 Berry 掉落，跨帧断言物品绑定 `ChunkView` 临时节点且动画结束后可拾取，并在退出前通过 `ItemMgr` 清理，覆盖掉落归属回归链路。
- 2026-08-09：建筑召唤器迁移到 JSON 后继续复用现有建筑黄金路径的 `Wall_Wood_Summoner` 场景；该场景通过 `GameRes.CreateItemData → ItemMgr.InstantiateItem → Load` 真实验证 JSON ItemData、`Mod_Building` 放置、快照占地与清理，无需复制一套迁移专用场景。
- 2026-08-09：WorldModel 进入世界阶段新增活动视野绑定断言，要求完整窗口的 `ChunkView` 均已就绪，避免维度切换或普通进入只完成中心区块就被判定为 Ready。
- 2026-08-09：矿洞入口/出口的一一对应由 `WorldModel.Cave` 纯生成回归断言唯一数量和同格坐标；Golden Path 的生态销毁场景继续跳过永久 `CaveExit`，避免把配对基线误判为可删除自然物。
- 2026-08-09：建筑黄金路径在世界就绪阶段验证石墙召唤器写入新区块 `BlockingTileId`、Tilemap 刷新与可逆清理，避免临时阻挡格干扰移动阶段。
- 2026-08-09：生态场景选择可销毁自然物时跳过确定性跨维度传送门；该类入口是永久基线，不得被“销毁后不复活”断言误写成删除差量，矿物/树木等普通自然物仍保持原验证。
- 2026-08-09：玩家奔跑场景扩展为实际 Rigidbody2D 速度回归：走路起步、走跑互切、松开后默认 0.07 秒的极短惯性及停止均通过 `Mover.Move` 验证，并在 Cleanup 恢复原速度。
- 2026-08-09：GM 生物召唤修复复用 WorldModel 场景的正式 `ItemMgr.InstantiateItem(...) → Load()` 生命周期；这是运行时控制台局部入口修正，现有 Chicken 创建、`RuntimeEntities` 归属与重进恢复断言覆盖同一生产链路，未额外启动完整 Golden Path。

## 当前地表气候与水文场景（2026-08-08）

Hydrology 场景要求每个模型区块提供 `temperature/temperature.celsius/basePrecipitation/precipitation/windX/windY`，断言旧版温度通道按 Profile 映射到 `0..50℃`、区域风向为单位向量，且移动窗口内实际出现迎风增雨或背风雨影；同时用 `SurfaceBiomeClassifier` 核对旧 Biome 有序规则，并要求 `mountain`、`riverSurfaceLevel/riverKind`。高海拔非水格必须使用二维石地，淡水格必须标记为 `River(1)` 或 `Lake(2)`；若遇到湖泊还会断言湖面高度存在且不低于湖底高度。正式 Surface 默认使用带连续下坡偏转的 D∞ 高度汇流，旧版 D8 内核继续由纯测试覆盖盆地与出口行为。

## 配置执行器契约（2026-08-06）

- `FlatWorldGoldenPathConfiguration` 是版本化、强验证的输入和结果快照；运行结果必须回显最终有效配置，便于复现 AI 临时构造的场景。
- `FlatWorldGoldenPathExecutor` 是生产系统入口适配层：通过 `NewWorldCreationRequest` 创建真实世界，通过玩家模块 API 调视野，通过 `WorldGenerationRuntimeHooks` 在 `Map.Act()` 前配置当前 Map 实例。
- `player.cameraOrthographicSize` 控制移动阶段视距；`player.screenshotOrthographicSize` 仅在三次截图前临时拉远相机，通过真实 `Mod_Cam` 刷新可见 Chunk 窗口并等待全部 Ready，截图后恢复移动视距。
- WorldModel 往返场景验证表现协程已排空，且 `ChunkCommitted` 订阅数必须等于当前 `PresentationStatus=Bound` 的 View 数；不得再与截图缩放切换前的历史基线比较，因为相机恢复会合法改变表现窗口数量。
- `WorldGenerationRuntimeHooks` 默认没有订阅者；执行器结束或 Domain Reload 时必须解除订阅，所以临时河流/地形参数不会污染 Prefab、后续游戏或存档默认配置。
- `world.noiseScale` 必须在进入世界后的 `ChunkMgr.ActiveGenerationProfile.Settings.WorldCoordinateScale` 中保持一致；河流距离倍率按 `clamp(0.01 / scale, 0.25, 4)` 回显，WorldModel 场景在 `OnWorldReady` 阶段断言该接线。
- 状态化子场景继续负责跨帧的调用、观测、断言和清理；配置决定场景是否启用及运行参数，执行器不绕过生产系统直接伪造结果。
- 快速有限世界/强化河流示例见 `references/wrapped-river-fast.json`。
