# FlatWorld 黄金路径扩展图

## 权威文件

- 主流程：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathCommand.cs`
- 场景编排：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.cs`
- 复杂领域场景：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathScenarios.<Subsystem>.cs`
- 测试入口：`.agents/skills/flatworld-test-automation/scripts/run_unity_tests.py --golden-path`
- 配置模型：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathConfiguration.cs`
- 生产系统执行器：`Assets/Editor/FlatWorld/Automation/FlatWorldGoldenPathExecutor.cs`
- 地图生成临时入口：`Assets/5_Scripts/5-3_GamePlay/World/Map/WorldGenerationRuntimeHooks.cs`

`--golden-path` 通过 Editor 状态机从真实 `GameStartScene` 启动；功能场景以上述 Editor 文件为唯一权威入口，不要在 Unity Test Framework 测试中再复制一套。
命令在接管每个请求前强制刷新 AssetDatabase，确保 Editor 失焦或关闭 Auto Refresh 时也不会运行旧脚本。
进入真实启动场景前会临时启用标准 Domain Reload，并在结束后按磁盘 `EditorSettings.asset` 的原值恢复，避免前一个 PlayMode 测试留下静态单例或事件。

可用 `--golden-config <partial.json>` 或重复的 `--golden-set section.field=<json-value>` 构造本次场景。结果 JSON 的 `configuration` 保存合并、验证后的完整快照；复现失败时直接复用该对象。临时配置只能调整隔离运行时实例，禁止用 Editor API 写 Prefab、SO 或 ProjectSettings 来构造场景。

## 运行产物与审计

- 每次请求的结构化结果位于 `Library/FlatWorldSkillTests/golden-result-<request-id>.json`。不能只看终端 `PASS`；必须打开该文件，确认 `state=completed`、`outcome=Passed`、`failed=0` 且 `failures` 为空。
- 主流程监听本次 Play Mode 的 `Error`、`Exception` 和 `Assert`，出现时会把错误消息与堆栈写入结构化失败。运行前用 Unity MCP 清空 Console，运行后用 MCP `read_console` 读取错误详情；不要扫描完整 `Editor.log`。MCP 断线时先重连正确的 Unity 实例。
- 三张截图位于 `Library/FlatWorldSkillTests/GoldenPathCaptures/<request-id>/initial.png`、`middle.png` 和 `final.png`，并通过结果 JSON 的 `screenshotPaths` 返回。Agent 必须逐张实际打开，检查黑屏、空白、纯色、紫色材质、错误遮罩，以及玩家、地形、主要 HUD 和阶段变化是否正常。
- 截图审计只补充视觉覆盖，不替代运行时状态断言。结构化结果、运行时报错检查或任一截图审计失败时，都应诊断、修复并重新执行完整黄金路径。

## 阶段选择

| 扩展阶段 | 适用行为 | 典型断言 |
| --- | --- | --- |
| `OnWorldReady` | 玩家模块、初始 Buff、背包、装备、建筑、可确定生成的 AI | 模块存在，公开 API 改变运行时状态 |
| `TickWorldReadyScenarios` | 初始地块/表现必须等待真实帧末批处理的场景 | 等待 `Time.frameCount` 与可观察版本推进；完成临时状态清理后才允许截图、移动和自动保存 |
| `OnTraversalTick` | 需持续观测的 Buff Tick、体力、环境、AI 追踪 | 运行阶段按 Editor 帧驱动，效果在玩家移动时逐步发生 |
| `OnChunkReady` | 跨 Chunk 玩法、一次性阶段断言 | 目标 Chunk Ready，玩法状态与 Chunk 字典同时健康 |
| `BeforeWorldExit` | 存档捕获、长时状态、退出前结果 | 权威状态已进入存档根或结果已完成 |
| `Cleanup` | 会影响后续或留存的测试状态 | 通过和失败路径都恢复速度、生命、Buff 与临时对象 |

主流程已在首次正式退出后读取隔离存档并重进相同玩家/WorldKey；修改退出、动态 Scene 或 Item 注册时必须保留这条真实磁盘往返断言，不得用内存状态替代。

