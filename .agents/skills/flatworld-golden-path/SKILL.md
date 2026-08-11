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
3. 将场景挂到现有阶段：进入世界后、世界就绪跨帧等待、移动 Tick、Chunk Ready、退出世界前或统一清理。`TickWorldReadyScenarios` 专门等待初始地块的帧末批处理，完成后才允许截图和自动保存；只有新功能确实需要新的生命周期边界时，才修改主编排命令。
4. 每项可选行为实现或适配 `IFlatWorldGoldenPathOperation`，使用稳定 `Id/SystemId` 和“安排行为 → 跨 Tick 观测 → 断言 → 恢复状态”生命周期；复杂场景拆到 `FlatWorldGoldenPathScenarios.<Subsystem>.cs` partial 文件。回调不得阻塞 Editor 主线程。
5. 使用隔离存档、固定种子和带超时的条件等待。禁止真实输入、用截图代替可观察状态断言、无界等待、随机重试和静默跳过。
6. 保留原有断言；失败时修复生产代码或测试接线，不得放宽断言制造通过。
   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path
   ```
   ```powershell
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-config .agents/skills/flatworld-golden-path/references/wrapped-river-fast.json
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-config .agents/skills/flatworld-golden-path/references/inventory-combat-focused.json
   python .agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path --golden-set world.radius=64 --golden-set player.maximumMoveSpeed=40.0 --golden-set player.screenshotOrthographicSize=24.0
   ```

## 持续重试规则
- Golden Path 不设固定执行次数上限；只要仍有可修复的生产代码、资源或测试基础设施问题，就在每次失败后先定位并修复，再重新执行完整流程。
- 首个运行时错误出现后，由执行器按 JSON 中的 `execution.errorCollectionSeconds` 启动宽限计时。当前状态仍可可靠推进时继续原流程并尽量覆盖、收集更多去重错误；状态机异常导致后续断言已无意义时停止阶段推进，但保持运行直到宽限计时结束，以收集异步错误。计时结束后统一失败退出，Agent 再集中修复已收集错误并从完整 Golden Path 入口开始下一轮。
- 禁止无修改地重复碰运气；每次重跑前必须有明确的新证据、修复或外部状态恢复。
- 只有遇到无法由当前项目解决的外部阻塞，或继续执行需要用户提供新的权限/资源时，才停止并提交完整结果证据。

## 运行结果审计（强制）
每次执行 `--golden-path` 后都必须完成以下检查：
1. 打开终端输出指向的 `Library/FlatWorldSkillTests/golden-result-<request-id>.json`，要求 `state` 为 `completed`、`outcome` 为 `Passed`、`failed` 为 `0`，并且 `failures` 为空。退出码为 `0` 或终端显示 `PASS` 不能替代这一步。
2. 明确检查运行时错误与警告。运行前通过 Unity MCP `read_console(action="clear")` 清空 Console；运行后只通过 `read_console(action="get", types=["error", "warning"], format="detailed")` 读取 Console 窗口中的本次 `Error`、`Exception`、`Assert` 和 `Warning`，不要读取或扫描完整 `Editor.log`。黄金路径命令会在首个 Play Mode 错误后按 `execution.errorCollectionSeconds` 继续收集并把全部去重错误写入结构化失败；本次运行警告按消息聚合到结果 JSON 的 `warnings`，保留首个堆栈与 `occurrenceCount`，终端直接列出主要 5 类。警告不启动首错计时，也不单独把通过改为失败。Agent 必须一次阅读并集中处理本轮消息；不得在发现首个错误时手动提前终止。只有能够证明是预期、无害且与本次改动无关的既有警告才可保留，并在最终总结中说明依据。MCP 暂时断线时先恢复/重连 Unity 实例，不用文件日志代替。
3. 从结果 JSON 的 `screenshotPaths` 读取截图路径，要求正好包含可访问的 `initial.png`、`middle.png`、`final.png`。逐张使用 `view_image` 或当前环境等效的图像查看能力实际打开，不能只检查文件存在或大小。
4. 目视确认三张图均不是黑屏、空白、纯色或紫色材质错误；真实玩家、世界地形与主要 HUD 可见；没有报错弹窗或异常遮罩；三个阶段的地形/相机画面与移动流程相符。三张图完全相同或任一图明显异常时，先诊断再重跑。
5. 截图是额外的视觉回归检查，不能替代生产状态、Chunk、存档或玩法断言。任一结构化结果、运行时错误与警告审计或截图检查未通过，黄金路径都不得报告完成。

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

## 当前同目标交互重试场景（2026-08-10）
`OnWorldReady` 使用真实玩家 `Mod_InteractSender.TryInteractAtCurrentPosition()` 对同一临时 `IInteractable` 连续请求两次，要求两次都触发 `OnInteractStart`；覆盖矿洞出口首次请求被加载收尾瞬态拒绝后，下一次 E 仍能重试。`finally` 与统一 Cleanup 都会取消交互、恢复距离并销毁探针。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前主世界出生点复活场景（2026-08-09）
`OnWorldReady` 读取玩家 `flatworld.playerSpawn` 存档，先断言模拟矿洞地址到主世界地址必须被生产路由识别为跨维度切换，再使用真实 `DamageReceiver.ForceHurt()` 触发濒死并调用公开 `Mod_PlayerDeathState.RespawnFromDying()`；当前地表实例断言玩家回到持久化主世界出生点且生命恢复，并在清理阶段恢复测试前的位置与生命。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前玩家速度过渡场景（2026-08-09）
`OnWorldReady` 通过真实 `Mover.Move` 和 Rigidbody2D 速度验证走路起步、进入/退出奔跑的平滑过渡，以及松开方向后默认 0.07 秒的极短惯性和停止；场景不依赖真实设备输入，Cleanup 恢复测试前速度与移动状态。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前 GM 管理员无敌场景（2026-08-09）
`OnWorldReady` 先断言管理员无敌默认关闭，随后通过真实 `PlayerAdminController` 关闭无敌并用 `DamageReceiver.ForceHurt()` 观察正常扣血，再重新开启无敌并施加致死环境伤害，断言满血恢复且未进入 `Mod_PlayerDeathState` 濒死。通过和失败路径都会恢复玩家名、生命和原无敌开关。实现位于 `FlatWorldGoldenPathScenarios.PlayerMovement.cs`。

## 当前自动保存场景（2026-08-09）
`TickWorldReadyScenarios` 完成初始跨帧表现断言后，通过 `GameManager.SaveGameInBackgroundCoroutine()` 启动真实分帧快照与后台原子写盘，随后调用 `GameManager.SaveGame()` 验证手动保存入口和 `LastSaveSucceeded`；`OnTraversalTick` 等待最多 10 秒，断言写入成功、保存状态最终结束、`GameController` 没有输入锁、Mover/Rigidbody2D 可用且 `Time.timeScale` 未改，退出前必须完成。实现位于 `FlatWorldGoldenPathScenarios.AutoSave.cs`。

## 当前建筑放置场景（2026-08-08）
`OnWorldReady` 使用正式 `Wall_Wood_Summoner` 在玩家附近扫描可放置的新版权威地块，通过 `ValidateAuthoritativePlacement()` 与 `TryCreateInstalledBuilding()` 验证新区块读取、建筑实例和 `BuildingOccupancyRegistry` 动态占地；同时初始化正式 `BuildingShadow`，断言图片、材质和 `Shadow` 排序层接线。石墙阻挡写入后由 `TickWorldReadyScenarios` 等待 `ChunkLightOccluderRenderer.RebuildVersion` 跨帧递增，再断言阴影增加、移除石墙并等待下一帧恢复；临时墙清理完成后才启动自动保存。实现位于 `FlatWorldGoldenPathScenarios.Building.cs`。

## 当前 GM 区块加载调速场景（2026-08-08）
`OnWorldReady` 先通过 `ChunkMgr.TrySetChunkLoadSpeedMultiplier()` 验证有限倍率会提升旧队列预算并同步到新 WorldModel 的实际生成并发，再传入正无穷验证“自动最大”会达到 CPU 安全并发上限、旧管线四倍数量预算与两倍毫秒预算，而不会返回 `int.MaxValue/Infinity` 堵塞主线程。`finally` 和统一 `Cleanup` 都按场景开始前的有限/无限状态恢复。实现位于 `FlatWorldGoldenPathScenarios.ChunkLoading.cs`。

## 当前 WorldModel 流送场景（2026-08-08）
`OnWorldReady` 断言主线程表现协程队列已排空且空闲预取实际并发不超过 1；已完成的 `LoadChunkDistance..UnActiveDistance` 外圈数据必须保持 Dormant 且不持有 Simulation/Presentation/Navigation 租约，但不要求整圈阻塞式生成完。原有往返场景离开可见圈后验证 View 与三类租约释放，并验证固定创建的 Chicken 已随起始区块休眠；返回时等待分帧表现完成，再验证该生物恢复、同一 `ChunkRuntime`、地形哈希、唯一租约和 `ChunkCommitted` 订阅均未重复。清理阶段销毁测试生物。实现位于 `FlatWorldGoldenPathScenarios.WorldModel.cs`。

## 近期变更
> 最多保留 5 条，按新到旧排列；超过时删除最旧条目。
- 2026-08-12：`environment.tile-effects` 只选择淡水格，除减速/潮湿外验证唯一水质能力 Buff、长按 E 未满/满 1 秒的补水边界、`food.drink` 句柄、蓝色水粒子、脏水确定性感染及离水清理恢复。
- 2026-08-12：`buff.burning` 操作在燃烧 Tick 后切换施加正式“感染”Buff，继续断言每秒 1 点真实伤害、角色绿色染色即时启用，并在 Cleanup 验证移除后恢复原色与生命。
- 2026-08-12：`buff.burning` 操作在燃烧 Tick 前追加光耀表现检查；通过真实 `BuffManager` 施加/移除“光耀”，断言角色子层级 Point Light2D 同步启停并在 Cleanup 关闭；`FlatWorld.Automation.Editor` 因公开访问 `Light2D`，必须直接引用 `Unity.RenderPipelines.Universal.Runtime`。
- 2026-08-11：Golden Path 引入 `IFlatWorldGoldenPathOperation` 注册表与 JSON 全开/白名单/黑名单选择，默认回归 23 项真实操作；新增背包制作、战斗目标、背包 UI、音频 Cue、角色气泡、时间天气和已加载网格寻路覆盖。单项操作失败会隔离并继续同阶段其他系统，结果回显实际启用 ID。
- 2026-08-11：Golden Path 运行结果审计同时读取 Console 的 `error` 与 `warning`；运行期警告按消息聚合写入结果 `warnings` 并回显出现次数，脚本直接展示主要 5 类。首个错误才启动 JSON 宽限计时，可推进时继续覆盖、不可推进时仅等待异步错误；Agent 必须按根因直接反馈主要错误，JSON 仅作为完整证据。

## 当前地表气候与水文场景（2026-08-08）
Hydrology 场景要求每个模型区块提供 `temperature/temperature.celsius/basePrecipitation/precipitation/windX/windY`，断言旧版温度通道按 Profile 映射到 `0..50℃`、区域风向为单位向量，且移动窗口内实际出现迎风增雨或背风雨影；同时用 `SurfaceBiomeClassifier` 核对旧 Biome 有序规则，并要求 `mountain`、`riverSurfaceLevel/riverKind`。高海拔非水格必须使用二维石地，淡水格必须标记为 `River(1)` 或 `Lake(2)`；若遇到湖泊还会断言湖面高度存在且不低于湖底高度。正式 Surface 默认使用带连续下坡偏转的 D∞ 高度汇流，旧版 D8 内核继续由纯测试覆盖盆地与出口行为。

## 配置执行器契约（2026-08-06）
- `FlatWorldGoldenPathConfiguration` 是版本化、强验证的输入和结果快照；运行结果必须回显最终有效配置，便于复现 AI 临时构造的场景。