## 场景组织约定

- 阶段方法只做编排，具体功能用子方法和私有静态状态实现。
- 每个生产功能保留一个代表性完整场景；边界组合留在领域 Smoke 测试。
- 场景使用真实 Prefab、管理器和公开生产 API；不复制生产算法，不用反射直接改私有字段。
- 等待运行时结果时记录截止时间，在后续 Tick 检查；禁止 `Thread.Sleep`、忙等待或阻塞 Editor 主线程。
- 对组件、配置或入口缺失直接抛出带业务语义的异常；禁止因为功能缺失而静默返回伪装通过。
- 会影响后续场景的速度、生命、Buff、物品或生成对象必须在断言后恢复；需跨阶段保留的状态要有明确的最终清理。

## 当前玩法场景

| 场景 | 安排阶段 | 观测与断言 | 清理 |
| --- | --- | --- | --- |
| 新玩家安全陆地出生 | 初始 Chunk 窗口稳定后的 `OnWorldReady` | 玩家脚下权威 `ChunkTerrainData` 必须非水且可走，运行时位置与 `Data_Player` 存档位置一致 | 不修改状态，仅重置完成标记 |
| 主世界出生点复活 | `OnWorldReady` 读取出生点后触发一次真实濒死/复活 | `DamageReceiver.ForceHurt()` 后，`Mod_PlayerDeathState.RespawnFromDying()` 必须把玩家送回 `flatworld.playerSpawn` 且恢复生命 | 恢复测试前位置、生命、速度与物理状态 |
| 自动保存不锁操作 | `TickWorldReadyScenarios` 完成初始临时状态清理后启动，`OnTraversalTick` 观察 | 后台写入完成、无输入锁、Mover/Rigidbody2D/TimeScale 正常 | 仅清空任务引用 |
| 玩家长按奔跑与速度过渡 | `OnWorldReady` 调用真实 `Mover.HandleRunInputPressed/Released()` 和 `Mover.Move` | 走路起步、走跑互切均平滑靠近目标速度；松开方向保留默认 0.07 秒的极短惯性后停止，松开奔跑恢复普通速度倍率 | 立即恢复非奔跑状态、原速度与原移动状态；失败路径统一兜底 |
| 新区块建筑放置 | `OnWorldReady` 使用正式 `Wall_Wood_Summoner` 扫描玩家附近候选格并写入临时石墙；`TickWorldReadyScenarios` 分两帧观察 | 权威地块校验、动态占地和正式虚影正确；`RebuildVersion` 推进后阴影增加，移除再推进一帧后恢复原数量 | 先清理虚影、建筑和召唤器，石墙跨帧断言结束后再清理；失败路径统一兜底 |
| WorldModel 空闲预取、协程表现与生物显隐 | `OnWorldReady` 检查已完成外圈数据和预取并发；往返前按 `ItemMgr.InstantiateItem(...) → Load()` 创建真实 Chicken，原长距离阶段驱动起始区块离开并重新进入可见圈 | 预取实际并发不超过 1；已完成外圈数据 Dormant 且无三类租约；View 解绑后 Chicken inactive，重绑后恢复 active；往返复用同一模型且租约/订阅不重复 | 通过 `ItemMgr.DespawnItem()` 清理测试生物并清空场景引用，生产窗口自行回收 View 与排队项 |
| 燃烧 Buff | 首次 `OnTraversalTick` 通过 `BuffManager.AddBuff(BurningBuffIds.Burning)` 施加 | 后续移动 Tick 观察玩家生命下降；`OnChunkReady` 确认定义仍注册且至少发生一次 Tick | 移除燃烧并恢复测试前生命值 |
| 有限世界右边界环绕 | `OnWorldReady` 后、首次截图和长距离移动前，将真实玩家移动到右边界并通过 `Mover.Move` 越界 | 等待规范目标 Chunk Ready，验证环绕余量、玩家数据、Chunk 字典和地图存档键；恢复原位置并再次等待原 Chunk Ready | 通过和失败路径都恢复位置、速度与移动速度 |
| WorldModel 旧版气候、有序 Biome、二维石地山地、D∞ 河流、湖泊、冲积平原与草地表现 | `OnWorldReady` 先核对 `world.noiseScale` 已进入纯 Profile 及河流距离倍率，再扫描已绑定模型区块；后续在每次 `OnChunkReady` 累积观察 | 断言独立温度通道按 Profile 映射摄氏值、`basePrecipitation/precipitation` 出现地形降雨差异、`windX/windY` 为单位向量，并用 `SurfaceBiomeClassifier` 核对旧有序群系；`mountain` 与活动高度阈值/石地 Tile 一致，淡水 `riverDepth/riverFlow`、`riverKind`、湖泊 `riverSurfaceLevel`（遇到湖泊时）、低坡 `riverFloodplain` 与草状态存在，并确认对应 Ground/Grass Tilemap 已实际写入 Tile | 仅清空场景累计状态，不修改生产世界 |
| 运行时水体地块效果 | `OnChunkReady` 从活动 `ChunkRuntime` 选择可走陆地与水体格，临时移动真实玩家并调用 `TileEffectReceiver.RefreshCurrentTileEffects()` | 断言水体 `TileData_Water`、`水体减速/潮湿`、正式速度倍率，以及离水后的 Buff/速度恢复 | `finally` 与统一 `Cleanup` 恢复原位置、速度并重新绑定原脚下地块 |
| GM 自定义玩家移速倍率 | `OnWorldReady` 通过真实 `PlayerAdminController.TrySetAdminMoveSpeedMultiplier` 输入 `2.75x` | 断言管理员倍率与 `Mover.Speed.MultiplicativeModifier` 同步变化，且其他乘法修饰保持不变 | 立即恢复原管理员倍率；失败清理路径再次兜底恢复 |
| GM 管理员无敌开关 | `OnWorldReady` 通过真实 `PlayerAdminController` 关闭/重新开启无敌，并调用 `DamageReceiver.ForceHurt()` | 关闭后必须正常扣血；重新开启后立即满血，致死伤害不得进入 `Mod_PlayerDeathState` 濒死 | 恢复原玩家名、生命与无敌状态；失败清理路径再次兜底恢复 |
| GM 自定义区块加载倍率 | `OnWorldReady` 通过真实 `ChunkMgr.TrySetChunkLoadSpeedMultiplier` 依次输入 `2.5x` 与正无穷 | 断言有限倍率提升旧队列预算并立即同步新 WorldModel 调度器；“自动最大”达到 CPU 安全并发、四倍数量预算和两倍毫秒预算，不得返回 `int.MaxValue/Infinity` 堵塞主线程 | 按场景开始前的有限/无限状态立即恢复；失败清理路径再次兜底恢复 |
| 退出后同存档重进 | 完整移动、三张截图和 `BeforeWorldExit` 断言后 | 首次退出后旧 WorldKey 动态 Scene 必须卸载，`Player_DIC` 与运行时 Item 注册不得保留旧对象；读取隔离存档并 `ContinueGame()` 后必须恢复同一玩家与 WorldKey | 重进成功后再执行一次正式退出，回到 `GameStartScene` |

## 示例：燃烧 Buff

1. 按 `flatworld-buff` 定位真实 Buff 注册和添加 API。
2. 在首个合适的 `OnTraversalTick` 记录玩家生命、Buff 层数和截止时间，然后通过公开 API 施加燃烧；用私有状态确保只施加一次。
3. 后续 `OnTraversalTick` 只观测 Tick 结果，不阻塞原有移动。
4. 在后续 `OnChunkReady` 断言 Buff 已注册、至少产生一次 Tick 效果，且 Chunk 窗口仍健康。
5. 在 `Cleanup` 移除测试 Buff 并恢复生命，通过和失败路径都执行。

不要在 Skill 中硬编 Buff ID 或伪造 API；每次从当前生产实现获取真实入口。
